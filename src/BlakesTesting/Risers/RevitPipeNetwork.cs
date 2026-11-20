#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace BlakesTesting.Risers
{
    /// <summary>ElementId as a long, whatever the Revit year (2024 made ids 64-bit and deprecated IntegerValue).</summary>
    internal static class LongIds
    {
        public static long ToLong(this ElementId id)
        {
#if REVIT2024_OR_GREATER
            return id.Value;
#else
            return id.IntegerValue;
#endif
        }

        public static ElementId ToElementId(long id)
        {
#if REVIT2024_OR_GREATER
            return new ElementId(id);
#else
            return new ElementId(checked((int)id));
#endif
        }
    }

    /// <summary>
    ///     The open model as the riser walk sees it: pipes, fittings, valves and flex pipe, joined through their
    ///     connectors. Looked up only as the walk reaches them and kept, so a run costs the size of the trails, not of
    ///     the model.
    /// </summary>
    internal sealed class RevitPipeNetwork : IPipeNetwork
    {
        private readonly Document _doc;
        private readonly double _verticalFromDegrees;
        private readonly Dictionary<long, TrailNode?> _nodes = new Dictionary<long, TrailNode?>();
        private readonly Dictionary<long, List<long>> _neighbors = new Dictionary<long, List<long>>();

        public RevitPipeNetwork(Document doc, double verticalFromDegrees = 45.0)
        {
            _doc = doc;
            _verticalFromDegrees = verticalFromDegrees;
        }

        // ------------------------------------------------------------------ IPipeNetwork

        public TrailNode? Node(long id)
        {
            if (_nodes.TryGetValue(id, out TrailNode? cached)) return cached;
            TrailNode? node = Build(_doc.GetElement(LongIds.ToElementId(id)));
            _nodes[id] = node;
            return node;
        }

        public IReadOnlyList<long> Neighbors(long id)
        {
            if (_neighbors.TryGetValue(id, out List<long>? cached)) return cached;
            List<long> list = new List<long>();
            Element? e = _doc.GetElement(LongIds.ToElementId(id));
            ConnectorManager? cm = e == null ? null : Connectors(e);
            if (cm != null)
                foreach (Connector c in cm.Connectors)
                    AddOwners(c, id, list);
            _neighbors[id] = list;
            return list;
        }

        public IReadOnlyList<long> InletNeighbors(long manifoldId)
        {
            List<long> list = new List<long>();
            Element? e = _doc.GetElement(LongIds.ToElementId(manifoldId));
            ConnectorManager? cm = e == null ? null : Connectors(e);
            if (cm == null) return list;

            List<Connector> ports = PipePorts(cm);
            foreach (int i in ManifoldPorts.PickInlets(ports.Select(Radius).ToList()))
                AddOwners(ports[i], manifoldId, list);
            return list;
        }

        // ------------------------------------------------------------------ what each element is

        /// <summary>True when the pipe's TYPE is a PEX one (the same "Pex in the name" test Round Pex Lengths uses).</summary>
        public static bool IsPex(Document doc, Element pipe)
        {
            string name = doc.GetElement(pipe.GetTypeId())?.Name ?? pipe.Name;
            return name.IndexOf("pex", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>Every manifold in the model: pipe fittings with 5+ pipe ports (or 4 named Multiport/Manifold).</summary>
        public static List<FamilyInstance> FindManifolds(Document doc)
        {
            List<FamilyInstance> manifolds = new List<FamilyInstance>();
            foreach (Element e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeFitting).WhereElementIsNotElementType())
                if (e is FamilyInstance fi && IsManifold(doc, fi))
                    manifolds.Add(fi);
            return manifolds;
        }

        public static bool IsManifold(Document doc, FamilyInstance fi)
        {
            ConnectorManager? cm = fi.MEPModel?.ConnectorManager;
            if (cm == null) return false;
            int ports = PipePorts(cm).Count;
            if (ports < 4) return false;
            string family = fi.Symbol?.FamilyName ?? "";
            string type = fi.Symbol?.Name ?? "";
            return ManifoldPorts.IsManifold(ports, family + " " + type);
        }

        private TrailNode? Build(Element? e)
        {
            long id = e == null ? 0 : e.Id.ToLong();
            switch (e)
            {
                case Pipe pipe:
                {
                    if (!(pipe.Location is LocationCurve lc)) return null;
                    XYZ a = lc.Curve.GetEndPoint(0), b = lc.Curve.GetEndPoint(1);
                    bool vertical = PipeShape.IsVertical(b.X - a.X, b.Y - a.Y, b.Z - a.Z, _verticalFromDegrees);
                    return new TrailNode(id, NodeKind.Pipe, IsPex(_doc, pipe), vertical, Math.Min(a.Z, b.Z), Math.Max(a.Z, b.Z));
                }
                case FlexPipe _:
                    return new TrailNode(id, NodeKind.Pass);
                case FamilyInstance fi:
                {
                    BuiltInCategory cat = CategoryOf(fi);
                    if (cat != BuiltInCategory.OST_PipeFitting && cat != BuiltInCategory.OST_PipeAccessory) return null;
                    return new TrailNode(id, IsManifold(_doc, fi) ? NodeKind.Manifold : NodeKind.Pass);
                }
                default:
                    return null;
            }
        }

        private static BuiltInCategory CategoryOf(Element e)
        {
            Category? c = e.Category;
            return c == null ? BuiltInCategory.INVALID : (BuiltInCategory)c.Id.ToLong();
        }

        private static ConnectorManager? Connectors(Element e)
        {
            switch (e)
            {
                case MEPCurve curve: return curve.ConnectorManager;
                case FamilyInstance fi: return fi.MEPModel?.ConnectorManager;
                default: return null;
            }
        }

        private static List<Connector> PipePorts(ConnectorManager cm)
        {
            List<Connector> ports = new List<Connector>();
            foreach (Connector c in cm.Connectors)
                if (c.Domain == Domain.DomainPiping && c.ConnectorType != ConnectorType.Logical)
                    ports.Add(c);
            return ports;
        }

        private static double Radius(Connector c)
        {
            try
            {
                return c.Shape == ConnectorProfileType.Round ? c.Radius : Math.Max(c.Width, c.Height) / 2.0;
            }
            catch
            {
                return 0.0;
            }
        }

        /// <summary>The owners of the connectors this port is joined to - pipes, fittings, valves; never a system.</summary>
        private static void AddOwners(Connector port, long self, List<long> into)
        {
            if (!port.IsConnected) return;
            foreach (Connector other in port.AllRefs)
            {
                Element owner = other.Owner;
                if (owner == null || !(owner is MEPCurve || owner is FamilyInstance)) continue;
                long id = owner.Id.ToLong();
                if (id != self && !into.Contains(id)) into.Add(id);
            }
        }
    }
}

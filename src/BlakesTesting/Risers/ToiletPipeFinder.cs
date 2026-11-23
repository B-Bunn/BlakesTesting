#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using BlakesTesting.Commands;

namespace BlakesTesting.Risers
{
    /// <summary>What the finder looked at, for the dialog: the toilets found and the bends it turned down, with the reason.</summary>
    internal sealed class ToiletSearch
    {
        public List<ToiletPipe> Pipes { get; } = new List<ToiletPipe>();

        /// <summary>Toilet-named bends in the model.</summary>
        public int Bends { get; set; }

        /// <summary>Bends with no vertical pipe rising from them (a flat bend, or one whose pipe goes down).</summary>
        public int NoRiser { get; set; }

        /// <summary>Bends whose rising pipe was not a 3" sanitary ABS, PVC or cast iron one (so not the toilet's).</summary>
        public int WrongPipe { get; set; }

        /// <summary>Toilet pipes whose level would not read, so they sit under <see cref="PotFlangeRiserRules.NoLevel"/>.</summary>
        public int NoLevel => Pipes.Count(p => p.Level == PotFlangeRiserRules.NoLevel);
    }

    /// <summary>
    ///     Finds each toilet's vertical pipe: from every bend named like a toilet's (<see cref="PotFlangeRiserRules.IsToiletBend"/>),
    ///     the pipe on one of its ports that stands steeper than 45 degrees and RISES from that port (a pipe that drops
    ///     away from the bend is a stack coming down to it, not a pot flange stub). A no-hub coupling between the bend and
    ///     the pipe is looked through, as Place Sleeves does. The pipe then has to be the toilet's kind - sanitary, 3",
    ///     ABS, PVC or cast iron.
    ///     <para>
    ///         The pipe's level is its <c>TPI Level</c> when the job has that parameter filled in (the level every other
    ///         BIM Tools command reports it under), else the pipe's reference level.
    ///     </para>
    /// </summary>
    internal static class ToiletPipeFinder
    {
        public static ToiletSearch Find(Document doc)
        {
            ToiletSearch search = new ToiletSearch();
            HashSet<long> seen = new HashSet<long>();

            foreach (Element e in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_PipeFitting).WhereElementIsNotElementType())
            {
                if (!(e is FamilyInstance bend)) continue;
                if (!PotFlangeRiserRules.IsToiletBend(bend.Symbol?.FamilyName, bend.Symbol?.Name)) continue;
                search.Bends++;

                List<Pipe> rising = RisingPipes(doc, bend);
                if (rising.Count == 0)
                {
                    search.NoRiser++;
                    continue;
                }

                bool any = false;
                foreach (Pipe pipe in rising)
                {
                    if (!IsToiletKindOfPipe(doc, pipe)) continue;
                    any = true;
                    if (!seen.Add(pipe.Id.ToLong())) continue;
                    search.Pipes.Add(new ToiletPipe(pipe.Id.ToLong(), LevelOf(doc, pipe), RiserCutLengths.ReadLengthFeet(pipe)));
                }

                if (!any) search.WrongPipe++;
            }

            return search;
        }

        /// <summary>The pipes standing on this bend's ports (straight, or through one coupling) that rise from the port.</summary>
        private static List<Pipe> RisingPipes(Document doc, FamilyInstance bend)
        {
            List<Pipe> found = new List<Pipe>();
            ConnectorManager? cm = bend.MEPModel?.ConnectorManager;
            if (cm == null) return found;

            foreach (Connector port in cm.Connectors)
            {
                if (port.Domain != Domain.DomainPiping || port.ConnectorType == ConnectorType.Logical || !port.IsConnected) continue;
                foreach (Connector other in port.AllRefs)
                {
                    Element owner = other.Owner;
                    if (owner is Pipe pipe)
                    {
                        if (RisesFrom(pipe, other.Origin) && !found.Any(p => p.Id == pipe.Id)) found.Add(pipe);
                    }
                    else if (owner is FamilyInstance coupling && coupling.Id != bend.Id
                             && PotFlangeRiserRules.IsCoupling(coupling.Symbol?.FamilyName, coupling.Symbol?.Name))
                    {
                        ConnectorManager? ccm = coupling.MEPModel?.ConnectorManager;
                        if (ccm == null) continue;
                        foreach (Connector side in ccm.Connectors)
                        {
                            if (side.Domain != Domain.DomainPiping || side.ConnectorType == ConnectorType.Logical || !side.IsConnected) continue;
                            foreach (Connector beyond in side.AllRefs)
                                if (beyond.Owner is Pipe far && RisesFrom(far, beyond.Origin) && !found.Any(p => p.Id == far.Id))
                                    found.Add(far);
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>True when the pipe is steeper than 45 degrees and the end at <paramref name="joint"/> is its LOWER end.</summary>
        private static bool RisesFrom(Pipe pipe, XYZ joint)
        {
            if (!(pipe.Location is LocationCurve lc)) return false;
            XYZ a = lc.Curve.GetEndPoint(0), b = lc.Curve.GetEndPoint(1);
            if (!PipeShape.IsVertical(b.X - a.X, b.Y - a.Y, b.Z - a.Z)) return false;
            XYZ low = a.Z <= b.Z ? a : b;
            XYZ high = a.Z <= b.Z ? b : a;
            // The joint is the end the bend (or the coupling's far side) touches: it must be the low one.
            return joint.DistanceTo(low) < joint.DistanceTo(high);
        }

        private static bool IsToiletKindOfPipe(Document doc, Pipe pipe)
        {
            if (!PotFlangeRiserRules.IsSanitary(SystemName(doc, pipe))) return false;

            Parameter? diameter = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM);
            if (diameter == null || !diameter.HasValue || !PotFlangeRiserRules.IsToiletSize(diameter.AsDouble() * 12.0)) return false;

            string typeName = doc.GetElement(pipe.GetTypeId())?.Name ?? pipe.Name;
            return PotFlangeRiserRules.IsToiletMaterial(typeName);
        }

        /// <summary>The system type's name ("Sanitary"), else the system's own name ("Sanitary 4").</summary>
        private static string? SystemName(Document doc, Pipe pipe)
        {
            ElementId typeId = pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId() ?? ElementId.InvalidElementId;
            string? type = doc.GetElement(typeId)?.Name;
            if (PotFlangeRiserRules.IsSanitary(type)) return type;
            try
            {
                return pipe.MEPSystem?.Name ?? type;
            }
            catch
            {
                return type;
            }
        }

        /// <summary>TPI Level when filled in, else the pipe's reference level, else <see cref="PotFlangeRiserRules.NoLevel"/>.</summary>
        public static string LevelOf(Document doc, Element pipe)
        {
            try
            {
                Parameter? tpi = pipe.LookupParameter("TPI Level");
                if (tpi != null && tpi.HasValue)
                {
                    string? text = tpi.StorageType == StorageType.String ? tpi.AsString() : tpi.AsValueString();
                    if (!string.IsNullOrWhiteSpace(text)) return text!.Trim();
                }

                ElementId levelId = pipe.get_Parameter(BuiltInParameter.RBS_START_LEVEL_PARAM)?.AsElementId() ?? ElementId.InvalidElementId;
                if (doc.GetElement(levelId) is Level level && !string.IsNullOrWhiteSpace(level.Name)) return level.Name.Trim();
            }
            catch
            {
            }

            return PotFlangeRiserRules.NoLevel;
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BlakesTesting.Risers;

namespace BlakesTesting.Trench
{
    /// <summary>What a run found to plan the trench under, and what it left out.</summary>
    internal sealed class TrenchScope
    {
        public TrenchScope(TrenchPlanResult plan, string floor, int pipes, bool fromSelection, string viewName, int setAside, int floorsRefused)
        {
            Plan = plan;
            Floor = floor;
            Pipes = pipes;
            FromSelection = fromSelection;
            ViewName = viewName;
            SetAside = setAside;
            FloorsRefused = floorsRefused;
        }

        public TrenchPlanResult Plan { get; }

        /// <summary>Where the floor came from, as the report says it: <c>level Level 1 (0'-0")</c>.</summary>
        public string Floor { get; }

        public int Pipes { get; }
        public bool FromSelection { get; }
        public string ViewName { get; }

        /// <summary>Pipes steeper than 45 degrees or plumb, which have no trench of their own.</summary>
        public int SetAside { get; }

        /// <summary>Floor readings Revit would not make, so those floors were passed over at that point.</summary>
        public int FloorsRefused { get; }

        public string Over => FromSelection ? "the " + Pipes + " selected pipe(s)" : "the " + Pipes + " pipe(s) in " + ViewName;
    }

    /// <summary>
    ///     Reads the model into <see cref="TrenchRules.Plan"/>: the pipes (the selection's, else the active view's), the
    ///     floor over them (a level, or the floors a parameter picks, each read at its real top by a vertical ray - BIM
    ///     Tools' Calculate Spoils does the same) and each pipe's bottom and fittings. Nothing here writes to the model.
    /// </summary>
    internal static class TrenchModel
    {
        /// <summary>A fitting whose centre is further than this past a pipe's end (feet) is not run on to.</summary>
        private const double FittingReach = 2.0;

        /// <summary>One floor that matched, with what its top is read from.</summary>
        private sealed class FloorTop
        {
            public FloorTop(Floor floor, List<Solid> solids, BoundingBoxXYZ box)
            {
                Floor = floor;
                Solids = solids;
                Box = box;
            }

            public Floor Floor { get; }
            public List<Solid> Solids { get; }
            public BoundingBoxXYZ Box { get; }
        }

        public static List<string> LevelNames(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .OrderBy(l => l.ProjectElevation).ThenBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .Select(l => l.Name).ToList();

        /// <summary>The level named <paramref name="name"/> - as spelled, else in any case - or null.</summary>
        public static Level? FindLevel(Document doc, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            List<Level> levels = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().ToList();
            return levels.FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.Ordinal))
                   ?? levels.FirstOrDefault(l => string.Equals(l.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>What the settings lack for THIS model, as the window says it, or null when a run can go ahead.</summary>
        public static string? WhatIsMissing(Document doc, TrenchMapSettings settings)
        {
            string? missing = settings.WhatIsMissing();
            if (missing != null) return missing;

            if (settings.Source == TrenchFloorSource.Level && FindLevel(doc, settings.LevelName) == null)
                return "Trench Map needs the level the floor is at (this model has no level \"" + settings.LevelName + "\").";
            if (settings.Source == TrenchFloorSource.Floors && MatchingFloors(doc, settings.FloorParameter, settings.FloorValue).Count == 0)
                return "Trench Map needs which floors are the floor (no floor in this model has " + settings.FloorParameter + " = \"" + settings.FloorValue + "\").";
            return null;
        }

        /// <summary>
        ///     Lays out the trench under the selection's pipes, or the active view's when none is selected. Null, with
        ///     <paramref name="notRun"/> saying why, when there is nothing to lay one out under.
        /// </summary>
        public static TrenchScope? PlanTrench(UIDocument uidoc, TrenchMapSettings settings, out string? notRun)
        {
            notRun = null;
            Document doc = uidoc.Document;
            View view = doc.ActiveView;
            double offset = settings.FloorOffset ?? 0;

            List<MEPCurve> pipes = new List<MEPCurve>();
            ICollection<ElementId> selected = uidoc.Selection.GetElementIds();
            bool fromSelection = selected.Count > 0;
            if (fromSelection)
            {
                foreach (ElementId id in selected)
                    if (doc.GetElement(id) is MEPCurve curve && IsPipeCategory(curve)) pipes.Add(curve);
            }
            else
            {
                pipes.AddRange(new FilteredElementCollector(doc, view.Id).OfClass(typeof(MEPCurve))
                    .OfCategory(BuiltInCategory.OST_PipeCurves).Cast<MEPCurve>());
            }

            if (pipes.Count == 0)
            {
                notRun = "No pipes to work on - " + (fromSelection ? "the selection has none." : "select the pipes, or run it in a view that shows them.");
                return null;
            }

            // The top of the trench over a point: the floor there less the offset.
            int refused = 0;
            Func<double, double, double, double?> topOfTrench;
            string floor;
            if (settings.Source == TrenchFloorSource.Level)
            {
                Level? level = FindLevel(doc, settings.LevelName);
                if (level == null)
                {
                    notRun = "This model has no level \"" + settings.LevelName + "\" - choose one in the Trench Map settings.";
                    return null;
                }

                double top = level.ProjectElevation - offset;
                topOfTrench = (x, y, bottom) => top;
                floor = "level " + level.Name + " (" + TrenchText.Feet(level.ProjectElevation) + ")";
            }
            else
            {
                List<FloorTop> floors = MatchingFloors(doc, settings.FloorParameter, settings.FloorValue);
                if (floors.Count == 0)
                {
                    notRun = "No floor in this model has " + settings.FloorParameter + " = \"" + settings.FloorValue + "\" - choose the floors in the Trench Map settings.";
                    return null;
                }

                topOfTrench = (x, y, bottom) => TopAt(floors, x, y, bottom, ref refused) - offset;
                floor = "the " + floors.Count + " floor(s) where " + settings.FloorParameter + " = \"" + settings.FloorValue + "\"";
            }

            // Each pipe's trench line: the bottom of the pipe at its ends, and how far on to the centre of the fitting at
            // each end. A plumb or steep pipe (a riser) rises out of the trench and has none.
            List<TrenchRun> runs = new List<TrenchRun>();
            int setAside = 0;
            foreach (MEPCurve pipe in pipes)
            {
                if (!(pipe.Location is LocationCurve lc)) continue;
                XYZ e0 = lc.Curve.GetEndPoint(0), e1 = lc.Curve.GetEndPoint(1), vec = e1 - e0;
                if (!TrenchRules.IsTrenchPipe(vec.X, vec.Y, vec.Z))
                {
                    setAside++;
                    continue;
                }

                double od = OutsideDiameter(pipe);
                (double X, double Y, double Z) b0 = TrenchRules.BottomOfPipe(e0.X, e0.Y, e0.Z, vec.X, vec.Y, vec.Z, od);
                (double X, double Y, double Z) b1 = TrenchRules.BottomOfPipe(e1.X, e1.Y, e1.Z, vec.X, vec.Y, vec.Z, od);
                XYZ along = new XYZ(vec.X, vec.Y, 0).Normalize();
                runs.Add(new TrenchRun
                {
                    Id = pipe.Id.ToLong(),
                    BopStart = b0,
                    BopEnd = b1,
                    ExtendStart = ToFittingCentre(pipe, e0, along.Negate()),
                    ExtendEnd = ToFittingCentre(pipe, e1, along),
                });
            }

            TrenchPlanResult plan = TrenchRules.Plan(runs, settings, topOfTrench);
            return new TrenchScope(plan, floor, pipes.Count, fromSelection, view.Name, setAside, refused);
        }

        private static bool IsPipeCategory(Element e) =>
            e.Category != null && e.Category.Id.ToLong() == (long)BuiltInCategory.OST_PipeCurves;

        /// <summary>A pipe's outside diameter (feet); its nominal one when the outside is not read.</summary>
        private static double OutsideDiameter(Element pipe)
        {
            double od = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER)?.AsDouble() ?? 0;
            if (od > 0) return od;
            return pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM)?.AsDouble() ?? 0;
        }

        /// <summary>
        ///     How far past <paramref name="end"/>, in plan along <paramref name="outward"/>, the centre of the fitting
        ///     connected there is - an elbow's, a tee's, a cleanout's - so the trench runs on to it and a bend's corner is
        ///     dug. 0 for an end with nothing connected, or a centre further off than <see cref="FittingReach"/>.
        /// </summary>
        private static double ToFittingCentre(MEPCurve pipe, XYZ end, XYZ outward)
        {
            Connector? nearest = null;
            double best = double.MaxValue;
            foreach (Connector c in pipe.ConnectorManager.Connectors)
            {
                if (c.ConnectorType != ConnectorType.End) continue;
                double d = c.Origin.DistanceTo(end);
                if (d < best)
                {
                    best = d;
                    nearest = c;
                }
            }

            if (nearest == null || !nearest.IsConnected) return 0;

            double reach = 0;
            foreach (Connector other in nearest.AllRefs)
            {
                if (other.Owner == null || other.Owner.Id == pipe.Id) continue;
                if (!(other.Owner is FamilyInstance fitting) || !(fitting.Location is LocationPoint point)) continue;
                XYZ toCentre = point.Point - end;
                double past = toCentre.X * outward.X + toCentre.Y * outward.Y;
                if (past > reach && past <= FittingReach) reach = past;
            }

            return reach;
        }

        // ------------------------------------------------------------------ the floors

        /// <summary>A parameter's value as a person reads it, trimmed; null for none, no value, or blank.</summary>
        private static string? ParameterText(Parameter? p)
        {
            if (p == null || !p.HasValue) return null;
            string? text = p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
            return text == null || string.IsNullOrWhiteSpace(text) ? null : text.Trim();
        }

        /// <summary>The floor's value of the parameter named <paramref name="parameter"/>: its own, else its type's.</summary>
        private static string? ValueOf(Document doc, Element floor, string parameter)
        {
            string? value = ParameterText(floor.LookupParameter(parameter));
            if (value != null) return value;
            return ParameterText(doc.GetElement(floor.GetTypeId())?.LookupParameter(parameter));
        }

        private static List<FloorTop> MatchingFloors(Document doc, string parameter, string value)
        {
            List<FloorTop> matched = new List<FloorTop>();
            if (string.IsNullOrWhiteSpace(parameter) || string.IsNullOrWhiteSpace(value)) return matched;

            Options options = new Options { ComputeReferences = false, IncludeNonVisibleObjects = false, DetailLevel = ViewDetailLevel.Fine };
            foreach (Floor floor in new FilteredElementCollector(doc).OfClass(typeof(Floor)).WhereElementIsNotElementType().Cast<Floor>())
            {
                string? reads = ValueOf(doc, floor, parameter);
                if (reads == null || !string.Equals(reads, value.Trim(), StringComparison.OrdinalIgnoreCase)) continue;

                BoundingBoxXYZ? box = floor.get_BoundingBox(null);
                if (box == null) continue;
                List<Solid> solids = new List<Solid>();
                CollectSolids(floor.get_Geometry(options), solids);
                if (solids.Count == 0) continue;
                matched.Add(new FloorTop(floor, solids, box));
            }

            return matched;
        }

        private static void CollectSolids(GeometryElement? geometry, List<Solid> solids)
        {
            if (geometry == null) return;
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid solid)
                {
                    if (solid.Volume > 0 && solid.Faces.Size > 0) solids.Add(solid);
                }
                else if (obj is GeometryInstance instance)
                {
                    CollectSolids(instance.GetInstanceGeometry(), solids);
                }
            }
        }

        /// <summary>
        ///     The floor the top of the trench is measured down from at (x, y), for a trench bottom at
        ///     <paramref name="z"/>: the tops of <paramref name="floors"/> straight over and under the point, chosen between
        ///     by <see cref="TrenchRules.FloorOver"/>. Null where no floor is - off the floors' edge, or under a hole. A
        ///     floor whose solid Revit will not cut with the ray is passed over, and counted in <paramref name="refused"/>.
        /// </summary>
        private static double? TopAt(List<FloorTop> floors, double x, double y, double z, ref int refused)
        {
            List<double> tops = new List<double>();
            foreach (FloorTop f in floors)
            {
                if (x < f.Box.Min.X - 1e-6 || x > f.Box.Max.X + 1e-6 || y < f.Box.Min.Y - 1e-6 || y > f.Box.Max.Y + 1e-6) continue;

                Line ray = Line.CreateBound(new XYZ(x, y, f.Box.Min.Z - 1.0), new XYZ(x, y, f.Box.Max.Z + 1.0));
                double top = double.NegativeInfinity;
                foreach (Solid solid in f.Solids)
                {
                    try
                    {
                        SolidCurveIntersection hits = solid.IntersectWithCurve(ray, new SolidCurveIntersectionOptions());
                        for (int i = 0; i < hits.SegmentCount; i++)
                        {
                            Curve segment = hits.GetCurveSegment(i);
                            top = Math.Max(top, Math.Max(segment.GetEndPoint(0).Z, segment.GetEndPoint(1).Z));
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        refused++;
                    }
                }

                if (!double.IsNegativeInfinity(top)) tops.Add(top);
            }

            return TrenchRules.FloorOver(tops, z);
        }
    }
}

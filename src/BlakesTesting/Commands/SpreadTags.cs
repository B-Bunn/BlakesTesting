#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BlakesTesting.Tags;

namespace BlakesTesting.Commands
{
    /// <summary>
    ///     Un-piles overlapping tags. Only tags whose labels overlap another tag are moved, each to the free spot nearest
    ///     where it is now, beside the pile on its left or right, level with its old height where it can be, with a
    ///     leader on so it still points at what it tags. Tags that are already readable stay put, and moved tags keep
    ///     clear of them. Works on the selected tags, or every tag in the active view when none are selected.
    ///     One Undo takes it all back.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SpreadTags : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            TagSpreader.Run(commandData, new SpreadOptions { Side = SpreadSide.Both }, ref message);
    }

    /// <summary>The same, but only to the left of the pile - for a view with no room on its right.</summary>
    [Transaction(TransactionMode.Manual)]
    public class SpreadTagsLeft : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            TagSpreader.Run(commandData, new SpreadOptions { Side = SpreadSide.Left }, ref message);
    }

    /// <summary>The same, but only to the right of the pile.</summary>
    [Transaction(TransactionMode.Manual)]
    public class SpreadTagsRight : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            TagSpreader.Run(commandData, new SpreadOptions { Side = SpreadSide.Right }, ref message);
    }

    /// <summary>The Revit side of the spread: reads every tag in the view, asks <see cref="TagLayout" /> where they go, moves them.</summary>
    internal static class TagSpreader
    {
        public static Result Run(ExternalCommandData commandData, SpreadOptions options, ref string message)
        {
            UIDocument? uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Open a model first.";
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            View view = doc.ActiveView;
            if (view is ViewSheet || view.ViewType == ViewType.Schedule || view.ViewType == ViewType.Legend)
            {
                message = "Open a plan, section, elevation or 3D view - tags are spread in the view they live in.";
                return Result.Cancelled;
            }

            // Every tag in the view is an obstacle; only the selected ones (or all, with no selection) may move.
            List<IndependentTag> inView = new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
            if (inView.Count == 0)
            {
                message = "This view has no tags.";
                return Result.Cancelled;
            }

            HashSet<int> selected = uidoc.Selection.GetElementIds().Select(id => id.IntegerValueCompat()).ToHashSet();
            bool useSelection = inView.Any(t => selected.Contains(t.Id.IntegerValueCompat()));
            string scope = useSelection ? "selected" : "in this view";

            XYZ right = view.RightDirection;
            XYZ up = view.UpDirection;
            double ft = Math.Max(1, view.Scale) / 12.0;
            int pinned = 0, measured = 0;

            // Tags with a leader cannot be measured (their box includes the leader), so calibrate their size from the tags in
            // this view that can: the typical measured height, and width per character. A fixed default would be wrong for
            // any tag text size - and made a second run spread tags further than the first.
            var measuredH = new List<double>();
            var measuredWPerChar = new List<double>();
            foreach (IndependentTag tag in inView)
            {
                (double w, double h, bool real) = LabelSize(tag, view, right, up, options, ft);
                if (!real) continue;
                measuredH.Add(h);
                measuredWPerChar.Add(w / Math.Max(1, TextLength(tag)));
            }

            double fallbackH = options.LabelHeight * ft, fallbackCharW = options.CharWidth * ft;
            if (measuredH.Count >= 5)
            {
                measuredH.Sort();
                measuredWPerChar.Sort();
                fallbackH = measuredH[measuredH.Count / 2];
                fallbackCharW = measuredWPerChar[measuredWPerChar.Count / 2];
            }

            var byId = new Dictionary<int, IndependentTag>();
            var items = new List<TagItem>();
            foreach (IndependentTag tag in inView)
            {
                int id = tag.Id.IntegerValueCompat();
                bool movable = (!useSelection || selected.Contains(id)) && !tag.Pinned;
                if (tag.Pinned && (!useSelection || selected.Contains(id))) pinned++;

                XYZ head = tag.TagHeadPosition;
                (double width, double height, bool real) = LabelSize(tag, view, right, up, options, ft);
                if (real) measured++;
                else
                {
                    width = Math.Max(1, TextLength(tag)) * fallbackCharW;
                    height = fallbackH;
                }

                byId[id] = tag;
                double hr = head.DotProduct(right), hu = head.DotProduct(up);
                (double ar, double au) = LeaderEnd(doc, tag, hr, hu, right, up);
                items.Add(new TagItem(id, hr, hu, width, height, movable, ar, au, force: useSelection && movable));
            }

            List<DrawingLine> drawing = options.GeometryPenalty > 0 ? DrawingLines(doc, view, right, up, ft) : new List<DrawingLine>();
            var stats = new LayoutStats();
            List<TagPlacement> placements = TagLayout.Compute(items, options, Math.Max(1, view.Scale), drawing, stats);
            Dictionary<int, TagItem> itemById = items.ToDictionary(i => i.Id);

            int moved = 0;
            var failed = new List<string>();
            int candidates = placements.Count(p => itemById[p.Id].Movable);
            using (Transaction transaction = new Transaction(doc, "Spread tags"))
            {
                transaction.Start();
                foreach (TagPlacement placement in placements.Where(p => p.Moved))
                {
                    IndependentTag tag = byId[placement.Id];
                    TagItem item = itemById[placement.Id];
                    try
                    {
                        // Move along the view's own axes only, so the tag keeps its depth in the view.
                        XYZ target = tag.TagHeadPosition + right * (placement.Right - item.Right) + up * (placement.Up - item.Up);
                        TryLeader(tag);
                        tag.TagHeadPosition = target;
                        moved++;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(placement.Id + ": " + ex.Message);
                    }
                }

                transaction.Commit();
            }

            int stuck = placements.Count(p => p.Stuck);
            List<TagItem> sizes = items.Where(i => i.Movable).OrderBy(i => i.Height).ToList();
            TagItem? mid = sizes.Count > 0 ? sizes[sizes.Count / 2] : null;
            string report = useSelection
                ? $"Re-laid out {moved} of {candidates} selected tag(s); {candidates - moved - stuck} were already in the best spot found."
                : $"Spread {moved} overlapping tag(s) {scope}; {candidates - moved - stuck} already readable.";
            if (!useSelection && moved == 0 && stuck == 0) report += "\nNothing overlaps. Select tags and run again to re-lay those out anyway.";
            if (stuck > 0) report += $"\n{stuck} overlap but have no free spot within {options.MaxDistance} in of paper - left where they are.";
            report += $"\nLeader crossings: {stats.CrossingsBefore} -> {stats.CrossingsAfter}. Read {drawing.Count} drawing line(s); {stats.OnDrawing} tag(s) still sit on the drawing.";
            if (mid != null) report += $"\nLabel size used (typical): {mid.Width / ft:F2} x {mid.Height / ft:F2} in of paper; {measured} of {items.Count} measured from the tag, the rest estimated from those.";
            if (pinned > 0) report += $"\n{pinned} pinned tag(s) were not touched.";
            if (failed.Count > 0) report += $"\n{failed.Count} could not be moved:\n" + string.Join("\n", failed.Take(10));
            TaskDialog.Show("Spread Tags", report);
            return Result.Succeeded;
        }

        /// <summary>
        ///     The lines of the model a label should keep off, in view coordinates: a pipe, duct or other run as its centre line,
        ///     anything else as its box (edges and diagonals) - boxes bigger than 2" of paper are skipped, so a wall or a
        ///     slab does not fence the labels out of the whole view. Tags and annotations are not drawing.
        /// </summary>
        private static List<DrawingLine> DrawingLines(Document doc, View view, XYZ right, XYZ up, double ft)
        {
            var lines = new List<DrawingLine>();
            double limit = 2 * ft;
            foreach (Element e in new FilteredElementCollector(doc, view.Id).WhereElementIsNotElementType())
            {
                try
                {
                    if (e is IndependentTag || e.Category == null || e.Category.CategoryType != CategoryType.Model) continue;
                    if (e.Location is LocationCurve lc && lc.Curve != null)
                    {
                        XYZ a = lc.Curve.GetEndPoint(0), b = lc.Curve.GetEndPoint(1);
                        lines.Add(new DrawingLine(a.DotProduct(right), a.DotProduct(up), b.DotProduct(right), b.DotProduct(up)));
                        continue;
                    }

                    BoundingBoxXYZ? box = e.get_BoundingBox(view);
                    if (box == null) continue;
                    double minR = double.MaxValue, maxR = double.MinValue, minU = double.MaxValue, maxU = double.MinValue;
                    foreach (double x in new[] { box.Min.X, box.Max.X })
                        foreach (double y in new[] { box.Min.Y, box.Max.Y })
                            foreach (double z in new[] { box.Min.Z, box.Max.Z })
                            {
                                var c = new XYZ(x, y, z);
                                double r = c.DotProduct(right), u = c.DotProduct(up);
                                minR = Math.Min(minR, r); maxR = Math.Max(maxR, r);
                                minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
                            }

                    if (maxR - minR > limit || maxU - minU > limit) continue;
                    lines.Add(new DrawingLine(minR, minU, maxR, minU));
                    lines.Add(new DrawingLine(maxR, minU, maxR, maxU));
                    lines.Add(new DrawingLine(maxR, maxU, minR, maxU));
                    lines.Add(new DrawingLine(minR, maxU, minR, minU));
                    lines.Add(new DrawingLine(minR, minU, maxR, maxU));
                    lines.Add(new DrawingLine(minR, maxU, maxR, minU));
                }
                catch (Exception)
                {
                    // an element with no usable geometry in this view is simply not an obstacle
                }
            }

            return lines;
        }

        /// <summary>
        ///     Where the tag's leader ends, in view coordinates: the point on the first tagged element nearest the tag - the
        ///     closest point of a pipe's run, a fitting's own point, or the middle of anything else. A tag whose element cannot
        ///     be read is taken to point at its own position.
        /// </summary>
        private static (double r, double u) LeaderEnd(Document doc, IndependentTag tag, double headR, double headU, XYZ right, XYZ up)
        {
            try
            {
                foreach (ElementId id in tag.GetTaggedLocalElementIds())
                {
                    Element? e = doc.GetElement(id);
                    if (e == null) continue;
                    if (e.Location is LocationCurve lc && lc.Curve != null)
                    {
                        XYZ a = lc.Curve.GetEndPoint(0), b = lc.Curve.GetEndPoint(1);
                        double ar = a.DotProduct(right), au = a.DotProduct(up), br = b.DotProduct(right), bu = b.DotProduct(up);
                        double dr = br - ar, du = bu - au, len2 = dr * dr + du * du;
                        double t = len2 < 1e-12 ? 0 : Math.Max(0, Math.Min(1, ((headR - ar) * dr + (headU - au) * du) / len2));
                        return (ar + dr * t, au + du * t);
                    }

                    if (e.Location is LocationPoint lp) return (lp.Point.DotProduct(right), lp.Point.DotProduct(up));
                    BoundingBoxXYZ? box = e.get_BoundingBox(null);
                    if (box != null)
                    {
                        XYZ c = (box.Min + box.Max) * 0.5;
                        return (c.DotProduct(right), c.DotProduct(up));
                    }
                }
            }
            catch (Exception)
            {
                // fall through
            }

            return (headR, headU);
        }

        /// <summary>Turn the leader on so the tag still points at what it tags; a tag family without one just moves.</summary>
        private static void TryLeader(IndependentTag tag)
        {
            try
            {
                if (!tag.HasLeader) tag.HasLeader = true;
            }
            catch (Exception)
            {
                // The family has no leader to show - the tag moves anyway.
            }
        }

        /// <summary>
        ///     The label's size in model feet along the view's axes. A tag with no leader reports its true box; one with a
        ///     leader would report the leader too, so its size comes from its text and the paper-size defaults.
        /// </summary>
        private static (double width, double height, bool real) LabelSize(IndependentTag tag, View view, XYZ right, XYZ up, SpreadOptions options, double ft)
        {
            double fallbackH = options.LabelHeight * ft;
            double fallbackW = Math.Max(1, TextLength(tag)) * options.CharWidth * ft;
            try
            {
                if (!tag.HasLeader)
                {
                    BoundingBoxXYZ? box = tag.get_BoundingBox(view);
                    if (box != null)
                    {
                        var corners = new List<XYZ>();
                        foreach (double x in new[] { box.Min.X, box.Max.X })
                            foreach (double y in new[] { box.Min.Y, box.Max.Y })
                                foreach (double z in new[] { box.Min.Z, box.Max.Z })
                                    corners.Add(new XYZ(x, y, z));
                        double w = corners.Max(c => c.DotProduct(right)) - corners.Min(c => c.DotProduct(right));
                        double h = corners.Max(c => c.DotProduct(up)) - corners.Min(c => c.DotProduct(up));
                        // Believe it only if it is label-sized (under 3" of paper): anything else is not just the text.
                        if (w > 1e-6 && h > 1e-6 && w < 3 * ft && h < 3 * ft) return (w, h, true);
                    }
                }
            }
            catch (Exception)
            {
                // fall through to the estimate
            }

            return (fallbackW, fallbackH, false);
        }

        private static int TextLength(IndependentTag tag)
        {
            try
            {
                return tag.TagText?.Length ?? 1;
            }
            catch (Exception)
            {
                return 1;
            }
        }
    }

    internal static class ElementIdCompat
    {
        /// <summary>ElementId's number: <c>Value</c> from 2024 on, <c>IntegerValue</c> before it (which 2024 deprecates).</summary>
        public static int IntegerValueCompat(this ElementId id)
        {
#if REVIT2024_OR_GREATER
            return checked((int)id.Value);
#else
            return id.IntegerValue;
#endif
        }
    }
}

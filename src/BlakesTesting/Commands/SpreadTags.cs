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
            int pinned = 0;
            var byId = new Dictionary<int, IndependentTag>();
            var items = new List<TagItem>();
            foreach (IndependentTag tag in inView)
            {
                int id = tag.Id.IntegerValueCompat();
                bool movable = (!useSelection || selected.Contains(id)) && !tag.Pinned;
                if (tag.Pinned && (!useSelection || selected.Contains(id))) pinned++;

                XYZ head = tag.TagHeadPosition;
                (double width, double height) = LabelSize(tag, view, right, up, options, ft);
                byId[id] = tag;
                items.Add(new TagItem(id, head.DotProduct(right), head.DotProduct(up), width, height, movable));
            }

            List<TagPlacement> placements = TagLayout.Compute(items, options, Math.Max(1, view.Scale));
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

            string report = $"Spread {moved} overlapping tag(s) {scope}; {candidates - moved} already readable or left alone.";
            if (pinned > 0) report += $"\n{pinned} pinned tag(s) were not touched.";
            if (failed.Count > 0) report += $"\n{failed.Count} could not be moved:\n" + string.Join("\n", failed.Take(10));
            TaskDialog.Show("Spread Tags", report);
            return Result.Succeeded;
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
        private static (double width, double height) LabelSize(IndependentTag tag, View view, XYZ right, XYZ up, SpreadOptions options, double ft)
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
                        if (w > 1e-6 && h > 1e-6 && w < 3 * ft && h < 3 * ft) return (w, h);
                    }
                }
            }
            catch (Exception)
            {
                // fall through to the estimate
            }

            return (fallbackW, fallbackH);
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

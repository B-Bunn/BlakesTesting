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
    ///     Spreads piled-up tags out. Tags placed on top of each other (every riser's number landing top-right of the
    ///     same spot) are moved to tidy columns to the bottom-left of the pile, with leaders switched on so each still
    ///     points at what it tags. Works on the selected tags, or every tag in the active view when none are selected.
    ///     One Undo takes it all back.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SpreadTagsBottomLeft : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return TagSpreader.Run(commandData, new SpreadOptions { Direction = SpreadDirection.BottomLeft }, ref message);
        }
    }

    /// <summary>The same, towards the bottom-right - for a pile that has room on that side instead.</summary>
    [Transaction(TransactionMode.Manual)]
    public class SpreadTagsBottomRight : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            return TagSpreader.Run(commandData, new SpreadOptions { Direction = SpreadDirection.BottomRight }, ref message);
        }
    }

    /// <summary>The Revit side of the spread: reads the tags, asks <see cref="TagLayout" /> where they go, moves them.</summary>
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

            // The selected tags, else every tag in the active view.
            List<IndependentTag> tags = uidoc.Selection.GetElementIds()
                .Select(id => doc.GetElement(id))
                .OfType<IndependentTag>()
                .Where(t => t.OwnerViewId == view.Id)
                .ToList();
            string scope = "selected";
            if (tags.Count == 0)
            {
                scope = "in this view";
                tags = new FilteredElementCollector(doc, view.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().ToList();
            }

            if (tags.Count == 0)
            {
                message = "No tags found - select some, or open a view that has tags.";
                return Result.Cancelled;
            }

            XYZ right = view.RightDirection;
            XYZ up = view.UpDirection;
            int skippedPinned = 0;
            var byId = new Dictionary<int, IndependentTag>();
            var items = new List<TagItem>();
            foreach (IndependentTag tag in tags)
            {
                if (tag.Pinned)
                {
                    skippedPinned++;
                    continue;
                }

                XYZ head = tag.TagHeadPosition;
                XYZ host = HostPoint(doc, tag) ?? head;
                int id = tag.Id.IntegerValueCompat();
                byId[id] = tag;
                items.Add(new TagItem(id, head.DotProduct(right), head.DotProduct(up), host.DotProduct(right), host.DotProduct(up)));
            }

            List<TagPlacement> placements = TagLayout.Compute(items, options, Math.Max(1, view.Scale));

            int moved = 0;
            var failed = new List<string>();
            using (Transaction transaction = new Transaction(doc, "Spread tags " + options.Direction))
            {
                transaction.Start();
                foreach (TagPlacement placement in placements)
                {
                    IndependentTag tag = byId[placement.Id];
                    TagItem item = items.First(i => i.Id == placement.Id);
                    double dRight = placement.Right - item.HeadRight;
                    double dUp = placement.Up - item.HeadUp;
                    if (Math.Abs(dRight) < 1e-9 && Math.Abs(dUp) < 1e-9) continue;

                    try
                    {
                        // Move along the view's own axes only, so the tag keeps its depth in the view.
                        XYZ target = tag.TagHeadPosition + right * dRight + up * dUp;
                        if (!tag.HasLeader) tag.HasLeader = true; // the tag has to point back at what it tags
                        tag.TagHeadPosition = target;
                        moved++;
                    }
                    catch (Exception ex)
                    {
                        failed.Add(tag.Id.IntegerValueCompat() + ": " + ex.Message);
                    }
                }

                transaction.Commit();
            }

            string report = $"Spread {moved} of {tags.Count} tag(s) {scope} to the {options.Direction}.";
            if (skippedPinned > 0) report += $"\n{skippedPinned} pinned tag(s) left where they are.";
            if (failed.Count > 0) report += $"\n{failed.Count} could not be moved:\n" + string.Join("\n", failed.Take(10));
            TaskDialog.Show("Spread Tags", report);
            return Result.Succeeded;
        }

        /// <summary>The centre of the first element the tag tags, or null (linked / no geometry).</summary>
        private static XYZ? HostPoint(Document doc, IndependentTag tag)
        {
            foreach (ElementId id in tag.GetTaggedLocalElementIds())
            {
                BoundingBoxXYZ? box = doc.GetElement(id)?.get_BoundingBox(null);
                if (box != null) return (box.Min + box.Max) * 0.5;
            }

            return null;
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

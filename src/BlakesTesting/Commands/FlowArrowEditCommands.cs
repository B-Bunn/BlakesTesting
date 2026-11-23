#nullable enable
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BlakesTesting.Flow;
using BlakesTesting.Risers;

namespace BlakesTesting.Commands
{
    /// <summary>Finds the arrows to work on: the selected ones, or every one in the active view. Null when there are none.</summary>
    internal static class FlowArrowTargets
    {
        public static bool Find(UIDocument uidoc, string title, out View view, out List<Autodesk.Revit.DB.Group> groups, out List<ElementId> loose, out bool onlySelected)
        {
            Document doc = uidoc.Document;
            view = doc.ActiveView;
            (groups, loose) = FlowArrowDrawing.Find(doc, view);
            onlySelected = FlowArrowDrawing.Restrict(uidoc.Selection.GetElementIds(), ref groups, ref loose);
            if (groups.Count + loose.Count > 0) return true;
            TaskDialog.Show(title, "There are no flow arrows in " + view.Name + ".");
            return false;
        }

        public static string Where(View view, bool onlySelected) => onlySelected ? "from the selection" : "in " + view.Name;
    }

    /// <summary>Clear Flow Arrows: takes the arrows out of the active view (or only the selected ones).</summary>
    [Transaction(TransactionMode.Manual)]
    public class ClearFlowArrows : IExternalCommand
    {
        private const string Title = "Clear Flow Arrows";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument? uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Open a model first.";
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            if (!FlowArrowTargets.Find(uidoc, Title, out View view, out List<Autodesk.Revit.DB.Group> groups, out List<ElementId> loose, out bool onlySelected))
                return Result.Cancelled;

            TaskDialog confirm = new TaskDialog(Title)
            {
                MainInstruction = "Remove " + (groups.Count + loose.Count) + " flow arrow(s) " + FlowArrowTargets.Where(view, onlySelected) + "?",
                MainContent = view is View3D ? "In a 3D view they are model elements, so they go from every view." : "",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.Yes,
            };
            if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            using (Transaction t = new Transaction(doc, Title))
            {
                t.Start();
                List<ElementId> types = new List<ElementId>();
                List<ElementId> groupIds = new List<ElementId>();
                foreach (Autodesk.Revit.DB.Group g in groups)
                {
                    groupIds.Add(g.Id);
                    if (!types.Contains(g.GroupType.Id)) types.Add(g.GroupType.Id);
                }

                try
                {
                    if (groupIds.Count > 0) doc.Delete(groupIds);
                    List<ElementId> stillThere = loose.FindAll(id => doc.GetElement(id) != null);
                    if (stillThere.Count > 0) doc.Delete(stillThere);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    t.RollBack();
                    message = "Revit would not remove them: " + ex.Message;
                    return Result.Failed;
                }

                // A group type with no group left is clutter in the project: take it out too.
                foreach (ElementId typeId in types) FlowArrowDrawing.DeleteTypeIfUnused(doc, typeId);
                t.Commit();
            }

            return Result.Succeeded;
        }
    }

    /// <summary>
    ///     Recolor Flow Arrows: changes the color of arrows already placed (and their text) in the active view, or only the
    ///     selected ones. Works on arrows made before the color setting existed too.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class RecolorFlowArrows : IExternalCommand
    {
        private const string Title = "Recolor Flow Arrows";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument? uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Open a model first.";
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            if (!FlowArrowTargets.Find(uidoc, Title, out View view, out List<Autodesk.Revit.DB.Group> groups, out List<ElementId> loose, out bool onlySelected))
                return Result.Cancelled;

            FlowArrowSettings settings = FlowArrowSettings.Load();
            TaskDialog ask = new TaskDialog(Title)
            {
                MainInstruction = "Recolor " + (groups.Count + loose.Count) + " flow arrow(s) " + FlowArrowTargets.Where(view, onlySelected) + "?",
                MainContent = view is View3D ? "In a 3D view they are model elements, so the new color shows in every view." : "",
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Pick a color...",
                settings.Color.HasValue ? "Starts from " + settings.Color.Value.ToHex() + ", the color new arrows are placed in." : "");
            ask.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Back to the line style's own color",
                "Black in most templates (the \"" + settings.LineStyle + "\" line style).");
            TaskDialogResult choice = ask.Show();

            RgbColor? color;
            if (choice == TaskDialogResult.CommandLink1)
            {
                using (System.Windows.Forms.ColorDialog dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true })
                {
                    RgbColor start = settings.Color ?? new RgbColor(255, 0, 0);
                    dlg.Color = System.Drawing.Color.FromArgb(start.R, start.G, start.B);
                    if (dlg.ShowDialog(new RevitWindow()) != System.Windows.Forms.DialogResult.OK) return Result.Cancelled;
                    color = new RgbColor(dlg.Color.R, dlg.Color.G, dlg.Color.B);
                }
            }
            else if (choice == TaskDialogResult.CommandLink2)
            {
                color = null;
            }
            else
            {
                return Result.Cancelled;
            }

            int ungrouped;
            using (Transaction t = new Transaction(doc, Title))
            {
                t.Start();
                try
                {
                    FlowArrowDrawing.Recolor(doc, groups, loose, color, settings.LineStyle, out ungrouped);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    t.RollBack();
                    message = "Revit would not recolor them: " + ex.Message;
                    return Result.Failed;
                }

                t.Commit();
            }

            if (ungrouped > 0)
                TaskDialog.Show(Title, ungrouped + " arrow(s) were recolored but Revit would not group them again, so they are loose lines and text now. Clear Flow Arrows still finds them.");
            return Result.Succeeded;
        }
    }

    /// <summary>
    ///     Flip Flow Arrows: points the arrows already placed the other way (the selected ones, or every one in the view).
    ///     The text stays where it is.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FlipFlowArrows : IExternalCommand
    {
        private const string Title = "Flip Flow Arrows";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument? uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Open a model first.";
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            if (!FlowArrowTargets.Find(uidoc, Title, out View view, out List<Autodesk.Revit.DB.Group> groups, out List<ElementId> loose, out bool onlySelected))
                return Result.Cancelled;

            int flipped, skipped, ungrouped;
            using (Transaction t = new Transaction(doc, Title))
            {
                t.Start();
                try
                {
                    flipped = FlowArrowEditing.Flip(doc, groups, out skipped, out ungrouped);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    t.RollBack();
                    message = "Revit would not turn them round: " + ex.Message;
                    return Result.Failed;
                }

                if (flipped == 0) t.RollBack();
                else t.Commit();
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Turned " + flipped + " arrow(s) round " + FlowArrowTargets.Where(view, onlySelected) + ".");
            if (loose.Count > 0) sb.AppendLine(loose.Count + " loose piece(s) (arrows that were not grouped) were left alone: Revit cannot tell which lines belong to which arrow.");
            if (skipped > 0) sb.AppendLine(skipped + " arrow(s) were skipped: placed in a 3D view before the build that told an arrow's lines from its text, so flipping them would turn the text too. Place those again.");
            if (ungrouped > 0) sb.AppendLine(ungrouped + " arrow(s) could not be grouped again and are loose lines now. Clear Flow Arrows still finds them.");
            if (flipped == 0 || sb.Length > 60) TaskDialog.Show(Title, sb.ToString().TrimEnd());
            return flipped == 0 ? Result.Cancelled : Result.Succeeded;
        }
    }

    /// <summary>
    ///     Edit Flow Arrow Text: gives the arrows already placed (the selected ones, or every one in the view) new text, or
    ///     takes their text off. An arrow with no text to begin with is skipped, since nothing says where its text would go.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class EditFlowArrowText : IExternalCommand
    {
        private const string Title = "Edit Flow Arrow Text";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument? uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Open a model first.";
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            if (!FlowArrowTargets.Find(uidoc, Title, out View view, out List<Autodesk.Revit.DB.Group> groups, out List<ElementId> loose, out bool onlySelected))
                return Result.Cancelled;

            FlowArrowSettings settings = FlowArrowSettings.Load();
            if (!FlowArrowTextForm.Ask(settings, groups.Count, out string text)) return Result.Cancelled;

            int changed, skipped, ungrouped;
            using (Transaction t = new Transaction(doc, Title))
            {
                t.Start();
                try
                {
                    changed = FlowArrowEditing.SetText(doc, view, groups, text, settings, out skipped, out ungrouped);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    t.RollBack();
                    message = "Revit would not change the text: " + ex.Message;
                    return Result.Failed;
                }

                if (changed == 0) t.RollBack();
                else t.Commit();
            }

            if (text.Length > 0)
            {
                settings.Remember(text);
                settings.Save();
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Changed the text of " + changed + " arrow(s) " + FlowArrowTargets.Where(view, onlySelected) + (text.Length > 0 ? " to \"" + text + "\"." : ": it is off now."));
            if (loose.Count > 0) sb.AppendLine(loose.Count + " loose piece(s) (arrows that were not grouped) were left alone.");
            if (skipped > 0) sb.AppendLine(skipped + " arrow(s) were skipped: they have no text to change, or were placed in a 3D view before the build that told an arrow's lines from its text.");
            if (ungrouped > 0) sb.AppendLine(ungrouped + " arrow(s) could not be grouped again and are loose lines now. Clear Flow Arrows still finds them.");
            if (changed == 0 || skipped > 0 || ungrouped > 0) TaskDialog.Show(Title, sb.ToString().TrimEnd());
            return changed == 0 ? Result.Cancelled : Result.Succeeded;
        }
    }
}

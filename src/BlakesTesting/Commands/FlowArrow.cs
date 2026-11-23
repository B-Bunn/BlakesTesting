#nullable enable
using System;
using System.Collections.Generic;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BlakesTesting.Flow;
using BlakesTesting.Risers;

namespace BlakesTesting.Commands
{
    /// <summary>
    ///     Flow Arrow: marks the direction of flow on a pipe with an arrow and a label you choose ("FLOW", "STORM"...), in the
    ///     manner of a spot slope. Works in a plan, section, elevation or detail view (view-specific detail lines and a text
    ///     note, sized on paper from the view's scale) and in a 3D view (model lines, the text drawn from lines, because
    ///     Revit's API cannot make text in a 3D view).
    ///     <para>
    ///         Two clicks per arrow: a point on the pipe (the arrow goes on the side of the pipe you click), then a point
    ///         toward the way it flows. A second click across the pipe puts it on that side instead. A second click on the same spot (or just across the pipe) lets the pipe's slope
    ///         decide, pointing toward its lower end. The command repeats until Esc. Each arrow is grouped (a detail group in
    ///         a 2D view, a model group in 3D) so one click moves or deletes it, and one Undo takes it back.
    ///     </para>
    ///     <para>Hold Shift as the command starts to skip the window and use the last settings.</para>
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FlowArrow : IExternalCommand
    {
        private const string Title = "Flow Arrow";

        /// <summary>Under this many feet of fall, a pipe is level and has no downhill to follow.</summary>
        private const double LevelTolerance = 1e-4;

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument? uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Open a model first.";
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            View view = doc.ActiveView;
            string? viewProblem = FlowArrowFrame.ViewProblem(view);
            if (viewProblem != null)
            {
                TaskDialog.Show(Title, viewProblem);
                return Result.Cancelled;
            }

            FlowArrowSettings settings = FlowArrowSettings.Load();
            bool skipWindow = (System.Windows.Forms.Control.ModifierKeys & System.Windows.Forms.Keys.Shift) == System.Windows.Forms.Keys.Shift;
            if (!skipWindow)
            {
                if (!FlowArrowForm.Edit(settings, out settings)) return Result.Cancelled;
                settings.Remember(settings.Text);
                settings.Save();
            }

            bool model = view is View3D;
            int placed = 0, byFall = 0, level = 0, failed = 0;
            string? firstProblem = null;

            while (true)
            {
                Reference pipeRef;
                try
                {
                    pipeRef = uidoc.Selection.PickObject(ObjectType.PointOnElement, new PipeFilter(),
                        Title + (settings.Text.Length > 0 ? " \"" + settings.Text + "\"" : "") + ": click a point on the side of a pipe you want the arrow on (Esc to finish)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }

                Pipe? pipe = doc.GetElement(pipeRef.ElementId) as Pipe;
                LocationCurve? location = pipe?.Location as LocationCurve;
                if (pipe == null || location == null) continue;

                XYZ? click2;
                try
                {
                    click2 = model
                        ? uidoc.Selection.PickObject(ObjectType.PointOnElement, "Click toward the way it flows. Click across the pipe to put the text on that side. Click the same spot to follow the pipe's slope (Esc cancels this arrow)").GlobalPoint
                        : uidoc.Selection.PickPoint(ObjectSnapTypes.None, "Click toward the way it flows. Click across the pipe to put the text on that side. Click the same spot to follow the pipe's slope (Esc cancels this arrow)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    continue;
                }

                XYZ click1 = pipeRef.GlobalPoint;
                XYZ a = location.Curve.GetEndPoint(0), b = location.Curve.GetEndPoint(1);
                double fall = a.Z - b.Z;
                int auto = fall >= -LevelTolerance ? 1 : -1;

                FlowArrowFrame frame = FlowArrowFrame.Create(view, click1);
                FlowArrowInput input = new FlowArrowInput(frame.To2D(a), frame.To2D(b), HalfWidth(pipe), frame.To2D(click1),
                    click2 == null ? (V2?)null : frame.To2D(click2), frame.Scale, auto, settings);
                FlowArrowShape? shape = FlowArrowLayout.Compute(input, out string? problem);
                if (shape == null)
                {
                    TaskDialog.Show(Title, problem ?? "That arrow could not be laid out.");
                    continue;
                }

                try
                {
                    using (Transaction t = new Transaction(doc, Title))
                    {
                        t.Start();
                        try
                        {
                            FlowArrowDrawing.Draw(doc, frame, shape, settings);
                        }
                        catch (InvalidOperationException ex)
                        {
                            t.RollBack();
                            failed++;
                            firstProblem ??= ex.Message;
                            continue;
                        }

                        t.Commit();
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    failed++;
                    firstProblem ??= "Revit would not draw the arrow: " + ex.Message;
                    continue;
                }

                placed++;
                if (shape.Automatic)
                {
                    if (Math.Abs(fall) <= LevelTolerance) level++;
                    else byFall++;
                }
            }

            if (placed == 0 && failed == 0) return Result.Cancelled;
            TaskDialog.Show(Title, Report(settings, model, placed, byFall, level, failed, firstProblem));
            return failed > 0 && placed == 0 ? Result.Failed : Result.Succeeded;
        }

        private static string Report(FlowArrowSettings settings, bool model, int placed, int byFall, int level, int failed, string? problem)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Placed " + placed + " arrow(s)" + (settings.Text.Length > 0 ? " saying \"" + settings.Text + "\"" : " with no text") + ".");
            if (byFall > 0) sb.AppendLine(byFall + " followed the pipe's slope (pointing toward its lower end).");
            if (level > 0) sb.AppendLine(level + " were on a level pipe, which has no downhill, so they point from the pipe's first end to its second (the way it was drawn). Click toward the flow to choose.");
            if (failed > 0) sb.AppendLine(failed + " could not be drawn" + (problem != null ? ": " + problem : "") + ".");
            if (model)
            {
                sb.AppendLine();
                sb.AppendLine("This is a 3D view, so each arrow is model lines in a plane facing the camera, and its text is drawn from lines too (capitals only).");
                sb.AppendLine("Model lines show in other views as well. They are Comments = \"" + FlowArrowDrawing.Mark + "\", so a view filter can hide them there.");
            }

            sb.AppendLine();
            sb.AppendLine("Each arrow is one group, so one click selects it. Undo takes back the last one; Clear Flow Arrows removes them from a view.");
            return sb.ToString().TrimEnd();
        }

        /// <summary>Half the pipe's outside diameter, feet.</summary>
        private static double HalfWidth(Pipe pipe)
        {
            double od = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER)?.AsDouble() ?? 0;
            if (od <= 0) od = pipe.Diameter;
            return Math.Max(od / 2, 0.01);
        }

        private sealed class PipeFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Pipe;

            public bool AllowReference(Reference reference, XYZ position) => true;
        }
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
            View view = doc.ActiveView;
            (List<Autodesk.Revit.DB.Group> groups, List<ElementId> loose) = FlowArrowDrawing.Find(doc, view);

            // With some arrows selected, only those go.
            bool onlySelected = FlowArrowDrawing.Restrict(uidoc.Selection.GetElementIds(), ref groups, ref loose);

            if (groups.Count + loose.Count == 0)
            {
                TaskDialog.Show(Title, "There are no flow arrows in " + view.Name + ".");
                return Result.Cancelled;
            }

            TaskDialog confirm = new TaskDialog(Title)
            {
                MainInstruction = "Remove " + (groups.Count + loose.Count) + " flow arrow(s) " + (onlySelected ? "from the selection" : "from " + view.Name) + "?",
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
                foreach (ElementId typeId in types)
                {
                    try
                    {
                        if (doc.GetElement(typeId) is GroupType type && type.Groups.Size == 0) doc.Delete(typeId);
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        // Only tidying.
                    }
                }

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
            View view = doc.ActiveView;
            (List<Autodesk.Revit.DB.Group> groups, List<ElementId> loose) = FlowArrowDrawing.Find(doc, view);
            bool onlySelected = FlowArrowDrawing.Restrict(uidoc.Selection.GetElementIds(), ref groups, ref loose);
            int count = groups.Count + loose.Count;
            if (count == 0)
            {
                TaskDialog.Show(Title, "There are no flow arrows in " + view.Name + ".");
                return Result.Cancelled;
            }

            FlowArrowSettings settings = FlowArrowSettings.Load();
            TaskDialog ask = new TaskDialog(Title)
            {
                MainInstruction = "Recolor " + count + " flow arrow(s) " + (onlySelected ? "from the selection" : "in " + view.Name) + "?",
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
}

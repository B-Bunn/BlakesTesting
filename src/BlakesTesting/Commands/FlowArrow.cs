#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BlakesTesting.Flow;

namespace BlakesTesting.Commands
{
    /// <summary>
    ///     Flow Arrow: marks the direction of flow on a pipe with an arrow and a label you choose ("FLOW", "STORM"...), in the
    ///     manner of a spot slope. Works in a plan, section, elevation or detail view (view-specific detail lines and a text
    ///     note, sized on paper from the view's scale) and in a 3D view (model lines, the text drawn from lines, because
    ///     Revit's API cannot make text in a 3D view).
    ///     <para>
    ///         Two clicks per arrow: a point on the pipe (the arrow goes on the side of the pipe you click), then a point
    ///         toward the way it flows. A second click across the pipe puts it on that side instead. A second click on the
    ///         same spot (or just across the pipe) lets the pipe's slope decide, pointing toward its lower end. The command
    ///         repeats until Esc. Each arrow is grouped (a detail group in a 2D view, a model group in 3D) so one click moves or
    ///         deletes it, and one Undo takes it back.
    ///     </para>
    ///     <para>Hold Shift as the command starts to skip the window and use the last settings.</para>
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FlowArrow : IExternalCommand
    {
        private const string Title = "Flow Arrow";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!FlowArrowCommon.Begin(commandData, Title, FlowArrowFormMode.Single, ref message, out UIDocument uidoc, out View view, out FlowArrowSettings settings))
                return Result.Cancelled;

            Document doc = uidoc.Document;
            bool model = view is View3D;
            int placed = 0, byFall = 0, level = 0, failed = 0;
            string? firstProblem = null;

            while (true)
            {
                Reference pipeRef;
                try
                {
                    pipeRef = uidoc.Selection.PickObject(ObjectType.PointOnElement, new FlowArrowCommon.PipeFilter(),
                        Title + (settings.Text.Length > 0 ? " \"" + settings.Text + "\"" : "") + ": click a point on the side of a pipe you want the arrow on (Esc to finish)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }

                FlowPipe? pipe = FlowArrowCommon.Read(doc.GetElement(pipeRef.ElementId));
                if (pipe == null) continue;

                XYZ? click2 = FlowArrowCommon.PickSecond(uidoc, model, FlowArrowCommon.SecondClickPrompt);
                if (click2 == null) continue;

                XYZ click1 = pipeRef.GlobalPoint;
                FlowArrowFrame frame = FlowArrowFrame.Create(view, click1);
                FlowArrowShape? shape = FlowArrowLayout.Compute(FlowArrowCommon.Input(frame, pipe, click1, click2, settings), out string? problem);
                if (shape == null)
                {
                    TaskDialog.Show(Title, problem ?? "That arrow could not be laid out.");
                    continue;
                }

                if (!TryDraw(doc, frame, new List<FlowArrowShape> { shape }, settings, pipe.Pipe.Id, ref firstProblem))
                {
                    failed++;
                    continue;
                }

                placed++;
                if (shape.Automatic)
                {
                    if (pipe.Level) level++;
                    else byFall++;
                }
            }

            if (placed == 0 && failed == 0) return Result.Cancelled;
            TaskDialog.Show(Title, Report(settings, model, placed, byFall, level, failed, firstProblem));
            return failed > 0 && placed == 0 ? Result.Failed : Result.Succeeded;
        }

        /// <summary>Draws in its own transaction (one Undo); false, with the reason kept, when Revit would not.</summary>
        internal static bool TryDraw(Document doc, FlowArrowFrame frame, IList<FlowArrowShape> shapes, FlowArrowSettings settings, ElementId pipeId, ref string? firstProblem)
        {
            try
            {
                using (Transaction t = new Transaction(doc, "Flow Arrow"))
                {
                    t.Start();
                    try
                    {
                        FlowArrowDrawing.DrawShapes(doc, frame, shapes, settings, pipeId);
                    }
                    catch (InvalidOperationException ex)
                    {
                        t.RollBack();
                        firstProblem ??= ex.Message;
                        return false;
                    }

                    t.Commit();
                    return true;
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                firstProblem ??= "Revit would not draw the arrow: " + ex.Message;
                return false;
            }
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
                sb.AppendLine(FlowArrowCommon.SizeNote(true));
            }

            sb.AppendLine();
            sb.AppendLine("Each arrow is one group, so one click selects it. Undo takes back the last one. Flip, Edit Text, Recolor and Clear Flow Arrows change the ones already placed.");
            return sb.ToString().TrimEnd();
        }
    }

    /// <summary>
    ///     Flow Run: the arrow repeated along a stretch of one pipe. Click where the run starts and where it ends; arrows go
    ///     in the middle of equal cells about the spacing apart, pointing toward the second click (or away, or downhill: the
    ///     window says), the text on the one nearest the middle. One group per run, one Undo.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FlowArrowRun : IExternalCommand
    {
        private const string Title = "Flow Run";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!FlowArrowCommon.Begin(commandData, Title, FlowArrowFormMode.Run, ref message, out UIDocument uidoc, out View view, out FlowArrowSettings settings))
                return Result.Cancelled;

            Document doc = uidoc.Document;
            bool model = view is View3D;
            int runs = 0, arrows = 0, failed = 0;
            string? firstProblem = null;

            while (true)
            {
                Reference pipeRef;
                try
                {
                    pipeRef = uidoc.Selection.PickObject(ObjectType.PointOnElement, new FlowArrowCommon.PipeFilter(),
                        Title + ": click where the run starts, on the side of the pipe you want the arrows on (Esc to finish)");
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    break;
                }

                FlowPipe? pipe = FlowArrowCommon.Read(doc.GetElement(pipeRef.ElementId));
                if (pipe == null) continue;

                XYZ? click2 = FlowArrowCommon.PickSecond(uidoc, model,
                    "Click where the run ends, on the same pipe. Arrows repeat about every " + settings.RunSpacingFeet.ToString("0.##") + " ft (Esc cancels this run)");
                if (click2 == null) continue;

                XYZ click1 = pipeRef.GlobalPoint;
                FlowArrowFrame frame = FlowArrowFrame.Create(view, click1);
                List<FlowArrowShape>? shapes = FlowArrowLayout.ComputeRun(FlowArrowCommon.Input(frame, pipe, click1, click2, settings), out string? problem);
                if (shapes == null)
                {
                    TaskDialog.Show(Title, problem ?? "That run could not be laid out.");
                    continue;
                }

                if (!FlowArrow.TryDraw(doc, frame, shapes, settings, pipe.Pipe.Id, ref firstProblem))
                {
                    failed++;
                    continue;
                }

                runs++;
                arrows += shapes.Count;
            }

            if (runs == 0 && failed == 0) return Result.Cancelled;
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Placed " + runs + " run(s), " + arrows + " arrow(s) in all" + (settings.Text.Length > 0 ? ", saying \"" + settings.Text + "\"" : "") + ".");
            if (failed > 0) sb.AppendLine(failed + " could not be drawn" + (firstProblem != null ? ": " + firstProblem : "") + ".");
            if (model) sb.AppendLine("\n" + FlowArrowCommon.SizeNote(true));
            sb.AppendLine("\nEach run is one group. Undo takes back the last one; Clear Flow Arrows removes them.");
            TaskDialog.Show(Title, sb.ToString().TrimEnd());
            return failed > 0 && runs == 0 ? Result.Failed : Result.Succeeded;
        }
    }

    /// <summary>
    ///     Flow Arrow on Selected Pipes: one arrow on the middle of each pipe, pointing downhill by its slope, with no
    ///     clicking. Uses the selected pipes or, with none selected, asks you to pick them. Pipes too short for the arrow and
    ///     text, pointing at the viewer, or (when the window's box is ticked) already carrying an arrow in this view, are
    ///     left alone and counted. One Undo for the lot.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class FlowArrowOnSelectedPipes : IExternalCommand
    {
        private const string Title = "Flow Arrow on Selected Pipes";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            if (!FlowArrowCommon.Begin(commandData, Title, FlowArrowFormMode.Selected, ref message, out UIDocument uidoc, out View view, out FlowArrowSettings settings))
                return Result.Cancelled;

            Document doc = uidoc.Document;
            List<FlowPipe> pipes = uidoc.Selection.GetElementIds()
                .Select(id => FlowArrowCommon.Read(doc.GetElement(id)))
                .Where(p => p != null).Select(p => p!).ToList();
            if (pipes.Count == 0)
            {
                try
                {
                    pipes = uidoc.Selection.PickObjects(ObjectType.Element, new FlowArrowCommon.PipeFilter(), "Select the pipes to mark, then click Finish")
                        .Select(r => FlowArrowCommon.Read(doc.GetElement(r.ElementId)))
                        .Where(p => p != null).Select(p => p!).ToList();
                }
                catch (Autodesk.Revit.Exceptions.OperationCanceledException)
                {
                    return Result.Cancelled;
                }
            }

            if (pipes.Count == 0) return Result.Cancelled;

            HashSet<string> marked = settings.SkipMarked
                ? FlowArrowDrawing.PipeIdsWithArrows(FlowArrowDrawing.Find(doc, view).Groups)
                : new HashSet<string>();

            bool model = view is View3D;
            int placed = 0, byFall = 0, level = 0, endOn = 0, tooShort = 0, already = 0, failed = 0;
            string? firstProblem = null;

            using (Transaction t = new Transaction(doc, Title))
            {
                t.Start();
                foreach (FlowPipe pipe in pipes)
                {
                    XYZ mid = ((LocationCurve)pipe.Pipe.Location).Curve.Evaluate(0.5, true);
                    FlowArrowFrame frame = FlowArrowFrame.Create(view, mid);
                    FlowArrowShape? shape = FlowArrowLayout.Compute(FlowArrowCommon.Input(frame, pipe, mid, null, settings), out _);
                    if (shape == null)
                    {
                        endOn++;
                        continue;
                    }

                    if ((frame.To2D(pipe.B) - frame.To2D(pipe.A)).Length < FlowArrowLayout.MinimumPipeLength(settings, frame.Scale))
                    {
                        tooShort++;
                        continue;
                    }

                    if (marked.Contains(pipe.Pipe.Id.ToString()))
                    {
                        already++;
                        continue;
                    }

                    using (SubTransaction sub = new SubTransaction(doc))
                    {
                        sub.Start();
                        try
                        {
                            FlowArrowDrawing.Draw(doc, frame, shape, settings, pipe.Pipe.Id);
                            sub.Commit();
                        }
                        catch (Exception ex) when (ex is InvalidOperationException || ex is Autodesk.Revit.Exceptions.ApplicationException)
                        {
                            sub.RollBack();
                            failed++;
                            firstProblem ??= ex.Message;
                            continue;
                        }
                    }

                    placed++;
                    if (pipe.Level) level++;
                    else byFall++;
                }

                if (placed == 0)
                    t.RollBack();
                else
                    t.Commit();
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Placed " + placed + " arrow(s) on " + pipes.Count + " pipe(s)" + (settings.Text.Length > 0 ? ", saying \"" + settings.Text + "\"" : "") + ".");
            if (byFall > 0) sb.AppendLine(byFall + " point downhill by the pipe's slope.");
            if (level > 0) sb.AppendLine(level + " are on level pipes, which have no downhill, so they point the way the pipe was drawn: check them, and Flip Flow Arrows turns any that are wrong.");
            if (already > 0) sb.AppendLine(already + " pipe(s) already have an arrow in this view and were left alone.");
            if (tooShort > 0) sb.AppendLine(tooShort + " pipe(s) were too short in this view for the arrow and text.");
            if (endOn > 0) sb.AppendLine(endOn + " pipe(s) point at the viewer in this view (a riser in a plan) and have no way along them.");
            if (failed > 0) sb.AppendLine(failed + " could not be drawn" + (firstProblem != null ? ": " + firstProblem : "") + ".");
            if (model) sb.AppendLine("\n" + FlowArrowCommon.SizeNote(true));
            sb.AppendLine("\nOne Undo takes back all of them.");
            TaskDialog.Show(Title, sb.ToString().TrimEnd());
            return placed == 0 ? Result.Cancelled : Result.Succeeded;
        }
    }
}

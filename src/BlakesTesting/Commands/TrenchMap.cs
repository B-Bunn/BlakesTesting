#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using BlakesTesting.Trench;

namespace BlakesTesting.Commands
{
    /// <summary>
    ///     Trench Map: a color-coded plan of the trenches under the pipes, with a legend off to the side. Each trench is a
    ///     filled rectangle along the pipe, as wide as BIM Tools' Calculate Spoils digs it at that depth and colored by
    ///     width and depth together (one color per width and depth-band combination), and the legend beside the trench
    ///     lists each color with its width, depth range and footage. The depth is the spoils depth (top of the trench to
    ///     the bottom of the trench) or the bottom of the pipe's, whichever the settings say.
    ///     <para>
    ///         Uses the selected pipes or, with nothing selected, every pipe in the active plan view. The first run (or
    ///         one with a floor the model does not have) opens the settings window to ask. Everything is drawn in the
    ///         active view alone, as filled regions and text notes, in one transaction: one Undo. A run clears the last
    ///         map from the view first; <see cref="ClearTrenchMap"/> clears it without drawing another.
    ///     </para>
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class TrenchMap : IExternalCommand
    {
        private const string Title = "Trench Map";

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
            string? viewProblem = TrenchDrawing.ViewProblem(view);
            if (viewProblem != null)
            {
                TaskDialog.Show(Title, viewProblem);
                return Result.Cancelled;
            }

            TrenchMapSettings settings = TrenchMapSettings.Load();
            string? missing = TrenchModel.WhatIsMissing(doc, settings);
            if (missing != null)
            {
                if (!TrenchMapSettingsForm.Edit(settings, TrenchModel.LevelNames(doc), missing, out settings, out string? saveProblem)) return Result.Cancelled;
                if (saveProblem != null) TaskDialog.Show(Title, saveProblem);

                missing = TrenchModel.WhatIsMissing(doc, settings);
                if (missing != null)
                {
                    TaskDialog.Show(Title, missing);
                    return Result.Cancelled;
                }
            }

            TrenchScope? scope = TrenchModel.PlanTrench(uidoc, settings, out string? notRun);
            if (scope == null)
            {
                TaskDialog.Show(Title, notRun ?? "There is nothing to map.");
                return Result.Cancelled;
            }

            if (scope.Plan.Pieces.Count == 0)
            {
                TaskDialog.Show(Title, "No trench to draw under " + scope.Over + ". " + Left(scope));
                return Result.Cancelled;
            }

            TrenchDrawResult drawn;
            try
            {
                using (Transaction t = new Transaction(doc, Title))
                {
                    t.Start();
                    try
                    {
                        drawn = TrenchDrawing.Draw(doc, view, settings, scope.Plan, scope.Plan.LengthNoFloor);
                    }
                    catch (InvalidOperationException ex)
                    {
                        t.RollBack();
                        message = ex.Message;
                        return Result.Failed;
                    }

                    t.Commit();
                }
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                message = "Revit would not draw the map: " + ex.Message;
                return Result.Failed;
            }

            TaskDialog.Show(Title, Report(settings, scope, drawn, view));
            return Result.Succeeded;
        }

        private static string Report(TrenchMapSettings settings, TrenchScope scope, TrenchDrawResult drawn, View view)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Drew " + drawn.Regions + " trench piece(s) over " + scope.Over + " in " + view.Name + ": "
                          + TrenchText.Feet(scope.Plan.TrenchLength) + " of trench, deepest " + TrenchText.Feet(scope.Plan.DeepestDepth) + ".");
            sb.AppendLine("Colored by " + (settings.ColorBy == TrenchColorBy.BottomOfPipe ? "width x depth to the bottom of the pipe" : "width x spoils depth") + ".");
            sb.AppendLine("Floor: " + scope.Floor + ", offset " + TrenchText.Feet(settings.FloorOffset ?? 0)
                          + (settings.BelowPipe > 0 ? ", " + TrenchText.Feet(settings.BelowPipe) + " below the pipe" : "") + ".");
            sb.AppendLine();
            foreach (TrenchLegendRow row in drawn.Legend) sb.AppendLine("  " + row.Text);
            sb.AppendLine();
            sb.AppendLine("The legend is to the right of the trench; if it is out of sight, widen the view's crop region.");

            string left = Left(scope);
            if (left.Length > 0) sb.AppendLine(left);
            if (drawn.Skipped > 0)
                sb.AppendLine(drawn.Skipped + " piece(s) were too small or refused by Revit and are not drawn" + (drawn.FirstProblem != null ? " (" + drawn.FirstProblem + ")" : "") + ".");
            if (drawn.Cleared > 0) sb.AppendLine("The last map in this view (" + drawn.Cleared + " element(s)) was replaced.");
            if (settings.Transparency > 0 && !drawn.TransparencyApplied)
                sb.AppendLine("This Revit would not make the colors see-through, so they are solid: Visibility/Graphics > Filled regions may hide pipes behind them.");
            sb.AppendLine("Undo takes the whole map back out; Clear Trench Map removes it from a view.");
            return sb.ToString().TrimEnd();
        }

        /// <summary>The runs, or lengths of run, that are not on the map and why.</summary>
        private static string Left(TrenchScope scope)
        {
            List<string> parts = new List<string>();
            if (scope.SetAside > 0) parts.Add(scope.SetAside + " pipe(s) steeper than 45 degrees (risers) have no trench");
            if (scope.Plan.LengthNoFloor > 0.01) parts.Add(TrenchText.Feet(scope.Plan.LengthNoFloor) + " of run has no floor over it");
            if (scope.Plan.LengthAboveTop > 0.01) parts.Add(TrenchText.Feet(scope.Plan.LengthAboveTop) + " of run is above the top of the trench");
            if (scope.FloorsRefused > 0) parts.Add(scope.FloorsRefused + " floor reading(s) Revit would not make were passed over");
            return parts.Count == 0 ? "" : "Left out: " + string.Join("; ", parts) + ".";
        }
    }

    /// <summary>Opens the Trench Map settings: what the depth is measured to, the floor, the width rule and the depths the colors change at. Saved per user in %AppData%\BlakesTesting\TrenchMap.ini.</summary>
    [Transaction(TransactionMode.Manual)]
    public class TrenchMapOptions : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document? doc = commandData.Application.ActiveUIDocument?.Document;
            IEnumerable<string> levels = doc == null ? Enumerable.Empty<string>() : TrenchModel.LevelNames(doc);
            if (!TrenchMapSettingsForm.Edit(TrenchMapSettings.Load(), levels, null, out _, out string? saveProblem)) return Result.Cancelled;
            if (saveProblem != null)
            {
                message = saveProblem;
                return Result.Failed;
            }

            return Result.Succeeded;
        }
    }

    /// <summary>Takes the trench map (its colored regions and its legend) out of the active view.</summary>
    [Transaction(TransactionMode.Manual)]
    public class ClearTrenchMap : IExternalCommand
    {
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
            string? viewProblem = TrenchDrawing.ViewProblem(view);
            if (viewProblem != null)
            {
                TaskDialog.Show("Trench Map", viewProblem);
                return Result.Cancelled;
            }

            int cleared;
            using (Transaction t = new Transaction(doc, "Clear Trench Map"))
            {
                t.Start();
                cleared = TrenchDrawing.Clear(doc, view);
                t.Commit();
            }

            TaskDialog.Show("Trench Map", cleared == 0
                ? "There is no trench map in " + view.Name + "."
                : "Removed the trench map (" + cleared + " element(s)) from " + view.Name + ". The Trench Map filled region and text types stay in the project, unused.");
            return Result.Succeeded;
        }
    }
}

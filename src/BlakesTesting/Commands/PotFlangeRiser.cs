#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using BlakesTesting.Risers;

namespace BlakesTesting.Commands
{
    /// <summary>
    ///     Pot Flange Riser: "fakes" the toilet's vertical pipe so every toilet on a level is the same length. It finds each
    ///     toilet's vertical pipe (the pipe standing on a quarter bend / medium turn, the way Place Sleeves finds a toilet -
    ///     <see cref="ToiletPipeFinder"/>), takes the LONGEST one on each level, and writes that length into MFG.CutLength -
    ///     the parameter Round Riser PEX Cut Lengths writes - on every toilet pipe of that level, then writes the level's
    ///     name into a new <c>Pot Flange Riser</c> parameter (created and bound to Pipes if the job does not have it).
    ///     The pipes' own lengths are never touched. For testing it paints the toilet pipes blue in the active view.
    ///     One Undo.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class PotFlangeRiser : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            PotFlangeRisers.Run(commandData, ref message);
    }

    /// <summary>Takes the blue off the toilet pipes in the active view (the selected pipes, or every pipe in the view).</summary>
    [Transaction(TransactionMode.Manual)]
    public class ClearPotFlangeRiserColors : IExternalCommand
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
            string? problem = ViewColoring.Problem(view);
            if (problem != null)
            {
                message = "Nothing to clear: " + problem + ".";
                return Result.Cancelled;
            }

            List<Element> pipes = uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id)).Where(e => e is Pipe).ToList();
            if (pipes.Count == 0) pipes = new FilteredElementCollector(doc, view.Id).OfClass(typeof(Pipe)).ToList();

            int cleared;
            using (Transaction t = new Transaction(doc, "Clear Pot Flange Riser colors"))
            {
                t.Start();
                cleared = ViewColoring.ClearColor(view, pipes, PotFlangeRisers.Blue);
                t.Commit();
            }

            TaskDialog.Show(PotFlangeRisers.Title,
                $"Cleared the blue from {cleared} pipe{(cleared == 1 ? "" : "s")} in this view. A cleared pipe loses any other override it had.");
            return Result.Succeeded;
        }
    }

    internal static class PotFlangeRisers
    {
        public const string Title = "Pot Flange Riser";

        /// <summary>Blue, for the testing aid: the toilet pipes the command found and wrote.</summary>
        public static readonly RgbColor Blue = new RgbColor(0, 102, 255);

        /// <summary>
        ///     The parameter's GUID, fixed so a project made on one machine and one made on another carry the SAME shared
        ///     parameter (a schedule or filter built on one still finds the other). When this moves into BIM Tools it
        ///     belongs in the team's shared parameter file under this GUID.
        /// </summary>
        private static readonly Guid ParameterGuid = new Guid("6a1c2f5e-3b7d-4e0a-9f48-5d2c8b1e7a90");

        public static Result Run(ExternalCommandData commandData, ref string message)
        {
            UIDocument? uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Open a model first.";
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            View view = doc.ActiveView;
            ToiletSearch search = ToiletPipeFinder.Find(doc);
            List<ToiletLevelPlan> plans = PotFlangeRiserRules.Plan(search.Pipes);

            if (search.Pipes.Count == 0)
            {
                TaskDialog.Show(Title, "No toilet pipes found.\n\n" + SearchLine(search)
                                       + "\n\nA toilet pipe is a 3\" sanitary ABS, PVC or cast iron pipe standing on a quarter bend, "
                                       + "fourth bend or medium turn (directly, or through a no-hub coupling).");
                return Result.Cancelled;
            }

            StringBuilder preview = new StringBuilder();
            preview.AppendLine(SearchLine(search));
            preview.AppendLine();
            preview.AppendLine("Level  -  toilets  -  longest (every one is brought to this)  -  shortest now");
            foreach (ToiletLevelPlan p in plans)
                preview.AppendLine(p.CanWrite
                    ? $"{p.Level}  -  {p.Pipes.Count}  -  {PotFlangeRiserRules.FormatFeetInches(p.LongestFeet)}  -  {PotFlangeRiserRules.FormatFeetInches(p.ShortestFeet)}"
                    : $"{p.Level}  -  {p.Pipes.Count}  -  NO LENGTH READ, nothing to write");
            if (search.NoLevel > 0)
                preview.AppendLine($"\n{search.NoLevel} toilet pipe{(search.NoLevel == 1 ? " has" : "s have")} no level (no TPI Level and no reference level) and "
                                   + $"are grouped together under {PotFlangeRiserRules.NoLevel}.");
            string? colorProblem = ViewColoring.Problem(view);
            preview.Append("\n" + (colorProblem == null ? "The toilet pipes will be painted blue in this view." : $"Not painted blue: {colorProblem}."));

            int writable = plans.Where(p => p.CanWrite).Sum(p => p.Pipes.Count);
            TaskDialog confirm = new TaskDialog(Title)
            {
                MainInstruction = "Write MFG.CutLength and Pot Flange Riser?",
                MainContent = preview.ToString(),
                CommonButtons = TaskDialogCommonButtons.Cancel,
            };
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Write {writable} toilet pipe{(writable == 1 ? "" : "s")}",
                "Each level's longest toilet pipe length into MFG.CutLength on every toilet pipe of that level, and the level's name "
                + "into Pot Flange Riser (the parameter is added if the job lacks it). One Undo takes it back.");
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Just paint them blue and select them",
                "Writes nothing. Temporary Isolate the selection to check these are the toilets.");
            // Set only after the links exist; the safe choice, which writes nothing, is the default.
            confirm.DefaultButton = TaskDialogResult.CommandLink2;
            TaskDialogResult choice = confirm.Show();

            List<ElementId> allIds = search.Pipes.Select(p => LongIds.ToElementId(p.PipeId)).ToList();

            if (choice == TaskDialogResult.CommandLink2)
            {
                int painted = 0, paintFailed = 0;
                if (colorProblem == null)
                    using (Transaction t = new Transaction(doc, "Color toilet pipes"))
                    {
                        t.Start();
                        painted = ViewColoring.Paint(doc, view, allIds, Blue, true, out paintFailed);
                        t.Commit();
                    }

                uidoc.Selection.SetElementIds(allIds);
                TaskDialog.Show(Title, PaintLine(colorProblem, painted, paintFailed) + "\n\nThe toilet pipes are now selected.");
                return Result.Succeeded;
            }

            if (choice != TaskDialogResult.CommandLink1) return Result.Cancelled;

            // ---------------------------------------------------------------- the write
            string? parameterProblem = null;
            Dictionary<WriteOutcome, int> cutOutcomes = new Dictionary<WriteOutcome, int>();
            Dictionary<WriteOutcome, int> levelOutcomes = new Dictionary<WriteOutcome, int>();
            List<ElementId> done = new List<ElementId>();
            int painted2 = 0, paintFailed2 = 0;
            bool committed;
            using (Transaction t = new Transaction(doc, "Pot flange riser"))
            {
                t.Start();
                parameterProblem = EnsureParameter(doc);
                foreach (ToiletLevelPlan plan in plans)
                {
                    if (!plan.CanWrite) continue;
                    foreach (ToiletPipe tp in plan.Pipes)
                    {
                        Element? pipe = doc.GetElement(LongIds.ToElementId(tp.PipeId));
                        if (pipe == null) continue;
                        WriteOutcome cut = RiserCutLengths.Write(pipe, plan.LongestFeet);
                        Add(cutOutcomes, cut);
                        Add(levelOutcomes, parameterProblem == null ? WriteLevel(pipe, plan.Level) : WriteOutcome.NoParameter);
                        if (cut == WriteOutcome.Written || cut == WriteOutcome.Unchanged) done.Add(pipe.Id);
                    }
                }

                // Only a pipe whose cut length is now right is painted: a blue pipe whose write failed would lie.
                if (colorProblem == null) painted2 = ViewColoring.Paint(doc, view, done, Blue, true, out paintFailed2);
                committed = t.Commit() == TransactionStatus.Committed;
            }

            if (!committed)
            {
                message = "Revit rolled the change back - nothing was written.";
                TaskDialog.Show(Title, message);
                return Result.Failed;
            }

            uidoc.Selection.SetElementIds(allIds);
            StringBuilder report = new StringBuilder();
            report.AppendLine($"MFG.CutLength changed on {Count(cutOutcomes, WriteOutcome.Written)} toilet pipe{(Count(cutOutcomes, WriteOutcome.Written) == 1 ? "" : "s")}; "
                              + $"{Count(cutOutcomes, WriteOutcome.Unchanged)} already had the right value.");
            report.AppendLine($"{ParameterName} changed on {Count(levelOutcomes, WriteOutcome.Written)}; {Count(levelOutcomes, WriteOutcome.Unchanged)} already right.");
            report.AppendLine();
            foreach (ToiletLevelPlan plan in plans)
                report.AppendLine(plan.CanWrite
                    ? $"{plan.Level}: {plan.Pipes.Count} toilet pipe{(plan.Pipes.Count == 1 ? "" : "s")} at {PotFlangeRiserRules.FormatFeetInches(plan.LongestFeet)}"
                    : $"{plan.Level}: {plan.Pipes.Count} toilet pipe{(plan.Pipes.Count == 1 ? "" : "s")} - no length read, NOT written");
            Problem(report, cutOutcomes, WriteOutcome.NoParameter, "MFG.CutLength is not on these pipes - bind it (SP Insert) and run again");
            Problem(report, cutOutcomes, WriteOutcome.ReadOnly, "MFG.CutLength is read-only");
            Problem(report, cutOutcomes, WriteOutcome.Refused, "Revit refused the MFG.CutLength value");
            Problem(report, cutOutcomes, WriteOutcome.UnknownType, "MFG.CutLength is neither a Length nor a Text parameter here");
            Problem(report, cutOutcomes, WriteOutcome.BadLength, "length would not read");
            if (parameterProblem != null) report.AppendLine($"\n{ParameterName} NOT WRITTEN: {parameterProblem}");
            else
            {
                Problem(report, levelOutcomes, WriteOutcome.NoParameter, $"{ParameterName} is not on these pipes");
                Problem(report, levelOutcomes, WriteOutcome.ReadOnly, $"{ParameterName} is read-only");
                Problem(report, levelOutcomes, WriteOutcome.Refused, $"Revit refused the {ParameterName} value");
                Problem(report, levelOutcomes, WriteOutcome.UnknownType, $"{ParameterName} is not a Text parameter here");
            }

            report.AppendLine("\n" + PaintLine(colorProblem, painted2, paintFailed2) + "\nThe toilet pipes are now selected.");
            TaskDialog.Show(Title, report.ToString().TrimEnd());
            return Result.Succeeded;
        }

        private const string ParameterName = PotFlangeRiserRules.ParameterName;

        // ------------------------------------------------------------------ the Pot Flange Riser parameter

        /// <summary>Writes the level's name into Pot Flange Riser, leaving a value already there alone.</summary>
        private static WriteOutcome WriteLevel(Element e, string level)
        {
            Parameter? p = e.LookupParameter(ParameterName);
            if (p == null) return WriteOutcome.NoParameter;
            if (p.IsReadOnly) return WriteOutcome.ReadOnly;
            if (p.StorageType != StorageType.String) return WriteOutcome.UnknownType;
            if (p.AsString() == level) return WriteOutcome.Unchanged;
            return p.Set(level) ? WriteOutcome.Written : WriteOutcome.Refused;
        }

        /// <summary>
        ///     Makes sure Pot Flange Riser exists on Pipes as an instance Text parameter; returns null when it does, else why
        ///     not. Call inside a transaction. A parameter of that name already bound (by anyone) is used as it is, and only
        ///     the Pipes category is added to it when it lacks it. Otherwise it is created as a shared parameter from a
        ///     throwaway shared parameter file in the temp folder, and the user's own shared parameter file setting is put
        ///     back afterwards - nothing is written to the team's file.
        /// </summary>
        private static string? EnsureParameter(Document doc)
        {
            try
            {
                long pipes = (long)BuiltInCategory.OST_PipeCurves;
                DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator();
                while (it.MoveNext())
                {
                    Definition def = it.Key;
                    if (def.Name != ParameterName) continue;
                    if (!(it.Current is InstanceBinding binding))
                        return $"{ParameterName} already exists in this project as a type parameter - delete it from Project Parameters and run again.";
                    bool hasPipes = false;
                    foreach (Category c in binding.Categories)
                        if (c.Id.ToLong() == pipes) hasPipes = true;

                    // The first version made it under Data; it belongs under Graphics beside the TPI parameters. Only a
                    // parameter still in Data is moved - one somebody put elsewhere on purpose is left where it is.
                    bool moveToGraphics = def is InternalDefinition internalDef && internalDef.GetGroupTypeId() == GroupTypeId.Data;
                    if (hasPipes && !moveToGraphics) return null;

                    CategorySet widened = doc.Application.Create.NewCategorySet();
                    foreach (Category c in binding.Categories) widened.Insert(c);
                    if (!hasPipes) widened.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_PipeCurves));
                    InstanceBinding rebound = doc.Application.Create.NewInstanceBinding(widened);
                    bool ok = moveToGraphics ? doc.ParameterBindings.ReInsert(def, rebound, GroupTypeId.Graphics) : doc.ParameterBindings.ReInsert(def, rebound);
                    if (!ok) return $"{ParameterName} exists but Revit would not update its categories or group.";
                    doc.Regenerate();
                    return null;
                }

                return CreateParameter(doc);
            }
            catch (Exception ex)
            {
                return "could not add the parameter: " + ex.Message;
            }
        }

        private static string? CreateParameter(Document doc)
        {
            Autodesk.Revit.ApplicationServices.Application app = doc.Application;
            string previous = app.SharedParametersFilename;
            string temp = Path.Combine(Path.GetTempPath(), "BlakesTesting-PotFlangeRiser-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllText(temp, "");
                app.SharedParametersFilename = temp;
                DefinitionFile? file = app.OpenSharedParameterFile();
                if (file == null) return "Revit could not open a shared parameter file to create it in.";

                DefinitionGroup group = file.Groups.Create("BlakesTesting");
                ExternalDefinitionCreationOptions options = new ExternalDefinitionCreationOptions(ParameterName, SpecTypeId.String.Text)
                {
                    GUID = ParameterGuid,
                    Visible = true,
                    UserModifiable = true,
                };
                Definition def = group.Definitions.Create(options);

                CategorySet categories = app.Create.NewCategorySet();
                categories.Insert(doc.Settings.Categories.get_Item(BuiltInCategory.OST_PipeCurves));
                if (!doc.ParameterBindings.Insert(def, app.Create.NewInstanceBinding(categories), GroupTypeId.Graphics))
                    return $"Revit would not bind {ParameterName} to Pipes.";
                doc.Regenerate();
                return null;
            }
            finally
            {
                try
                {
                    app.SharedParametersFilename = previous;
                }
                catch
                {
                }

                try
                {
                    File.Delete(temp);
                }
                catch
                {
                }
            }
        }

        // ------------------------------------------------------------------ reporting

        private static string SearchLine(ToiletSearch s)
        {
            string line = $"Toilet bends found: {s.Bends}  ->  {s.Pipes.Count} toilet pipe{(s.Pipes.Count == 1 ? "" : "s")}";
            if (s.NoRiser > 0) line += $"\n  {s.NoRiser} with no vertical pipe rising from them";
            if (s.WrongPipe > 0) line += $"\n  {s.WrongPipe} whose rising pipe is not 3\" sanitary ABS, PVC or cast iron";
            return line;
        }

        private static string PaintLine(string? problem, int painted, int failed)
        {
            if (problem != null) return $"Not painted: {problem}.";
            string line = $"Painted {painted} pipe{(painted == 1 ? "" : "s")} blue in this view.";
            if (failed > 0) line += $" {failed} could not be painted here (hidden in this view?).";
            return line;
        }

        private static void Add(Dictionary<WriteOutcome, int> by, WriteOutcome outcome) =>
            by[outcome] = (by.TryGetValue(outcome, out int n) ? n : 0) + 1;

        private static int Count(Dictionary<WriteOutcome, int> by, WriteOutcome outcome) => by.TryGetValue(outcome, out int n) ? n : 0;

        private static void Problem(StringBuilder sb, Dictionary<WriteOutcome, int> by, WriteOutcome outcome, string why)
        {
            int n = Count(by, outcome);
            if (n > 0) sb.AppendLine($"NOT WRITTEN, {n} pipe{(n == 1 ? "" : "s")}: {why}");
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
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
    ///     Writes MFG.CutLength on every pipe: the PEX that serves a water riser (a tower) is rounded UP by the Round Pex
    ///     Lengths rules (3" steps to 9'-0", then whole feet); every other pipe - PEX distribution, mains, branches,
    ///     other materials - gets its real length. The pipe's own length is never touched, so the model groups stay
    ///     whole and the risers stay connected.
    ///     <para>
    ///         Which PEX is a riser comes from the manifolds, not from names: from each 4- or 6-port manifold the
    ///         trail goes out through the inlet, along the horizontal PEX, up (or down) the first vertical, and every
    ///         vertical PEX pipe on the way is a riser pipe until the trail turns horizontal again. See
    ///         <see cref="RiserTrail"/>. With nothing selected it does the whole model; with PIPES selected it writes
    ///         only those (still working out riser-or-not from the whole model); with MANIFOLDS selected it does only
    ///         the risers of those manifolds. It shows what it found and asks before it writes; one Undo.
    ///     </para>
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class RoundRiserPexLengths : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            RiserCutLengths.Run(commandData, ref message, selectOnly: false);
    }

    /// <summary>
    ///     The preview of <see cref="RoundRiserPexLengths"/>: finds the riser pipes and SELECTS them, writing nothing.
    ///     Use Temporary Isolate on the selection to check the trail follows the tower before you write.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SelectRiserPex : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            RiserCutLengths.Run(commandData, ref message, selectOnly: true);
    }

    /// <summary>
    ///     The manual override for a tower the trail did not find (or found wrong): rounds the SELECTED pipes up the
    ///     pex ladder into MFG.CutLength, riser or not. Running Round Riser PEX Cut Lengths over the whole model
    ///     afterwards works everything out from the manifolds again and overwrites this.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class RoundSelectedPexAsRiser : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            RiserCutLengths.Override(commandData, ref message, round: true);
    }

    /// <summary>
    ///     The other manual override: puts the SELECTED pipes' real length into MFG.CutLength, for a pipe the trail
    ///     rounded that should not be. Overwritten by a later whole-model run in the same way.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class SetSelectedExactCutLength : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            RiserCutLengths.Override(commandData, ref message, round: false);
    }

    internal enum WriteOutcome
    {
        Written,
        Unchanged,
        NoParameter,
        ReadOnly,
        BadLength,
        Refused,
        UnknownType,
    }

    internal static class RiserCutLengths
    {
        public const string ParameterName = "MFG.CutLength";
        private const string Title = "Round Riser PEX Cut Lengths";

        // ------------------------------------------------------------------ the whole-model / selection run

        public static Result Run(ExternalCommandData commandData, ref string message, bool selectOnly)
        {
            UIDocument? uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Open a model first.";
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            List<Element> selected = uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id)).Where(e => e != null).ToList();
            List<FamilyInstance> pickedManifolds = selected.OfType<FamilyInstance>().Where(f => RevitPipeNetwork.IsManifold(doc, f)).ToList();
            List<Element> pickedPipes = selected.Where(e => e is Pipe || e is FlexPipe).ToList();
            bool onlyManifolds = pickedManifolds.Count > 0 && pickedPipes.Count == 0;

            // Which manifolds to walk from. A picked pipe still has to be judged against EVERY manifold, or a riser pipe
            // that hangs off one nobody selected would be written at its real length.
            List<FamilyInstance> allManifolds = RevitPipeNetwork.FindManifolds(doc);
            List<FamilyInstance> seeds = onlyManifolds ? pickedManifolds : allManifolds;

            RevitPipeNetwork net = new RevitPipeNetwork(doc);
            List<TrailResult> trails = new List<TrailResult>();
            HashSet<long> risers = RiserTrail.WalkAll(net, seeds.Select(m => m.Id.ToLong()), new TrailOptions(), trails);

            string detailsPath = WriteDetails(doc, seeds, trails);
            ICollection<ElementId> riserIds = risers.Select(LongIds.ToElementId).ToList();

            if (selectOnly)
            {
                if (riserIds.Count > 0) uidoc.Selection.SetElementIds(riserIds);
                TaskDialog.Show(Title, SummaryOfTrails(seeds.Count, trails, risers.Count, detailsPath)
                                       + (riserIds.Count > 0 ? "\n\nThe riser pipes are now selected - Temporary Isolate to check them." : ""));
                return Result.Succeeded;
            }

            // What is written to which pipes.
            List<Element> scope;
            string scopeText;
            if (pickedPipes.Count > 0)
            {
                scope = pickedPipes;
                scopeText = $"the {scope.Count} selected pipe{(scope.Count == 1 ? "" : "s")}";
            }
            else if (onlyManifolds)
            {
                scope = risers.Select(id => doc.GetElement(LongIds.ToElementId(id))).Where(e => e != null).ToList();
                scopeText = $"the riser pipes of the {pickedManifolds.Count} selected manifold{(pickedManifolds.Count == 1 ? "" : "s")}";
            }
            else
            {
                scope = AllPipes(doc);
                scopeText = "every pipe in the model";
            }

            if (scope.Count == 0)
            {
                message = "There are no pipes to write.";
                return Result.Cancelled;
            }

            int rounded = scope.Count(e => e is Pipe && risers.Contains(e.Id.ToLong()));
            int exact = scope.Count - rounded;

            StringBuilder preview = new StringBuilder();
            preview.AppendLine($"Writing {scopeText}.");
            preview.AppendLine();
            preview.AppendLine($"Riser PEX, rounded up:  {rounded} pipe{(rounded == 1 ? "" : "s")}");
            preview.AppendLine($"Everything else, real length:  {exact} pipe{(exact == 1 ? "" : "s")}");
            preview.AppendLine();
            preview.Append(SummaryOfTrails(seeds.Count, trails, risers.Count, detailsPath));
            if (seeds.Count == 0)
                preview.Append("\n\nNO MANIFOLDS FOUND, so nothing counts as a riser and every pipe would be written at its real length - "
                               + "including risers that were rounded before. Cancel unless that is what you want.");

            TaskDialog confirm = new TaskDialog(Title)
            {
                MainInstruction = "Write MFG.CutLength?",
                MainContent = preview.ToString(),
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.CommandLink2, // the safe one: nothing is written
            };
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, $"Write MFG.CutLength on {scope.Count} pipe{(scope.Count == 1 ? "" : "s")}",
                "Riser PEX rounded up, everything else its real length. One Undo takes it back.");
            confirm.AddCommandLink(TaskDialogCommandLinkId.CommandLink2, "Just select the riser pipes",
                "Writes nothing. Temporary Isolate the selection to check it is the tower.");
            TaskDialogResult choice = confirm.Show();

            if (choice == TaskDialogResult.CommandLink2)
            {
                if (riserIds.Count > 0) uidoc.Selection.SetElementIds(riserIds);
                return Result.Succeeded;
            }

            if (choice != TaskDialogResult.CommandLink1) return Result.Cancelled;

            Tally tally = new Tally();
            bool committed;
            using (Transaction t = new Transaction(doc, "Round riser PEX cut lengths"))
            {
                t.Start();
                foreach (Element e in scope)
                {
                    double length = ReadLengthFeet(e);
                    bool round = e is Pipe && risers.Contains(e.Id.ToLong());
                    tally.Add(e, Write(e, round ? CutLengthRules.RoundPexFeet(length) : length), round);
                }

                committed = t.Commit() == TransactionStatus.Committed;
            }

            if (!committed)
            {
                message = "Revit rolled the change back - no pipe's MFG.CutLength was changed.";
                TaskDialog.Show(Title, message);
                return Result.Failed;
            }

            if (riserIds.Count > 0) uidoc.Selection.SetElementIds(riserIds);
            TaskDialog.Show(Title, tally.Report(riserIds.Count > 0 ? "The riser pipes are now selected." : ""));
            return Result.Succeeded;
        }

        // ------------------------------------------------------------------ the two manual overrides

        public static Result Override(ExternalCommandData commandData, ref string message, bool round)
        {
            UIDocument? uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null)
            {
                message = "Open a model first.";
                return Result.Cancelled;
            }

            Document doc = uidoc.Document;
            List<Element> pipes = uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id))
                .Where(e => e is Pipe || e is FlexPipe).ToList();
            if (pipes.Count == 0)
            {
                message = "Select the pipes first.";
                return Result.Cancelled;
            }

            Tally tally = new Tally();
            bool committed;
            using (Transaction t = new Transaction(doc, round ? "Round selected pipes as riser" : "Selected pipes at real length"))
            {
                t.Start();
                foreach (Element e in pipes)
                {
                    double length = ReadLengthFeet(e);
                    tally.Add(e, Write(e, round ? CutLengthRules.RoundPexFeet(length) : length), round);
                }

                committed = t.Commit() == TransactionStatus.Committed;
            }

            if (!committed)
            {
                message = "Revit rolled the change back - no pipe's MFG.CutLength was changed.";
                TaskDialog.Show(Title, message);
                return Result.Failed;
            }

            TaskDialog.Show(Title, tally.Report("Running Round Riser PEX Cut Lengths over the whole model works every pipe out from the "
                                                + "manifolds again, so it will overwrite this."));
            return Result.Succeeded;
        }

        // ------------------------------------------------------------------ writing

        /// <summary>
        ///     Puts one length (decimal feet) into MFG.CutLength through whichever Set overload the job's binding takes -
        ///     a Length parameter takes the double, the older Text binding takes the same number as a string - and says
        ///     what happened. A value already there is left alone, so a re-run touches nothing it need not.
        /// </summary>
        public static WriteOutcome Write(Element e, double feet)
        {
            if (!CutLengthRules.IsUsable(feet)) return WriteOutcome.BadLength;
            Parameter? p = e.LookupParameter(ParameterName);
            if (p == null) return WriteOutcome.NoParameter;
            if (p.IsReadOnly) return WriteOutcome.ReadOnly;

            switch (p.StorageType)
            {
                case StorageType.Double:
                    if (p.HasValue && Math.Abs(p.AsDouble() - feet) < 1e-9) return WriteOutcome.Unchanged;
                    return p.Set(feet) ? WriteOutcome.Written : WriteOutcome.Refused;
                case StorageType.String:
                    string text = feet.ToString(CultureInfo.InvariantCulture);
                    if (p.AsString() == text) return WriteOutcome.Unchanged;
                    return p.Set(text) ? WriteOutcome.Written : WriteOutcome.Refused;
                default:
                    return WriteOutcome.UnknownType;
            }
        }

        /// <summary>A pipe's own length in decimal feet, or NaN when it will not read.</summary>
        public static double ReadLengthFeet(Element e)
        {
            try
            {
                Parameter? p = e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
                if (p == null || !p.HasValue) p = e.LookupParameter("Length");
                if (p == null || !p.HasValue || p.StorageType != StorageType.Double) return double.NaN;
                return p.AsDouble();
            }
            catch
            {
                return double.NaN;
            }
        }

        private static List<Element> AllPipes(Document doc)
        {
            List<Element> all = new FilteredElementCollector(doc).OfClass(typeof(Pipe)).WhereElementIsNotElementType().ToList();
            all.AddRange(new FilteredElementCollector(doc).OfClass(typeof(FlexPipe)).WhereElementIsNotElementType());
            return all;
        }

        // ------------------------------------------------------------------ reporting

        private static string SummaryOfTrails(int manifolds, List<TrailResult> trails, int riserPipes, string detailsPath)
        {
            int found = trails.Count(t => t.Status == TrailStatus.Found);
            int noRiser = trails.Count(t => t.Status == TrailStatus.NoRiser);
            int tooShort = trails.Count(t => t.Status == TrailStatus.TooShort);
            int noInlet = trails.Count(t => t.Status == TrailStatus.NoInlet);
            int limit = trails.Count(t => t.HitLimit);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Manifolds walked: {manifolds}  ->  {riserPipes} riser pipe{(riserPipes == 1 ? "" : "s")} found");
            sb.AppendLine($"  riser found from {found}");
            if (noRiser > 0) sb.AppendLine($"  NO vertical PEX reached from {noRiser}");
            if (tooShort > 0) sb.AppendLine($"  only a short vertical stub from {tooShort} (under {new TrailOptions().MinRiseFeet:0.#} ft)");
            if (noInlet > 0) sb.AppendLine($"  NO usable inlet on {noInlet} (all ports the same size, or the inlet is unconnected)");
            if (limit > 0) sb.AppendLine($"  horizontal walk hit its limit from {limit}");
            if (detailsPath.Length > 0) sb.Append($"\nPer-manifold detail: {detailsPath}");
            return sb.ToString().TrimEnd();
        }

        /// <summary>One line per manifold - what it walked and why - to a text file, for working out why a tower came out wrong.</summary>
        private static string WriteDetails(Document doc, List<FamilyInstance> manifolds, List<TrailResult> trails)
        {
            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"Round Riser PEX Cut Lengths - {doc.Title} - {DateTime.Now:yyyy-MM-dd HH:mm}");
                sb.AppendLine("One block per manifold. 'approach' = horizontal PEX walked to the riser (kept at real length); 'riser' = vertical PEX rounded.");
                sb.AppendLine();
                Dictionary<long, FamilyInstance> byId = manifolds.ToDictionary(m => m.Id.ToLong());
                foreach (TrailResult t in trails)
                {
                    FamilyInstance m = byId[t.ManifoldId];
                    sb.AppendLine($"Manifold {t.ManifoldId}  {m.Symbol?.FamilyName} : {m.Symbol?.Name}");
                    sb.AppendLine($"  {t.Status}{(t.HitLimit ? "  (horizontal walk hit its limit)" : "")}  rise {t.RiseFeet:0.0} ft");
                    sb.AppendLine($"  approach {t.ApproachPipes.Count}: {string.Join(" ", t.ApproachPipes)}");
                    sb.AppendLine($"  riser    {t.RiserPipes.Count}: {string.Join(" ", t.RiserPipes)}");
                    if (t.StubPipes.Count > 0) sb.AppendLine($"  stub (too short, left at real length) {t.StubPipes.Count}: {string.Join(" ", t.StubPipes)}");
                    sb.AppendLine();
                }

                string path = Path.Combine(Path.GetTempPath(), "BlakesTesting-RiserPex.txt");
                File.WriteAllText(path, sb.ToString());
                return path;
            }
            catch
            {
                return "";
            }
        }

        private sealed class Tally
        {
            private readonly Dictionary<WriteOutcome, List<long>> _by = new Dictionary<WriteOutcome, List<long>>();
            private int _rounded;
            private int _exact;

            public void Add(Element e, WriteOutcome outcome, bool rounded)
            {
                if (!_by.TryGetValue(outcome, out List<long>? ids)) _by[outcome] = ids = new List<long>();
                ids.Add(e.Id.ToLong());
                if (outcome == WriteOutcome.Written || outcome == WriteOutcome.Unchanged)
                {
                    if (rounded) _rounded++;
                    else _exact++;
                }
            }

            private int Count(WriteOutcome o) => _by.TryGetValue(o, out List<long>? ids) ? ids.Count : 0;

            public string Report(string footer)
            {
                int written = Count(WriteOutcome.Written);
                int same = Count(WriteOutcome.Unchanged);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine($"{ParameterName} changed on {written} pipe{(written == 1 ? "" : "s")}; {same} already had the right value.");
                sb.AppendLine($"Rounded up: {_rounded}.  Real length: {_exact}.");

                Problem(sb, WriteOutcome.NoParameter, $"{ParameterName} is not on these pipes - bind it (SP Insert) and run again");
                Problem(sb, WriteOutcome.ReadOnly, "read-only");
                Problem(sb, WriteOutcome.BadLength, "length would not read");
                Problem(sb, WriteOutcome.Refused, "Revit refused the value");
                Problem(sb, WriteOutcome.UnknownType, $"{ParameterName} is neither a Length nor a Text parameter here");
                if (footer.Length > 0) sb.Append("\n" + footer);
                return sb.ToString().TrimEnd();
            }

            private void Problem(StringBuilder sb, WriteOutcome outcome, string why)
            {
                if (!_by.TryGetValue(outcome, out List<long>? ids) || ids.Count == 0) return;
                sb.AppendLine($"NOT WRITTEN, {ids.Count} pipe{(ids.Count == 1 ? "" : "s")}: {why} (e.g. {string.Join(", ", ids.Take(5))})");
            }
        }
    }
}

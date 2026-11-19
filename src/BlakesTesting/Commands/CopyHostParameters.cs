#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace BlakesTesting.Commands
{
    /// <summary>
    ///     Copies every instance parameter of a host family onto the families nested inside it (and the ones nested in
    ///     those), so a view filter, schedule or Isolate that reads a parameter sees the nested parts the same way it sees
    ///     the host. Works on the selected host families, or - with nothing selected - on every family in the active view
    ///     that has nested families. Shows what it is about to change first; one Undo takes it all back.
    ///     Only nested families that were set to <b>Shared</b> in the family editor exist as elements in the project, so
    ///     only those can be reached.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class CopyHostParametersToNested : IExternalCommand
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
            List<FamilyInstance> picked = uidoc.Selection.GetElementIds().Select(id => doc.GetElement(id)).OfType<FamilyInstance>().ToList();
            bool fromSelection = picked.Count > 0;
            if (!fromSelection)
            {
                View view = doc.ActiveView;
                if (view is ViewSheet || view.ViewType == ViewType.Schedule || view.ViewType == ViewType.Legend)
                {
                    message = "Select the host families, or open a plan, section, elevation or 3D view to do every nested family in it.";
                    return Result.Cancelled;
                }

                picked = new FilteredElementCollector(doc, view.Id).OfClass(typeof(FamilyInstance)).Cast<FamilyInstance>().ToList();
            }

            // A selected family that sits inside another selected one is reached through it; only the outermost are hosts.
            HashSet<int> pickedIds = picked.Select(f => f.Id.IntegerValueCompat()).ToHashSet();
            List<FamilyInstance> hosts = picked.Where(f => !HasPickedAncestor(f, pickedIds)).ToList();

            List<HostPlan> plans = new List<HostPlan>();
            int hostsWithoutNested = 0;
            foreach (FamilyInstance host in hosts)
            {
                List<FamilyInstance> nested = NestedParams.Descendants(doc, host);
                if (nested.Count == 0)
                {
                    hostsWithoutNested++;
                    continue;
                }

                plans.Add(NestedParams.Plan(host, nested));
            }

            if (plans.Count == 0)
            {
                message = fromSelection
                    ? "The selected families have no nested families to copy to. Only nested families set to Shared in the family editor can be reached."
                    : "No family in this view has nested families. Only nested families set to Shared in the family editor can be reached.";
                return Result.Cancelled;
            }

            int nestedTotal = plans.Sum(p => p.Nested.Count);
            int changes = plans.Sum(p => p.Copies.Count);
            Dictionary<string, int> missing = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (HostPlan p in plans)
            foreach (string name in p.MissingOnNested)
                missing[name] = missing.TryGetValue(name, out int n) ? n + 1 : 1;

            StringBuilder preview = new StringBuilder();
            preview.AppendLine($"{plans.Count} host famil{(plans.Count == 1 ? "y" : "ies")}, {nestedTotal} nested famil{(nestedTotal == 1 ? "y" : "ies")} ({(fromSelection ? "selected" : "in this view")}).");
            preview.AppendLine($"{changes} parameter value{(changes == 1 ? "" : "s")} will change.");
            if (hostsWithoutNested > 0) preview.AppendLine($"{hostsWithoutNested} famil{(hostsWithoutNested == 1 ? "y has" : "ies have")} no nested families and {(hostsWithoutNested == 1 ? "is" : "are")} skipped.");
            AppendMissing(preview, missing);
            preview.AppendLine();
            preview.Append("Copy now? Undo takes it back.");

            if (changes == 0)
            {
                TaskDialog.Show("Copy Host Parameters to Nested", preview.ToString().Replace("Copy now? Undo takes it back.", "Nothing to change - the nested families already match their hosts."));
                return Result.Succeeded;
            }

            TaskDialog confirm = new TaskDialog("Copy Host Parameters to Nested")
            {
                MainInstruction = "Copy host parameters onto nested families?",
                MainContent = preview.ToString(),
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.Yes,
            };
            if (confirm.Show() != TaskDialogResult.Yes) return Result.Cancelled;

            int written = 0, failed = 0;
            using (Transaction t = new Transaction(doc, "Copy host parameters to nested families"))
            {
                t.Start();
                foreach (HostPlan p in plans)
                foreach (ParamCopy c in p.Copies)
                {
                    if (NestedParams.Apply(c)) written++;
                    else failed++;
                }

                t.Commit();
            }

            // Leave the hosts and their parts selected, so Isolate Element / Temporary Hide-Isolate keeps the whole assembly.
            List<ElementId> keep = new List<ElementId>();
            foreach (HostPlan p in plans)
            {
                keep.Add(p.Host.Id);
                keep.AddRange(p.Nested.Select(n => n.Id));
            }

            uidoc.Selection.SetElementIds(keep);

            StringBuilder report = new StringBuilder();
            report.AppendLine($"Copied {written} parameter value{(written == 1 ? "" : "s")} onto {nestedTotal} nested famil{(nestedTotal == 1 ? "y" : "ies")} in {plans.Count} host{(plans.Count == 1 ? "" : "s")}.");
            if (failed > 0) report.AppendLine($"{failed} value{(failed == 1 ? "" : "s")} could not be set (read-only, or the element is owned by someone else).");
            AppendMissing(report, missing);
            report.AppendLine();
            report.Append("The hosts and their nested families are now selected.");
            TaskDialog.Show("Copy Host Parameters to Nested", report.ToString());
            return Result.Succeeded;
        }

        private static bool HasPickedAncestor(FamilyInstance f, HashSet<int> pickedIds)
        {
            Element? parent = f.SuperComponent;
            int guard = 0;
            while (parent != null && guard++ < 64)
            {
                if (pickedIds.Contains(parent.Id.IntegerValueCompat())) return true;
                parent = (parent as FamilyInstance)?.SuperComponent;
            }

            return false;
        }

        /// <summary>Names of host parameters that some nested family does not have, so a filter on them still cannot see that part.</summary>
        private static void AppendMissing(StringBuilder sb, Dictionary<string, int> missing)
        {
            if (missing.Count == 0) return;
            const int shown = 10;
            sb.AppendLine();
            sb.AppendLine("Host parameters some nested families do not have (add them to that family, or bind the project/shared parameter to its category):");
            foreach (KeyValuePair<string, int> kv in missing.OrderByDescending(k => k.Value).ThenBy(k => k.Key).Take(shown))
                sb.AppendLine($"  {kv.Key}");
            if (missing.Count > shown) sb.AppendLine($"  ... and {missing.Count - shown} more");
        }
    }

    /// <summary>One value to copy: from a host parameter to the same parameter on a nested family.</summary>
    internal sealed class ParamCopy
    {
        public ParamCopy(Parameter from, Parameter to)
        {
            From = from;
            To = to;
        }

        public Parameter From { get; }
        public Parameter To { get; }
    }

    internal sealed class HostPlan
    {
        public HostPlan(FamilyInstance host, List<FamilyInstance> nested, List<ParamCopy> copies, List<string> missingOnNested)
        {
            Host = host;
            Nested = nested;
            Copies = copies;
            MissingOnNested = missingOnNested;
        }

        public FamilyInstance Host { get; }
        public List<FamilyInstance> Nested { get; }

        /// <summary>Only the values that differ - parameters already equal are not listed.</summary>
        public List<ParamCopy> Copies { get; }

        /// <summary>Names of host parameters that at least one nested family has no matching parameter for.</summary>
        public List<string> MissingOnNested { get; }
    }

    internal static class NestedParams
    {
        /// <summary>
        ///     Built-in instance parameters that place, host or identify an element. Copying the host's onto a nested
        ///     family would move it or break its link to the host, so they are never copied. (Everything else - Mark,
        ///     Comments, the family's own and shared parameters, System Type, ... - is.)
        /// </summary>
        private static readonly HashSet<BuiltInParameter> NeverCopy = new HashSet<BuiltInParameter>
        {
            BuiltInParameter.FAMILY_LEVEL_PARAM,
            BuiltInParameter.SCHEDULE_LEVEL_PARAM,
            BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,
            BuiltInParameter.FAMILY_TOP_LEVEL_PARAM,
            BuiltInParameter.FAMILY_BASE_LEVEL_OFFSET_PARAM,
            BuiltInParameter.FAMILY_TOP_LEVEL_OFFSET_PARAM,
            BuiltInParameter.INSTANCE_ELEVATION_PARAM,
            BuiltInParameter.INSTANCE_FREE_HOST_OFFSET_PARAM,
            BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM,
            BuiltInParameter.INSTANCE_HEAD_HEIGHT_PARAM,
            BuiltInParameter.HOST_ID_PARAM,
            BuiltInParameter.SYMBOL_ID_PARAM,
            BuiltInParameter.ELEM_FAMILY_AND_TYPE_PARAM,
            BuiltInParameter.ELEM_FAMILY_PARAM,
            BuiltInParameter.ELEM_TYPE_PARAM,
            BuiltInParameter.ELEM_PARTITION_PARAM, // workset
            BuiltInParameter.DESIGN_OPTION_PARAM,
            BuiltInParameter.PHASE_CREATED,
            BuiltInParameter.PHASE_DEMOLISHED,
        };

        /// <summary>Every family nested in <paramref name="host"/>, at any depth, each once.</summary>
        public static List<FamilyInstance> Descendants(Document doc, FamilyInstance host)
        {
            List<FamilyInstance> found = new List<FamilyInstance>();
            HashSet<int> seen = new HashSet<int> { host.Id.IntegerValueCompat() };
            Stack<FamilyInstance> todo = new Stack<FamilyInstance>();
            todo.Push(host);
            while (todo.Count > 0)
            {
                ICollection<ElementId>? subs = todo.Pop().GetSubComponentIds();
                if (subs == null) continue;
                foreach (ElementId id in subs)
                {
                    if (!seen.Add(id.IntegerValueCompat())) continue;
                    if (doc.GetElement(id) is FamilyInstance child)
                    {
                        found.Add(child);
                        todo.Push(child);
                    }
                }
            }

            return found;
        }

        /// <summary>Works out, for each nested family, which host parameter values differ and so need copying.</summary>
        public static HostPlan Plan(FamilyInstance host, List<FamilyInstance> nested)
        {
            List<Parameter> source = host.Parameters.Cast<Parameter>().Where(CanCopyFrom).ToList();
            List<ParamCopy> copies = new List<ParamCopy>();
            HashSet<string> missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (FamilyInstance child in nested)
            {
                TargetIndex index = new TargetIndex(child);
                foreach (Parameter from in source)
                {
                    Parameter? to = index.Find(from);
                    if (to == null)
                    {
                        missing.Add(from.Definition.Name);
                        continue;
                    }

                    if (to.IsReadOnly) continue; // e.g. already driven by the host through an association
                    if (!SameValue(from, to)) copies.Add(new ParamCopy(from, to));
                }
            }

            return new HostPlan(host, nested, copies, missing.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());
        }

        public static bool Apply(ParamCopy c)
        {
            try
            {
                switch (c.From.StorageType)
                {
                    case StorageType.Double: return c.To.Set(c.From.AsDouble());
                    case StorageType.Integer: return c.To.Set(c.From.AsInteger());
                    case StorageType.String: return c.To.Set(c.From.AsString() ?? string.Empty);
                    case StorageType.ElementId: return c.To.Set(c.From.AsElementId());
                    default: return false;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static bool CanCopyFrom(Parameter p)
        {
            if (!p.HasValue || p.Definition == null) return false;
            if (p.StorageType == StorageType.None) return false;
            return !(p.Definition is InternalDefinition d && NeverCopy.Contains(d.BuiltInParameter));
        }

        private static bool SameValue(Parameter a, Parameter b)
        {
            if (!b.HasValue) return false;
            switch (a.StorageType)
            {
                case StorageType.Double: return Math.Abs(a.AsDouble() - b.AsDouble()) < 1e-9;
                case StorageType.Integer: return a.AsInteger() == b.AsInteger();
                case StorageType.String: return string.Equals(a.AsString() ?? string.Empty, b.AsString() ?? string.Empty, StringComparison.Ordinal);
                case StorageType.ElementId: return a.AsElementId().IntegerValueCompat() == b.AsElementId().IntegerValueCompat();
                default: return true;
            }
        }

        /// <summary>
        ///     A nested family's parameters, looked up the way Revit identifies them: a shared parameter by its GUID, a
        ///     built-in by its id, and a family parameter by name - and only where the kind of value (number, text,
        ///     Yes/No, length, ...) is the same, so a text "Size" is never forced into a numeric one.
        /// </summary>
        private sealed class TargetIndex
        {
            private readonly Dictionary<Guid, Parameter> _byGuid = new Dictionary<Guid, Parameter>();
            private readonly Dictionary<BuiltInParameter, Parameter> _byBuiltIn = new Dictionary<BuiltInParameter, Parameter>();
            private readonly Dictionary<string, List<Parameter>> _byName = new Dictionary<string, List<Parameter>>(StringComparer.OrdinalIgnoreCase);

            public TargetIndex(FamilyInstance child)
            {
                foreach (Parameter p in child.Parameters)
                {
                    if (p.Definition == null) continue;
                    if (p.IsShared) _byGuid[p.GUID] = p;
                    if (p.Definition is InternalDefinition d && d.BuiltInParameter != BuiltInParameter.INVALID) _byBuiltIn[d.BuiltInParameter] = p;
                    if (!_byName.TryGetValue(p.Definition.Name, out List<Parameter>? list)) _byName[p.Definition.Name] = list = new List<Parameter>();
                    list.Add(p);
                }
            }

            public Parameter? Find(Parameter from)
            {
                if (from.IsShared) return _byGuid.TryGetValue(from.GUID, out Parameter? shared) ? shared : null;
                if (from.Definition is InternalDefinition d && d.BuiltInParameter != BuiltInParameter.INVALID)
                    return _byBuiltIn.TryGetValue(d.BuiltInParameter, out Parameter? builtIn) ? builtIn : null;
                if (!_byName.TryGetValue(from.Definition.Name, out List<Parameter>? named)) return null;
                return named.FirstOrDefault(p => p.StorageType == from.StorageType && p.Definition.GetDataType() == from.Definition.GetDataType() && !p.IsShared);
            }
        }
    }
}

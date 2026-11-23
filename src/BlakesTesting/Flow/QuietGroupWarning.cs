#nullable enable
using Autodesk.Revit.DB;

namespace BlakesTesting.Flow
{
    /// <summary>
    ///     Revit warns "A group has been changed outside group edit mode. The change is being allowed because there is only
    ///     one instance of the type" (<c>BuiltInFailures.GroupFailures.AtomViolationWhenOnePlaceInstance</c>) when an element in
    ///     a group is touched after the group exists. Every flow arrow is its own group with one instance, so the change is always
    ///     allowed and the warning only gets in the way: this discards that one warning, and every other failure is left to Revit.
    /// </summary>
    internal sealed class QuietGroupWarning : IFailuresPreprocessor
    {
        /// <summary>Makes the transaction discard the warning. Call it once the transaction has started.</summary>
        public static void On(Transaction t)
        {
            FailureHandlingOptions options = t.GetFailureHandlingOptions();
            options.SetFailuresPreprocessor(new QuietGroupWarning());
            t.SetFailureHandlingOptions(options);
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            System.Guid wanted = BuiltInFailures.GroupFailures.AtomViolationWhenOnePlaceInstance.Guid;
            foreach (FailureMessageAccessor message in failuresAccessor.GetFailureMessages())
                if (message.GetSeverity() == FailureSeverity.Warning && message.GetFailureDefinitionId().Guid == wanted)
                    failuresAccessor.DeleteWarning(message);
            return FailureProcessingResult.Continue;
        }
    }
}

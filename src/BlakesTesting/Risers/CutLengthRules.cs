#nullable enable
using System;

namespace BlakesTesting.Risers
{
    /// <summary>
    ///     The pex rounding rules from Round Pex Lengths / BIM Tools' Round Parameter Pipe Lengths, with no Revit in
    ///     them: up to 9'-0" round UP to the next 3", from 9'-0" up round UP to the next whole foot.
    ///     Feet in, feet out - Revit's internal unit, which is what a Length parameter takes. The ladder works in
    ///     inches in between, so a conversion to inches before Parameter.Set (to match what the box on screen reads)
    ///     is a twelvefold error: the harness states every answer in both units.
    /// </summary>
    public static class CutLengthRules
    {
        /// <summary>Where the 3" ladder stops and the foot ladder starts, in inches (9'-0"). Both agree exactly here.</summary>
        public const double FootLadderStartsInches = 108.0;

        /// <summary>Round a PEX length up the ladder. A length that is not positive and finite comes back unchanged.</summary>
        public static double RoundPexFeet(double lengthFeet)
        {
            if (!IsUsable(lengthFeet)) return lengthFeet;
            double inches = lengthFeet * 12.0;

            // A pipe drawn at exactly 4'-3" can read back as 51.00000000000001 inches, and a bare ceiling would then
            // take it to 4'-6". BIM Tools' pex ladder has no slack for that (only its flex ladder does); this one
            // forgives a hundred-thousandth of an inch, which is float noise and not a trade tolerance. It is the one
            // place this differs from that ladder, and only for a length within 0.00001" above a 3" or 1' mark.
            const double slack = 1e-5;
            if (inches < FootLadderStartsInches) return Math.Ceiling(inches / 3.0 - slack) * 3.0 / 12.0;
            return Math.Ceiling(inches / 12.0 - slack);
        }

        /// <summary>A length worth writing: finite and greater than zero (an unwritten Length parameter reads 0).</summary>
        public static bool IsUsable(double lengthFeet) =>
            !double.IsNaN(lengthFeet) && !double.IsInfinity(lengthFeet) && lengthFeet > 0.0;
    }
}

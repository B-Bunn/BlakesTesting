#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlakesTesting.Risers
{
    /// <summary>
    ///     One toilet's vertical pipe (the pot flange riser), read off the model: which pipe, which level it belongs to
    ///     and how long it is. No Revit in it, so <see cref="PotFlangeRiserRules.Plan"/> can be tested without Revit.
    /// </summary>
    public sealed class ToiletPipe
    {
        public ToiletPipe(long pipeId, string level, double lengthFeet)
        {
            PipeId = pipeId;
            Level = PotFlangeRiserRules.CleanLevel(level);
            LengthFeet = lengthFeet;
        }

        public long PipeId { get; }

        /// <summary>The level the pipe belongs to, trimmed; <see cref="PotFlangeRiserRules.NoLevel"/> when it has none.</summary>
        public string Level { get; }

        /// <summary>The pipe's own length in decimal feet; NaN when it would not read.</summary>
        public double LengthFeet { get; }
    }

    /// <summary>What one level's toilet pipes are brought to: every one of them gets <see cref="LongestFeet"/>.</summary>
    public sealed class ToiletLevelPlan
    {
        public ToiletLevelPlan(string level, List<ToiletPipe> pipes, double longestFeet, long longestPipeId)
        {
            Level = level;
            Pipes = pipes;
            LongestFeet = longestFeet;
            LongestPipeId = longestPipeId;
        }

        public string Level { get; }
        public List<ToiletPipe> Pipes { get; }

        /// <summary>The longest usable length on this level, in feet; 0 when no pipe on it had a length that would read.</summary>
        public double LongestFeet { get; }

        /// <summary>The pipe that set <see cref="LongestFeet"/> (the first one, when two are the same length); 0 when there is none.</summary>
        public long LongestPipeId { get; }

        /// <summary>True when there is a length to write: at least one pipe on the level read.</summary>
        public bool CanWrite => CutLengthRules.IsUsable(LongestFeet);

        /// <summary>The shortest usable length on the level, for the preview - how far the level is being raised.</summary>
        public double ShortestFeet => Pipes.Select(p => p.LengthFeet).Where(CutLengthRules.IsUsable).DefaultIfEmpty(0.0).Min();
    }

    /// <summary>
    ///     The rules of Pot Flange Riser, with no Revit in them. A toilet's vertical pipe is found the way the other
    ///     commands find a toilet (Place Sleeves' <c>IsPipeToiletDrain</c> in BIM Tools): a 3" sanitary ABS, PVC or cast
    ///     iron pipe standing on a quarter bend (family named Quarter, "Fourth Bend", or - this command's addition -
    ///     "Medium Turn"), directly or through a no-hub coupling. Every such pipe on a level is then written the length
    ///     of the LONGEST one on that level, so every toilet on one floor is cut the same.
    /// </summary>
    public static class PotFlangeRiserRules
    {
        /// <summary>The parameter that gets the level's name.</summary>
        public const string ParameterName = "Pot Flange Riser";

        /// <summary>Shown (and used as the group) for a toilet pipe whose level cannot be read.</summary>
        public const string NoLevel = "(no level)";

        /// <summary>The toilet's pipe size, in inches, as Place Sleeves reads it (3", whatever rounding Revit's diameter carries).</summary>
        public const double ToiletSizeInches = 3.0;

        /// <summary>
        ///     Words in a bend's family or type name that make it the toilet's bend. "Quarter" and "300 Fourth Bend" are
        ///     the two Place Sleeves keys on; "Medium Turn" is the name of the same fitting in some libraries.
        /// </summary>
        public static readonly string[] BendWords = { "quarter", "fourth bend", "medium turn" };

        /// <summary>The pipe-type words Place Sleeves accepts at a toilet: ABS, PVC or cast iron.</summary>
        public static readonly string[] MaterialWords = { "abs", "pvc", "cast" };

        public static bool IsToiletBend(string? family, string? type)
        {
            string name = ((family ?? "") + " " + (type ?? "")).ToLowerInvariant();
            return BendWords.Any(name.Contains);
        }

        /// <summary>A no-hub coupling between the bend and the pipe, which Place Sleeves looks through as well.</summary>
        public static bool IsCoupling(string? family, string? type) =>
            ((family ?? "") + " " + (type ?? "")).IndexOf("coupling", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsSanitary(string? systemName) =>
            systemName != null && systemName.IndexOf("sanitary", StringComparison.OrdinalIgnoreCase) >= 0;

        public static bool IsToiletSize(double diameterInches) =>
            !double.IsNaN(diameterInches) && Math.Round(diameterInches, MidpointRounding.AwayFromZero) == ToiletSizeInches;

        public static bool IsToiletMaterial(string? pipeTypeName)
        {
            if (pipeTypeName == null) return false;
            string name = pipeTypeName.ToLowerInvariant();
            return MaterialWords.Any(name.Contains);
        }

        /// <summary>Decimal feet as feet and inches to the nearest 1/8": 3.375 is 3'-4 1/2".</summary>
        public static string FormatFeetInches(double feet)
        {
            if (double.IsNaN(feet) || double.IsInfinity(feet)) return "?";
            long eighths = (long)Math.Round(Math.Abs(feet) * 12.0 * 8.0, MidpointRounding.AwayFromZero);
            long wholeInches = eighths / 8;
            long frac = eighths % 8;
            long ft = wholeInches / 12;
            long inch = wholeInches % 12;
            string fraction = "";
            if (frac != 0)
            {
                long g = frac % 4 == 0 ? 4 : frac % 2 == 0 ? 2 : 1;
                fraction = $" {frac / g}/{8 / g}";
            }

            return (feet < 0 ? "-" : "") + $"{ft}'-{inch}{fraction}\"";
        }

        public static string CleanLevel(string? level)
        {
            string text = (level ?? "").Trim();
            return text.Length == 0 ? NoLevel : text;
        }

        /// <summary>
        ///     Groups the toilet pipes by level (names compared without case, in level-name order) and finds each level's
        ///     longest usable length. A pipe appearing twice (two bends reaching one pipe) is counted once. Every pipe on a
        ///     level is brought to that level's longest - the target does not depend on the pipe's own length, so a pipe whose
        ///     length would not read is still written, and a level where no pipe read has nothing to write
        ///     (<see cref="ToiletLevelPlan.CanWrite"/>).
        /// </summary>
        public static List<ToiletLevelPlan> Plan(IEnumerable<ToiletPipe> pipes)
        {
            HashSet<long> seen = new HashSet<long>();
            Dictionary<string, List<ToiletPipe>> byLevel = new Dictionary<string, List<ToiletPipe>>(StringComparer.OrdinalIgnoreCase);
            foreach (ToiletPipe p in pipes)
            {
                if (!seen.Add(p.PipeId)) continue;
                if (!byLevel.TryGetValue(p.Level, out List<ToiletPipe>? list)) byLevel[p.Level] = list = new List<ToiletPipe>();
                list.Add(p);
            }

            List<ToiletLevelPlan> plans = new List<ToiletLevelPlan>();
            foreach (KeyValuePair<string, List<ToiletPipe>> pair in byLevel.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
            {
                double longest = 0.0;
                long longestId = 0;
                foreach (ToiletPipe p in pair.Value)
                {
                    if (!CutLengthRules.IsUsable(p.LengthFeet) || p.LengthFeet <= longest) continue;
                    longest = p.LengthFeet;
                    longestId = p.PipeId;
                }

                plans.Add(new ToiletLevelPlan(pair.Key, pair.Value, longest, longestId));
            }

            return plans;
        }
    }
}

#nullable enable
using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace BlakesTesting.Trench
{
    /// <summary>
    ///     Lengths as a person types and reads them in the Trench Map settings and legend: feet and inches
    ///     (<c>4'-6"</c>, <c>0'-18"</c>, <c>4' 6 1/2"</c>), inches alone (<c>54"</c>) or a plain number, which is
    ///     FEET (<c>4.5</c>) - not Revit's own rule, where a bare number in a feet-and-inches field is inches. No Revit
    ///     in here, so it is tested without Revit; everything is feet inside.
    /// </summary>
    internal static class TrenchText
    {
        /// <summary>
        ///     Reads a length into feet. False for blank text, a number that is not one, a negative length or a length
        ///     that is not finite.
        /// </summary>
        public static bool TryParseFeet(string? text, out double feet)
        {
            feet = 0;
            if (text == null) return false;
            string t = text.Trim().Replace('’', '\'').Replace('”', '"').Replace('″', '"').Replace('′', '\'');
            if (t.Length == 0) return false;

            double total;
            int tick = t.IndexOf('\'');
            if (tick >= 0)
            {
                if (!TryNumber(t.Substring(0, tick), out double ft)) return false;
                string rest = t.Substring(tick + 1).Trim().TrimStart('-').Trim();
                double inches = 0;
                if (rest.Length > 0 && !TryInches(rest, out inches)) return false;
                total = ft + inches / 12.0;
            }
            else if (t.EndsWith("\"", StringComparison.Ordinal))
            {
                if (!TryInches(t, out double inches)) return false;
                total = inches / 12.0;
            }
            else
            {
                if (!TryNumber(t, out total)) return false;
            }

            if (double.IsNaN(total) || double.IsInfinity(total) || total < 0) return false;
            feet = total;
            return true;
        }

        /// <summary>Inches: "6", "6.5", "6 1/2", "6-1/2", "1/2", each with an optional closing inch mark.</summary>
        private static bool TryInches(string text, out double inches)
        {
            inches = 0;
            string t = text.Trim().TrimEnd('"').Trim().Replace('-', ' ');
            string[] parts = t.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || parts.Length > 2) return false;
            double sum = 0;
            foreach (string part in parts)
            {
                int slash = part.IndexOf('/');
                if (slash >= 0)
                {
                    if (!TryNumber(part.Substring(0, slash), out double n) || !TryNumber(part.Substring(slash + 1), out double d) || d == 0)
                        return false;
                    sum += n / d;
                }
                else
                {
                    if (!TryNumber(part, out double n)) return false;
                    sum += n;
                }
            }

            inches = sum;
            return true;
        }

        private static bool TryNumber(string text, out double value) =>
            double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
            && !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>Feet as <c>4'-6"</c>, to the nearest 1/16": <c>0'-6 1/2"</c>, <c>12'-0"</c>.</summary>
        public static string Feet(double feet)
        {
            if (double.IsNaN(feet) || double.IsInfinity(feet)) return "?";
            bool negative = feet < 0;
            long sixteenths = (long)Math.Round(Math.Abs(feet) * 12 * 16, MidpointRounding.AwayFromZero);
            long wholeFeet = sixteenths / 192;
            long rest = sixteenths % 192;
            long wholeInches = rest / 16;
            long frac = rest % 16;

            StringBuilder sb = new StringBuilder();
            if (negative && sixteenths != 0) sb.Append('-');
            sb.Append(wholeFeet).Append("'-").Append(wholeInches);
            if (frac != 0)
            {
                long a = frac, b = 16;
                while (a % 2 == 0)
                {
                    a /= 2;
                    b /= 2;
                }

                sb.Append(' ').Append(a).Append('/').Append(b);
            }

            sb.Append('"');
            return sb.ToString();
        }

        /// <summary>A trench width the way it is said on a plan: whole inches (<c>18"</c>), else with decimals (<c>19.5"</c>).</summary>
        public static string Width(double feet)
        {
            double inches = feet * 12;
            double rounded = Math.Round(inches, 2);
            return rounded.ToString("0.##", CultureInfo.InvariantCulture) + "\"";
        }

        /// <summary>A number written so any culture reads it back: shortest round-trip text.</summary>
        public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>A list of lengths as typed in one box: <c>4'-0", 8'-0"</c>.</summary>
        public static string FeetList(System.Collections.Generic.IEnumerable<double> values) =>
            string.Join(", ", values.Select(Feet));

        /// <summary>Reads a comma- or semicolon-separated list of lengths; false when any item is not one (blank is an empty list).</summary>
        public static bool TryParseFeetList(string? text, out System.Collections.Generic.List<double> values)
        {
            values = new System.Collections.Generic.List<double>();
            if (string.IsNullOrWhiteSpace(text)) return true;
            foreach (string item in text!.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (string.IsNullOrWhiteSpace(item)) continue;
                if (!TryParseFeet(item, out double feet)) return false;
                values.Add(feet);
            }

            return true;
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace BlakesTesting.Risers
{
    /// <summary>A colour with no Revit or System.Drawing in it, so the settings can be tested without either.</summary>
    public readonly struct RgbColor : IEquatable<RgbColor>
    {
        public RgbColor(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
        }

        public byte R { get; }
        public byte G { get; }
        public byte B { get; }

        public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";

        /// <summary>Reads #RRGGBB (the # is optional).</summary>
        public static bool TryParse(string? text, out RgbColor color)
        {
            color = default;
            if (text == null) return false;
            string t = text.Trim().TrimStart('#');
            if (t.Length != 6) return false;
            if (!int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v)) return false;
            color = new RgbColor((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            return true;
        }

        public bool Equals(RgbColor other) => R == other.R && G == other.G && B == other.B;
        public override bool Equals(object? obj) => obj is RgbColor c && Equals(c);
        public override int GetHashCode() => (R << 16) | (G << 8) | B;
    }

    /// <summary>
    ///     The testing aid that paints the pipes a run has just sorted: in the active view, the pipes it ROUNDED in one
    ///     colour and the ones it did NOT in another, as a solid fill, so a wrong riser is obvious at a glance. The
    ///     settings are saved per user in <see cref="DefaultPath"/>; the Riser PEX Color Settings command edits them.
    ///     Defaults: rounded = green, not rounded = red, on PEX only (the red on a copper main is noise when the point
    ///     is to check the riser PEX), lines coloured too (a surface fill shows in no wireframe view).
    /// </summary>
    public sealed class ColorSettings
    {
        /// <summary>The master switch. Off = nothing is coloured.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Colour the PEX pipes that were rounded (the riser).</summary>
        public bool ColorRounded { get; set; } = true;

        /// <summary>Colour the PEX pipes that were NOT rounded (distribution, branches, mains).</summary>
        public bool ColorOtherPex { get; set; } = true;

        /// <summary>Colour every other pipe too (copper, steel...) - they take the not-rounded colour.</summary>
        public bool ColorOtherPipes { get; set; } = false;

        /// <summary>Also colour the pipe's lines, not just its fill - so it shows in a coarse or wireframe view.</summary>
        public bool ColorLines { get; set; } = true;

        public RgbColor RoundedColor { get; set; } = new RgbColor(0, 200, 0);
        public RgbColor NotRoundedColor { get; set; } = new RgbColor(255, 0, 0);

        /// <summary>The colour a pipe gets, or null for "leave it alone".</summary>
        public RgbColor? Choose(bool rounded, bool isPex)
        {
            if (!Enabled) return null;
            if (rounded) return ColorRounded ? RoundedColor : (RgbColor?)null;
            if (isPex) return ColorOtherPex ? NotRoundedColor : (RgbColor?)null;
            return ColorOtherPipes ? NotRoundedColor : (RgbColor?)null;
        }

        public ColorSettings Clone() => new ColorSettings
        {
            Enabled = Enabled,
            ColorRounded = ColorRounded,
            ColorOtherPex = ColorOtherPex,
            ColorOtherPipes = ColorOtherPipes,
            ColorLines = ColorLines,
            RoundedColor = RoundedColor,
            NotRoundedColor = NotRoundedColor,
        };

        // ------------------------------------------------------------------ the file

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlakesTesting", "RiserPexColors.ini");

        public string ToText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Round Riser PEX - colouring settings (edit with the Riser PEX Color Settings command)");
            sb.AppendLine($"Enabled={Enabled}");
            sb.AppendLine($"ColorRounded={ColorRounded}");
            sb.AppendLine($"ColorOtherPex={ColorOtherPex}");
            sb.AppendLine($"ColorOtherPipes={ColorOtherPipes}");
            sb.AppendLine($"ColorLines={ColorLines}");
            sb.AppendLine($"RoundedColor={RoundedColor.ToHex()}");
            sb.AppendLine($"NotRoundedColor={NotRoundedColor.ToHex()}");
            return sb.ToString();
        }

        /// <summary>Reads settings text. Anything missing, unknown or unreadable keeps its default.</summary>
        public static ColorSettings Parse(string? text)
        {
            ColorSettings s = new ColorSettings();
            if (text == null) return s;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                bool b;
                RgbColor c;
                switch (key.ToLowerInvariant())
                {
                    case "enabled": if (bool.TryParse(value, out b)) s.Enabled = b; break;
                    case "colorrounded": if (bool.TryParse(value, out b)) s.ColorRounded = b; break;
                    case "colorotherpex": if (bool.TryParse(value, out b)) s.ColorOtherPex = b; break;
                    case "colorotherpipes": if (bool.TryParse(value, out b)) s.ColorOtherPipes = b; break;
                    case "colorlines": if (bool.TryParse(value, out b)) s.ColorLines = b; break;
                    case "roundedcolor": if (RgbColor.TryParse(value, out c)) s.RoundedColor = c; break;
                    case "notroundedcolor": if (RgbColor.TryParse(value, out c)) s.NotRoundedColor = c; break;
                }
            }

            return s;
        }

        /// <summary>The saved settings, or the defaults when there is no file or it cannot be read.</summary>
        public static ColorSettings Load(string? path = null)
        {
            try
            {
                string p = path ?? DefaultPath;
                return File.Exists(p) ? Parse(File.ReadAllText(p)) : new ColorSettings();
            }
            catch
            {
                return new ColorSettings();
            }
        }

        /// <summary>Saves the settings; false when the file could not be written.</summary>
        public bool Save(string? path = null)
        {
            try
            {
                string p = path ?? DefaultPath;
                string? dir = Path.GetDirectoryName(p);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(p, ToText());
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

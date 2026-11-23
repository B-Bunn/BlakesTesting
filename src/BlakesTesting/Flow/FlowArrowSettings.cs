#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BlakesTesting.Risers;

namespace BlakesTesting.Flow
{
    public enum FlowArrowStyle
    {
        /// <summary>A shaft with an open vee head, like a spot slope arrow.</summary>
        Arrow,

        /// <summary>Three vees in a row, no shaft.</summary>
        Chevrons,
    }

    public enum FlowArrowPosition
    {
        /// <summary>Beside the pipe, clear of it, the text beyond the arrow.</summary>
        Beside,

        /// <summary>Along the pipe's centre line, the text above it.</summary>
        On,

        /// <summary>The arrow beside the pipe and the text ON the pipe, on an opaque background so the pipe seems broken for it.
        ///          (In a 3D view the text is lines, which cannot hide the pipe, so it just sits on it.)</summary>
        InPipe,
    }

    public enum FlowArrowTextReading
    {
        /// <summary>The text runs along the pipe, upright: level on a horizontal pipe, bottom to top on a vertical one.</summary>
        WithPipe,

        /// <summary>The text is always level, whichever way the pipe runs.</summary>
        Horizontal,
    }

    public enum FlowArrowRunDirection
    {
        /// <summary>Flow Run: the arrows point toward the second click.</summary>
        TowardSecondClick,

        /// <summary>Flow Run: the arrows point away from the second click.</summary>
        AwayFromSecondClick,

        /// <summary>Flow Run: each pipe's slope decides, pointing toward its lower end.</summary>
        FollowSlope,
    }

    /// <summary>
    ///     Flow Arrow's settings, one per-user file (<see cref="DefaultPath"/>), edited in the window the command opens.
    ///     Sizes are PAPER inches, as a spot slope's are: the arrow and the text keep that size on the sheet at any view
    ///     scale. No Revit types in here, so it is tested without Revit.
    /// </summary>
    public sealed class FlowArrowSettings
    {
        public const int MaxRecent = 8;

        /// <summary>What the arrow says. Empty = an arrow with no text.</summary>
        public string Text { get; set; } = "FLOW";

        /// <summary>Texts used before, newest first, for the window's drop-down.</summary>
        public List<string> Recent { get; set; } = new List<string>();

        public FlowArrowStyle Style { get; set; } = FlowArrowStyle.Arrow;

        public FlowArrowPosition Position { get; set; } = FlowArrowPosition.Beside;

        public FlowArrowTextReading TextReading { get; set; } = FlowArrowTextReading.WithPipe;

        /// <summary>Flow Run: about how far apart the arrows are, feet of the model.</summary>
        public double RunSpacingFeet { get; set; } = 8;

        public FlowArrowRunDirection RunDirection { get; set; } = FlowArrowRunDirection.TowardSecondClick;

        /// <summary>Place on Selected Pipes: leave a pipe alone when it already has an arrow in the view.</summary>
        public bool SkipMarked { get; set; } = true;

        /// <summary>Arrow length on paper, inches.</summary>
        public double ArrowLengthInches { get; set; } = 0.5;

        /// <summary>Text height on paper, inches.</summary>
        public double TextSizeInches { get; set; } = 3.0 / 32;

        /// <summary>The arrow's and the text's color, or null for the line style's own color (black in most templates).</summary>
        public RgbColor? Color { get; set; }

        /// <summary>The line style (a subcategory of Lines) the arrow is drawn in; one the project lacks is skipped.</summary>
        public string LineStyle { get; set; } = "Medium Lines";

        /// <summary>The text with its line breaks and edges taken out: one line, as the arrow's label.</summary>
        public static string CleanText(string? text) =>
            (text ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();

        /// <summary>Puts the current text at the front of <see cref="Recent"/> (once, case-insensitively).</summary>
        public void Remember(string? text)
        {
            string t = CleanText(text);
            if (t.Length == 0) return;
            Recent.RemoveAll(r => string.Equals(r, t, StringComparison.OrdinalIgnoreCase));
            Recent.Insert(0, t);
            if (Recent.Count > MaxRecent) Recent.RemoveRange(MaxRecent, Recent.Count - MaxRecent);
        }

        /// <summary>Keeps every value in a range the drawing can use.</summary>
        public FlowArrowSettings Normalize()
        {
            Text = CleanText(Text);
            if (Text.Length > 40) Text = Text.Substring(0, 40);
            if (double.IsNaN(ArrowLengthInches) || ArrowLengthInches < 0.125) ArrowLengthInches = 0.125;
            if (ArrowLengthInches > 4) ArrowLengthInches = 4;
            if (double.IsNaN(TextSizeInches) || TextSizeInches < 1.0 / 32) TextSizeInches = 1.0 / 32;
            if (TextSizeInches > 0.75) TextSizeInches = 0.75;
            if (string.IsNullOrWhiteSpace(LineStyle)) LineStyle = "Medium Lines";
            if (double.IsNaN(RunSpacingFeet) || RunSpacingFeet < 1) RunSpacingFeet = 1;
            if (RunSpacingFeet > 200) RunSpacingFeet = 200;
            return this;
        }

        public FlowArrowSettings Clone() => new FlowArrowSettings
        {
            Text = Text,
            Recent = new List<string>(Recent),
            Style = Style,
            Position = Position,
            ArrowLengthInches = ArrowLengthInches,
            TextSizeInches = TextSizeInches,
            LineStyle = LineStyle,
            Color = Color,
            TextReading = TextReading,
            RunSpacingFeet = RunSpacingFeet,
            RunDirection = RunDirection,
            SkipMarked = SkipMarked,
        };

        // ------------------------------------------------------------------ the file

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlakesTesting", "FlowArrow.ini");

        public string ToText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Flow Arrow settings (edit in the window the Flow Arrow command opens)");
            sb.AppendLine("Text=" + Text);
            sb.AppendLine("Style=" + Style);
            sb.AppendLine("Position=" + Position);
            sb.AppendLine("ArrowLengthInches=" + ArrowLengthInches.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("TextSizeInches=" + TextSizeInches.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("LineStyle=" + LineStyle);
            sb.AppendLine("Color=" + (Color.HasValue ? Color.Value.ToHex() : "auto"));
            sb.AppendLine("TextReading=" + TextReading);
            sb.AppendLine("RunSpacingFeet=" + RunSpacingFeet.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            sb.AppendLine("RunDirection=" + RunDirection);
            sb.AppendLine("SkipMarked=" + SkipMarked);
            foreach (string r in Recent) sb.AppendLine("Recent=" + r);
            return sb.ToString();
        }

        /// <summary>Reads settings text. Anything missing, unknown or unreadable keeps its default.</summary>
        public static FlowArrowSettings Parse(string? text)
        {
            FlowArrowSettings s = new FlowArrowSettings();
            if (text == null) return s;
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.Trim().Length == 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();

                switch (key.ToLowerInvariant())
                {
                    case "text": s.Text = value; break;
                    case "recent": if (value.Length > 0 && s.Recent.Count < MaxRecent) s.Recent.Add(value); break;
                    case "style": if (Enum.TryParse(value, true, out FlowArrowStyle st)) s.Style = st; break;
                    case "position": if (Enum.TryParse(value, true, out FlowArrowPosition po)) s.Position = po; break;
                    case "arrowlengthinches":
                        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double len)) s.ArrowLengthInches = len;
                        break;
                    case "textsizeinches":
                        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double size)) s.TextSizeInches = size;
                        break;
                    case "linestyle": s.LineStyle = value; break;
                    case "textreading": if (Enum.TryParse(value, true, out FlowArrowTextReading tr)) s.TextReading = tr; break;
                    case "rundirection": if (Enum.TryParse(value, true, out FlowArrowRunDirection rd)) s.RunDirection = rd; break;
                    case "skipmarked": if (bool.TryParse(value, out bool sm)) s.SkipMarked = sm; break;
                    case "runspacingfeet":
                        if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double sp)) s.RunSpacingFeet = sp;
                        break;
                    case "color": s.Color = RgbColor.TryParse(value, out RgbColor c) ? c : (RgbColor?)null; break;
                }
            }

            return s.Normalize();
        }

        /// <summary>The saved settings, or the defaults when there is no file or it cannot be read.</summary>
        public static FlowArrowSettings Load(string? path = null)
        {
            try
            {
                string p = path ?? DefaultPath;
                return File.Exists(p) ? Parse(File.ReadAllText(p)) : new FlowArrowSettings();
            }
            catch
            {
                return new FlowArrowSettings();
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

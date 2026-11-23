#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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

        /// <summary>Arrow length on paper, inches.</summary>
        public double ArrowLengthInches { get; set; } = 0.5;

        /// <summary>Text height on paper, inches.</summary>
        public double TextSizeInches { get; set; } = 3.0 / 32;

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

#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace BlakesTesting.Trench
{
    /// <summary>Where the floor over a trench is read from (BIM Tools' Calculate Spoils has the same two).</summary>
    public enum TrenchFloorSource
    {
        /// <summary>One level's elevation, over the whole run.</summary>
        Level = 0,

        /// <summary>The floors whose chosen parameter reads the chosen value, at their real top over each point.</summary>
        Floors = 1,
    }

    /// <summary>What the depth half of the colour is.</summary>
    public enum TrenchColorBy
    {
        /// <summary>The spoils depth: the top of the trench (the floor less the offset) down to the bottom of the trench.</summary>
        SpoilsDepth = 0,

        /// <summary>The bottom of the pipe: the floor down to the bottom of the pipe (the offset and the dig below it left out).</summary>
        BottomOfPipe = 1,
    }

    /// <summary>One row of the width rule: a trench shallower than <see cref="ShallowerThan"/> is <see cref="Width"/> wide (feet).</summary>
    public sealed class TrenchWidthBand
    {
        public double ShallowerThan { get; set; }
        public double Width { get; set; }
    }

    /// <summary>
    ///     Trench Map's settings, one per-user file (<see cref="DefaultPath"/>) edited in the Trench Map Settings window.
    ///     Lengths are feet. The width rule, the offset and the dig below the pipe are BIM Tools' Calculate Spoils rules
    ///     (TPITools/TopconSurface/SpoilsRules.cs), so a trench here is as wide and as deep as the Spoils tool digs it
    ///     (without its sluff: the map is drawn at the bottom width). <see cref="Normalize"/> makes whatever a file held usable.
    /// </summary>
    public sealed class TrenchMapSettings
    {
        /// <summary>18": the width of a trench shallower than the first row's depth.</summary>
        public const double DefaultShallowWidth = 1.5;

        /// <summary>24": the width of a deeper trench.</summary>
        public const double DefaultDeeperWidth = 2.0;

        /// <summary>4'-0": the depth at which the default trench widens.</summary>
        public const double DefaultThreshold = 4.0;

        /// <summary>The steepest see-through the map takes: past it the trench colours barely show.</summary>
        public const int MaxTransparency = 90;

        /// <summary>How far below the top of the floor the trench starts (feet). Null until somebody gives one: the run asks.</summary>
        public double? FloorOffset { get; set; }

        public TrenchFloorSource Source { get; set; } = TrenchFloorSource.Level;

        /// <summary>The level whose elevation is the floor, by name, for <see cref="TrenchFloorSource.Level"/>.</summary>
        public string LevelName { get; set; } = "";

        /// <summary>The floors' parameter tested, by name, for <see cref="TrenchFloorSource.Floors"/>.</summary>
        public string FloorParameter { get; set; } = "";

        /// <summary>The value <see cref="FloorParameter"/> must read (trimmed, any case).</summary>
        public string FloorValue { get; set; } = "";

        /// <summary>The width rule, shallowest first; deeper than the last row is <see cref="DeeperWidth"/>.</summary>
        public List<TrenchWidthBand> WidthBands { get; set; } = DefaultBands();

        public double DeeperWidth { get; set; } = DefaultDeeperWidth;

        /// <summary>How far below the bottom of the pipe the trench is dug (bedding), feet. 0 digs to the bottom of the pipe.</summary>
        public double BelowPipe { get; set; }

        public TrenchColorBy ColorBy { get; set; } = TrenchColorBy.SpoilsDepth;

        /// <summary>
        ///     The depths the colours change at, feet, ascending: 4, 8, 12 makes bands under 4', 4' to 8', 8' to 12' and
        ///     12' and deeper. No marks is one band.
        /// </summary>
        public List<double> DepthMarks { get; set; } = new List<double> { 4, 8, 12 };

        /// <summary>How see-through the trench colours are, per cent (0 solid), so the pipes and floor plan show through.</summary>
        public int Transparency { get; set; } = 30;

        /// <summary>Put each combination's total trench length in the legend.</summary>
        public bool LegendFootage { get; set; } = true;

        public static List<TrenchWidthBand> DefaultBands() =>
            new List<TrenchWidthBand> { new TrenchWidthBand { ShallowerThan = DefaultThreshold, Width = DefaultShallowWidth } };

        public static bool IsPositive(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0;

        public TrenchMapSettings Clone() => new TrenchMapSettings
        {
            FloorOffset = FloorOffset,
            Source = Source,
            LevelName = LevelName,
            FloorParameter = FloorParameter,
            FloorValue = FloorValue,
            WidthBands = WidthBands.Select(b => new TrenchWidthBand { ShallowerThan = b.ShallowerThan, Width = b.Width }).ToList(),
            DeeperWidth = DeeperWidth,
            BelowPipe = BelowPipe,
            ColorBy = ColorBy,
            DepthMarks = DepthMarks.ToList(),
            Transparency = Transparency,
            LegendFootage = LegendFootage,
        };

        /// <summary>
        ///     Puts right whatever a hand-edited or older file held: blank names for missing ones, width rows that are not
        ///     positive numbers dropped and the rest sorted (no usable row at all is the default rule - none on purpose is
        ///     <see cref="DeeperWidth"/> at every depth, which an EMPTY list keeps), a deeper width that is not positive
        ///     back to 24", a negative or unreadable dig below the pipe back to 0, a negative offset forgotten (the run
        ///     asks again), depth marks sorted, distinct and positive, and the transparency kept between 0 and
        ///     <see cref="MaxTransparency"/>.
        /// </summary>
        public TrenchMapSettings Normalize()
        {
            LevelName = (LevelName ?? "").Trim();
            FloorParameter = (FloorParameter ?? "").Trim();
            FloorValue = (FloorValue ?? "").Trim();
            if (!Enum.IsDefined(typeof(TrenchFloorSource), Source)) Source = TrenchFloorSource.Level;
            if (!Enum.IsDefined(typeof(TrenchColorBy), ColorBy)) ColorBy = TrenchColorBy.SpoilsDepth;

            List<TrenchWidthBand>? given = WidthBands;
            List<TrenchWidthBand> bands = (given ?? new List<TrenchWidthBand>())
                .Where(b => b != null && IsPositive(b.ShallowerThan) && IsPositive(b.Width))
                .GroupBy(b => b.ShallowerThan).Select(g => g.First())
                .OrderBy(b => b.ShallowerThan)
                .ToList();
            WidthBands = given == null || (given.Count > 0 && bands.Count == 0) ? DefaultBands() : bands;
            if (!IsPositive(DeeperWidth)) DeeperWidth = DefaultDeeperWidth;
            if (double.IsNaN(BelowPipe) || double.IsInfinity(BelowPipe) || BelowPipe < 0) BelowPipe = 0;
            if (FloorOffset.HasValue && (double.IsNaN(FloorOffset.Value) || double.IsInfinity(FloorOffset.Value) || FloorOffset.Value < 0))
                FloorOffset = null;

            DepthMarks = (DepthMarks ?? new List<double>()).Where(IsPositive).Distinct().OrderBy(d => d).ToList();
            if (Transparency < 0) Transparency = 0;
            if (Transparency > MaxTransparency) Transparency = MaxTransparency;
            return this;
        }

        // ------------------------------------------------------------------ the rules

        /// <summary>
        ///     The width of a trench <paramref name="depth"/> deep: the first row the depth is shallower than, else the
        ///     deeper width. Exactly at a row's depth is deeper ("shallower than 4'" is 18"; 4'-0" is 24").
        /// </summary>
        public double WidthFor(double depth)
        {
            foreach (TrenchWidthBand band in WidthBands)
                if (depth < band.ShallowerThan) return band.Width;
            return DeeperWidth;
        }

        /// <summary>
        ///     What the colour's depth reads beyond the spoils depth: nothing for <see cref="TrenchColorBy.SpoilsDepth"/>;
        ///     for the bottom of the pipe, the floor offset less the dig below the pipe - the floor down to the bottom of
        ///     the pipe is the spoils depth plus that.
        /// </summary>
        public double MetricShift => ColorBy == TrenchColorBy.BottomOfPipe ? (FloorOffset ?? 0) - BelowPipe : 0;

        /// <summary>How many depth bands the colours have: one more than the marks.</summary>
        public int DepthBandCount => DepthMarks.Count + 1;

        /// <summary>The depth band a colour depth falls in: 0 is shallower than the first mark; a depth at a mark is the deeper band.</summary>
        public int BandOf(double metric)
        {
            int band = 0;
            foreach (double mark in DepthMarks)
            {
                if (metric >= mark) band++;
                else break;
            }

            return band;
        }

        /// <summary>The widths the rule can give, narrowest first.</summary>
        public List<double> Widths() =>
            WidthBands.Select(b => Math.Round(b.Width, 6)).Append(Math.Round(DeeperWidth, 6)).Distinct().OrderBy(w => w).ToList();

        /// <summary>The depth range of a band as the legend says it: "0'-0" to 4'-0"", "4'-0" to 8'-0"", "12'-0" and deeper".</summary>
        public string BandText(int band)
        {
            if (DepthMarks.Count == 0) return "any depth";
            double low = band <= 0 ? 0 : DepthMarks[Math.Min(band, DepthMarks.Count) - 1];
            if (band >= DepthMarks.Count) return TrenchText.Feet(DepthMarks[DepthMarks.Count - 1]) + " and deeper";
            return TrenchText.Feet(low) + " to " + TrenchText.Feet(DepthMarks[band]);
        }

        /// <summary>What the settings lack for a run, as the window says it, or null when a run can go ahead (the level's existence is the model's to say).</summary>
        public string? WhatIsMissing()
        {
            List<string> asks = new List<string>();
            if (!FloorOffset.HasValue) asks.Add("the floor offset - how far below the top of the floor the trench starts");
            if (Source == TrenchFloorSource.Level)
            {
                if (LevelName.Length == 0) asks.Add("the level the floor is at");
            }
            else if (FloorParameter.Length == 0 || FloorValue.Length == 0)
            {
                asks.Add("which floors are the floor (a parameter, and the value it reads)");
            }

            return asks.Count == 0 ? null : "Trench Map needs " + string.Join(", and ", asks) + ".";
        }

        // ------------------------------------------------------------------ the file

        public static string DefaultPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlakesTesting", "TrenchMap.ini");

        public string ToText()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# Trench Map settings (edit with the Trench Map Settings command). Lengths are feet.");
            sb.AppendLine("FloorOffset=" + (FloorOffset.HasValue ? TrenchText.Number(FloorOffset.Value) : ""));
            sb.AppendLine("Source=" + Source);
            sb.AppendLine("LevelName=" + LevelName);
            sb.AppendLine("FloorParameter=" + FloorParameter);
            sb.AppendLine("FloorValue=" + FloorValue);
            sb.AppendLine("WidthBands=" + string.Join(";", WidthBands.Select(b => TrenchText.Number(b.ShallowerThan) + ":" + TrenchText.Number(b.Width))));
            sb.AppendLine("DeeperWidth=" + TrenchText.Number(DeeperWidth));
            sb.AppendLine("BelowPipe=" + TrenchText.Number(BelowPipe));
            sb.AppendLine("ColorBy=" + ColorBy);
            sb.AppendLine("DepthMarks=" + string.Join(";", DepthMarks.Select(TrenchText.Number)));
            sb.AppendLine("Transparency=" + Transparency.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("LegendFootage=" + LegendFootage);
            return sb.ToString();
        }

        /// <summary>Reads settings text. Anything missing, unknown or unreadable keeps its default.</summary>
        public static TrenchMapSettings Parse(string? text)
        {
            TrenchMapSettings s = new TrenchMapSettings();
            if (text == null) return s.Normalize();
            foreach (string raw in text.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal)) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string value = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "flooroffset":
                        s.FloorOffset = TryNumber(value, out double offset) ? offset : (double?)null;
                        break;
                    case "source":
                        if (Enum.TryParse(value, true, out TrenchFloorSource source)) s.Source = source;
                        break;
                    case "levelname": s.LevelName = value; break;
                    case "floorparameter": s.FloorParameter = value; break;
                    case "floorvalue": s.FloorValue = value; break;
                    case "widthbands":
                        // Blank is a rule on purpose (one width at every depth); text with no usable row keeps the default rule.
                        List<TrenchWidthBand> bands = ParseBands(value);
                        if (value.Length == 0 || bands.Count > 0) s.WidthBands = bands;
                        break;
                    case "deeperwidth":
                        if (TryNumber(value, out double deeper)) s.DeeperWidth = deeper;
                        break;
                    case "belowpipe":
                        if (TryNumber(value, out double below)) s.BelowPipe = below;
                        break;
                    case "colorby":
                        if (Enum.TryParse(value, true, out TrenchColorBy by)) s.ColorBy = by;
                        break;
                    case "depthmarks":
                        List<double> marks = ParseNumbers(value);
                        if (value.Length == 0 || marks.Count > 0) s.DepthMarks = marks;
                        break;
                    case "transparency":
                        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int t)) s.Transparency = t;
                        break;
                    case "legendfootage":
                        if (bool.TryParse(value, out bool footage)) s.LegendFootage = footage;
                        break;
                }
            }

            return s.Normalize();
        }

        private static bool TryNumber(string text, out double value) =>
            double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsNaN(value) && !double.IsInfinity(value);

        private static List<double> ParseNumbers(string text)
        {
            List<double> values = new List<double>();
            foreach (string item in text.Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries))
                if (TryNumber(item.Trim(), out double v)) values.Add(v);
            return values;
        }

        private static List<TrenchWidthBand> ParseBands(string text)
        {
            List<TrenchWidthBand> bands = new List<TrenchWidthBand>();
            foreach (string item in text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair = item.Split(':');
                if (pair.Length == 2 && TryNumber(pair[0].Trim(), out double depth) && TryNumber(pair[1].Trim(), out double width))
                    bands.Add(new TrenchWidthBand { ShallowerThan = depth, Width = width });
            }

            return bands;
        }

        /// <summary>The saved settings, or the defaults when there is no file or it cannot be read.</summary>
        public static TrenchMapSettings Load(string? path = null)
        {
            try
            {
                string p = path ?? DefaultPath;
                return File.Exists(p) ? Parse(File.ReadAllText(p)) : new TrenchMapSettings();
            }
            catch
            {
                return new TrenchMapSettings();
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

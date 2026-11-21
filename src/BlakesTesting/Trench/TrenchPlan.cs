#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BlakesTesting.Risers;

namespace BlakesTesting.Trench
{
    /// <summary>
    ///     One pipe's trench line: the bottom of the pipe at its two ends (feet, project coordinates -
    ///     <see cref="TrenchRules.BottomOfPipe"/>), and how far past each end, in plan, the trench runs on to the centre
    ///     of the fitting there (0 where there is none). The same input BIM Tools' Calculate Spoils plans from.
    /// </summary>
    public sealed class TrenchRun
    {
        public long Id { get; set; }
        public (double X, double Y, double Z) BopStart { get; set; }
        public (double X, double Y, double Z) BopEnd { get; set; }
        public double ExtendStart { get; set; }
        public double ExtendEnd { get; set; }
    }

    /// <summary>What a piece of trench is coloured by: how wide it is dug, and which depth band it falls in.</summary>
    public readonly struct TrenchClass : IEquatable<TrenchClass>
    {
        public TrenchClass(double width, int depthBand)
        {
            Width = Math.Round(width, 6);
            DepthBand = depthBand;
        }

        /// <summary>The width the trench is dug at (feet).</summary>
        public double Width { get; }

        /// <summary>0 = shallower than the first depth mark.</summary>
        public int DepthBand { get; }

        public bool Equals(TrenchClass other) => Width.Equals(other.Width) && DepthBand == other.DepthBand;
        public override bool Equals(object? obj) => obj is TrenchClass c && Equals(c);
        public override int GetHashCode() => unchecked(Width.GetHashCode() * 397 ^ DepthBand);
    }

    /// <summary>
    ///     A box of trench with one width and one depth band, drawn as one filled rectangle: from A to B along the run,
    ///     <see cref="HalfWidth"/> each side. Depths are feet; the colour's depth (<see cref="MetricA"/>) is the spoils
    ///     depth or the bottom of the pipe's, whichever the settings colour by.
    /// </summary>
    public sealed class TrenchPiece
    {
        public long RunId { get; set; }
        public double Ax { get; set; }
        public double Ay { get; set; }
        public double Bx { get; set; }
        public double By { get; set; }
        public double HalfWidth { get; set; }
        public double DepthA { get; set; }
        public double DepthB { get; set; }
        public double MetricA { get; set; }
        public double MetricB { get; set; }
        public TrenchClass Class { get; set; }

        public double Width => 2 * HalfWidth;
        public double Length => Math.Sqrt((Bx - Ax) * (Bx - Ax) + (By - Ay) * (By - Ay));

        /// <summary>The four corners in plan, in order round the rectangle.</summary>
        public (double X, double Y)[] Corners()
        {
            double length = Length;
            double ux = length > 0 ? (Bx - Ax) / length : 1, uy = length > 0 ? (By - Ay) / length : 0;
            double nx = -uy * HalfWidth, ny = ux * HalfWidth;
            return new[] { (Ax + nx, Ay + ny), (Bx + nx, By + ny), (Bx - nx, By - ny), (Ax - nx, Ay - ny) };
        }
    }

    /// <summary>The trench laid out under the runs, and what was left out of it.</summary>
    public sealed class TrenchPlanResult
    {
        public List<TrenchPiece> Pieces { get; } = new List<TrenchPiece>();
        public int Runs { get; set; }
        public int RunsWithTrench { get; set; }

        /// <summary>Length of run (feet) with no floor over it, so no depth to colour.</summary>
        public double LengthNoFloor { get; set; }

        /// <summary>Length of run (feet) at or above the top of the trench: nothing is dug there.</summary>
        public double LengthAboveTop { get; set; }

        /// <summary>The deepest spoils depth found (feet).</summary>
        public double DeepestDepth { get; set; }

        public double TrenchLength => Pieces.Sum(p => p.Length);
    }

    /// <summary>The Revit-free rules of Trench Map. Plan and the pipe geometry are BIM Tools' Calculate Spoils', minus its volume count and sluff.</summary>
    public static class TrenchRules
    {
        /// <summary>How far apart along a run the depth is read, feet, besides at each pipe's ends.</summary>
        public const double StationStep = 1.0;

        /// <summary>A point with no floor found over it (a floor drain's hole) takes the floor of its neighbours this close (feet).</summary>
        public const double FloorGapBridged = 5.0;

        /// <summary>A pipe whose plan length is less than this is plumb and has no trench line (BIM Tools' PlumbInPlan).</summary>
        public const double PlumbInPlan = 1e-6;

        /// <summary>Pipes steeper than this (radians, about 45 degrees) are risers: they rise out of the trench and have none.</summary>
        public const double SteepestTrenchPipe = 0.78;

        /// <summary>True for a pipe that lies in a trench: not plumb, and no steeper than 45 degrees.</summary>
        public static bool IsTrenchPipe(double dx, double dy, double dz)
        {
            double plan = Math.Sqrt(dx * dx + dy * dy);
            if (double.IsNaN(plan) || double.IsNaN(dz) || plan < PlumbInPlan) return false;
            return Math.Atan2(Math.Abs(dz), plan) < SteepestTrenchPipe;
        }

        /// <summary>
        ///     The bottom of a pipe's outside at one of its ends: the end moved out from the axis by the outside radius,
        ///     square to the axis and straight down in the vertical plane through it - the line Topcon Surface draws and
        ///     Calculate Spoils measures depth to (TopconSurfaceRules.BottomOfPipe in BIM Tools, the same arithmetic).
        /// </summary>
        public static (double X, double Y, double Z) BottomOfPipe(double ex, double ey, double ez,
            double ux, double uy, double uz, double outsideDiameter)
        {
            double r = outsideDiameter / 2;
            double length = Math.Sqrt(ux * ux + uy * uy + uz * uz);
            if (!(length > 0) || double.IsInfinity(length)) return (ex, ey, ez - r);

            double x = ux / length, y = uy / length, z = uz / length;
            double plan = Math.Sqrt(x * x + y * y);
            if (plan < 1e-12) return (ex, ey, ez - r);
            return (ex + r * z * x / plan, ey + r * z * y / plan, ez - r * plan);
        }

        /// <summary>
        ///     The floor a trench bottom at <paramref name="z"/> is dug from, out of the floor tops found straight over and
        ///     under the point (BIM Tools' SpoilsRules.FloorOver): the highest top at or below the bottom - a pipe sitting
        ///     above a floor digs nothing there - else the lowest top above it, the slab over a buried pipe.
        /// </summary>
        public static double? FloorOver(IEnumerable<double> tops, double z)
        {
            double? under = null, over = null;
            foreach (double top in tops)
            {
                if (double.IsNaN(top) || double.IsInfinity(top)) continue;
                if (top <= z)
                {
                    if (under == null || top > under.Value) under = top;
                }
                else if (over == null || top < over.Value)
                {
                    over = top;
                }
            }

            return under ?? over;
        }

        /// <summary>
        ///     The missing tops in <paramref name="tops"/> (null where no floor was found), filled straight between the found
        ///     tops either side when those are no more than twice <paramref name="bridge"/> apart along the run; otherwise
        ///     each found top is carried <paramref name="bridge"/> into the gap. What is left stays null: no floor there.
        /// </summary>
        public static double?[] BridgeGaps(IReadOnlyList<double> along, IReadOnlyList<double?> tops, double bridge)
        {
            double?[] filled = tops.ToArray();
            for (int i = 0; i < filled.Length; i++)
            {
                if (filled[i].HasValue) continue;
                int before = -1, after = -1;
                for (int j = i - 1; j >= 0; j--)
                    if (tops[j].HasValue)
                    {
                        before = j;
                        break;
                    }

                for (int j = i + 1; j < tops.Count; j++)
                    if (tops[j].HasValue)
                    {
                        after = j;
                        break;
                    }

                if (before >= 0 && after >= 0)
                {
                    double gap = along[after] - along[before];
                    if (gap > 0 && gap <= 2 * bridge)
                    {
                        double f = (along[i] - along[before]) / gap;
                        filled[i] = tops[before]!.Value + f * (tops[after]!.Value - tops[before]!.Value);
                    }
                    else if (along[i] - along[before] <= bridge) filled[i] = tops[before];
                    else if (along[after] - along[i] <= bridge) filled[i] = tops[after];
                }
                else if (before >= 0)
                {
                    if (along[i] - along[before] <= bridge) filled[i] = tops[before];
                }
                else if (after >= 0)
                {
                    if (along[after] - along[i] <= bridge) filled[i] = tops[after];
                }
            }

            return filled;
        }

        /// <summary>
        ///     Lays the trench out along every run: calculation points at each pipe's ends (out to the fittings' centres) and
        ///     every <see cref="StationStep"/> between, the top of the trench read at each through
        ///     <paramref name="topOfTrenchAt"/> (x, y, trench bottom; null where there is no floor), and boxes between
        ///     neighbouring points split wherever the depth crosses a row of the width rule, a mark between the colours' depth
        ///     bands or the top of the trench, so each box has ONE width and ONE depth band. Neighbouring boxes of one run
        ///     with the same class become one, and each run's first and last box run on past the run's ends by half their
        ///     width, so a bend or a riser's corner is covered square.
        /// </summary>
        public static TrenchPlanResult Plan(IEnumerable<TrenchRun> runs, TrenchMapSettings settings,
            Func<double, double, double, double?> topOfTrenchAt, double step = StationStep)
        {
            if (!TrenchMapSettings.IsPositive(step)) step = StationStep;
            TrenchPlanResult result = new TrenchPlanResult();
            double shift = settings.MetricShift;
            foreach (TrenchRun run in runs)
            {
                result.Runs++;
                double dx = run.BopEnd.X - run.BopStart.X, dy = run.BopEnd.Y - run.BopStart.Y;
                double length = Math.Sqrt(dx * dx + dy * dy);
                if (!(length > PlumbInPlan)) continue;

                double ux = dx / length, uy = dy / length;
                double fall = (run.BopEnd.Z - run.BopStart.Z) / length;
                double from = -Math.Max(0, Finite(run.ExtendStart));
                double to = length + Math.Max(0, Finite(run.ExtendEnd));

                List<double> along = Stations(from, to, length, step);
                double?[] rawTops = new double?[along.Count];
                double[] bottoms = new double[along.Count];
                for (int i = 0; i < along.Count; i++)
                {
                    double s = along[i];
                    bottoms[i] = run.BopStart.Z + fall * s - settings.BelowPipe;
                    double? top = topOfTrenchAt(run.BopStart.X + ux * s, run.BopStart.Y + uy * s, bottoms[i]);
                    rawTops[i] = top.HasValue && !double.IsNaN(top.Value) && !double.IsInfinity(top.Value) ? top : null;
                }

                double?[] tops = BridgeGaps(along, rawTops, FloorGapBridged);
                for (int i = 0; i < along.Count; i++)
                    if (tops[i].HasValue) result.DeepestDepth = Math.Max(result.DeepestDepth, tops[i]!.Value - bottoms[i]);

                List<TrenchPiece> pieces = new List<TrenchPiece>();
                for (int i = 0; i + 1 < along.Count; i++)
                {
                    double span = along[i + 1] - along[i];
                    if (!tops[i].HasValue || !tops[i + 1].HasValue)
                    {
                        result.LengthNoFloor += span;
                        continue;
                    }

                    double d0 = tops[i]!.Value - bottoms[i], d1 = tops[i + 1]!.Value - bottoms[i + 1];

                    // Where along this span (0 to 1) the depth crosses a mark that changes the width or the colour.
                    List<double> cuts = new List<double> { 0, 1 };
                    IEnumerable<double> marks = new[] { 0.0 }
                        .Concat(settings.WidthBands.Select(b => b.ShallowerThan))
                        .Concat(settings.DepthMarks.Select(m => m - shift));
                    foreach (double depthMark in marks)
                    {
                        if (Math.Abs(d1 - d0) < 1e-12) continue;
                        double f = (depthMark - d0) / (d1 - d0);
                        if (f > 1e-9 && f < 1 - 1e-9) cuts.Add(f);
                    }

                    cuts.Sort();
                    for (int c = 0; c + 1 < cuts.Count; c++)
                    {
                        double fa = cuts[c], fb = cuts[c + 1];
                        if (fb - fa < 1e-12) continue;
                        double middle = d0 + (d1 - d0) * (fa + fb) / 2;
                        double sa = along[i] + span * fa, sb = along[i] + span * fb;
                        if (!(middle > 0))
                        {
                            result.LengthAboveTop += sb - sa;
                            continue;
                        }

                        double width = settings.WidthFor(middle);
                        double da = Lerp(d0, d1, fa), db = Lerp(d0, d1, fb);
                        pieces.Add(new TrenchPiece
                        {
                            RunId = run.Id,
                            Ax = run.BopStart.X + ux * sa,
                            Ay = run.BopStart.Y + uy * sa,
                            Bx = run.BopStart.X + ux * sb,
                            By = run.BopStart.Y + uy * sb,
                            HalfWidth = width / 2,
                            DepthA = da,
                            DepthB = db,
                            MetricA = da + shift,
                            MetricB = db + shift,
                            Class = new TrenchClass(width, settings.BandOf(middle + shift)),
                        });
                    }
                }

                if (pieces.Count == 0) continue;
                result.RunsWithTrench++;
                pieces = Merge(pieces);

                // Square the run's two ends: a box that starts at the run's first point, or ends at its last, runs on by
                // half its width.
                TrenchPiece first = pieces[0], last = pieces[pieces.Count - 1];
                if (Near(first.Ax, first.Ay, run.BopStart.X + ux * from, run.BopStart.Y + uy * from))
                    Stretch(first, atA: true, by: first.HalfWidth);
                if (Near(last.Bx, last.By, run.BopStart.X + ux * to, run.BopStart.Y + uy * to))
                    Stretch(last, atA: false, by: last.HalfWidth);

                result.Pieces.AddRange(pieces);
            }

            return result;
        }

        private static List<double> Stations(double from, double to, double length, double step)
        {
            List<double> along = new List<double> { from };
            for (double s = from + step; s < to - 1e-6; s += step) along.Add(s);
            along.Add(to);
            foreach (double end in new[] { 0.0, length })
                if (end > from + 1e-6 && end < to - 1e-6 && !along.Any(a => Math.Abs(a - end) < 1e-6))
                    along.Add(end);
            along.Sort();
            return along;
        }

        /// <summary>Neighbouring boxes of one run that touch and are the same class become one box, so a level run is one box, not one a foot.</summary>
        private static List<TrenchPiece> Merge(List<TrenchPiece> pieces)
        {
            List<TrenchPiece> merged = new List<TrenchPiece> { pieces[0] };
            for (int i = 1; i < pieces.Count; i++)
            {
                TrenchPiece a = merged[merged.Count - 1], b = pieces[i];
                if (Near(a.Bx, a.By, b.Ax, b.Ay) && a.Class.Equals(b.Class))
                {
                    a.Bx = b.Bx;
                    a.By = b.By;
                    a.DepthB = b.DepthB;
                    a.MetricB = b.MetricB;
                }
                else
                {
                    merged.Add(b);
                }
            }

            return merged;
        }

        private static void Stretch(TrenchPiece p, bool atA, double by)
        {
            double length = p.Length;
            if (!(length > 0) || !(by > 0)) return;
            double f = by / length;
            if (atA)
            {
                p.Ax -= (p.Bx - p.Ax) * f;
                p.Ay -= (p.By - p.Ay) * f;
            }
            else
            {
                p.Bx += (p.Bx - p.Ax) * f;
                p.By += (p.By - p.Ay) * f;
            }
        }

        private static double Lerp(double a, double b, double f) => a + (b - a) * f;
        private static bool Near(double ax, double ay, double bx, double by) => Math.Abs(ax - bx) < 1e-6 && Math.Abs(ay - by) < 1e-6;
        private static double Finite(double value) => double.IsNaN(value) || double.IsInfinity(value) ? 0 : value;
    }

    /// <summary>One line of the legend: a colour, what it means, and how much trench is that colour.</summary>
    public sealed class TrenchLegendRow
    {
        public TrenchLegendRow(TrenchClass trenchClass, RgbColor color, string text, double length)
        {
            Class = trenchClass;
            Color = color;
            Text = text;
            Length = length;
        }

        public TrenchClass Class { get; }
        public RgbColor Color { get; }

        /// <summary>The line as the legend says it: <c>18" wide, 0'-0" to 4'-0" deep - 120'-0" LF</c>.</summary>
        public string Text { get; }

        /// <summary>Centreline length of that colour (feet).</summary>
        public double Length { get; }
    }

    /// <summary>The colours and the legend. One colour per width and depth-band combination, fixed by the settings alone, so a combination is the same colour from one run to the next.</summary>
    public static class TrenchLegend
    {
        private static readonly RgbColor[] Palette =
        {
            new RgbColor(31, 119, 180), new RgbColor(255, 127, 14), new RgbColor(44, 160, 44), new RgbColor(214, 39, 40),
            new RgbColor(148, 103, 189), new RgbColor(140, 86, 75), new RgbColor(227, 119, 194), new RgbColor(127, 127, 127),
            new RgbColor(188, 189, 34), new RgbColor(23, 190, 207), new RgbColor(255, 215, 0), new RgbColor(0, 128, 128),
        };

        /// <summary>
        ///     The colour of colour number <paramref name="index"/> of <paramref name="total"/>: a fixed table of twelve
        ///     for a few combinations; for more, a ramp from blue (shallow) through green and yellow to red (deep) in the
        ///     order the combinations are numbered, every other one a shade lighter so neighbours stay apart.
        /// </summary>
        public static RgbColor ColorAt(int index, int total)
        {
            if (index < 0) index = 0;
            if (total <= Palette.Length) return Palette[index % Palette.Length];
            double t = total > 1 ? Math.Min(index, total - 1) / (double)(total - 1) : 0;
            return FromHsl(240.0 * (1 - t), 0.85, index % 2 == 0 ? 0.42 : 0.58);
        }

        /// <summary>The colour a class is drawn in: its place in <see cref="TrenchMapSettings.ReachableClasses"/>.</summary>
        public static RgbColor ColorOf(TrenchMapSettings settings, TrenchClass c)
        {
            List<TrenchClass> all = settings.ReachableClasses();
            int index = all.FindIndex(x => x.Equals(c));
            if (index < 0)
            {
                // A combination the rule cannot give (a hand-built class): next to the others of its band.
                index = all.FindLastIndex(x => x.DepthBand <= c.DepthBand);
                if (index < 0) index = 0;
            }

            return ColorAt(index, Math.Max(1, all.Count));
        }

        /// <summary>
        ///     The legend's rows, narrowest width first and shallowest band first within it, for the combinations the pieces
        ///     actually use. <paramref name="lengthText"/> writes a length in feet the way the legend says it.
        /// </summary>
        public static List<TrenchLegendRow> Rows(TrenchMapSettings settings, IEnumerable<TrenchPiece> pieces, Func<double, string> lengthText)
        {
            Dictionary<TrenchClass, double> lengths = new Dictionary<TrenchClass, double>();
            foreach (TrenchPiece p in pieces)
            {
                lengths.TryGetValue(p.Class, out double sofar);
                lengths[p.Class] = sofar + p.Length;
            }

            List<TrenchLegendRow> rows = new List<TrenchLegendRow>();
            foreach (KeyValuePair<TrenchClass, double> pair in lengths.OrderBy(k => k.Key.Width).ThenBy(k => k.Key.DepthBand))
            {
                string text = TrenchText.Width(pair.Key.Width) + " wide, " + settings.BandText(pair.Key.DepthBand) + " deep";
                if (settings.LegendFootage) text += " - " + lengthText(pair.Value);
                rows.Add(new TrenchLegendRow(pair.Key, ColorOf(settings, pair.Key), text, pair.Value));
            }

            return rows;
        }

        /// <summary>The legend's title, and the line under it saying what the depth is measured to.</summary>
        public static (string Title, string Subtitle) Heading(TrenchMapSettings settings)
        {
            return settings.ColorBy == TrenchColorBy.BottomOfPipe
                ? ("TRENCH MAP - BOTTOM OF PIPE", "Color = trench width x depth from the floor to the bottom of the pipe")
                : ("TRENCH MAP - SPOILS", "Color = trench width x depth from the top of the trench to the bottom of the trench");
        }

        private static RgbColor FromHsl(double h, double s, double l)
        {
            double c = (1 - Math.Abs(2 * l - 1)) * s;
            double x = c * (1 - Math.Abs(h / 60.0 % 2 - 1));
            double m = l - c / 2;
            double r, g, b;
            if (h < 60) { r = c; g = x; b = 0; }
            else if (h < 120) { r = x; g = c; b = 0; }
            else if (h < 180) { r = 0; g = c; b = x; }
            else if (h < 240) { r = 0; g = x; b = c; }
            else if (h < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }
            return new RgbColor((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
        }
    }
}

#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlakesTesting.Tags
{
    /// <summary>Which side(s) of where a tag is now it may be moved to.</summary>
    public enum SpreadSide
    {
        /// <summary>Whichever free spot is nearest, left or right.</summary>
        Both,
        Left,
        Right,
    }

    /// <summary>
    ///     Knobs for <see cref="TagLayout" />. Lengths are PAPER inches (what you would measure on a printed sheet), so
    ///     a layout reads the same at 1/8" and 1/48" scale; <see cref="TagLayout" /> multiplies by the view scale.
    /// </summary>
    public sealed class SpreadOptions
    {
        public SpreadSide Side { get; set; } = SpreadSide.Both;

        /// <summary>Label height when the tag's real size cannot be read (a tag with a leader).</summary>
        public double LabelHeight { get; set; } = 0.2;

        /// <summary>Label width per character, for the same fallback.</summary>
        public double CharWidth { get; set; } = 0.13;

        /// <summary>Clear space kept around every label.</summary>
        public double Padding { get; set; } = 0.02;

        /// <summary>How far a moved tag sits off its old position, at least, so it clears what it was piled on.</summary>
        public double MinOffset { get; set; } = 0.1;

        /// <summary>Step between the columns tried beside the pile.</summary>
        public double ColumnStep { get; set; } = 0.1;

        /// <summary>How many columns out and how many rows up and down are tried before a tag is left where it is.</summary>
        public int MaxColumns { get; set; } = 10;

        public int MaxRows { get; set; } = 40;

        /// <summary>
        ///     The furthest a tag may be moved from where it is now. A tag with no free spot that close is left alone
        ///     (and counted) rather than sent somewhere its leader would run across the drawing.
        /// </summary>
        public double MaxDistance { get; set; } = 1.0;

        /// <summary>
        ///     How much more a vertical move costs than a horizontal one. 1 packs tags into the tightest blob; above 1
        ///     they stay level with what they tag and leaders run near-horizontal, but spread further sideways.
        /// </summary>
        public double VerticalWeight { get; set; } = 1.0;

        /// <summary>
        ///     How much a label sitting on a pipe, fitting or other drawing line is worth avoiding, as a distance: a spot
        ///     on the drawing has to be this much (paper inches) nearer than a clear one to win. 0 ignores the drawing.
        /// </summary>
        public double GeometryPenalty { get; set; } = 0.5;

        /// <summary>After placing, swap pairs of tags whose leaders cross so that they do not.</summary>
        public bool Untangle { get; set; } = true;
    }

    /// <summary>One tag going in: where its head is now, how big its label is, and whether it may be moved.</summary>
    public sealed class TagItem
    {
        public TagItem(int id, double right, double up, double width, double height, bool movable = true)
        {
            Id = id;
            Right = right;
            Up = up;
            Width = width;
            Height = height;
            Movable = movable;
        }

        public int Id { get; }

        /// <summary>The label's centre, in view coordinates (model feet): Right along the view's right direction.</summary>
        public double Right { get; }

        public double Up { get; }
        public double Width { get; }
        public double Height { get; }
        public bool Movable { get; }
    }

    /// <summary>One tag coming out: where to move it to (view coordinates), and whether that is a move at all.</summary>
    public sealed class TagPlacement
    {
        public TagPlacement(int id, double right, double up, bool moved, bool stuck = false)
        {
            Id = id;
            Right = right;
            Up = up;
            Moved = moved;
            Stuck = stuck;
        }

        public int Id { get; }
        public double Right { get; }
        public double Up { get; }
        public bool Moved { get; }

        /// <summary>It overlapped another tag but no free spot within <see cref="SpreadOptions.MaxDistance" /> was found.</summary>
        public bool Stuck { get; }
    }

    /// <summary>A line in the drawing a label should keep off: a pipe's run, or an edge of a fitting's box. View coordinates.</summary>
    public readonly struct DrawingLine
    {
        public DrawingLine(double x1, double y1, double x2, double y2)
        {
            X1 = x1;
            Y1 = y1;
            X2 = x2;
            Y2 = y2;
        }

        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }
    }

    /// <summary>What the layout did, for the report and the tests.</summary>
    public sealed class LayoutStats
    {
        public int CrossingsBefore { get; set; }
        public int CrossingsAfter { get; set; }
        public int OnDrawing { get; set; }
    }

    /// <summary>
    ///     The layout rule, with no Revit types in it so it can be tested without Revit (<c>tests/TagLayoutTest</c>).
    ///     <para>
    ///         A tag is only touched if its label overlaps another tag's. Those tags are then placed one at a time -
    ///         highest first - at the free spot nearest where each is now, beside the pile, on a left or right column
    ///         and level with its old height where it can be. "Free" means clear of every tag that is not moving and
    ///         of every tag already placed. A tag with no free spot within reach is left alone.
    ///     </para>
    /// </summary>
    public static class TagLayout
    {
        public static List<TagPlacement> Compute(IReadOnlyList<TagItem> items, SpreadOptions options, double viewScale, IReadOnlyList<DrawingLine>? obstacles = null, LayoutStats? stats = null)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (viewScale <= 0) throw new ArgumentOutOfRangeException(nameof(viewScale));

            double ft = viewScale / 12.0; // paper inches -> model feet
            double pad = options.Padding * ft;
            double minOffset = options.MinOffset * ft;
            double colStep = Math.Max(1e-6, options.ColumnStep) * ft;
            double rowStep = (items.Count == 0 ? options.LabelHeight : items.Max(t => t.Height)) + pad * 2;

            // Which tags are piled: those whose (padded) label touches another's.
            var all = new BoxGrid(Math.Max(rowStep, 1e-3) * 2);
            for (int i = 0; i < items.Count; i++) all.Add(i, MakeBox(items[i], items[i].Right, items[i].Up, pad));
            var piled = new HashSet<int>();
            for (int i = 0; i < items.Count; i++)
            {
                if (!items[i].Movable) continue;
                Box mine = MakeBox(items[i], items[i].Right, items[i].Up, pad);
                if (all.Query(mine).Any(j => j != i)) piled.Add(i);
            }

            // Obstacles: everything that is not being moved, where it is.
            var grid = new BoxGrid(Math.Max(rowStep, 1e-3) * 2);
            for (int i = 0; i < items.Count; i++)
                if (!piled.Contains(i)) grid.Add(i, MakeBox(items[i], items[i].Right, items[i].Up, pad));

            // The drawing's lines, hashed so a label's box asks only about the few near it.
            var lines = new DrawingLineGrid(Math.Max(rowStep, 1e-3) * 4);
            if (obstacles != null && options.GeometryPenalty > 0)
                foreach (DrawingLine seg in obstacles) lines.Add(seg);
            double penalty = options.GeometryPenalty * ft;

            // Spots to try, nearest first. dx is measured from the label's own edge (added per tag below).
            var spots = new List<(double dx, double dy, double cost)>();
            var sides = options.Side == SpreadSide.Left ? new[] { -1 } : options.Side == SpreadSide.Right ? new[] { 1 } : new[] { -1, 1 };
            foreach (int side in sides)
            {
                for (int c = 0; c < Math.Max(1, options.MaxColumns); c++)
                {
                    for (int r = -options.MaxRows; r <= options.MaxRows; r++)
                    {
                        double dx = side * (minOffset + c * colStep);
                        double dy = r * rowStep;
                        double cost = Math.Sqrt(dx * dx + options.VerticalWeight * options.VerticalWeight * dy * dy);
                        if (Math.Sqrt(dx * dx + dy * dy) > options.MaxDistance * ft) continue; // too far to be worth it
                        spots.Add((dx, dy, cost));
                    }
                }
            }

            // Ties go to the left (the way tags are usually tidied), then the smaller step.
            spots.Sort((a, b) =>
            {
                int byCost = a.cost.CompareTo(b.cost);
                if (byCost != 0) return byCost;
                return a.dx.CompareTo(b.dx);
            });

            var placements = new TagPlacement[items.Count];
            var spotHits = new Dictionary<int, bool>();
            foreach (int i in piled.OrderByDescending(k => items[k].Up).ThenBy(k => items[k].Right).ThenBy(k => items[k].Id))
            {
                TagItem t = items[i];
                double halfW = t.Width / 2;
                double best = double.MaxValue, bestRight = t.Right, bestUp = t.Up;
                bool found = false, bestHits = false;
                foreach (var spot in spots)
                {
                    if (spot.cost >= best) break; // every later spot costs at least this much more
                    double right = t.Right + spot.dx + Math.Sign(spot.dx) * halfW;
                    double up = t.Up + spot.dy;
                    if (grid.Query(MakeBox(t, right, up, pad)).Any()) continue;
                    bool hits = penalty > 0 && lines.Hits(MakeBox(t, right, up, 0));
                    double total = spot.cost + (hits ? penalty : 0);
                    if (total < best)
                    {
                        best = total;
                        bestRight = right;
                        bestUp = up;
                        bestHits = hits;
                        found = true;
                    }
                }

                if (found)
                {
                    grid.Add(i, MakeBox(t, bestRight, bestUp, pad));
                    placements[i] = new TagPlacement(t.Id, bestRight, bestUp, true);
                    spotHits[i] = bestHits;
                }
                else
                {
                    grid.Add(i, MakeBox(t, t.Right, t.Up, pad));
                    placements[i] = new TagPlacement(t.Id, t.Right, t.Up, false, stuck: true);
                }
            }

            if (options.Untangle)
                Untangle(items, placements, piled, grid, lines, penalty > 0, spotHits, options, ft, pad, stats);
            if (stats != null) stats.OnDrawing = spotHits.Values.Count(v => v);

            for (int i = 0; i < items.Count; i++)
                placements[i] ??= new TagPlacement(items[i].Id, items[i].Right, items[i].Up, false);
            return placements.ToList();
        }

        /// <summary>
        ///     Leaders run from where a tag was (close to what it tags) to where it is now. Wherever two leaders cross, try
        ///     swapping the two tags' places: keep the swap if it removes crossings (or, with the same crossings, makes
        ///     the leaders shorter) and the tags still fit - clear of every other tag, inside the reach, on their side,
        ///     and no more on the drawing than before. Repeats until nothing improves.
        /// </summary>
        private static void Untangle(IReadOnlyList<TagItem> items, TagPlacement[] placements, HashSet<int> piled, BoxGrid grid, DrawingLineGrid lines,
            bool avoidDrawing, Dictionary<int, bool> onDrawing, SpreadOptions options, double ft, double pad, LayoutStats? stats)
        {
            List<int> moved = piled.Where(i => placements[i].Moved).OrderBy(i => i).ToList();
            double reach = options.MaxDistance * ft;
            double near = 2 * (reach + (moved.Count == 0 ? 0 : moved.Max(i => items[i].Width)));
            var pos = new Dictionary<int, (double r, double u)>();
            foreach (int i in moved) pos[i] = (placements[i].Right, placements[i].Up);

            var neighbours = new Dictionary<int, List<int>>();
            foreach (int i in moved)
                neighbours[i] = moved.Where(j => j != i && Math.Abs(items[i].Right - items[j].Right) < near && Math.Abs(items[i].Up - items[j].Up) < near).ToList();

            // Leaders as segments: from the old position to the label's near edge (its centre, shortened by half a label).
            DrawingLine Leader(int i, (double r, double u) p) => new DrawingLine(items[i].Right, items[i].Up, p.r, p.u);

            int Crossings(int i, (double r, double u) pi, IEnumerable<int> others, Func<int, (double r, double u)> where)
            {
                int n = 0;
                DrawingLine a = Leader(i, pi);
                foreach (int j in others) if (Cross(a, Leader(j, where(j)))) n++;
                return n;
            }

            int total = 0;
            foreach (int i in moved) foreach (int j in neighbours[i]) if (i < j && Cross(Leader(i, pos[i]), Leader(j, pos[j]))) total++;
            if (stats != null) stats.CrossingsBefore = total;

            double Length(int i, (double r, double u) p) => Math.Sqrt(Math.Pow(p.r - items[i].Right, 2) + Math.Pow(p.u - items[i].Up, 2));

            bool Side(int i, (double r, double u) p) =>
                options.Side == SpreadSide.Both || (options.Side == SpreadSide.Left ? p.r < items[i].Right : p.r > items[i].Right);

            for (int pass = 0; pass < 6; pass++)
            {
                bool improved = false;
                foreach (int i in moved)
                {
                    foreach (int j in neighbours[i])
                    {
                        if (j < i) continue;
                        var pi = pos[i];
                        var pj = pos[j];
                        if (!Cross(Leader(i, pi), Leader(j, pj)) && Math.Abs(Length(i, pi) + Length(j, pj) - Length(i, pj) - Length(j, pi)) < 1e-9) continue;

                        var others = neighbours[i].Union(neighbours[j]).Where(k => k != i && k != j).ToList();
                        Func<int, (double r, double u)> at = k => pos[k];
                        int before = Crossings(i, pi, new[] { j }, k => pj) + Crossings(i, pi, others, at) + Crossings(j, pj, others, at);
                        int after = Crossings(i, pj, new[] { j }, k => pi) + Crossings(i, pj, others, at) + Crossings(j, pi, others, at);
                        double lenBefore = Length(i, pi) + Length(j, pj), lenAfter = Length(i, pj) + Length(j, pi);
                        if (!(after < before || (after == before && lenAfter < lenBefore - 1e-6))) continue;

                        // Would the swapped tags still fit and stay in bounds?
                        if (Length(i, pj) > reach + items[i].Width || Length(j, pi) > reach + items[j].Width) continue;
                        if (!Side(i, pj) || !Side(j, pi)) continue;
                        Box oldI = MakeBox(items[i], pi.r, pi.u, pad), oldJ = MakeBox(items[j], pj.r, pj.u, pad);
                        grid.Remove(i, oldI);
                        grid.Remove(j, oldJ);
                        Box newI = MakeBox(items[i], pj.r, pj.u, pad), newJ = MakeBox(items[j], pi.r, pi.u, pad);
                        bool fits = !grid.Query(newI).Any() && !grid.Query(newJ).Any() && !newI.Overlaps(newJ);
                        bool hitI = false, hitJ = false;
                        if (fits && avoidDrawing)
                        {
                            hitI = lines.Hits(MakeBox(items[i], pj.r, pj.u, 0));
                            hitJ = lines.Hits(MakeBox(items[j], pi.r, pi.u, 0));
                            bool wasI = onDrawing.TryGetValue(i, out bool a) && a, wasJ = onDrawing.TryGetValue(j, out bool b) && b;
                            if ((hitI ? 1 : 0) + (hitJ ? 1 : 0) > (wasI ? 1 : 0) + (wasJ ? 1 : 0)) fits = false;
                        }

                        if (!fits)
                        {
                            grid.Add(i, oldI);
                            grid.Add(j, oldJ);
                            continue;
                        }

                        grid.Add(i, newI);
                        grid.Add(j, newJ);
                        pos[i] = pj;
                        pos[j] = pi;
                        onDrawing[i] = hitI;
                        onDrawing[j] = hitJ;
                        improved = true;
                    }
                }

                if (!improved) break;
            }

            foreach (int i in moved) placements[i] = new TagPlacement(items[i].Id, pos[i].r, pos[i].u, true);
            total = 0;
            foreach (int i in moved) foreach (int j in neighbours[i]) if (i < j && Cross(Leader(i, pos[i]), Leader(j, pos[j]))) total++;
            if (stats != null) stats.CrossingsAfter = total;
        }

        /// <summary>Do two segments properly cross (not merely share an end)?</summary>
        private static bool Cross(DrawingLine a, DrawingLine b)
        {
            double d1 = Orient(b.X1, b.Y1, b.X2, b.Y2, a.X1, a.Y1), d2 = Orient(b.X1, b.Y1, b.X2, b.Y2, a.X2, a.Y2);
            double d3 = Orient(a.X1, a.Y1, a.X2, a.Y2, b.X1, b.Y1), d4 = Orient(a.X1, a.Y1, a.X2, a.Y2, b.X2, b.Y2);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static double Orient(double ax, double ay, double bx, double by, double cx, double cy) => (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);

        private static Box MakeBox(TagItem t, double right, double up, double pad) =>
            new Box(right - t.Width / 2 - pad, up - t.Height / 2 - pad, right + t.Width / 2 + pad, up + t.Height / 2 + pad);

        private readonly struct Box
        {
            public Box(double minX, double minY, double maxX, double maxY)
            {
                MinX = minX;
                MinY = minY;
                MaxX = maxX;
                MaxY = maxY;
            }

            public double MinX { get; }
            public double MinY { get; }
            public double MaxX { get; }
            public double MaxY { get; }

            public bool Overlaps(Box o) => MinX < o.MaxX && o.MinX < MaxX && MinY < o.MaxY && o.MinY < MaxY;
        }

        /// <summary>A hash grid of boxes, so a thousand tags are not each compared with a thousand others.</summary>
        private sealed class BoxGrid
        {
            private readonly double _cell;
            private readonly Dictionary<(long, long), List<(int id, Box box)>> _cells = new Dictionary<(long, long), List<(int, Box)>>();

            public BoxGrid(double cell) => _cell = cell;

            public void Add(int id, Box box)
            {
                foreach ((long, long) key in Keys(box))
                {
                    if (!_cells.TryGetValue(key, out List<(int, Box)>? list)) _cells[key] = list = new List<(int, Box)>();
                    list.Add((id, box));
                }
            }

            public void Remove(int id, Box box)
            {
                foreach ((long, long) key in Keys(box))
                    if (_cells.TryGetValue(key, out List<(int id, Box box)>? list)) list.RemoveAll(e => e.id == id);
            }

            public IEnumerable<int> Query(Box box)
            {
                var seen = new HashSet<int>();
                foreach ((long, long) key in Keys(box))
                {
                    if (!_cells.TryGetValue(key, out List<(int id, Box box)>? list)) continue;
                    foreach ((int id, Box other) in list)
                        if (other.Overlaps(box) && seen.Add(id)) yield return id;
                }
            }

            private IEnumerable<(long, long)> Keys(Box b)
            {
                long x0 = (long)Math.Floor(b.MinX / _cell), x1 = (long)Math.Floor(b.MaxX / _cell);
                long y0 = (long)Math.Floor(b.MinY / _cell), y1 = (long)Math.Floor(b.MaxY / _cell);
                for (long x = x0; x <= x1; x++)
                    for (long y = y0; y <= y1; y++)
                        yield return (x, y);
            }
        }

        /// <summary>A hash grid of line segments: does any pass through a given box?</summary>
        private sealed class DrawingLineGrid
        {
            private readonly double _cell;
            private readonly Dictionary<(long, long), List<DrawingLine>> _cells = new Dictionary<(long, long), List<DrawingLine>>();

            public DrawingLineGrid(double cell) => _cell = cell;

            public void Add(DrawingLine s)
            {
                double length = Math.Sqrt(Math.Pow(s.X2 - s.X1, 2) + Math.Pow(s.Y2 - s.Y1, 2));
                int steps = Math.Max(1, (int)Math.Ceiling(length / (_cell * 0.5)));
                var seen = new HashSet<(long, long)>();
                for (int k = 0; k <= steps; k++)
                {
                    double f = (double)k / steps;
                    double x = s.X1 + (s.X2 - s.X1) * f, y = s.Y1 + (s.Y2 - s.Y1) * f;
                    long cx = (long)Math.Floor(x / _cell), cy = (long)Math.Floor(y / _cell);
                    // the cell and its neighbours, so a line grazing a corner is not missed
                    for (long dx = -1; dx <= 1; dx++)
                        for (long dy = -1; dy <= 1; dy++)
                            if (seen.Add((cx + dx, cy + dy)))
                            {
                                if (!_cells.TryGetValue((cx + dx, cy + dy), out List<DrawingLine>? list)) _cells[(cx + dx, cy + dy)] = list = new List<DrawingLine>();
                                list.Add(s);
                            }
                }
            }

            public bool Hits(Box b)
            {
                long x0 = (long)Math.Floor(b.MinX / _cell), x1 = (long)Math.Floor(b.MaxX / _cell);
                long y0 = (long)Math.Floor(b.MinY / _cell), y1 = (long)Math.Floor(b.MaxY / _cell);
                for (long x = x0; x <= x1; x++)
                    for (long y = y0; y <= y1; y++)
                        if (_cells.TryGetValue((x, y), out List<DrawingLine>? list))
                            foreach (DrawingLine s in list)
                                if (Crosses(s, b)) return true;
                return false;
            }

            /// <summary>Liang-Barsky: does the segment pass through the box?</summary>
            private static bool Crosses(DrawingLine s, Box b)
            {
                double dx = s.X2 - s.X1, dy = s.Y2 - s.Y1, t0 = 0, t1 = 1;
                double[] p = { -dx, dx, -dy, dy };
                double[] q = { s.X1 - b.MinX, b.MaxX - s.X1, s.Y1 - b.MinY, b.MaxY - s.Y1 };
                for (int i = 0; i < 4; i++)
                {
                    if (Math.Abs(p[i]) < 1e-12)
                    {
                        if (q[i] < 0) return false;
                        continue;
                    }

                    double r = q[i] / p[i];
                    if (p[i] < 0) { if (r > t1) return false; if (r > t0) t0 = r; }
                    else { if (r < t0) return false; if (r < t1) t1 = r; }
                }

                return true;
            }
        }
    }
}

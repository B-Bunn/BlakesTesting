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
        public double Padding { get; set; } = 0.04;

        /// <summary>How far a moved tag sits off its old position, at least, so it clears what it was piled on.</summary>
        public double MinOffset { get; set; } = 0.25;

        /// <summary>Step between the columns tried beside the pile.</summary>
        public double ColumnStep { get; set; } = 0.12;

        /// <summary>How many columns out and how many rows up and down are tried before a tag is left where it is.</summary>
        public int MaxColumns { get; set; } = 12;

        public int MaxRows { get; set; } = 40;

        /// <summary>
        ///     How much more a vertical move costs than a horizontal one. Above 1, tags stay level with what they tag
        ///     and leaders run near-horizontal, as in a hand-tidied riser.
        /// </summary>
        public double VerticalWeight { get; set; } = 1.5;
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
        public TagPlacement(int id, double right, double up, bool moved)
        {
            Id = id;
            Right = right;
            Up = up;
            Moved = moved;
        }

        public int Id { get; }
        public double Right { get; }
        public double Up { get; }
        public bool Moved { get; }
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
        public static List<TagPlacement> Compute(IReadOnlyList<TagItem> items, SpreadOptions options, double viewScale)
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
            foreach (int i in piled.OrderByDescending(k => items[k].Up).ThenBy(k => items[k].Right).ThenBy(k => items[k].Id))
            {
                TagItem t = items[i];
                double halfW = t.Width / 2;
                bool placed = false;
                foreach (var spot in spots)
                {
                    double right = t.Right + spot.dx + Math.Sign(spot.dx) * halfW;
                    double up = t.Up + spot.dy;
                    Box candidate = MakeBox(t, right, up, pad);
                    if (grid.Query(candidate).Any()) continue;
                    grid.Add(i, candidate);
                    placements[i] = new TagPlacement(t.Id, right, up, true);
                    placed = true;
                    break;
                }

                if (!placed)
                {
                    grid.Add(i, MakeBox(t, t.Right, t.Up, pad));
                    placements[i] = new TagPlacement(t.Id, t.Right, t.Up, false);
                }
            }

            for (int i = 0; i < items.Count; i++)
                placements[i] ??= new TagPlacement(items[i].Id, items[i].Right, items[i].Up, false);
            return placements.ToList();
        }

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
    }
}

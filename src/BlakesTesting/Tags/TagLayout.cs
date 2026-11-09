#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlakesTesting.Tags
{
    /// <summary>Which corner of a tag cluster the spread-out tags are moved to.</summary>
    public enum SpreadDirection
    {
        BottomLeft,
        BottomRight,
        TopLeft,
        TopRight,
    }

    /// <summary>
    ///     Knobs for <see cref="TagLayout" />. Lengths are PAPER inches (what you would measure on a printed sheet), so
    ///     a layout reads the same at 1/8" and 1/48" scale; <see cref="TagLayout" /> multiplies by the view scale.
    /// </summary>
    public sealed class SpreadOptions
    {
        public SpreadDirection Direction { get; set; } = SpreadDirection.BottomLeft;

        /// <summary>Tags whose heads are closer than this (paper inches) count as one pile to spread.</summary>
        public double ClusterRadius { get; set; } = 0.4;

        /// <summary>Gap between the pile and the first spread-out tag.</summary>
        public double Gap { get; set; } = 0.35;

        /// <summary>Vertical distance between neighbouring spread-out tags.</summary>
        public double RowSpacing { get; set; } = 0.3;

        /// <summary>Horizontal distance between columns, when a pile has more tags than fit in one.</summary>
        public double ColumnSpacing { get; set; } = 0.7;

        /// <summary>Tags per column before the next column starts.</summary>
        public int MaxPerColumn { get; set; } = 8;
    }

    /// <summary>One tag going in: where its head is now, and where the thing it tags is (used only for ordering).</summary>
    public sealed class TagItem
    {
        public TagItem(int id, double headRight, double headUp, double hostRight, double hostUp)
        {
            Id = id;
            HeadRight = headRight;
            HeadUp = headUp;
            HostRight = hostRight;
            HostUp = hostUp;
        }

        public int Id { get; }
        public double HeadRight { get; }
        public double HeadUp { get; }
        public double HostRight { get; }
        public double HostUp { get; }
    }

    /// <summary>One tag coming out: the head position to move it to, in the same view coordinates as the input.</summary>
    public sealed class TagPlacement
    {
        public TagPlacement(int id, double right, double up)
        {
            Id = id;
            Right = right;
            Up = up;
        }

        public int Id { get; }
        public double Right { get; }
        public double Up { get; }
    }

    /// <summary>
    ///     The layout rule, with no Revit types in it so it can be tested without Revit
    ///     (<c>tests/TagLayoutTest</c>). Coordinates are view coordinates: Right along the view's right direction, Up along its
    ///     up direction, both in model feet.
    ///     <para>
    ///         Piles are found by single-link clustering of the tag heads. Each pile's tags are lined up in tidy columns
    ///         beside the pile's chosen corner - nearest column first, filled from the top - ordered by where the tagged
    ///         thing is, highest first, so the leader lines run side by side instead of crossing.
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
            double radius = options.ClusterRadius * ft;
            double gap = options.Gap * ft;
            double row = options.RowSpacing * ft;
            double col = options.ColumnSpacing * ft;
            int perColumn = Math.Max(1, options.MaxPerColumn);

            // +1 sends tags to the right/up of the pile, -1 to the left/down.
            double sx = options.Direction == SpreadDirection.BottomLeft || options.Direction == SpreadDirection.TopLeft ? -1 : 1;
            double sy = options.Direction == SpreadDirection.BottomLeft || options.Direction == SpreadDirection.BottomRight ? -1 : 1;

            var result = new List<TagPlacement>(items.Count);
            foreach (List<TagItem> pile in Cluster(items, radius))
            {
                // Piles of one are already readable - leave them where the user put them.
                if (pile.Count < 2)
                {
                    result.AddRange(pile.Select(t => new TagPlacement(t.Id, t.HeadRight, t.HeadUp)));
                    continue;
                }

                // The pile's edge nearest the destination corner: the spread starts one gap beyond it.
                double edgeRight = sx < 0 ? pile.Min(t => t.HeadRight) : pile.Max(t => t.HeadRight);
                double edgeUp = sy < 0 ? pile.Min(t => t.HeadUp) : pile.Max(t => t.HeadUp);

                // Ordered so leaders run parallel: top-most host first when going down, bottom-most first when going
                // up; ties by the host's horizontal position, mirrored the same way. Id last, so it is deterministic.
                List<TagItem> ordered = pile
                    .OrderBy(t => sy * t.HostUp)
                    .ThenBy(t => sx * t.HostRight)
                    .ThenBy(t => t.Id)
                    .ToList();

                for (int i = 0; i < ordered.Count; i++)
                {
                    int column = i / perColumn;
                    int slot = i % perColumn;
                    double right = edgeRight + sx * (gap + column * col);
                    double up = edgeUp + sy * (gap + slot * row);
                    result.Add(new TagPlacement(ordered[i].Id, right, up));
                }
            }

            return result;
        }

        /// <summary>Single-link clustering: two tags are in the same pile if a chain of tags, each within <paramref name="radius" /> of the next, joins them.</summary>
        private static List<List<TagItem>> Cluster(IReadOnlyList<TagItem> items, double radius)
        {
            int n = items.Count;
            int[] parent = Enumerable.Range(0, n).ToArray();

            int Find(int x)
            {
                while (parent[x] != x)
                {
                    parent[x] = parent[parent[x]];
                    x = parent[x];
                }

                return x;
            }

            double r2 = radius * radius;
            for (int a = 0; a < n; a++)
            {
                for (int b = a + 1; b < n; b++)
                {
                    double dr = items[a].HeadRight - items[b].HeadRight;
                    double du = items[a].HeadUp - items[b].HeadUp;
                    if (dr * dr + du * du <= r2) parent[Find(a)] = Find(b);
                }
            }

            var piles = new Dictionary<int, List<TagItem>>();
            for (int i = 0; i < n; i++)
            {
                int root = Find(i);
                if (!piles.TryGetValue(root, out List<TagItem>? list)) piles[root] = list = new List<TagItem>();
                list.Add(items[i]);
            }

            return piles.Values.ToList();
        }
    }
}

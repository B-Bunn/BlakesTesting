using System;
using System.Collections.Generic;
using System.Linq;
using BlakesTesting.Tags;

int failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
    if (!ok) failures++;
}

const double scale = 48; // 1/4" = 1'-0"
double ft = scale / 12;
var options = new SpreadOptions();

// 20 tags piled within a hair of each other, as in a riser top; plus 3 far away.
var rng = new Random(7);
var items = new List<TagItem>();
for (int i = 0; i < 20; i++) items.Add(new TagItem(i, 100 + rng.NextDouble() * 0.2, 500 + rng.NextDouble() * 0.2, 99 + i * 0.01, 498 - i * 0.05));
for (int i = 0; i < 3; i++) items.Add(new TagItem(100 + i, 300 + i * 40, 200, 300 + i * 40, 199));

var placed = TagLayout.Compute(items, options, scale).ToDictionary(p => p.Id);
Check(placed.Count == items.Count, "every tag is placed exactly once");

var pile = items.Where(t => t.Id < 20).ToList();
double minR = pile.Min(t => t.HeadRight), minU = pile.Min(t => t.HeadUp);
Check(pile.All(t => placed[t.Id].Right < minR && placed[t.Id].Up < minU), "pile moves to the bottom-left of itself");
Check(items.Where(t => t.Id >= 100).All(t => placed[t.Id].Right == t.HeadRight && placed[t.Id].Up == t.HeadUp), "lone tags stay put");

double minGap = double.MaxValue;
var moved = pile.Select(t => placed[t.Id]).ToList();
for (int a = 0; a < moved.Count; a++)
    for (int b = a + 1; b < moved.Count; b++)
        minGap = Math.Min(minGap, Math.Max(Math.Abs(moved[a].Right - moved[b].Right) / (options.ColumnSpacing / options.RowSpacing), Math.Abs(moved[a].Up - moved[b].Up)));
Check(minGap >= options.RowSpacing * ft - 1e-9, $"no two spread tags closer than a row ({minGap:F2} ft)");
Check(moved.Select(p => Math.Round(p.Right, 6)).Distinct().Count() == 3, "20 tags at 8 per column make 3 columns");

// Leaders should not cross: within a column, higher host -> higher tag.
var col0 = pile.Where(t => Math.Abs(placed[t.Id].Right - moved.Max(p => p.Right)) < 1e-6).OrderByDescending(t => t.HostUp).ToList();
Check(col0.Zip(col0.Skip(1), (a, b) => placed[a.Id].Up > placed[b.Id].Up).All(x => x), "host order is kept down the column");

// Mirrored direction.
var br = TagLayout.Compute(pile, new SpreadOptions { Direction = SpreadDirection.BottomRight }, scale);
double maxR = pile.Max(t => t.HeadRight);
Check(br.All(p => p.Right > maxR && p.Up < minU), "BottomRight goes right and down");

Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
return failures == 0 ? 0 : 1;

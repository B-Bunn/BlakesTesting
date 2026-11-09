using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BlakesTesting.Tags;

int failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
    if (!ok) failures++;
}

const double scale = 96; // 1/8" = 1'-0"
double ft = scale / 12;
var options = new SpreadOptions();
double w = 0.13 * ft, h = 0.2 * ft;

// The riser top: 20 number tags piled within a hair of each other; a lone tag far away; one large label already clear.
var rng = new Random(7);
var items = new List<TagItem>();
for (int i = 0; i < 20; i++) items.Add(new TagItem(i, 100 + rng.NextDouble() * 0.6, 500 + rng.NextDouble() * 3, w, h));
items.Add(new TagItem(100, 300, 200, w, h));
items.Add(new TagItem(101, 60, 505, w * 9, h * 2)); // a big "Riser-603" style label beside the pile, not overlapping anything

var result = TagLayout.Compute(items, options, scale);
var byId = result.ToDictionary(p => p.Id);
var item = items.ToDictionary(t => t.Id);

Check(result.Count == items.Count, "every tag comes back exactly once");
Check(!byId[100].Moved && !byId[101].Moved, "tags that overlap nothing are not touched");
Check(Enumerable.Range(0, 20).All(i => byId[i].Moved), "all 20 piled tags are moved");

bool Overlap(TagPlacement a, TagPlacement b)
{
    TagItem x = item[a.Id], y = item[b.Id];
    return Math.Abs(a.Right - b.Right) < (x.Width + y.Width) / 2 && Math.Abs(a.Up - b.Up) < (x.Height + y.Height) / 2;
}
bool anyOverlap = false;
for (int a = 0; a < result.Count; a++)
    for (int b = a + 1; b < result.Count; b++)
        if (Overlap(result[a], result[b])) anyOverlap = true;
Check(!anyOverlap, "after the spread no two labels overlap");

double maxMove = Enumerable.Range(0, 20).Max(i => Math.Sqrt(Math.Pow(byId[i].Right - item[i].Right, 2) + Math.Pow(byId[i].Up - item[i].Up, 2)));
Check(maxMove < 2.0 * ft + 20 * h, $"spread stays local: furthest move {maxMove:F1} ft (paper {maxMove / ft:F2} in)");
Check(Enumerable.Range(0, 20).Any(i => byId[i].Right < 100) && Enumerable.Range(0, 20).Any(i => byId[i].Right > 100), "tags land on both sides of the pile");

var leftOnly = TagLayout.Compute(items, new SpreadOptions { Side = SpreadSide.Left }, scale);
Check(leftOnly.Where(p => p.Moved).All(p => p.Right < item[p.Id].Right), "Side = Left only moves tags to the left");

// A static (non-movable) label sitting where a tag would go is respected.
var withStatic = new List<TagItem>(items) { new TagItem(200, 99.0, 501.5, w * 3, h * 3, movable: false) };
var r2 = TagLayout.Compute(withStatic, options, scale);
var staticP = r2.First(p => p.Id == 200);
Check(!staticP.Moved && r2.Where(p => p.Id < 20).All(p => !(Math.Abs(p.Right - 99.0) < (w + w * 3) / 2 && Math.Abs(p.Up - 501.5) < (h + h * 3) / 2)), "moved tags keep clear of fixed ones");

// 1000 tags: 50 risers of 20 piled tags each, quickly.
var big = new List<TagItem>();
for (int r = 0; r < 50; r++)
    for (int i = 0; i < 20; i++) big.Add(new TagItem(r * 100 + i, r * 40 + rng.NextDouble() * 0.6, 500 + rng.NextDouble() * 3, w, h));
var sw = Stopwatch.StartNew();
var rb = TagLayout.Compute(big, options, scale);
Check(sw.ElapsedMilliseconds < 5000, $"1000 piled tags laid out in {sw.ElapsedMilliseconds} ms");
Check(rb.Count(p => p.Moved) > 900, $"{rb.Count(p => p.Moved)} of 1000 moved");

Console.WriteLine(failures == 0 ? "ALL PASSED" : failures + " FAILED");
return failures == 0 ? 0 : 1;

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
Check(maxMove <= (options.MaxDistance + 0.2) * ft, $"no tag moves further than MaxDistance: furthest {maxMove / ft:F2} in of paper");
Check(Enumerable.Range(0, 20).Any(i => byId[i].Right < 100) && Enumerable.Range(0, 20).Any(i => byId[i].Right > 100), "tags land on both sides of the pile");

var leftOnly = TagLayout.Compute(items, new SpreadOptions { Side = SpreadSide.Left }, scale);
Check(leftOnly.Where(p => p.Moved).All(p => p.Right < item[p.Id].Right), "Side = Left only moves tags to the left");

// A static (non-movable) label sitting where a tag would go is respected.
var withStatic = new List<TagItem>(items) { new TagItem(200, 99.0, 501.5, w * 3, h * 3, movable: false) };
var r2 = TagLayout.Compute(withStatic, options, scale);
var staticP = r2.First(p => p.Id == 200);
Check(!staticP.Moved && r2.Where(p => p.Id < 20).All(p => !(Math.Abs(p.Right - 99.0) < (w + w * 3) / 2 && Math.Abs(p.Up - 501.5) < (h + h * 3) / 2)), "moved tags keep clear of fixed ones");

// Fewer crossed leaders: the same piles with and without the untangle pass.
int Crossings(IReadOnlyList<TagItem> src, List<TagPlacement> pl)
{
    var d = src.ToDictionary(t => t.Id);
    int n = 0;
    for (int a = 0; a < pl.Count; a++)
        for (int b = a + 1; b < pl.Count; b++)
        {
            if (!pl[a].Moved || !pl[b].Moved) continue;
            var (r1, u1, r2, u2) = (d[pl[a].Id], d[pl[b].Id], pl[a], pl[b]);
            double O(double ax, double ay, double bx, double by, double cx, double cy) => (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            double d1 = O(r2.Right, r2.Up, u2.Right, u2.Up, r1.Right, r1.Up), d2 = O(r2.Right, r2.Up, u2.Right, u2.Up, r1.Right, r1.Up);
            _ = d1; _ = d2;
            bool X((double x, double y) p1, (double x, double y) p2, (double x, double y) p3, (double x, double y) p4)
            {
                double o1 = O(p3.x, p3.y, p4.x, p4.y, p1.x, p1.y), o2 = O(p3.x, p3.y, p4.x, p4.y, p2.x, p2.y);
                double o3 = O(p1.x, p1.y, p2.x, p2.y, p3.x, p3.y), o4 = O(p1.x, p1.y, p2.x, p2.y, p4.x, p4.y);
                return o1 * o2 < 0 && o3 * o4 < 0;
            }
            if (X((r1.Right, r1.Up), (pl[a].Right, pl[a].Up), (u1.Right, u1.Up), (pl[b].Right, pl[b].Up))) n++;
        }
    return n;
}
var spreadPile = Enumerable.Range(0, 20).Select(i => new TagItem(i, 100 + rng.NextDouble() * 3, 500 + rng.NextDouble() * 6, w, h)).ToList();
var st = new LayoutStats();
var tangled = TagLayout.Compute(spreadPile, new SpreadOptions { Untangle = false }, scale);
var untangled = TagLayout.Compute(spreadPile, new SpreadOptions(), scale, null, st);
int c0 = Crossings(spreadPile, tangled), c1 = Crossings(spreadPile, untangled);
Check(c1 <= c0, $"untangling never adds crossings: {c0} -> {c1} (layout reported {st.CrossingsBefore} -> {st.CrossingsAfter})");
Check(c1 < c0 || c0 == 0, "untangling removes some crossings when there were any");
bool overlapAfter = false;
for (int a = 0; a < untangled.Count; a++)
    for (int b = a + 1; b < untangled.Count; b++)
        if (Math.Abs(untangled[a].Right - untangled[b].Right) < w && Math.Abs(untangled[a].Up - untangled[b].Up) < h) overlapAfter = true;
Check(!overlapAfter, "swapping leaves no two labels overlapping");

// Keep off the drawing: two long vertical pipes either side of the pile - labels sit between/beyond them, not on them.
var pipe = new[] { new DrawingLine(99.2, 400, 99.2, 600), new DrawingLine(101.4, 400, 101.4, 600) };
var piled = Enumerable.Range(0, 12).Select(i => new TagItem(i, 100.3 + rng.NextDouble() * 0.2 - 0.1, 500 + rng.NextDouble() * 2, w, h)).ToList();
int OnPipe(List<TagPlacement> pl) => pl.Count(p => Math.Abs(p.Right - 99.2) < w / 2 || Math.Abs(p.Right - 101.4) < w / 2);
var ignore = TagLayout.Compute(piled, new SpreadOptions { GeometryPenalty = 0 }, scale, pipe);
var avoid = TagLayout.Compute(piled, new SpreadOptions(), scale, pipe);
Check(OnPipe(ignore) > 0 && OnPipe(avoid) < OnPipe(ignore), $"labels on the pipes: {OnPipe(ignore)} ignoring it, {OnPipe(avoid)} avoiding it");
Check(OnPipe(avoid) == 0, "no label sits on a pipe when there is room beside them");

// A pile too dense for the reach: the tags that cannot be placed close by stay put and are flagged, not flung away.
var dense = Enumerable.Range(0, 60).Select(i => new TagItem(i, 100 + rng.NextDouble() * 0.2, 500 + rng.NextDouble() * 0.2, w, h)).ToList();
var rd = TagLayout.Compute(dense, new SpreadOptions { MaxDistance = 0.5 }, scale);
Check(rd.Any(p => p.Stuck) && rd.Where(p => p.Stuck).All(p => !p.Moved), $"{rd.Count(p => p.Stuck)} of 60 too dense for 0.5 in reach are left alone and flagged");
Check(rd.Where(p => p.Moved).All(p => Math.Sqrt(Math.Pow(p.Right - dense[p.Id].Right, 2) + Math.Pow(p.Up - dense[p.Id].Up, 2)) <= 0.5 * ft + w), "none moved past the reach");

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

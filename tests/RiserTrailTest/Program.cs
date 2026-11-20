using System;
using System.Collections.Generic;
using System.Linq;
using BlakesTesting.Risers;

int failures = 0;
void Check(bool ok, string what)
{
    Console.WriteLine((ok ? "ok   " : "FAIL ") + what);
    if (!ok) failures++;
}

string Ids(IEnumerable<long> ids) => string.Join(",", ids.OrderBy(i => i));
bool Same(IEnumerable<long> got, params long[] want) => Ids(got) == Ids(want);

// ---------------------------------------------------------------------------------------------------------------
// The rounding ladder - every answer in feet AND inches, so a factor of twelve cannot pass.
// ---------------------------------------------------------------------------------------------------------------
void Round(double inchesIn, double inchesOut)
{
    double feet = CutLengthRules.RoundPexFeet(inchesIn / 12.0);
    Check(Math.Abs(feet - inchesOut / 12.0) < 1e-9 && Math.Abs(feet * 12.0 - inchesOut) < 1e-7,
        $"{inchesIn}\" rounds to {inchesOut}\" ({inchesOut / 12.0:0.####} ft)");
}
Round(1, 3);              // anything over zero goes to the next 3"
Round(3, 3);              // exactly on a mark stays
Round(3.01, 6);
Round(51, 51);            // 4'-3" drawn exactly stays 4'-3" ...
Check(CutLengthRules.RoundPexFeet(4.25) == 4.25, "4.25 ft (51.00000000000001 in as a double) stays 4.25 ft");
Round(51.5, 54);
Round(107, 108);          // just under 9'
Round(108, 108);          // 9'-0" exactly: both ladders agree
Round(108.5, 120);        // from 9' up: next whole foot
Round(120, 120);
Round(121, 132);
Round(300, 300);
Check(CutLengthRules.RoundPexFeet(-1) == -1 && double.IsNaN(CutLengthRules.RoundPexFeet(double.NaN)) && CutLengthRules.RoundPexFeet(0) == 0,
    "a length that is not positive and finite comes back as it was (and is not usable)");
Check(!CutLengthRules.IsUsable(0) && !CutLengthRules.IsUsable(double.NaN) && !CutLengthRules.IsUsable(double.PositiveInfinity) && CutLengthRules.IsUsable(0.01),
    "0, NaN and infinity are not writable lengths");
for (double ft = 0.05; ft < 40; ft += 0.0137)
{
    double rounded = CutLengthRules.RoundPexFeet(ft);
    if (rounded < ft - 1e-9 || rounded - ft > 1.0 + 1e-9) { Check(false, $"rounding {ft} ft gave {rounded}"); break; }
}
Check(true, "rounding never goes down and never adds a foot or more (sweep 0.05 - 40 ft)");

// ---------------------------------------------------------------------------------------------------------------
// Pipe shape and manifold ports.
// ---------------------------------------------------------------------------------------------------------------
Check(PipeShape.IsVertical(0, 0, 9.5), "straight up is vertical");
Check(PipeShape.IsVertical(0, 0, -9.5), "straight down is vertical");
Check(!PipeShape.IsVertical(10, 3, 0), "level is not vertical");
Check(!PipeShape.IsVertical(10, 0, 0.3), "a slight fall is not vertical");
Check(PipeShape.IsVertical(1, 1, 1.5), "a 45+ degree riser offset counts as vertical");
Check(!PipeShape.IsVertical(2, 0, 1), "a gentle diagonal does not");
Check(!PipeShape.IsVertical(0, 0, 0), "a pipe with no length is not vertical");

double r34 = 0.75 / 24, r12 = 0.5 / 24;
Check(Same(ManifoldPorts.PickInlets(new[] { r12, r34, r12, r12, r12 }).Select(i => (long)i), 1), "the 3/4\" port of a 4-outlet is its inlet");
Check(Same(ManifoldPorts.PickInlets(new[] { r12, r12, r12, r12, r12, r12, r34 }).Select(i => (long)i), 6), "the 3/4\" port of a 6-outlet is its inlet");
Check(ManifoldPorts.PickInlets(new[] { r12, r12, r12, r12, r12 }).Count == 0, "every port the same size: no inlet is guessed");
Check(ManifoldPorts.PickInlets(new[] { r34 + 0.0001, r34, r12, r12, r12 }).Count == 2, "two ports of the biggest size are both inlets");
Check(ManifoldPorts.IsManifold(5, "Uponor Branch Multiport Tee 4-Outlet") && ManifoldPorts.IsManifold(7, "x"), "5 or 7 ports is a manifold");
Check(!ManifoldPorts.IsManifold(3, "Tee") && !ManifoldPorts.IsManifold(4, "Cross") && ManifoldPorts.IsManifold(4, "PEX Manifold 3-Outlet"),
    "a tee or a cross is not one; 4 ports named Manifold is");

// ---------------------------------------------------------------------------------------------------------------
// The trail. A small network builder: id -> node, id -> neighbours (links are two-way).
// ---------------------------------------------------------------------------------------------------------------
var nodes = new Dictionary<long, TrailNode>();
var links = new Dictionary<long, List<long>>();
var inlets = new Dictionary<long, List<long>>();
void Reset() { nodes.Clear(); links.Clear(); inlets.Clear(); }
void Link(long a, long b)
{
    if (!links.TryGetValue(a, out var la)) links[a] = la = new List<long>();
    if (!links.TryGetValue(b, out var lb)) links[b] = lb = new List<long>();
    la.Add(b); lb.Add(a);
}
void Flat(long id, bool pex = true) => nodes[id] = new TrailNode(id, NodeKind.Pipe, pex, false, 20, 20);
void Up(long id, double from, double to, bool pex = true) => nodes[id] = new TrailNode(id, NodeKind.Pipe, pex, true, Math.Min(from, to), Math.Max(from, to));
void Fit(long id) => nodes[id] = new TrailNode(id, NodeKind.Pass);
void Manifold(long id, params long[] inletTo)
{
    nodes[id] = new TrailNode(id, NodeKind.Manifold);
    inlets[id] = inletTo.ToList();
    foreach (long n in inletTo) Link(id, n);
}
FakeNet Net() => new FakeNet(nodes, links, inlets);

// The picture: manifold M(1) <- flat H(2) <- elbow(3) <- vertical V1(4) <- tee(5) <- V2(6) <- elbow(7) <- flat main(8).
// The tee's branch is a flat pipe (9) off to a second manifold. The manifold also has a vertical outlet stub (50).
Reset();
Manifold(1, 2);
Flat(2); Fit(3); Up(4, 10, 20); Fit(5); Up(6, 0, 10); Fit(7); Flat(8, pex: true);
Link(2, 3); Link(3, 4); Link(4, 5); Link(5, 6); Link(6, 7); Link(7, 8);
Flat(9); Link(5, 9);
Up(50, 20, 21.5); Link(1, 50); // an outlet stub: reached through the manifold, never through its inlet
var r = RiserTrail.Walk(Net(), 1);
Check(r.Status == TrailStatus.Found, "a manifold on a riser finds it");
Check(Same(r.RiserPipes, 4, 6), "the riser is the two vertical pipes, floor after floor, through the tee");
Check(Same(r.PathPipes, 2), "the horizontal PEX from the manifold to the riser is its path, and is rounded with it");
Check(!r.RiserPipes.Contains(8) && !r.RiserPipes.Contains(9) && !r.PathPipes.Contains(8) && !r.PathPipes.Contains(9),
    "the flat main the riser ends on and the tee's side branch are neither riser nor path");
Check(!r.RiserPipes.Contains(50) && !r.StubPipes.Contains(50), "the manifold's own outlet stub is never touched");
Check(Math.Abs(r.RiseFeet - 20) < 1e-9, "the rise is the whole trail's");

// A tee on the HORIZONTAL with the riser coming off its branch: the same tee is crossed on the way in and then
// again from the vertical side (the walk must not remember it as spent).
Reset();
Manifold(1, 2);
Flat(2); Fit(3); Flat(4); Fit(5); Up(6, 0, 12); Up(7, 12, 24); Fit(8);
Link(2, 3); Link(3, 4); Link(4, 5); Link(3, 6); Link(6, 8); Link(8, 7);
r = RiserTrail.Walk(Net(), 1);
Check(Same(r.RiserPipes, 6, 7), "a riser off the branch of a horizontal tee, with a coupling between its pipes");
Check(Same(r.ApproachPipes, 2, 4), "both horizontal pipes beyond the inlet are walked");
Check(Same(r.PathPipes, 2), "only the pipe on the way to the riser is path: the run that carries on past the tee is a different branch");

// Two manifolds on one riser: the same pipes, once.
Reset();
Manifold(1, 2); Manifold(30, 31);
Flat(2); Fit(3); Up(4, 0, 10); Fit(5); Up(6, 10, 20); Flat(31); Link(31, 32);
Fit(32);
Link(2, 3); Link(3, 4); Link(4, 5); Link(5, 6); Link(32, 5);
var results = new List<TrailResult>();
var all = RiserTrail.WalkAll(Net(), new long[] { 1, 30 }, null, results);
Check(Same(all, 2, 4, 6, 31) && results.Count == 2 && results.All(x => x.Status == TrailStatus.Found),
    "two manifolds on one riser: its pipes once, plus each manifold's own path");

// Walls: another manifold, and anything that is not PEX.
Reset();
Manifold(1, 2); Manifold(60, 61);
Flat(2); Fit(3); Flat(4); Fit(5); Link(2, 3); Link(3, 4); Link(4, 5);
Fit(6); Link(3, 6); Link(6, 60); Up(62, 0, 12); Link(60, 62);  // a second manifold on the branch, with a vertical outlet beyond it
Up(7, 0, 12, pex: false); Link(5, 7);                           // copper riser at the end of the PEX
r = RiserTrail.Walk(Net(), 1);
Check(r.Status == TrailStatus.NoRiser && r.RiserPipes.Count == 0, "another manifold and a non-PEX riser are walls: nothing found");

// Copper in the middle of a riser stops it.
Reset();
Manifold(1, 2);
Flat(2); Fit(3); Up(4, 0, 10); Fit(5); Up(6, 10, 20, pex: false); Fit(7); Up(8, 20, 30);
Link(2, 3); Link(3, 4); Link(4, 5); Link(5, 6); Link(6, 7); Link(7, 8);
r = RiserTrail.Walk(Net(), 1);
Check(Same(r.RiserPipes, 4), "a copper section ends the PEX trail; the PEX beyond it is not reached through it");

// A valve and a union in a row in the riser are walked through.
Reset();
Manifold(1, 2);
Flat(2); Fit(3); Up(4, 0, 10); Fit(5); Fit(6); Up(7, 10, 20);
Link(2, 3); Link(3, 4); Link(4, 5); Link(5, 6); Link(6, 7);
r = RiserTrail.Walk(Net(), 1);
Check(Same(r.RiserPipes, 4, 7), "fitting-to-fitting chains (valve, union) do not stop the riser");

// A short vertical stub is not a riser.
Reset();
Manifold(1, 2);
Flat(2); Fit(3); Up(4, 20, 22.0); Fit(5); Flat(6);
Link(2, 3); Link(3, 4); Link(4, 5); Link(5, 6);
r = RiserTrail.Walk(Net(), 1);
Check(r.Status == TrailStatus.TooShort && r.RiserPipes.Count == 0 && Same(r.StubPipes, 4) && r.PathPipes.Count == 0,
    "a 2 ft vertical stub is reported and left at real length, and so is the pipe that led to it");
r = RiserTrail.Walk(Net(), 1, new TrailOptions { MinRiseFeet = 1.5 });
Check(r.Status == TrailStatus.Found && Same(r.RiserPipes, 4) && Same(r.PathPipes, 2), "MinRiseFeet decides it");

// A riser with a 45 degree offset (a sloped pipe counts as vertical).
Reset();
Manifold(1, 2);
Flat(2); Fit(3); Up(4, 0, 10); Fit(5); Up(6, 10, 11); Fit(7); Up(8, 11, 21);
Link(2, 3); Link(3, 4); Link(4, 5); Link(5, 6); Link(6, 7); Link(7, 8);
r = RiserTrail.Walk(Net(), 1);
Check(Same(r.RiserPipes, 4, 6, 8), "an offset in the riser does not break the trail");

// No inlet / no riser.
Reset();
Manifold(1);
Flat(2);
r = RiserTrail.Walk(Net(), 1);
Check(r.Status == TrailStatus.NoInlet, "a manifold with no usable inlet is reported as such");
Reset();
Manifold(1, 2); Flat(2); Fit(3); Flat(4); Link(2, 3); Link(3, 4);
r = RiserTrail.Walk(Net(), 1);
Check(r.Status == TrailStatus.NoRiser && Same(r.ApproachPipes, 2, 4) && r.PathPipes.Count == 0, "a manifold whose branch never goes vertical is NoRiser, and rounds nothing");

// The walk limit.
Reset();
Manifold(1, 2);
for (long i = 2; i < 300; i++) { Flat(i); if (i > 2) Link(i - 1, i); }
r = RiserTrail.Walk(Net(), 1, new TrailOptions { MaxApproachNodes = 50 });
Check(r.HitLimit && r.ApproachPipes.Count <= 52, "a runaway horizontal walk stops at its limit and says so");

// A riser leads on to the vertical past a looped pair of tees (no endless walk).
Reset();
Manifold(1, 2);
Flat(2); Fit(3); Up(4, 0, 10); Fit(5); Fit(6); Up(7, 10, 20);
Link(2, 3); Link(3, 4); Link(4, 5); Link(5, 6); Link(6, 7); Link(5, 6); Link(6, 5);
r = RiserTrail.Walk(Net(), 1);
Check(Same(r.RiserPipes, 4, 7), "doubly-linked fittings do not loop the walk");

// ---------------------------------------------------------------------------------------------------------------
// The colouring settings: which pipe gets which colour, and the file they live in.
// ---------------------------------------------------------------------------------------------------------------
var green = new RgbColor(0, 200, 0);
var red = new RgbColor(255, 0, 0);
var cs = new ColorSettings();
Check(cs.Choose(true, true)?.Equals(green) == true, "default: a rounded riser pipe is green");
Check(cs.Choose(false, true)?.Equals(red) == true, "default: PEX that was not rounded is red");
Check(cs.Choose(false, false) == null, "default: a pipe that is not PEX is left alone");
cs.ColorOtherPipes = true;
Check(cs.Choose(false, false)?.Equals(red) == true, "other pipes on: they take the not-rounded colour");
cs.ColorRounded = false;
Check(cs.Choose(true, true) == null && cs.Choose(false, true) != null, "rounded off: only the not-rounded ones are coloured");
cs.ColorRounded = true; cs.ColorOtherPex = false;
Check(cs.Choose(true, true) != null && cs.Choose(false, true) == null, "other PEX off: only the rounded ones are coloured");
cs.Enabled = false;
Check(cs.Choose(true, true) == null && cs.Choose(false, true) == null && cs.Choose(false, false) == null, "the master switch off colours nothing");

Check(RgbColor.TryParse("#00C800", out var parsed) && parsed.Equals(green) && parsed.ToHex() == "#00C800", "hex colours read and write back");
Check(RgbColor.TryParse("ff0000", out parsed) && parsed.Equals(red), "the # is optional");
Check(!RgbColor.TryParse("red", out _) && !RgbColor.TryParse("#12345", out _) && !RgbColor.TryParse(null, out _) && !RgbColor.TryParse("#GG0000", out _),
    "a bad colour is refused");

var custom = new ColorSettings { Enabled = false, ColorRounded = false, ColorOtherPex = false, ColorOtherPipes = true, ColorLines = false,
    RoundedColor = new RgbColor(1, 2, 3), NotRoundedColor = new RgbColor(250, 251, 252) };
var back = ColorSettings.Parse(custom.ToText());
Check(!back.Enabled && !back.ColorRounded && !back.ColorOtherPex && back.ColorOtherPipes && !back.ColorLines
      && back.RoundedColor.Equals(new RgbColor(1, 2, 3)) && back.NotRoundedColor.Equals(new RgbColor(250, 251, 252)), "settings survive a round trip through their text");
var junk = ColorSettings.Parse("Enabled=maybe\nRoundedColor=nope\nwhat\n=x\n# comment\nColorLines=False\nUnknown=1\r\nNotRoundedColor=#0000FF\r\n");
Check(junk.Enabled && junk.RoundedColor.Equals(green) && !junk.ColorLines && junk.NotRoundedColor.Equals(new RgbColor(0, 0, 255)),
    "bad or unknown lines are ignored and keep their defaults; good ones (CRLF too) are read");
Check(ColorSettings.Parse(null).Enabled && ColorSettings.Parse("").RoundedColor.Equals(green), "no text gives the defaults");

string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bt-colors-" + Guid.NewGuid().ToString("N"), "sub", "c.ini");
Check(!System.IO.File.Exists(tmp) && ColorSettings.Load(tmp).Enabled, "no file: the defaults");
Check(custom.Save(tmp) && ColorSettings.Load(tmp).ColorOtherPipes && ColorSettings.Load(tmp).RoundedColor.Equals(new RgbColor(1, 2, 3)), "saved to a new folder and loaded back");
System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(tmp))!, true);
Check(!custom.Save(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "bt-no\0pe", "x.ini")), "an unwritable path reports false instead of throwing");
var clone = custom.Clone(); clone.RoundedColor = green;
Check(custom.RoundedColor.Equals(new RgbColor(1, 2, 3)), "a clone is a copy");

// The picture from the job: a manifold's horizontal runs along to a tee on the riser; past the tee a horizontal
// leaves the riser for a main (the "square" - NOT rounded); and the manifold's branch reaches a vertical PEX pipe that is
// not the main riser at all (the lone red vertical beside the tower) which comes up to that same kind of horizontal.
Reset();
Manifold(1, 2);
Flat(2); Flat(3); Fit(4); Flat(5); Fit(6); Up(7, 0, 9.5); Fit(8); Flat(9); Fit(10); Flat(11);
Link(2, 4); Link(3, 4); Link(4, 5); Link(5, 6); Link(6, 7); Link(7, 8); Link(8, 9); Link(9, 10); Link(10, 11);
Link(1, 2);
r = RiserTrail.Walk(Net(), 1);
Check(Same(r.RiserPipes, 7) && Same(r.PathPipes, 2, 5), "the horizontal pipes back from the riser are path; the horizontal it leaves on (9, 11) is not");

// A side branch at a tee on the way is not path - only the shortest way to the riser is.
Reset();
Manifold(1, 2);
Flat(2); Fit(3); Flat(4); Fit(5); Up(6, 0, 10); Flat(20); Fit(21); Flat(22);
Link(2, 3); Link(3, 4); Link(4, 5); Link(5, 6);
Link(3, 20); Link(20, 21); Link(21, 22);
r = RiserTrail.Walk(Net(), 1);
Check(Same(r.PathPipes, 2, 4) && !r.PathPipes.Contains(20) && !r.PathPipes.Contains(22) && r.ApproachPipes.Contains(22),
    "a side branch off a tee on the way is walked but never path");

// Two risers off the same approach (the doubled green pipes): the path to both, once.
Reset();
Manifold(1, 2);
Flat(2); Fit(3); Flat(4); Fit(5); Up(6, 0, 10); Up(7, 0, 10);
Link(2, 3); Link(3, 4); Link(4, 5); Link(5, 6); Link(3, 7);
r = RiserTrail.Walk(Net(), 1);
Check(Same(r.RiserPipes, 6, 7) && Same(r.PathPipes, 2, 4), "two verticals off one approach are both risers; the path is the pipes on the way to either (2 shared, 4 on the way to 6)");
Check(r.PathPipes.Count == r.PathPipes.Distinct().Count(), "no pipe is listed on the path twice");

// A path through a non-PEX pipe is no path (the trail ends at it).
Reset();
Manifold(1, 2);
Flat(2); Fit(3); Flat(4, pex: false); Fit(5); Up(6, 0, 10);
Link(2, 3); Link(3, 4); Link(4, 5); Link(5, 6);
r = RiserTrail.Walk(Net(), 1);
Check(r.Status == TrailStatus.NoRiser && r.PathPipes.Count == 0, "copper between the manifold and a riser cuts the way: nothing is rounded");

// The inlet-guessed flag travels through.
Reset();
Manifold(1, 2); Flat(2); Fit(3); Up(4, 0, 10); Link(2, 3); Link(3, 4);
var gnet = Net(); gnet.Guessed.Add(1);
r = RiserTrail.Walk(gnet, 1);
Check(r.InletGuessed && r.Status == TrailStatus.Found && Same(r.PathPipes, 2), "a manifold whose inlet was guessed says so and still finds its riser");
Check(!RiserTrail.Walk(Net(), 1).InletGuessed, "an ordinary manifold does not");

Console.WriteLine();
Console.WriteLine(failures == 0 ? "All checks passed." : $"{failures} check(s) FAILED.");
return failures == 0 ? 0 : 1;

sealed class FakeNet : IPipeNetwork
{
    private readonly Dictionary<long, TrailNode> _nodes;
    private readonly Dictionary<long, List<long>> _links;
    private readonly Dictionary<long, List<long>> _inlets;

    public FakeNet(Dictionary<long, TrailNode> nodes, Dictionary<long, List<long>> links, Dictionary<long, List<long>> inlets)
    {
        _nodes = nodes; _links = links; _inlets = inlets;
    }

    public TrailNode? Node(long id) => _nodes.TryGetValue(id, out var n) ? n : null;
    public IReadOnlyList<long> Neighbors(long id) => _links.TryGetValue(id, out var l) ? l : new List<long>();
    public IReadOnlyList<long> InletNeighbors(long manifoldId) => _inlets.TryGetValue(manifoldId, out var l) ? l : new List<long>();
    public bool InletGuessed(long manifoldId) => Guessed.Contains(manifoldId);
    public HashSet<long> Guessed { get; } = new HashSet<long>();
}

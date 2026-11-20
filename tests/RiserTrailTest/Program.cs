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
    double r = CutLengthRules.RoundPexFeet(ft);
    if (r < ft - 1e-9 || r - ft > 1.0 + 1e-9) { Check(false, $"rounding {ft} ft gave {r}"); break; }
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
IPipeNetwork Net() => new FakeNet(nodes, links, inlets);

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
Check(Same(r.ApproachPipes, 2), "the horizontal PEX to the riser is walked but not rounded");
Check(!r.RiserPipes.Contains(8) && !r.RiserPipes.Contains(9), "the flat main at the end and the tee's branch are not rounded");
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
Check(Same(r.ApproachPipes, 2, 4), "both horizontal pipes of the approach are walked");

// Two manifolds on one riser: the same pipes, once.
Reset();
Manifold(1, 2); Manifold(30, 31);
Flat(2); Fit(3); Up(4, 0, 10); Fit(5); Up(6, 10, 20); Flat(31); Link(31, 32);
Fit(32);
Link(2, 3); Link(3, 4); Link(4, 5); Link(5, 6); Link(32, 5);
var results = new List<TrailResult>();
var all = RiserTrail.WalkAll(Net(), new long[] { 1, 30 }, null, results);
Check(Same(all, 4, 6) && results.Count == 2 && results.All(x => x.Status == TrailStatus.Found), "two manifolds on one riser give its pipes once");

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
Check(r.Status == TrailStatus.TooShort && r.RiserPipes.Count == 0 && Same(r.StubPipes, 4), "a 2 ft vertical stub is reported and left at real length");
r = RiserTrail.Walk(Net(), 1, new TrailOptions { MinRiseFeet = 1.5 });
Check(r.Status == TrailStatus.Found && Same(r.RiserPipes, 4), "MinRiseFeet decides it");

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
Check(r.Status == TrailStatus.NoRiser && Same(r.ApproachPipes, 2, 4), "a manifold whose branch never goes vertical is NoRiser");

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
}

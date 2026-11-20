#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace BlakesTesting.Risers
{
    // The rule that finds the PEX pipes serving a water riser (a tower), with no Revit types in it so it can be
    // tested without Revit (tests/RiserTrailTest). RevitPipeNetwork feeds it the real model.
    //
    // THE RULE (Blake's boss's idea): everything that gets rounded is connected to a 4- or 6-port manifold. So start
    // at each manifold and follow the pipe:
    //   1. leave by the manifold's INLET (its biggest port - the outlets are the small ones going to fixtures) and
    //      walk the horizontal PEX until a vertical is reached. The pipes ON THE WAY from the manifold to that
    //      vertical (PathPipes) are the manifold's own branch to the riser, and they are rounded with it. Horizontal
    //      pipe that is not on that way - a side branch off a tee, a run leaving the riser for a main - is not.
    //   2. the first VERTICAL PEX pipe reached is the riser. Follow it - through couplings, tees, valves, unions -
    //      marking every vertical PEX pipe, floor after floor, until the trail turns horizontal (a tee's branch, the
    //      top or bottom elbow into a main): that is where it ends, and the horizontal it ends on is not rounded.
    //   3. a vertical trail shorter than MinRiseFeet is not a riser - it is a stub dropping into a manifold - so it
    //      is left alone and reported (and so is the path that led to it).
    // Another manifold is a wall in both walks, and so is any pipe that is not PEX.

    /// <summary>What a node of the pipe network is, as far as the walk cares.</summary>
    public enum NodeKind
    {
        /// <summary>A pipe: PEX or not, vertical or not. The only kind that is ever marked.</summary>
        Pipe,

        /// <summary>Anything the trail passes straight through: fitting, valve, union, flex pipe.</summary>
        Pass,

        /// <summary>A 4- or 6-port manifold: where a trail starts, and a wall to every other trail.</summary>
        Manifold,
    }

    public sealed class TrailNode
    {
        public TrailNode(long id, NodeKind kind, bool isPex = false, bool isVertical = false, double zLow = 0.0, double zHigh = 0.0)
        {
            Id = id;
            Kind = kind;
            IsPex = isPex;
            IsVertical = isVertical;
            ZLow = zLow;
            ZHigh = zHigh;
        }

        public long Id { get; }
        public NodeKind Kind { get; }
        public bool IsPex { get; }
        public bool IsVertical { get; }

        /// <summary>Lowest and highest point of a pipe, in feet; the rise of a trail is the spread of these.</summary>
        public double ZLow { get; }

        public double ZHigh { get; }
    }

    /// <summary>The model as the walk sees it. Ids are element ids.</summary>
    public interface IPipeNetwork
    {
        /// <summary>The node for an element, or null for anything the walk should ignore (a duct, a system).</summary>
        TrailNode? Node(long id);

        /// <summary>Every element physically connected to this one.</summary>
        IReadOnlyList<long> Neighbors(long id);

        /// <summary>What a manifold's inlet port(s) connect to - the only way a trail leaves a manifold.</summary>
        IReadOnlyList<long> InletNeighbors(long manifoldId);

        /// <summary>
        ///     True when the manifold's inlet could not be told from its outlets (every port the same size), so
        ///     <see cref="InletNeighbors"/> gave everything the manifold is connected to rather than one port.
        /// </summary>
        bool InletGuessed(long manifoldId);
    }

    public sealed class TrailOptions
    {
        /// <summary>
        ///     A vertical trail has to rise at least this far (feet) to be a riser. Floor to floor is about 9-10 ft, so
        ///     4 keeps a one-floor riser and drops the little vertical bit into a manifold or off a tee.
        /// </summary>
        public double MinRiseFeet { get; set; } = 4.0;

        /// <summary>
        ///     The horizontal walk from a manifold gives up after this many elements (a runaway through a huge PEX
        ///     main). It still reports what it found, and says it stopped.
        /// </summary>
        public int MaxApproachNodes { get; set; } = 600;
    }

    public enum TrailStatus
    {
        /// <summary>A riser was found; <see cref="TrailResult.RiserPipes"/> is the pipes to round.</summary>
        Found,

        /// <summary>The manifold is not connected to anything the walk can leave by.</summary>
        NoInlet,

        /// <summary>The walk from the inlet never reached a vertical PEX pipe.</summary>
        NoRiser,

        /// <summary>It reached vertical PEX, but none of it rises MinRiseFeet - a stub, not a tower.</summary>
        TooShort,
    }

    public sealed class TrailResult
    {
        public TrailResult(long manifoldId)
        {
            ManifoldId = manifoldId;
        }

        public long ManifoldId { get; }
        public TrailStatus Status { get; set; } = TrailStatus.NoInlet;

        /// <summary>The vertical PEX pipes to round.</summary>
        public List<long> RiserPipes { get; } = new List<long>();

        /// <summary>
        ///     The horizontal PEX pipes on the way from the manifold to the riser - its own branch back to the tower.
        ///     Rounded along with the riser. Only the pipes on that way: not the side branches the walk also touched.
        /// </summary>
        public List<long> PathPipes { get; } = new List<long>();

        /// <summary>Every horizontal PEX pipe the walk touched looking for the riser, path or not - for the log only.</summary>
        public List<long> ApproachPipes { get; } = new List<long>();

        /// <summary>True when the inlet could not be told from the outlets and every port was tried (see IPipeNetwork.InletGuessed).</summary>
        public bool InletGuessed { get; set; }

        /// <summary>Vertical PEX that was reached but belongs to a trail too short to be a riser.</summary>
        public List<long> StubPipes { get; } = new List<long>();

        /// <summary>How far the longest accepted trail rises, in feet.</summary>
        public double RiseFeet { get; set; }

        /// <summary>True when the horizontal walk stopped at <see cref="TrailOptions.MaxApproachNodes"/>.</summary>
        public bool HitLimit { get; set; }
    }

    public static class RiserTrail
    {
        /// <summary>Walks from one manifold. See the rule at the top of this file.</summary>
        public static TrailResult Walk(IPipeNetwork net, long manifoldId, TrailOptions? options = null)
        {
            options ??= new TrailOptions();
            TrailResult result = new TrailResult(manifoldId);

            IReadOnlyList<long> inlets = net.InletNeighbors(manifoldId);
            if (inlets.Count == 0) return result; // NoInlet

            // ---- 1. the horizontal approach: from the inlet to the first vertical PEX, however many tees it takes.
            // Breadth first, remembering who reached each element first, so the way back from a riser to the manifold
            // is the shortest one and can be read off afterwards.
            result.InletGuessed = net.InletGuessed(manifoldId);
            List<long> entries = new List<long>();
            Dictionary<long, long> cameFrom = new Dictionary<long, long>();
            HashSet<long> seen = new HashSet<long> { manifoldId };
            Queue<long> todo = new Queue<long>();
            int visited = 0;

            void Approach(long id, long from)
            {
                if (!seen.Add(id)) return;
                cameFrom[id] = from;
                TrailNode? node = net.Node(id);
                if (node == null || node.Kind == NodeKind.Manifold) return;       // another manifold is a wall
                if (node.Kind == NodeKind.Pipe)
                {
                    if (!node.IsPex) return;                                       // the trail ends at non-PEX
                    if (node.IsVertical) { entries.Add(id); return; }              // the riser starts here
                    result.ApproachPipes.Add(id);                                  // horizontal PEX the walk touched
                }

                todo.Enqueue(id);
            }

            foreach (long n in inlets) Approach(n, manifoldId);
            while (todo.Count > 0)
            {
                if (++visited > options.MaxApproachNodes)
                {
                    result.HitLimit = true;
                    break;
                }

                long from = todo.Dequeue();
                foreach (long n in net.Neighbors(from)) Approach(n, from);
            }

            if (entries.Count == 0)
            {
                result.Status = TrailStatus.NoRiser;
                return result;
            }

            // ---- 2. up (or down) the riser: every vertical PEX pipe connected through fittings, and no further.
            HashSet<long> marked = new HashSet<long>();
            HashSet<long> passed = new HashSet<long>();
            foreach (long entry in entries)
            {
                if (marked.Contains(entry)) continue;

                List<long> trail = new List<long>();
                Stack<long> pipes = new Stack<long>();
                Stack<long> passes = new Stack<long>();
                double low = double.PositiveInfinity, high = double.NegativeInfinity;

                void Consider(long id)
                {
                    TrailNode? node = net.Node(id);
                    if (node == null || node.Kind == NodeKind.Manifold) return;
                    if (node.Kind == NodeKind.Pipe)
                    {
                        // Only vertical PEX continues the trail; a horizontal pipe or other material ends it right there.
                        if (node.IsPex && node.IsVertical && marked.Add(id)) pipes.Push(id);
                        return;
                    }

                    if (passed.Add(id)) passes.Push(id);
                }

                marked.Add(entry);
                pipes.Push(entry);
                while (pipes.Count > 0)
                {
                    long p = pipes.Pop();
                    TrailNode node = net.Node(p)!;
                    trail.Add(p);
                    low = Math.Min(low, node.ZLow);
                    high = Math.Max(high, node.ZHigh);

                    foreach (long n in net.Neighbors(p)) Consider(n);
                    while (passes.Count > 0)
                        foreach (long n in net.Neighbors(passes.Pop())) Consider(n);
                }

                double rise = high - low;
                if (rise >= options.MinRiseFeet)
                {
                    result.RiserPipes.AddRange(trail);
                    result.RiseFeet = Math.Max(result.RiseFeet, rise);
                }
                else
                {
                    result.StubPipes.AddRange(trail);
                }
            }

            // ---- 3. the way back: for every entry that turned out to be part of a real riser, the PEX pipes between the
            // manifold and it are this manifold's own branch to the tower.
            HashSet<long> riser = new HashSet<long>(result.RiserPipes);
            HashSet<long> onPath = new HashSet<long>();
            foreach (long entry in entries)
            {
                if (!riser.Contains(entry)) continue;
                long cur = cameFrom[entry];
                while (cur != manifoldId && cameFrom.ContainsKey(cur))
                {
                    TrailNode? node = net.Node(cur);
                    if (node != null && node.Kind == NodeKind.Pipe && onPath.Add(cur)) result.PathPipes.Add(cur);
                    cur = cameFrom[cur];
                }
            }

            result.Status = result.RiserPipes.Count > 0 ? TrailStatus.Found : TrailStatus.TooShort;
            return result;
        }

        /// <summary>
        ///     Walks from every manifold and returns every pipe to ROUND, once: the riser pipes and each manifold's path
        ///     back to them (two manifolds on the same riser find the same riser pipes). The per-manifold results come
        ///     back too, for the report.
        /// </summary>
        public static HashSet<long> WalkAll(IPipeNetwork net, IEnumerable<long> manifolds, TrailOptions? options,
            List<TrailResult> results)
        {
            HashSet<long> risers = new HashSet<long>();
            foreach (long m in manifolds)
            {
                TrailResult r = Walk(net, m, options);
                results.Add(r);
                foreach (long id in r.RiserPipes) risers.Add(id);
                foreach (long id in r.PathPipes) risers.Add(id);
            }

            return risers;
        }
    }

    /// <summary>Which of a manifold's ports is the inlet.</summary>
    public static class ManifoldPorts
    {
        /// <summary>
        ///     The inlet is the biggest port (a 3/4" in, 1/2" outs). Returns the index of every port that is as big as
        ///     the biggest, or NONE when every port is the same size - then there is nothing to tell the inlet from an
        ///     outlet, and the caller decides what to do (the Revit side tries every port and says so). Radii in feet.
        /// </summary>
        public static List<int> PickInlets(IReadOnlyList<double> radii, double tolerance = 0.0005)
        {
            List<int> inlets = new List<int>();
            if (radii.Count < 2) return inlets;
            double max = radii.Max();
            for (int i = 0; i < radii.Count; i++)
                if (radii[i] >= max - tolerance) inlets.Add(i);
            if (inlets.Count == radii.Count) inlets.Clear();
            return inlets;
        }

        /// <summary>
        ///     A manifold is a pipe fitting with 5 or more pipe ports (an inlet and 4 outlets, or 6) - or 4 ports when
        ///     its family or type name says Multiport or Manifold. A cross has 4 and a tee 3, so neither qualifies.
        /// </summary>
        public static bool IsManifold(int pipePorts, string familyAndTypeName)
        {
            if (pipePorts >= 5) return true;
            if (pipePorts < 4) return false;
            return familyAndTypeName.IndexOf("multiport", StringComparison.OrdinalIgnoreCase) >= 0
                   || familyAndTypeName.IndexOf("manifold", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }

    public static class PipeShape
    {
        /// <summary>
        ///     Vertical means steeper than <paramref name="minAngleDegrees"/> from level, so a 45 degree offset in a riser
        ///     counts as part of it and a flat branch does not. A pipe with no length is not vertical.
        /// </summary>
        public static bool IsVertical(double dx, double dy, double dz, double minAngleDegrees = 45.0)
        {
            double flat = Math.Sqrt(dx * dx + dy * dy);
            double up = Math.Abs(dz);
            if (up < 1e-9) return false;
            return up >= flat * Math.Tan(minAngleDegrees * Math.PI / 180.0) - 1e-9;
        }
    }
}

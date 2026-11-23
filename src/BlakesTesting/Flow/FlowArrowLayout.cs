#nullable enable
using System;
using System.Collections.Generic;

namespace BlakesTesting.Flow
{
    /// <summary>A point or vector in a view's own coordinates (feet; x to the right, y up).</summary>
    public readonly struct V2
    {
        public V2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }

        public static V2 operator +(V2 a, V2 b) => new V2(a.X + b.X, a.Y + b.Y);
        public static V2 operator -(V2 a, V2 b) => new V2(a.X - b.X, a.Y - b.Y);
        public static V2 operator *(V2 a, double k) => new V2(a.X * k, a.Y * k);
        public double Dot(V2 o) => X * o.X + Y * o.Y;
        public double Length => Math.Sqrt(X * X + Y * Y);
    }

    /// <summary>One straight stroke of the drawing, in view coordinates.</summary>
    public readonly struct Seg
    {
        public Seg(V2 a, V2 b)
        {
            A = a;
            B = b;
        }

        public V2 A { get; }
        public V2 B { get; }
        public double Length => (B - A).Length;
    }

    /// <summary>What Flow Arrow is asked to draw: one pipe as the view sees it, and the two clicks.</summary>
    public sealed class FlowArrowInput
    {
        public FlowArrowInput(V2 pipeA, V2 pipeB, double pipeHalfWidth, V2 click1, V2? click2, double viewScale, int autoDirection, FlowArrowSettings settings)
        {
            PipeA = pipeA;
            PipeB = pipeB;
            PipeHalfWidth = pipeHalfWidth;
            Click1 = click1;
            Click2 = click2;
            ViewScale = viewScale;
            AutoDirection = autoDirection >= 0 ? 1 : -1;
            Settings = settings;
        }

        /// <summary>The pipe's two ends as the view sees them (a plan sees the pipe flattened).</summary>
        public V2 PipeA { get; }
        public V2 PipeB { get; }

        /// <summary>Half the pipe's outside diameter, feet.</summary>
        public double PipeHalfWidth { get; }

        /// <summary>The point picked on the pipe.</summary>
        public V2 Click1 { get; }

        /// <summary>The point picked for the direction (or, for a run, where it ends), or null.</summary>
        public V2? Click2 { get; }

        /// <summary>The view's scale denominator: 96 for 1/8" = 1'-0".</summary>
        public double ViewScale { get; }

        /// <summary>+1 = the flow runs from PipeA toward PipeB, -1 the other way: what the arrow does when the clicks do not say.</summary>
        public int AutoDirection { get; }

        public FlowArrowSettings Settings { get; }
    }

    /// <summary>What to draw: the arrow's strokes, the label's centre and angle, which way it points.</summary>
    public sealed class FlowArrowShape
    {
        public List<Seg> Lines { get; } = new List<Seg>();

        /// <summary>The label text; empty for an arrow with no text.</summary>
        public string Label { get; set; } = string.Empty;

        /// <summary>The middle of the label.</summary>
        public V2 LabelCenter { get; set; }

        /// <summary>The label's angle, radians counter-clockwise. Upright (between -90 and 90 degrees) when it follows the pipe, 0 when it is always level.</summary>
        public double LabelAngle { get; set; }

        /// <summary>The label's height on the drawing, feet (paper size at the view's scale).</summary>
        public double LabelHeight { get; set; }

        /// <summary>The point on the pipe's line the arrow belongs to.</summary>
        public V2 Anchor { get; set; }

        /// <summary>+1 = the arrow points from PipeA toward PipeB.</summary>
        public int Direction { get; set; }

        /// <summary>+1 = the arrow is on the text-up side of the pipe, -1 on the other.</summary>
        public int Side { get; set; }

        /// <summary>True when the clicks did not say which way, so the pipe's own slope decided.</summary>
        public bool Automatic { get; set; }
    }

    /// <summary>
    ///     The arrow's geometry, with no Revit in it. The pipe's line and the clicks decide everything:
    ///     <list type="bullet">
    ///         <item>the arrow sits at the first click, projected onto the pipe;</item>
    ///         <item>the second click, measured along the pipe, says which way it points;</item>
    ///         <item>the side the arrow and text go on is the side of the pipe's centre line the first click landed on, or,
    ///               if the second click is across the pipe, the side that one is on;</item>
    ///         <item>a second click within a tenth of a paper inch of the line (the same spot) leaves the direction to
    ///               <see cref="FlowArrowInput.AutoDirection"/>, and with no side given the text goes on the upper side.</item>
    ///     </list>
    ///     A run (<see cref="ComputeRun"/>) is the same arrow repeated along a stretch of pipe. Sizes are paper inches times
    ///     the view scale, so the arrow keeps its size on the sheet. The text follows the pipe upright or stays level.
    /// </summary>
    public static class FlowArrowLayout
    {
        /// <summary>A pipe whose view-length is under this (feet) points at the viewer: there is no way along it.</summary>
        public const double EndOnLength = 0.1;

        private const double ArrowHeadLength = 0.26;
        private const double ArrowHeadHalfWidth = 0.1;

        /// <summary>Paper inches to feet on the drawing at a view scale.</summary>
        public static double PaperToFeet(double paperInches, double viewScale) => paperInches * viewScale / 12.0;

        private sealed class Geo
        {
            public V2 A;
            public V2 U;
            public double Length;
            public double ArrowLength;
            public double TextHeight;
            public double Gap;
            public double Tolerance;
            public double HalfWidth;
            public double Angle;
            public V2 TextUp;
            public FlowArrowSettings Settings = new FlowArrowSettings();
        }

        private static Geo? MakeGeo(FlowArrowInput input, out string? problem)
        {
            problem = null;
            V2 ab = input.PipeB - input.PipeA;
            double length = ab.Length;
            if (length < EndOnLength)
            {
                problem = "That pipe points at the viewer in this view, so there is no way along it to draw an arrow. "
                          + "Pick a pipe that runs across the view.";
                return null;
            }

            FlowArrowSettings st = input.Settings;
            double scale = input.ViewScale > 0 ? input.ViewScale : 96;
            V2 u = ab * (1.0 / length);

            // The pipe's own upright angle: its direction, turned by a half turn when it would read upside down.
            double angle = Math.Atan2(u.Y, u.X);
            if (angle > Math.PI / 2 + 1e-9) angle -= Math.PI;
            else if (angle <= -Math.PI / 2 + 1e-9) angle += Math.PI;

            return new Geo
            {
                A = input.PipeA,
                U = u,
                Length = length,
                ArrowLength = PaperToFeet(st.ArrowLengthInches, scale),
                TextHeight = PaperToFeet(st.TextSizeInches, scale),
                Gap = PaperToFeet(0.04, scale),
                Tolerance = PaperToFeet(0.1, scale),
                HalfWidth = input.PipeHalfWidth,
                Angle = angle,
                TextUp = new V2(-Math.Sin(angle), Math.Cos(angle)),
                Settings = st,
            };
        }

        /// <summary>
        ///     The shortest stretch of pipe (feet, as the view sees it) Place on Selected Pipes will put an arrow on: the arrow
        ///     or the text, whichever is longer, and a quarter more, so it does not hang over the ends.
        /// </summary>
        public static double MinimumPipeLength(FlowArrowSettings settings, double viewScale)
        {
            double scale = viewScale > 0 ? viewScale : 96;
            double arrow = PaperToFeet(settings.ArrowLengthInches, scale);
            double text = StrokeFont.Width(FlowArrowSettings.CleanText(settings.Text), PaperToFeet(settings.TextSizeInches, scale));
            return Math.Max(arrow, text) * 1.25;
        }

        /// <summary>One arrow, from a point on the pipe and a point for the direction.</summary>
        public static FlowArrowShape? Compute(FlowArrowInput input, out string? problem)
        {
            Geo? g = MakeGeo(input, out problem);
            if (g == null) return null;

            // The anchor: the first click, put onto the pipe's line and kept on the pipe.
            double s = Math.Max(0, Math.Min(g.Length, (input.Click1 - g.A).Dot(g.U)));
            V2 anchor = g.A + g.U * s;

            double along = 0, across = 0;
            if (input.Click2.HasValue)
            {
                V2 m = input.Click2.Value - anchor;
                along = m.Dot(g.U);
                across = m.Dot(g.TextUp);
            }

            bool directionGiven = Math.Abs(along) > g.Tolerance;
            int direction = directionGiven ? (along > 0 ? 1 : -1) : input.AutoDirection;
            int side = PickSide(g, input.Click1, anchor, across);
            return Build(g, s, direction, side, !directionGiven, FlowArrowSettings.CleanText(g.Settings.Text));
        }

        /// <summary>
        ///     The same arrow repeated along the stretch of pipe between the two clicks, about every
        ///     <see cref="FlowArrowSettings.RunSpacingFeet"/>, split into equal cells with an arrow in the middle of each. Only
        ///     the arrow nearest the middle carries the label. The direction follows
        ///     <see cref="FlowArrowSettings.RunDirection"/>.
        /// </summary>
        public static List<FlowArrowShape>? ComputeRun(FlowArrowInput input, out string? problem)
        {
            Geo? g = MakeGeo(input, out problem);
            if (g == null) return null;
            if (!input.Click2.HasValue)
            {
                problem = "A run needs two clicks: where it starts and where it ends.";
                return null;
            }

            double s0 = Math.Max(0, Math.Min(g.Length, (input.Click1 - g.A).Dot(g.U)));
            double s1 = Math.Max(0, Math.Min(g.Length, (input.Click2.Value - g.A).Dot(g.U)));
            double lo = Math.Min(s0, s1), hi = Math.Max(s0, s1);
            if (hi - lo < g.ArrowLength * 0.5)
            {
                problem = "That stretch is shorter than the arrow. Click further apart along the pipe.";
                return null;
            }

            FlowArrowSettings st = g.Settings;
            int direction;
            switch (st.RunDirection)
            {
                case FlowArrowRunDirection.FollowSlope: direction = input.AutoDirection; break;
                case FlowArrowRunDirection.AwayFromSecondClick: direction = s1 >= s0 ? -1 : 1; break;
                default: direction = s1 >= s0 ? 1 : -1; break;
            }

            // The side: the first click's side of the centre line; the second is meant to be along the pipe, so it only
            // decides when it is clearly across.
            V2 anchor0 = g.A + g.U * s0;
            double across = (input.Click2.Value - (g.A + g.U * s1)).Dot(g.TextUp);
            int side = PickSide(g, input.Click1, anchor0, across);

            double spacing = Math.Max(1, st.RunSpacingFeet);
            int n = Math.Max(1, (int)Math.Round((hi - lo) / spacing));
            double step = (hi - lo) / n;
            string label = FlowArrowSettings.CleanText(st.Text);
            List<FlowArrowShape> shapes = new List<FlowArrowShape>();
            for (int k = 0; k < n; k++)
                shapes.Add(Build(g, lo + step * (k + 0.5), direction, side, st.RunDirection == FlowArrowRunDirection.FollowSlope, k == n / 2 ? label : string.Empty));
            return shapes;
        }

        private static int PickSide(Geo g, V2 click1, V2 anchor, double across)
        {
            // A second click across the pipe says it; failing that, the side of the pipe's centre line the FIRST click landed
            // on (a point on the pipe's surface, so a click on the lower half puts the arrow below); failing that, the upper
            // side. A click on the centre line itself (a pipe drawn as one line, or a click dead on it) says nothing.
            double firstAcross = (click1 - anchor).Dot(g.TextUp);
            double firstTolerance = Math.Max(0.15 * g.HalfWidth, 0.002);
            if (Math.Abs(across) > g.Tolerance) return across > 0 ? 1 : -1;
            if (Math.Abs(firstAcross) > firstTolerance) return firstAcross > 0 ? 1 : -1;
            return 1;
        }

        private static FlowArrowShape Build(Geo g, double s, int direction, int side, bool automatic, string label)
        {
            FlowArrowSettings st = g.Settings;
            V2 anchor = g.A + g.U * s;

            // Beside and In-the-pipe put the arrow clear of the pipe; On puts it on the centre line.
            double headHalf = ArrowHeadHalfWidth * g.ArrowLength;
            double arrowOffset = st.Position != FlowArrowPosition.On ? g.HalfWidth + headHalf + g.Gap : 0;

            // The text's own axes. Its extent toward the arrow's side is half its height when it runs along the pipe, and
            // grows with its width when it is always level and the pipe is steep.
            double textAngle = st.TextReading == FlowArrowTextReading.Horizontal ? 0 : g.Angle;
            V2 tx = new V2(Math.Cos(textAngle), Math.Sin(textAngle));
            V2 ty = new V2(-Math.Sin(textAngle), Math.Cos(textAngle));
            V2 n = g.TextUp * side;
            double textWidth = StrokeFont.Width(label, g.TextHeight);
            double extent = textWidth / 2 * Math.Abs(n.Dot(tx)) + g.TextHeight * 0.62 * Math.Abs(n.Dot(ty));
            double labelOffset = Math.Max(arrowOffset + headHalf, g.HalfWidth) + g.Gap + extent;

            FlowArrowShape shape = new FlowArrowShape
            {
                Anchor = anchor,
                Direction = direction,
                Side = side,
                Automatic = automatic,
                Label = label,
                LabelAngle = textAngle,
                LabelHeight = g.TextHeight,
                LabelCenter = st.Position == FlowArrowPosition.InPipe ? anchor : anchor + n * labelOffset,
            };

            V2 centre = anchor + n * arrowOffset;
            V2 flow = g.U * direction;
            V2 across2 = new V2(-flow.Y, flow.X);
            V2 At(double x, double y) => centre + flow * x + across2 * y;

            double half = g.ArrowLength / 2;
            if (st.Style == FlowArrowStyle.Chevrons)
            {
                double c = 0.17 * g.ArrowLength;
                foreach (double tip in new[] { -0.2, 0.1, 0.4 })
                {
                    double x = tip * g.ArrowLength;
                    shape.Lines.Add(new Seg(At(x - c, headHalf * 1.1), At(x, 0)));
                    shape.Lines.Add(new Seg(At(x, 0), At(x - c, -headHalf * 1.1)));
                }
            }
            else
            {
                double head = ArrowHeadLength * g.ArrowLength;
                shape.Lines.Add(new Seg(At(-half, 0), At(half, 0)));
                shape.Lines.Add(new Seg(At(half - head, headHalf), At(half, 0)));
                shape.Lines.Add(new Seg(At(half, 0), At(half - head, -headHalf)));
            }

            return shape;
        }
    }
}

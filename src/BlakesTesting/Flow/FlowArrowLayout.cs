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

        /// <summary>The point picked for the direction, or null.</summary>
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

        /// <summary>The label's angle, radians counter-clockwise, always upright (between -90 and 90 degrees).</summary>
        public double LabelAngle { get; set; }

        /// <summary>The label's height on the drawing, feet (paper size at the view's scale).</summary>
        public double LabelHeight { get; set; }

        /// <summary>The point on the pipe's line the arrow belongs to.</summary>
        public V2 Anchor { get; set; }

        /// <summary>+1 = the arrow points from PipeA toward PipeB.</summary>
        public int Direction { get; set; }

        /// <summary>+1 = the label is on the text-up side of the pipe, -1 on the other.</summary>
        public int Side { get; set; }

        /// <summary>True when the clicks did not say which way, so the pipe's own slope decided.</summary>
        public bool Automatic { get; set; }
    }

    /// <summary>
    ///     The arrow's geometry, with no Revit in it. The pipe's line and the two clicks decide everything:
    ///     <list type="bullet">
    ///         <item>the arrow sits at the first click, projected onto the pipe;</item>
    ///         <item>the second click, measured along the pipe, says which way it points;</item>
    ///         <item>the second click, measured across the pipe, says which side the text is on;</item>
    ///         <item>a second click within a tenth of a paper inch of the line (the same spot) leaves the direction to
    ///               <see cref="FlowArrowInput.AutoDirection"/>, and no click across leaves the text on the upper side.</item>
    ///     </list>
    ///     Sizes are paper inches times the view scale, so the arrow keeps its size on the sheet. The text is always upright.
    /// </summary>
    public static class FlowArrowLayout
    {
        /// <summary>A pipe whose view-length is under this (feet) points at the viewer: there is no way along it.</summary>
        public const double EndOnLength = 0.1;

        private const double ArrowHeadLength = 0.26;
        private const double ArrowHeadHalfWidth = 0.1;

        /// <summary>Paper inches to feet on the drawing at a view scale.</summary>
        public static double PaperToFeet(double paperInches, double viewScale) => paperInches * viewScale / 12.0;

        public static FlowArrowShape? Compute(FlowArrowInput input, out string? problem)
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
            V2 u = ab * (1.0 / length);
            double scale = input.ViewScale > 0 ? input.ViewScale : 96;
            double arrowLength = PaperToFeet(st.ArrowLengthInches, scale);
            double textHeight = PaperToFeet(st.TextSizeInches, scale);
            double gap = PaperToFeet(0.04, scale);
            double clickTolerance = PaperToFeet(0.1, scale);

            // The anchor: the first click, put onto the pipe's line and kept on the pipe.
            double s = Math.Max(0, Math.Min(length, (input.Click1 - input.PipeA).Dot(u)));
            V2 anchor = input.PipeA + u * s;

            // Text is upright: its angle is the pipe's, turned by a half turn when it would read upside down.
            double angle = Math.Atan2(u.Y, u.X);
            if (angle > Math.PI / 2 + 1e-9) angle -= Math.PI;
            else if (angle <= -Math.PI / 2 + 1e-9) angle += Math.PI;
            V2 textUp = new V2(-Math.Sin(angle), Math.Cos(angle));

            double along = 0, across = 0;
            if (input.Click2.HasValue)
            {
                V2 m = input.Click2.Value - anchor;
                along = m.Dot(u);
                across = m.Dot(textUp);
            }

            bool directionGiven = Math.Abs(along) > clickTolerance;
            int direction = directionGiven ? (along > 0 ? 1 : -1) : input.AutoDirection;
            int side = Math.Abs(across) > clickTolerance ? (across > 0 ? 1 : -1) : 1;

            double headHalf = ArrowHeadHalfWidth * arrowLength;
            double arrowOffset = st.Position == FlowArrowPosition.Beside ? input.PipeHalfWidth + headHalf + gap : 0;
            double labelOffset = Math.Max(arrowOffset + headHalf, input.PipeHalfWidth) + gap + textHeight * 0.62;

            FlowArrowShape shape = new FlowArrowShape
            {
                Anchor = anchor,
                Direction = direction,
                Side = side,
                Automatic = !directionGiven,
                Label = FlowArrowSettings.CleanText(st.Text),
                LabelAngle = angle,
                LabelHeight = textHeight,
                LabelCenter = anchor + textUp * (side * labelOffset),
            };

            V2 centre = anchor + textUp * (side * arrowOffset);
            V2 flow = u * direction;
            V2 across2 = new V2(-flow.Y, flow.X);
            V2 At(double x, double y) => centre + flow * x + across2 * y;

            double half = arrowLength / 2;
            if (st.Style == FlowArrowStyle.Chevrons)
            {
                double c = 0.17 * arrowLength;
                foreach (double tip in new[] { -0.2, 0.1, 0.4 })
                {
                    double x = tip * arrowLength;
                    shape.Lines.Add(new Seg(At(x - c, headHalf * 1.1), At(x, 0)));
                    shape.Lines.Add(new Seg(At(x, 0), At(x - c, -headHalf * 1.1)));
                }
            }
            else
            {
                double head = ArrowHeadLength * arrowLength;
                shape.Lines.Add(new Seg(At(-half, 0), At(half, 0)));
                shape.Lines.Add(new Seg(At(half - head, headHalf), At(half, 0)));
                shape.Lines.Add(new Seg(At(half, 0), At(half - head, -headHalf)));
            }

            return shape;
        }
    }
}

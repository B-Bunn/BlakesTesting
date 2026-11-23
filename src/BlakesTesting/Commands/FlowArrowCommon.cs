#nullable enable
using System;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using BlakesTesting.Flow;

namespace BlakesTesting.Commands
{
    /// <summary>A pipe as the placing commands need it: its two ends, its half width and which way it falls.</summary>
    internal sealed class FlowPipe
    {
        public FlowPipe(Pipe pipe, XYZ a, XYZ b, double halfWidth)
        {
            Pipe = pipe;
            A = a;
            B = b;
            HalfWidth = halfWidth;
        }

        public Pipe Pipe { get; }
        public XYZ A { get; }
        public XYZ B { get; }
        public double HalfWidth { get; }

        /// <summary>No fall to speak of: there is no downhill to follow.</summary>
        public bool Level => Math.Abs(A.Z - B.Z) <= FlowArrowCommon.LevelTolerance;

        /// <summary>+1 = it falls from A toward B (or is level: the way it was drawn), -1 the other way.</summary>
        public int AutoDirection => A.Z - B.Z >= -FlowArrowCommon.LevelTolerance ? 1 : -1;
    }

    /// <summary>What the placing and editing commands share.</summary>
    internal static class FlowArrowCommon
    {
        /// <summary>Under this many feet of fall, a pipe is level.</summary>
        public const double LevelTolerance = 1e-4;

        /// <summary>
        ///     Checks there is a model and a view that takes arrows, then loads the settings and (unless Shift is held) shows
        ///     the window. False means the command should end as cancelled; <paramref name="message"/> may say why.
        /// </summary>
        public static bool Begin(ExternalCommandData data, string title, FlowArrowFormMode mode, ref string message,
            out UIDocument uidoc, out View view, out FlowArrowSettings settings)
        {
            uidoc = data.Application.ActiveUIDocument!;
            view = null!;
            settings = new FlowArrowSettings();
            if (uidoc == null)
            {
                message = "Open a model first.";
                return false;
            }

            view = uidoc.Document.ActiveView;
            string? viewProblem = FlowArrowFrame.ViewProblem(view);
            if (viewProblem != null)
            {
                TaskDialog.Show(title, viewProblem);
                return false;
            }

            settings = FlowArrowSettings.Load();
            bool skipWindow = (System.Windows.Forms.Control.ModifierKeys & System.Windows.Forms.Keys.Shift) == System.Windows.Forms.Keys.Shift;
            if (skipWindow) return true;
            if (!FlowArrowForm.Edit(settings, mode, out settings)) return false;
            settings.Remember(settings.Text);
            settings.Save();
            return true;
        }

        public static FlowPipe? Read(Element? element)
        {
            if (!(element is Pipe pipe) || !(pipe.Location is LocationCurve location)) return null;
            XYZ a = location.Curve.GetEndPoint(0), b = location.Curve.GetEndPoint(1);
            double od = pipe.get_Parameter(BuiltInParameter.RBS_PIPE_OUTER_DIAMETER)?.AsDouble() ?? 0;
            if (od <= 0) od = pipe.Diameter;
            return new FlowPipe(pipe, a, b, Math.Max(od / 2, 0.01));
        }

        /// <summary>The second click: a free point in a 2D view; in 3D, where there is none, a point on any element. Null when cancelled.</summary>
        public static XYZ? PickSecond(UIDocument uidoc, bool model, string prompt)
        {
            try
            {
                return model
                    ? uidoc.Selection.PickObject(ObjectType.PointOnElement, prompt).GlobalPoint
                    : uidoc.Selection.PickPoint(ObjectSnapTypes.None, prompt);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return null;
            }
        }

        public sealed class PipeFilter : ISelectionFilter
        {
            public bool AllowElement(Element elem) => elem is Pipe;

            public bool AllowReference(Reference reference, XYZ position) => true;
        }

        public static FlowArrowInput Input(FlowArrowFrame frame, FlowPipe pipe, XYZ click1, XYZ? click2, FlowArrowSettings settings) =>
            new FlowArrowInput(frame.To2D(pipe.A), frame.To2D(pipe.B), pipe.HalfWidth, frame.To2D(click1),
                click2 == null ? (V2?)null : frame.To2D(click2), frame.Scale, pipe.AutoDirection, settings);

        public const string SecondClickPrompt = "Click toward the way it flows. Click across the pipe to put the arrow on that side. Click the same spot to follow the pipe's slope (Esc cancels this arrow)";

        public static string SizeNote(bool model) => model
            ? "This is a 3D view, so each arrow is model lines in a plane facing the camera, and its text is drawn from lines too (capitals only).\n"
              + "Model lines show in other views as well. They are Comments = \"" + FlowArrowDrawing.MarkLine + "\" or \"" + FlowArrowDrawing.MarkText + "\", so a view filter can hide them there."
            : "";
    }
}

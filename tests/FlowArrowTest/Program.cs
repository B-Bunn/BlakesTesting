#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BlakesTesting.Flow;
using BlakesTesting.Risers;

// Revit-free checks of Flow Arrow: the settings file, the arrow's geometry (direction and side from the two clicks, upright
// text, paper-size scaling) and the stroke font the 3D label is drawn with. Exit code 1 on any failure.
internal static class Program
{
    private static int _failures;
    private static int _checks;

    private static void Check(bool ok, string what)
    {
        _checks++;
        if (ok) return;
        _failures++;
        Console.WriteLine("FAIL: " + what);
    }

    private static void Near(double actual, double expected, string what, double tol = 1e-6) =>
        Check(Math.Abs(actual - expected) <= tol, what + " (expected " + expected + ", got " + actual + ")");

    private static FlowArrowSettings Plain() => new FlowArrowSettings { Text = "FLOW", Position = FlowArrowPosition.Beside };

    // A 4" pipe along +x from (0,0) to (30,0), 1/8" scale (96), first click at x = 10.
    private static FlowArrowShape Horizontal(V2? click2, int auto = 1, FlowArrowSettings? st = null, double scale = 96)
    {
        FlowArrowShape? s = FlowArrowLayout.Compute(
            new FlowArrowInput(new V2(0, 0), new V2(30, 0), 4.0 / 12 / 2, new V2(10, 0.1), click2, scale, auto, st ?? Plain()), out string? problem);
        Check(s != null && problem == null, "horizontal pipe lays out (" + problem + ")");
        return s!;
    }

    private static int Main()
    {
        Settings();
        Direction();
        Side();
        FirstClickSide();
        Auto();
        Upright();
        PaperSize();
        Styles();
        EndOn();
        Font();
        Snap();
        NewOptions();
        Run();
        Console.WriteLine(_failures == 0 ? "OK - " + _checks + " checks" : _failures + " of " + _checks + " checks FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static void Settings()
    {
        FlowArrowSettings d = new FlowArrowSettings();
        Check(d.Text == "FLOW", "default text is FLOW");

        FlowArrowSettings s = new FlowArrowSettings { Text = "STORM", Style = FlowArrowStyle.Chevrons, Position = FlowArrowPosition.On, ArrowLengthInches = 0.75, TextSizeInches = 0.125, LineStyle = "Wide Lines" };
        s.Remember("FLOW"); s.Remember("STORM"); s.Remember("flow");
        Check(s.Recent.Count == 2 && s.Recent[0] == "flow" && s.Recent[1] == "STORM", "recent is newest first, once each, case-insensitively");
        FlowArrowSettings back = FlowArrowSettings.Parse(s.ToText());
        Check(back.Text == "STORM" && back.Style == FlowArrowStyle.Chevrons && back.Position == FlowArrowPosition.On, "text, style and position survive the file");
        Near(back.ArrowLengthInches, 0.75, "arrow length survives the file");
        Near(back.TextSizeInches, 0.125, "text size survives the file");
        Check(back.LineStyle == "Wide Lines" && back.Recent.SequenceEqual(new[] { "flow", "STORM" }), "line style and recent survive the file");

        FlowArrowSettings odd = FlowArrowSettings.Parse("Text=A=B\nStyle=nonsense\nArrowLengthInches=abc\nTextSizeInches=99\n");
        Check(odd.Text == "A=B", "a text with = in it is read whole");
        Check(odd.Style == FlowArrowStyle.Arrow, "an unknown style keeps the default");
        Near(odd.ArrowLengthInches, 0.5, "an unreadable length keeps the default");
        Near(odd.TextSizeInches, 0.75, "a huge text size is capped");
        Check(FlowArrowSettings.Parse(null).Text == "FLOW", "no file is the defaults");

        // Color: none by default, and a color (or "auto") survives the file.
        Check(new FlowArrowSettings().Color == null, "the default color is the line style's own");
        FlowArrowSettings red = new FlowArrowSettings { Color = new RgbColor(220, 0, 0) };
        FlowArrowSettings redBack = FlowArrowSettings.Parse(red.ToText());
        Check(redBack.Color.HasValue && redBack.Color.Value.Equals(new RgbColor(220, 0, 0)), "a color survives the file");
        Check(FlowArrowSettings.Parse(new FlowArrowSettings().ToText()).Color == null, "'auto' reads back as no color");
        Check(FlowArrowSettings.Parse("Color=nonsense\n").Color == null, "an unreadable color is no color");
        Check(red.Clone().Color.HasValue && red.Clone().Color!.Value.Equals(red.Color!.Value), "a clone keeps the color");
        Check(FlowArrowSettings.CleanText("  A\r\nB ") == "A  B", "line breaks become spaces");
        Check(FlowArrowSettings.Parse("Text=\n").Text == "", "an empty text stays empty: an arrow with no text");
        FlowArrowSettings full = new FlowArrowSettings();
        for (int i = 0; i < 12; i++) full.Remember("T" + i);
        Check(full.Recent.Count == FlowArrowSettings.MaxRecent && full.Recent[0] == "T11", "recent is capped, newest kept");
    }

    private static void Direction()
    {
        // Click 2 to the right of click 1: points toward B (+x). To the left: toward A.
        FlowArrowShape right = Horizontal(new V2(16, 0));
        Check(right.Direction == 1 && !right.Automatic, "a second click further along B points toward B");
        FlowArrowShape left = Horizontal(new V2(4, 0));
        Check(left.Direction == -1 && !left.Automatic, "a second click back toward A points toward A");

        // The head is at the end the arrow points to: the head's tip is the shaft's far end in the flow direction.
        Seg shaft = right.Lines[0];
        Check(shaft.B.X > shaft.A.X, "pointing right: the shaft runs left to right");
        Seg shaftL = left.Lines[0];
        Check(shaftL.B.X < shaftL.A.X, "pointing left: the shaft runs right to left");
        Check(right.Lines[1].B.X == shaft.B.X && right.Lines[2].A.X == shaft.B.X, "the head's strokes meet at the shaft's tip");
    }

    private static void Side()
    {
        FlowArrowShape above = Horizontal(new V2(16, 2));
        Check(above.Side == 1 && above.LabelCenter.Y > 0, "a click above the pipe puts the text above");
        FlowArrowShape below = Horizontal(new V2(16, -2));
        Check(below.Side == -1 && below.LabelCenter.Y < 0, "a click below the pipe puts the text below");
        FlowArrowShape none = Horizontal(new V2(16, 0));
        Check(none.Side == 1, "a click along the pipe leaves the text on the upper side");
        // The arrow is on the same side as the text, and the text is beyond it.
        Check(above.Lines[0].A.Y > 4.0 / 12 / 2 && above.LabelCenter.Y > above.Lines[0].A.Y, "beside the pipe: the arrow clears the pipe and the text sits beyond it");
        Near(above.Anchor.X, 10, "the arrow is at the first click's place along the pipe");
        Near(above.Anchor.Y, 0, "and the anchor is on the pipe's line, not at the click");
        Near(above.LabelCenter.X, 10, "the text is centred on the arrow");
    }

    private static FlowArrowShape At(V2 click1, V2? click2) =>
        FlowArrowLayout.Compute(new FlowArrowInput(new V2(0, 0), new V2(30, 0), 4.0 / 12 / 2, click1, click2, 96, 1, Plain()), out _)!;

    private static void FirstClickSide()
    {
        // Click on the lower half of the pipe, then along it: the arrow goes below.
        FlowArrowShape low = At(new V2(10, -0.1), new V2(16, 0));
        Check(low.Side == -1 && low.LabelCenter.Y < 0, "a first click on the lower half puts the arrow and text below");
        FlowArrowShape high = At(new V2(10, 0.1), new V2(16, 0));
        Check(high.Side == 1 && high.LabelCenter.Y > 0, "a first click on the upper half puts them above");
        // Dead on the centre line says nothing: the upper side.
        Check(At(new V2(10, 0), new V2(16, 0)).Side == 1, "a first click dead on the centre line leaves the upper side");
        Check(At(new V2(10, -0.001), new V2(16, 0)).Side == 1, "a first click a hair off the centre line says nothing either");
        // A deliberate second click across the pipe wins over where the first landed.
        Check(At(new V2(10, -0.1), new V2(16, 2)).Side == 1, "a second click across the pipe overrides the first click's side");
        Check(At(new V2(10, 0.1), new V2(16, -2)).Side == -1, "in either direction");
        // The same with no second click at all (auto direction).
        Check(At(new V2(10, -0.1), null).Side == -1, "with no second click the first click's side still counts");

        // A vertical pipe: its text-up side is the left, so a click on its RIGHT half puts the arrow on the right.
        FlowArrowShape? right = FlowArrowLayout.Compute(
            new FlowArrowInput(new V2(0, 0), new V2(0, 20), 0.17, new V2(0.1, 10), new V2(0, 14), 96, 1, Plain()), out _);
        Check(right != null && right.LabelCenter.X > 0, "a click on the right half of a vertical pipe puts the arrow on its right");
        FlowArrowShape? left = FlowArrowLayout.Compute(
            new FlowArrowInput(new V2(0, 0), new V2(0, 20), 0.17, new V2(-0.1, 10), new V2(0, 14), 96, 1, Plain()), out _);
        Check(left != null && left.LabelCenter.X < 0, "and on the left half, its left");
        // The pipe drawn the other way round makes no difference to which side is clicked.
        FlowArrowShape? flipped = FlowArrowLayout.Compute(
            new FlowArrowInput(new V2(0, 20), new V2(0, 0), 0.17, new V2(0.1, 10), new V2(0, 14), 96, 1, Plain()), out _);
        Check(flipped != null && flipped.LabelCenter.X > 0, "whichever end the pipe was drawn from");
    }

    private static void Auto()
    {
        // The second click on the same spot: the pipe's slope decides.
        FlowArrowShape same = Horizontal(new V2(10, 0.05), auto: -1);
        Check(same.Direction == -1 && same.Automatic, "the same spot twice follows the pipe's slope (here toward A)");
        FlowArrowShape none = Horizontal(null, auto: 1);
        Check(none.Direction == 1 && none.Automatic, "no second click follows the pipe's slope (toward B)");
        // Just across the pipe: the side is taken, the direction is still the slope's.
        FlowArrowShape across = Horizontal(new V2(10, -3), auto: 1);
        Check(across.Automatic && across.Side == -1 && across.Direction == 1, "just across the pipe sets the side and follows the slope");
        // The tolerance is paper: 0.1" of it, which is 0.8 ft at 1/8" scale and 0.4 ft at 1/4" scale.
        FlowArrowShape near96 = Horizontal(new V2(10.6, 0), auto: -1, scale: 96);
        Check(near96.Automatic, "0.6 ft away (0.075\" of paper) is the same spot at 1/8\" scale");
        FlowArrowShape near48 = Horizontal(new V2(10.6, 0), auto: -1, scale: 48);
        Check(!near48.Automatic && near48.Direction == 1, "the same 0.6 ft is a real click (0.15\" of paper) at 1/4\" scale");
    }

    private static void Upright()
    {
        // The pipe drawn from the right to the left: the text still reads left to right.
        FlowArrowShape? back = FlowArrowLayout.Compute(
            new FlowArrowInput(new V2(30, 0), new V2(0, 0), 0.17, new V2(10, 0), new V2(4, 0), 96, 1, Plain()), out _);
        Check(back != null && Math.Abs(back.LabelAngle) < 1e-9, "a pipe drawn right to left still has level text");
        Check(back != null && back.LabelCenter.Y > 0, "and its text is still above it");

        // A vertical pipe: the text reads bottom to top (+90 degrees) whichever end is first.
        foreach ((V2 a, V2 b) in new[] { (new V2(0, 0), new V2(0, 20)), (new V2(0, 20), new V2(0, 0)) })
        {
            FlowArrowShape? v = FlowArrowLayout.Compute(new FlowArrowInput(a, b, 0.17, new V2(0, 10), new V2(0, 14), 96, 1, Plain()), out _);
            Near(v!.LabelAngle, Math.PI / 2, "a vertical pipe's text reads bottom to top");
            Check(v.LabelCenter.X < 0, "a vertical pipe's text-up side is to its left");
        }

        // No angle ever leaves (-90, 90].
        for (int deg = 0; deg < 360; deg += 15)
        {
            double r = deg * Math.PI / 180;
            FlowArrowShape? s = FlowArrowLayout.Compute(new FlowArrowInput(new V2(0, 0), new V2(20 * Math.Cos(r), 20 * Math.Sin(r)), 0.17, new V2(0, 0), null, 96, 1, Plain()), out _);
            Check(s != null && s.LabelAngle > -Math.PI / 2 - 1e-9 && s.LabelAngle <= Math.PI / 2 + 1e-9, "text angle is upright for a pipe at " + deg + " degrees");
        }
    }

    private static void PaperSize()
    {
        FlowArrowShape eighth = Horizontal(new V2(16, 0), scale: 96);
        FlowArrowShape quarter = Horizontal(new V2(16, 0), scale: 48);
        Near(eighth.Lines[0].Length, 0.5 * 96 / 12, "a 1/2\" arrow is 4 ft long at 1/8\" scale");
        Near(quarter.Lines[0].Length, 0.5 * 48 / 12, "and 2 ft at 1/4\" scale");
        Near(eighth.LabelHeight, 3.0 / 32 * 96 / 12, "the text is 3/32\" of paper, 0.75 ft at 1/8\" scale");
        FlowArrowShape big = Horizontal(new V2(16, 0), st: new FlowArrowSettings { ArrowLengthInches = 1, Position = FlowArrowPosition.On });
        Near(big.Lines[0].Length, 8, "a 1\" arrow is 8 ft at 1/8\" scale");
    }

    private static void Styles()
    {
        FlowArrowShape arrow = Horizontal(new V2(16, 0));
        Check(arrow.Lines.Count == 3, "the arrow style is a shaft and two head strokes");
        FlowArrowShape chev = Horizontal(new V2(16, 0), st: new FlowArrowSettings { Style = FlowArrowStyle.Chevrons });
        Check(chev.Lines.Count == 6, "the chevron style is three vees");
        FlowArrowShape on = Horizontal(new V2(16, 0), st: new FlowArrowSettings { Position = FlowArrowPosition.On });
        Near(on.Lines[0].A.Y, 0, "'on pipe' draws the arrow on the pipe's line");
        Check(on.LabelCenter.Y > 4.0 / 12 / 2, "and still keeps the text clear of the pipe");
        FlowArrowShape blank = Horizontal(new V2(16, 0), st: new FlowArrowSettings { Text = "  " });
        Check(blank.Label.Length == 0, "a blank text is no label");
    }

    private static void EndOn()
    {
        FlowArrowShape? s = FlowArrowLayout.Compute(new FlowArrowInput(new V2(5, 5), new V2(5.02, 5), 0.17, new V2(5, 5), null, 96, 1, Plain()), out string? problem);
        Check(s == null && problem != null, "a pipe that points at the viewer is refused, with a reason");
    }

    private static void Snap()
    {
        // A vertical pipe's arrow in a camera whose up is a hair off vertical: put exactly on Z, length kept.
        var a = (X: 10.0, Y: 20.0, Z: 5.0);
        var b = (X: 10.0 + 1e-6, Y: 20.0 - 2e-6, Z: 9.0);
        var s = AxisSnap.SnapEnd(a, b);
        Check(s.X == 10 && s.Y == 20, "a nearly vertical line becomes exactly vertical");
        double before = Math.Sqrt(1e-12 + 4e-12 + 16), after = s.Z - a.Z;
        Near(after, before, "and keeps its length", 1e-9);
        var down = AxisSnap.SnapEnd(a, (10.0 + 1e-5, 20.0, 1.0));
        Check(down.X == 10 && down.Z < a.Z, "a nearly vertical line pointing down stays pointing down");

        var h = AxisSnap.SnapEnd((0, 0, 0), (7.0, 1e-4, -1e-4));
        Check(h.Y == 0 && h.Z == 0 && Math.Abs(h.X - 7.0) < 1e-6, "a nearly horizontal line becomes exactly along X");
        var hy = AxisSnap.SnapEnd((0, 0, 0), (1e-4, -7.0, 0));
        Check(hy.X == 0 && hy.Y < 0, "and along Y, in the direction it pointed");

        // A real slope is left alone: 5 degrees is a drawn angle, not an error.
        var sloped = (X: 10.0, Y: 0.0, Z: 10.0 * Math.Tan(5 * Math.PI / 180));
        var kept = AxisSnap.SnapEnd((0, 0, 0), sloped);
        Check(kept == sloped, "a line 5 degrees off an axis is not touched");
        var diag = AxisSnap.SnapEnd((0, 0, 0), (3.0, 4.0, 0.0));
        Check(diag == (3.0, 4.0, 0.0), "a diagonal is not touched");

        // Already exact, or nothing: unchanged.
        Check(AxisSnap.SnapEnd((1, 1, 1), (1, 1, 8)) == (1, 1, 8), "an exact vertical line is unchanged");
        Check(AxisSnap.SnapEnd((1, 1, 1), (1, 1, 1)) == (1, 1, 1), "a zero-length line is unchanged");
    }

    private static FlowArrowShape With(Action<FlowArrowSettings> tweak, V2 click1, V2? click2, V2? a = null, V2? b = null)
    {
        FlowArrowSettings st = Plain();
        tweak(st);
        return FlowArrowLayout.Compute(new FlowArrowInput(a ?? new V2(0, 0), b ?? new V2(30, 0), 4.0 / 12 / 2, click1, click2, 96, 1, st), out _)!;
    }

    private static void NewOptions()
    {
        // In the pipe: the text sits ON the pipe, the arrow stays beside it.
        FlowArrowShape inPipe = With(st => st.Position = FlowArrowPosition.InPipe, new V2(10, 0.1), new V2(16, 0));
        Near(inPipe.LabelCenter.Y, 0, "in-the-pipe text is on the pipe's centre line");
        Near(inPipe.LabelCenter.X, 10, "and centred on the arrow's place along it");
        Check(inPipe.Lines[0].A.Y > 4.0 / 12 / 2, "while the arrow is beside the pipe, clear of it");

        // Always-level text on a vertical pipe: level, and clear of the pipe by half its width.
        V2 va = new V2(0, 0), vb = new V2(0, 20);
        FlowArrowShape level = With(st => st.TextReading = FlowArrowTextReading.Horizontal, new V2(0.1, 10), new V2(0, 14), va, vb);
        Near(level.LabelAngle, 0, "level text on a vertical pipe has angle 0");
        FlowArrowShape along = With(st => { }, new V2(0.1, 10), new V2(0, 14), va, vb);
        Check(Math.Abs(level.LabelCenter.X) > Math.Abs(along.LabelCenter.X), "level text on a vertical pipe is held further from it, so its width clears the pipe");
        Check(level.LabelCenter.X > 0, "and still on the side that was clicked");
        double halfWidth = StrokeFont.Width("FLOW", level.LabelHeight) / 2;
        Check(level.LabelCenter.X - halfWidth > 4.0 / 12 / 2, "its near edge is clear of the pipe");
        // On a horizontal pipe it makes no difference.
        FlowArrowShape h1 = With(st => st.TextReading = FlowArrowTextReading.Horizontal, new V2(10, 0.1), new V2(16, 0));
        FlowArrowShape h2 = With(st => { }, new V2(10, 0.1), new V2(16, 0));
        Near(h1.LabelCenter.Y, h2.LabelCenter.Y, "level text on a level pipe is placed as before");

        // Settings: the new ones survive the file.
        FlowArrowSettings s = new FlowArrowSettings { Position = FlowArrowPosition.InPipe, TextReading = FlowArrowTextReading.Horizontal, RunSpacingFeet = 12.5, RunDirection = FlowArrowRunDirection.FollowSlope, SkipMarked = false };
        FlowArrowSettings back = FlowArrowSettings.Parse(s.ToText());
        Check(back.Position == FlowArrowPosition.InPipe && back.TextReading == FlowArrowTextReading.Horizontal, "position and text reading survive the file");
        Check(back.RunDirection == FlowArrowRunDirection.FollowSlope && !back.SkipMarked, "run direction and skip-marked survive the file");
        Near(back.RunSpacingFeet, 12.5, "run spacing survives the file");
        Check(new FlowArrowSettings().SkipMarked, "skipping pipes that already have an arrow is on by default");
        Near(FlowArrowSettings.Parse("RunSpacingFeet=0\n").RunSpacingFeet, 1, "a run spacing under a foot is raised to a foot");

        // The shortest pipe Place on Selected Pipes will mark: the longer of arrow and text, and a quarter more.
        FlowArrowSettings plain = Plain();
        Near(FlowArrowLayout.MinimumPipeLength(plain, 96), Math.Max(4, StrokeFont.Width("FLOW", 0.75)) * 1.25, "the shortest pipe it will mark is the longer of the arrow and the text, plus a quarter");
        Check(FlowArrowLayout.MinimumPipeLength(plain, 48) < FlowArrowLayout.MinimumPipeLength(plain, 96), "and it is shorter at a larger scale");
    }

    private static List<FlowArrowShape> RunOf(Action<FlowArrowSettings> tweak, V2 c1, V2? c2, int auto = 1, V2? a = null, V2? b = null)
    {
        FlowArrowSettings st = Plain();
        tweak(st);
        List<FlowArrowShape>? r = FlowArrowLayout.ComputeRun(new FlowArrowInput(a ?? new V2(0, 0), b ?? new V2(60, 0), 4.0 / 12 / 2, c1, c2, 96, auto, st), out string? problem);
        Check(r != null, "run lays out (" + problem + ")");
        return r!;
    }

    private static void Run()
    {
        // 8 ft spacing over 32 ft: four equal cells, an arrow in the middle of each.
        List<FlowArrowShape> run = RunOf(st => { }, new V2(10, 0.1), new V2(42, 0));
        Check(run.Count == 4, "32 ft at about 8 ft apart is four arrows");
        Near(run[0].Anchor.X, 14, "the first is in the middle of the first cell");
        Near(run[3].Anchor.X, 38, "the last in the middle of the last");
        Near(run[1].Anchor.X - run[0].Anchor.X, 8, "they are evenly spaced");
        Check(run.All(r => r.Direction == 1), "toward the second click, to the right");
        Check(run.Count(r => r.Label.Length > 0) == 1 && run[2].Label == "FLOW", "only one arrow, near the middle, carries the text");

        // Backwards: toward the start, and the same stretch.
        List<FlowArrowShape> back = RunOf(st => { }, new V2(42, 0.1), new V2(10, 0));
        Check(back.Count == 4 && back.All(r => r.Direction == -1), "clicked right to left the arrows point left");
        List<FlowArrowShape> away = RunOf(st => st.RunDirection = FlowArrowRunDirection.AwayFromSecondClick, new V2(10, 0.1), new V2(42, 0));
        Check(away.All(r => r.Direction == -1), "'away from the second click' reverses them");
        List<FlowArrowShape> slope = RunOf(st => st.RunDirection = FlowArrowRunDirection.FollowSlope, new V2(10, 0.1), new V2(42, 0), auto: -1);
        Check(slope.All(r => r.Direction == -1 && r.Automatic), "'follow slope' takes the pipe's fall, whichever way the clicks went");

        // Spacing, and the least: one arrow.
        List<FlowArrowShape> wide = RunOf(st => st.RunSpacingFeet = 20, new V2(10, 0), new V2(42, 0));
        Check(wide.Count == 2, "20 ft spacing over 32 ft is two arrows");
        List<FlowArrowShape> one = RunOf(st => st.RunSpacingFeet = 50, new V2(10, 0), new V2(42, 0));
        Check(one.Count == 1 && one[0].Label == "FLOW" && Math.Abs(one[0].Anchor.X - 26) < 1e-9, "a spacing longer than the run is one arrow in the middle");

        // Side: the first click's side, as for one arrow.
        Check(RunOf(st => { }, new V2(10, -0.1), new V2(42, 0)).All(r => r.Side == -1), "the first click on the lower half puts the whole run below");
        Check(RunOf(st => { }, new V2(10, 0), new V2(42, 3)).All(r => r.Side == 1), "a second click clearly above puts it above");

        // Too short, and not two clicks.
        Check(FlowArrowLayout.ComputeRun(new FlowArrowInput(new V2(0, 0), new V2(60, 0), 0.17, new V2(10, 0), new V2(11, 0), 96, 1, Plain()), out string? p1) == null && p1 != null,
            "a stretch shorter than the arrow is refused with a reason");
        Check(FlowArrowLayout.ComputeRun(new FlowArrowInput(new V2(0, 0), new V2(60, 0), 0.17, new V2(10, 0), null, 96, 1, Plain()), out string? p2) == null && p2 != null,
            "a run with one click is refused with a reason");

        // The end of the pipe stops it: a second click past the end only reaches the end.
        List<FlowArrowShape> clipped = RunOf(st => { }, new V2(40, 0), new V2(500, 0));
        Check(clipped.Max(r => r.Anchor.X) <= 60 + 1e-9, "a second click past the end of the pipe stops at the end");
    }

    private static void Font()
    {
        foreach (char c in "FLOW STORM SAN VENT 1/2\" -+.,:()?0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ")
            Check(c == ' ' || StrokeFont.Layout(c.ToString(), 6, new V2(0, 0), 0).Count > 0, "glyph '" + c + "' has strokes");
        Check(StrokeFont.Layout("flow", 1, new V2(0, 0), 0).Count == StrokeFont.Layout("FLOW", 1, new V2(0, 0), 0).Count, "lower case is drawn as capitals");
        Check(StrokeFont.Layout("a~b", 1, new V2(0, 0), 0).Count > 0, "an unknown character is drawn as a question mark, not dropped");
        Check(StrokeFont.Layout("", 1, new V2(0, 0), 0).Count == 0, "no text, no strokes");

        // Centred: the strokes' extent is symmetric about the centre in x, and spans the cap height in y.
        var segs = StrokeFont.Layout("FLOW", 0.75, new V2(10, 20), 0);
        double minX = segs.Min(s => Math.Min(s.A.X, s.B.X)), maxX = segs.Max(s => Math.Max(s.A.X, s.B.X));
        double minY = segs.Min(s => Math.Min(s.A.Y, s.B.Y)), maxY = segs.Max(s => Math.Max(s.A.Y, s.B.Y));
        Near((minX + maxX) / 2, 10, "the text is centred left to right");
        Near(minY, 20 - 0.375, "the capitals' bottom is half a height under the centre");
        Near(maxY, 20 + 0.375, "and their top half a height over it");
        Near(maxX - minX, StrokeFont.Width("FLOW", 0.75), "the width matches what Width says");

        // Turned a quarter turn, it stands up: the extent is now tall, not wide.
        var up = StrokeFont.Layout("FLOW", 0.75, new V2(0, 0), Math.PI / 2);
        double w = up.Max(s => Math.Max(s.A.X, s.B.X)) - up.Min(s => Math.Min(s.A.X, s.B.X));
        double h = up.Max(s => Math.Max(s.A.Y, s.B.Y)) - up.Min(s => Math.Min(s.A.Y, s.B.Y));
        Check(h > w, "a text turned 90 degrees stands up");
        Check(segs.All(s => s.Length > 0), "no stroke is zero length");
    }
}

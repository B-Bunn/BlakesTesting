#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BlakesTesting.Risers;
using BlakesTesting.Trench;

// Revit-free checks of Trench Map: the length text, the settings file, the trench plan (BIM Tools' Calculate Spoils
// rules) and the legend. Exit code 1 on any failure.
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

    private static void Same(string actual, string expected, string what) =>
        Check(actual == expected, what + " (expected '" + expected + "', got '" + actual + "')");

    // The settings the first version of these checks used: colours change at 4, 8 and 12 feet.
    private static TrenchMapSettings Classic(double offset = 0, double below = 0) =>
        new TrenchMapSettings { FloorOffset = offset, BelowPipe = below, LevelName = "L", DepthMarks = new List<double> { 4, 8, 12 } }.Normalize();

    private static int Main()
    {
        LengthText();
        SettingsRules();
        SettingsFile();
        PipeGeometry();
        PlanLevelRun();
        PlanSlopedRun();
        PlanBottomOfPipe();
        PlanGaps();
        PlanJoins();
        Legend();

        Console.WriteLine(_failures == 0 ? "OK - " + _checks + " checks" : _failures + " of " + _checks + " checks FAILED");
        return _failures == 0 ? 0 : 1;
    }

    // ---------------------------------------------------------------- length text
    private static void LengthText()
    {
        foreach ((string text, double feet) in new[]
                 {
                     ("4'-6\"", 4.5), ("4' 6\"", 4.5), ("4'6\"", 4.5), ("4'", 4.0), ("0'-18\"", 1.5), ("54\"", 4.5), ("4.5", 4.5),
                     ("4'-6 1/2\"", 4.5 + 0.5 / 12), ("4'-1/2\"", 4 + 0.5 / 12), ("  12'-0\" ", 12.0), ("0", 0.0), ("1.5'", 1.5),
                 })
        {
            bool ok = TrenchText.TryParseFeet(text, out double got);
            Check(ok, "parses '" + text + "'");
            if (ok) Near(got, feet, "'" + text + "' in feet");
        }

        foreach (string bad in new[] { "", "   ", "abc", "-3", "4'-x\"", "4'-6 1/0\"", "NaN", "1/2/3\"" })
            Check(!TrenchText.TryParseFeet(bad, out _), "rejects '" + bad + "'");

        Same(TrenchText.Feet(4.5), "4'-6\"", "4.5 ft");
        Same(TrenchText.Feet(4), "4'-0\"", "4 ft");
        Same(TrenchText.Feet(0), "0'-0\"", "0 ft");
        Same(TrenchText.Feet(0.53125), "0'-6 3/8\"", "6 3/8 inches");
        Same(TrenchText.Feet(12.5), "12'-6\"", "12.5 ft");
        Same(TrenchText.Feet(1.99999), "2'-0\"", "rounds up to the next foot");
        Same(TrenchText.Width(1.5), "18\"", "18 inch width");
        Same(TrenchText.Width(2), "24\"", "24 inch width");
        Same(TrenchText.Width(1.625), "19.5\"", "19.5 inch width");

        Check(TrenchText.TryParseFeetList("4'-0\", 8'-0\"; 12", out List<double> list) && list.SequenceEqual(new[] { 4.0, 8.0, 12.0 }), "list of lengths");
        Check(TrenchText.TryParseFeetList("", out List<double> empty) && empty.Count == 0, "blank list is empty");
        Check(!TrenchText.TryParseFeetList("4, x", out _), "a bad item fails the list");
        Same(TrenchText.FeetList(new[] { 4.0, 8.0 }), "4'-0\", 8'-0\"", "list written");
    }

    // ---------------------------------------------------------------- settings rules
    private static void SettingsRules()
    {
        TrenchMapSettings s = Classic();
        Near(s.WidthFor(0.5), 1.5, "shallow trench is 18 in");
        Near(s.WidthFor(3.99), 1.5, "just under 4 ft is 18 in");
        Near(s.WidthFor(4.0), 2.0, "exactly 4 ft is 24 in");
        Near(s.WidthFor(9), 2.0, "deeper is 24 in");

        Check(s.BandOf(0) == 0 && s.BandOf(3.9) == 0, "under the first mark is band 0");
        Check(s.BandOf(4) == 1 && s.BandOf(7.99) == 1, "a mark starts the deeper band");
        Check(s.BandOf(8) == 2 && s.BandOf(12) == 3 && s.BandOf(40) == 3, "bands 2 and 3");
        Check(s.DepthBandCount == 4, "four bands for three marks");
        Same(s.BandText(0), "0'-0\" to 4'-0\"", "band 0 text");
        Same(s.BandText(1), "4'-0\" to 8'-0\"", "band 1 text");
        Same(s.BandText(3), "12'-0\" and deeper", "last band text");

        TrenchMapSettings none = new TrenchMapSettings { DepthMarks = new List<double>() };
        Check(none.DepthBandCount == 1 && none.BandOf(50) == 0, "no marks is one band");
        Same(none.BandText(0), "any depth", "one band's text");

        Check(s.Widths().SequenceEqual(new[] { 1.5, 2.0 }), "the default widths");
        DefaultMarksEverySixInches();
        Near(s.MetricShift, 0, "spoils depth has no shift");
        TrenchMapSettings bop = new TrenchMapSettings { ColorBy = TrenchColorBy.BottomOfPipe, FloorOffset = 0.5, BelowPipe = 0.25 };
        Near(bop.MetricShift, 0.25, "bottom of pipe shifts by offset less dig below");

        Check(Classic().WhatIsMissing() == null || new TrenchMapSettings().WhatIsMissing() != null, "a fresh settings object asks for the offset and level");
        Check(new TrenchMapSettings { FloorOffset = 0.5, LevelName = "Level 1" }.WhatIsMissing() == null, "offset and level are enough");
        Check(new TrenchMapSettings { FloorOffset = 0.5, Source = TrenchFloorSource.Floors, FloorParameter = "Comments" }.WhatIsMissing() != null,
            "floors need the value too");

        TrenchMapSettings dirty = new TrenchMapSettings
        {
            WidthBands = new List<TrenchWidthBand>
            {
                new TrenchWidthBand { ShallowerThan = 8, Width = 2 }, new TrenchWidthBand { ShallowerThan = 4, Width = 1.5 },
                new TrenchWidthBand { ShallowerThan = -1, Width = 3 }, new TrenchWidthBand { ShallowerThan = 4, Width = 9 },
            },
            DeeperWidth = double.NaN,
            BelowPipe = -2,
            FloorOffset = -1,
            DepthMarks = new List<double> { 8, 4, 4, -3, 0, 12 },
            Transparency = 400,
            LevelName = "  L1 ",
        }.Normalize();
        Check(dirty.WidthBands.Select(b => b.ShallowerThan).SequenceEqual(new[] { 4.0, 8.0 }), "bands sorted, bad and repeated rows dropped");
        Near(dirty.WidthBands[0].Width, 1.5, "the first of two rows at one depth is kept");
        Near(dirty.DeeperWidth, 2.0, "bad deeper width back to 24 in");
        Near(dirty.BelowPipe, 0, "negative dig below back to 0");
        Check(dirty.FloorOffset == null, "negative offset forgotten");
        Check(dirty.DepthMarks.SequenceEqual(new[] { 4.0, 8.0, 12.0 }), "marks sorted and distinct");
        Check(dirty.Transparency == TrenchMapSettings.MaxTransparency, "transparency capped");
        Same(dirty.LevelName, "L1", "names trimmed");

        TrenchMapSettings unusable = new TrenchMapSettings
        {
            WidthBands = new List<TrenchWidthBand> { new TrenchWidthBand { ShallowerThan = -1, Width = -1 } },
        }.Normalize();
        Check(unusable.WidthBands.Count == 1 && unusable.WidthBands[0].ShallowerThan == 4, "no usable row is the default rule");
        TrenchMapSettings onePurpose = new TrenchMapSettings { WidthBands = new List<TrenchWidthBand>(), DeeperWidth = 3 }.Normalize();
        Check(onePurpose.WidthBands.Count == 0 && Math.Abs(onePurpose.WidthFor(1) - 3) < 1e-9, "no rows on purpose is one width at every depth");
        Check(onePurpose.Widths().SequenceEqual(new[] { 3.0 }), "one width");
    }

    private static void SettingsFile()
    {
        TrenchMapSettings s = new TrenchMapSettings
        {
            FloorOffset = 0.5,
            Source = TrenchFloorSource.Floors,
            LevelName = "Level 1",
            FloorParameter = "Comments",
            FloorValue = "SOG",
            WidthBands = new List<TrenchWidthBand>
            {
                new TrenchWidthBand { ShallowerThan = 3.5, Width = 1.25 }, new TrenchWidthBand { ShallowerThan = 6, Width = 1.75 },
            },
            DeeperWidth = 2.5,
            BelowPipe = 0.333333333333,
            ColorBy = TrenchColorBy.BottomOfPipe,
            DepthMarks = new List<double> { 3, 6 },
            Transparency = 55,
            LegendFootage = false,
        }.Normalize();

        TrenchMapSettings back = TrenchMapSettings.Parse(s.ToText());
        Check(back.FloorOffset == 0.5, "offset round-trips");
        Check(back.Source == TrenchFloorSource.Floors && back.ColorBy == TrenchColorBy.BottomOfPipe, "enums round-trip");
        Same(back.LevelName + "|" + back.FloorParameter + "|" + back.FloorValue, "Level 1|Comments|SOG", "names round-trip");
        Check(back.WidthBands.Count == 2 && back.WidthBands[1].ShallowerThan == 6 && back.WidthBands[1].Width == 1.75, "bands round-trip");
        Near(back.DeeperWidth, 2.5, "deeper width round-trips");
        Near(back.BelowPipe, 0.333333333333, "dig below round-trips", 1e-12);
        Check(back.DepthMarks.SequenceEqual(new[] { 3.0, 6.0 }), "marks round-trip");
        Check(back.Transparency == 55 && !back.LegendFootage, "transparency and footage round-trip");

        TrenchMapSettings unsetOffset = TrenchMapSettings.Parse(new TrenchMapSettings().ToText());
        Check(unsetOffset.FloorOffset == null, "a blank offset stays blank");

        TrenchMapSettings garbage = TrenchMapSettings.Parse("nonsense\nFloorOffset=abc\nSource=Sideways\nWidthBands=x:y\nDepthMarks=q\nTransparency=lots\n");
        Check(garbage.FloorOffset == null && garbage.Source == TrenchFloorSource.Level, "garbage keeps the defaults");
        Check(garbage.WidthBands.Count == 1 && garbage.DepthMarks.Count == 24, "unreadable rows and marks keep the defaults");
        Check(TrenchMapSettings.Parse(null).DepthMarks.Count == 24, "null text is the defaults");

        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "trenchmap-test-" + Guid.NewGuid().ToString("N") + ".ini");
        try
        {
            Check(s.Save(path), "saves");
            Check(TrenchMapSettings.Load(path).DeeperWidth == 2.5, "loads what was saved");
            Check(TrenchMapSettings.Load(path + ".missing").DepthMarks.Count == 24, "a missing file is the defaults");
        }
        finally
        {
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
    }

    // ---------------------------------------------------------------- pipe geometry
    private static void PipeGeometry()
    {
        Check(TrenchRules.IsTrenchPipe(10, 0, 0), "level pipe is a trench pipe");
        Check(TrenchRules.IsTrenchPipe(10, 0, 5), "a 27 degree pipe is");
        Check(!TrenchRules.IsTrenchPipe(10, 0, 20), "a 63 degree pipe is a riser");
        Check(!TrenchRules.IsTrenchPipe(0, 0, 10), "a plumb pipe is");
        var level = TrenchRules.BottomOfPipe(0, 0, -5, 10, 0, 0, 0.5);
        Near(level.Z, -5.25, "level pipe: bottom is end less radius");
        Near(level.X, 0, "level pipe does not slide");
        var sloped = TrenchRules.BottomOfPipe(0, 0, -5, 10, 0, -1, 0.5);
        Check(level.Z - sloped.Z < 0.01 && sloped.Z < -5.24, "a gently sloped pipe is about the radius lower");
        Near(TrenchRules.BottomOfPipe(1, 2, 3, 0, 0, 5, 0.5).Z, 2.75, "plumb: end less radius");

        Check(TrenchRules.FloorOver(new[] { 0.0, 10.0 }, -5) == 0.0, "buried pipe: the slab over it");
        Check(TrenchRules.FloorOver(new[] { 0.0, 10.0 }, 5) == 0.0, "pipe above a floor: the floor under it");
        Check(TrenchRules.FloorOver(new double[0], 0) == null, "no floor");

        double?[] filled = TrenchRules.BridgeGaps(new[] { 0.0, 1.0, 2.0 }, new double?[] { 0, null, 2 }, 5);
        Near(filled[1] ?? double.NaN, 1, "a gap is bridged straight");
        double?[] far = TrenchRules.BridgeGaps(new[] { 0.0, 20.0, 40.0 }, new double?[] { 0, null, 2 }, 5);
        Check(far[1] == null, "a wide gap stays open");
    }

    // ---------------------------------------------------------------- the plan
    private static TrenchRun Run(long id, double x0, double y0, double z0, double x1, double y1, double z1, double extend = 0) =>
        new TrenchRun { Id = id, BopStart = (x0, y0, z0), BopEnd = (x1, y1, z1), ExtendStart = extend, ExtendEnd = extend };

    private static Func<double, double, double, double?> Floor(double top) => (x, y, z) => top;

    private static void PlanLevelRun()
    {
        TrenchMapSettings s = Classic();
        TrenchPlanResult r = TrenchRules.Plan(new[] { Run(1, 0, 0, -5, 10, 0, -5) }, s, Floor(0));
        Check(r.Pieces.Count == 1 && r.RunsWithTrench == 1, "a level run is one box");
        TrenchPiece p = r.Pieces[0];
        Check(p.Class.Equals(new TrenchClass(2, 1)), "5 ft deep: 24 in wide, band 4-8");
        Near(p.Length, 12, "squared one half width past each end");
        Near(p.Ax, -1, "starts a half width before the pipe");
        Near(p.Bx, 11, "ends a half width past it");
        Near(p.HalfWidth, 1, "half of 24 in");
        Near(r.DeepestDepth, 5, "deepest point");
        var corners = p.Corners();
        Near(Math.Abs(corners[0].Y), 1, "corners are a half width off the line");
        Check(corners[0].Y * corners[2].Y < 0, "and on both sides of it");

        // The fitting's centre runs the trench on past the pipe's end.
        TrenchPlanResult withFitting = TrenchRules.Plan(new[] { Run(1, 0, 0, -5, 10, 0, -5, extend: 0.5) }, s, Floor(0));
        Near(withFitting.Pieces[0].Length, 13, "run on to the fitting centres, then squared");
    }

    private static void PlanSlopedRun()
    {
        TrenchMapSettings s = Classic();
        // 12 ft run falling from 3 ft to 9 ft deep: 18 in under 4 ft, then 24 in; bands <4, 4-8, 8-12 cross at 2 ft and 10 ft.
        TrenchPlanResult r = TrenchRules.Plan(new[] { Run(7, 0, 0, -3, 12, 0, -9) }, s, Floor(0));
        Check(r.Pieces.Count == 3, "three boxes where the depth crosses 4 ft and 8 ft (got " + r.Pieces.Count + ")");
        if (r.Pieces.Count == 3)
        {
            Check(r.Pieces[0].Class.Equals(new TrenchClass(1.5, 0)), "first: 18 in, band 0");
            Check(r.Pieces[1].Class.Equals(new TrenchClass(2, 1)), "second: 24 in, band 1");
            Check(r.Pieces[2].Class.Equals(new TrenchClass(2, 2)), "third: 24 in, band 2");
            Near(r.Pieces[0].Bx, 2, "first box ends where the depth hits 4 ft");
            Near(r.Pieces[1].Bx, 10, "second ends where the depth hits 8 ft");
            Near(r.Pieces[0].Ax, -0.75, "first is squared by half of 18 in");
            Near(r.Pieces[2].Bx, 13, "last is squared by half of 24 in");
        }

        Near(r.DeepestDepth, 9, "deepest");

        // Widths that stay constant across a band merge: a long flat run is one box however many stations it has.
        TrenchPlanResult longRun = TrenchRules.Plan(new[] { Run(2, 0, 0, -5, 100, 0, -5) }, s, Floor(0));
        Check(longRun.Pieces.Count == 1, "a long flat run is one box");
    }

    private static void PlanBottomOfPipe()
    {
        // Floor offset 6 in and 3 in of bedding: the floor-to-bottom-of-pipe depth is the spoils depth plus 3 in.
        TrenchMapSettings spoils = Classic(0.5, 0.25);
        TrenchMapSettings bop = spoils.Clone();
        bop.ColorBy = TrenchColorBy.BottomOfPipe;

        // Floor at 0, offset 0.5 -> top of trench -0.5; pipe bottom -4.4 less bedding -> trench bottom -4.65: spoils depth 4.15.
        // A pipe bottom at -3.75 -> trench bottom -4.0 -> spoils depth 3.5 (below 4: 18 in); bottom of pipe depth 3.75 (still band 0).
        TrenchRun shallow = Run(1, 0, 0, -3.75, 10, 0, -3.75);
        Func<double, double, double, double?> top = (x, y, z) => -0.5;
        TrenchPlanResult a = TrenchRules.Plan(new[] { shallow }, spoils, top);
        Check(a.Pieces.Count == 1 && a.Pieces[0].Class.Equals(new TrenchClass(1.5, 0)), "spoils: 3.5 ft deep is 18 in, band 0");

        // Pipe bottom at -3.9: trench bottom -4.15, spoils depth 3.65 (band 0); bottom of pipe depth 3.9 (band 0).
        // Pipe bottom at -4.0: trench bottom -4.25, spoils depth 3.75 -> still 18 in; bottom of pipe depth 4.0 -> band 1.
        TrenchRun edge = Run(2, 0, 0, -4.0, 10, 0, -4.0);
        TrenchPlanResult spoilsEdge = TrenchRules.Plan(new[] { edge }, spoils, top);
        TrenchPlanResult bopEdge = TrenchRules.Plan(new[] { edge }, bop, top);
        Check(spoilsEdge.Pieces[0].Class.Equals(new TrenchClass(1.5, 0)), "spoils: 3.75 ft deep is band 0");
        Check(bopEdge.Pieces[0].Class.Equals(new TrenchClass(1.5, 1)), "bottom of pipe: 4.0 ft to the pipe is band 1, and the width still follows the spoils depth");
        Near(bopEdge.Pieces[0].MetricA, 4.0, "bottom-of-pipe depth is floor to bottom of pipe");
        Near(bopEdge.Pieces[0].DepthA, 3.75, "the spoils depth is untouched");

        // Sloped across the bottom-of-pipe mark: the box splits where floor-to-pipe depth is 4 ft (spoils depth 3.75).
        TrenchPlanResult slope = TrenchRules.Plan(new[] { Run(3, 0, 0, -3.0, 10, 0, -5.0) }, bop, top);
        Check(slope.Pieces.Count >= 2, "a slope splits at the bottom-of-pipe mark");
        Check(slope.Pieces[0].Class.DepthBand == 0 && slope.Pieces[slope.Pieces.Count - 1].Class.DepthBand >= 1, "shallow end band 0, deep end deeper");
    }

    private static void PlanGaps()
    {
        TrenchMapSettings s = Classic();
        TrenchPlanResult none = TrenchRules.Plan(new[] { Run(1, 0, 0, -5, 10, 0, -5) }, s, (x, y, z) => null);
        Check(none.Pieces.Count == 0 && none.RunsWithTrench == 0, "no floor: nothing drawn");
        Near(none.LengthNoFloor, 10, "and the length is counted");

        TrenchPlanResult above = TrenchRules.Plan(new[] { Run(1, 0, 0, -5, 10, 0, -5) }, s, Floor(-6));
        Check(above.Pieces.Count == 0, "a pipe above the top of the trench digs nothing");
        Near(above.LengthAboveTop, 10, "its length is counted");

        // A hole in the floor over the middle of a run is bridged from its neighbours.
        TrenchPlanResult hole = TrenchRules.Plan(new[] { Run(1, 0, 0, -5, 10, 0, -5) }, s, (x, y, z) => x > 4 && x < 6 ? (double?)null : 0);
        Check(hole.Pieces.Count == 1, "a floor drain's hole does not break the trench");
        Near(hole.LengthNoFloor, 0, "bridged, so not counted as no floor");

        // A plumb run has no trench line.
        TrenchPlanResult plumb = TrenchRules.Plan(new[] { Run(1, 0, 0, -5, 0, 0, 5) }, s, Floor(0));
        Check(plumb.Pieces.Count == 0 && plumb.Runs == 1, "plumb run: counted, nothing drawn");
    }

    private static void PlanJoins()
    {
        TrenchMapSettings s = Classic();
        // An L: two runs meeting at a corner each stretch past the corner by half their width, covering it square.
        TrenchPlanResult r = TrenchRules.Plan(new[] { Run(1, 0, 0, -5, 10, 0, -5), Run(2, 10, 0, -5, 10, 10, -5) }, s, Floor(0));
        Check(r.Pieces.Count == 2, "two runs, two boxes");
        Check(r.Pieces[0].Bx > 10 && r.Pieces[1].Ay < 0, "each runs a half width past the corner");
        Near(r.TrenchLength, r.Pieces.Sum(p => p.Length), "trench length is the sum");
    }

    // ---------------------------------------------------------------- the legend
    private static void Legend()
    {
        TrenchMapSettings s = Classic();
        TrenchPlanResult r = TrenchRules.Plan(new[] { Run(7, 0, 0, -3, 12, 0, -9), Run(8, 0, 20, -5, 10, 20, -5) }, s, Floor(0));
        List<TrenchLegendRow> rows = TrenchLegend.Rows(s, r.Pieces, f => TrenchText.Feet(f) + " LF");

        Check(rows.Count == 3, "three combinations are used (got " + rows.Count + ")");
        if (rows.Count == 3)
        {
            Check(rows[0].Class.Equals(new TrenchClass(1.5, 0)), "narrowest and shallowest first");
            Check(rows[1].Class.Equals(new TrenchClass(2, 1)) && rows[2].Class.Equals(new TrenchClass(2, 2)), "then by band within the width");
            Same(rows[0].Text, "18\" wide, 0'-0\" to 4'-0\" deep - 2'-9\" LF", "legend text with footage");
            // The 24 in / 4-8 ft combination: the sloped run's middle box (8 ft long) and the 10 ft flat run squared to 12 ft.
            Near(rows[1].Length, 8 + 12, "length of one combination adds across runs");
            Check(rows.Select(x => x.Color).Distinct().Count() == 3, "each combination has its own colour");
        }

        TrenchMapSettings noFootage = s.Clone();
        noFootage.LegendFootage = false;
        Same(TrenchLegend.Rows(noFootage, r.Pieces, f => "x")[0].Text, "18\" wide, 0'-0\" to 4'-0\" deep", "legend text without footage");

        // The colour belongs to the combination, not to which combinations a run happened to use.
        TrenchPlanResult onlyDeep = TrenchRules.Plan(new[] { Run(8, 0, 20, -5, 10, 20, -5) }, s, Floor(0));
        RgbColor alone = TrenchLegend.Rows(s, onlyDeep.Pieces, f => "")[0].Color;
        Check(alone.Equals(rows.First(x => x.Class.Equals(new TrenchClass(2, 1))).Color), "a combination keeps its colour from run to run");

        // Every combination the settings can give is a different colour.
        List<RgbColor> all = s.ReachableClasses().Select(c => TrenchLegend.ColorOf(s, c)).ToList();
        Check(all.Distinct().Count() == all.Count && all.Count == 4, "the 4 combinations of 4/8/12 ft marks are 4 colours (got " + all.Count + ")");

        // And a big rule still gives distinct colours.
        List<RgbColor> many = Enumerable.Range(0, 30).Select(i => TrenchLegend.ColorAt(i, 30)).ToList();
        Check(many.Distinct().Count() == 30, "30 combinations are 30 colours");

        (string title, string subtitle) = TrenchLegend.Heading(s);
        Check(title.Contains("SPOILS") && subtitle.Contains("top of the trench"), "spoils heading");
        TrenchMapSettings b2 = s.Clone();
        b2.ColorBy = TrenchColorBy.BottomOfPipe;
        Check(TrenchLegend.Heading(b2).Title.Contains("BOTTOM OF PIPE"), "bottom of pipe heading");
    }

    // ---------------------------------------------------------------- the 6 inch default
    private static void DefaultMarksEverySixInches()
    {
        TrenchMapSettings d = new TrenchMapSettings();
        Check(d.DepthMarks.Count == 24 && Math.Abs(d.DepthMarks[0] - 0.5) < 1e-9 && Math.Abs(d.DepthMarks[23] - 12) < 1e-9, "default marks: 6\" to 12'-0\", 24 of them");
        Check(d.DepthMarks.Zip(d.DepthMarks.Skip(1), (a, b) => b - a).All(g => Math.Abs(g - 0.5) < 1e-9), "every 6\" apart");
        Check(d.DepthBandCount == 25, "25 bands");
        Check(d.BandOf(0.49) == 0 && d.BandOf(0.5) == 1 && d.BandOf(5.25) == 10 && d.BandOf(30) == 24, "a colour change every 6\"");
        Same(d.BandText(0), "0'-0\" to 0'-6\"", "first band text");
        Same(d.BandText(10), "5'-0\" to 5'-6\"", "a middle band text");
        Same(d.BandText(24), "12'-0\" and deeper", "last band text");

        Check(TrenchMapSettings.MarksEvery(0.5, 2).SequenceEqual(new[] { 0.5, 1.0, 1.5, 2.0 }), "marks every step to the limit");
        Check(TrenchMapSettings.MarksEvery(1, 3).SequenceEqual(new[] { 1.0, 2.0, 3.0 }), "marks every foot");
        Check(TrenchMapSettings.MarksEvery(0, 3).Count == 0 && TrenchMapSettings.MarksEvery(1, -1).Count == 0, "no marks for a bad step or limit");
        Check(TrenchMapSettings.MarksEvery(0.01, 12).Count == 0, "no more than " + TrenchMapSettings.MaxMarks + " marks");

        // Width follows the spoils depth: 18" under 4 ft is bands 0-7, 24" is the rest, so 25 combinations.
        List<TrenchClass> reach = d.ReachableClasses();
        Check(reach.Count == 25 && reach.Take(8).All(c => c.Width == 1.5) && reach.Skip(8).All(c => c.Width == 2.0), "25 reachable combinations, 18 in under 4 ft");
        Check(reach.Select(c => TrenchLegend.ColorOf(d, c)).Distinct().Count() == 25, "25 different colours");

        // A band that holds a width change gives both widths.
        TrenchMapSettings straddle = new TrenchMapSettings { DepthMarks = new List<double> { 3, 6 } }.Normalize();
        Check(straddle.ReachableClasses().Count(c => c.DepthBand == 1) == 2, "a band across the 4 ft width change has both widths");
        // The bottom-of-pipe shift moves which bands exist: 6 in offset puts nothing in the bands under 6 in of spoils depth.
        TrenchMapSettings shifted = new TrenchMapSettings { ColorBy = TrenchColorBy.BottomOfPipe, FloorOffset = 1.0, DepthMarks = new List<double> { 0.5, 1, 2 } }.Normalize();
        Check(shifted.ReachableClasses().All(c => c.DepthBand >= 2), "bands the spoils depth cannot reach are left out");

        // A level run 5'-3" deep is band 10 under the default; a slope from 3 to 9 ft crosses every 6" mark.
        TrenchMapSettings level = new TrenchMapSettings { FloorOffset = 0, LevelName = "L" }.Normalize();
        TrenchPlanResult flat = TrenchRules.Plan(new[] { Run(1, 0, 0, -5.25, 10, 0, -5.25) }, level, Floor(0));
        Check(flat.Pieces.Count == 1 && flat.Pieces[0].Class.Equals(new TrenchClass(2, 10)), "5'-3\" deep: 24 in, band 10");
        TrenchPlanResult slope = TrenchRules.Plan(new[] { Run(2, 0, 0, -3, 24, 0, -9) }, level, Floor(0));
        Check(slope.Pieces.Count == 12, "3 ft to 9 ft crosses 11 marks: 12 boxes (got " + slope.Pieces.Count + ")");
        Check(slope.Pieces.Select(p => p.Class.DepthBand).Distinct().Count() == 12, "each its own colour");
        List<TrenchLegendRow> rows = TrenchLegend.Rows(level, slope.Pieces, f => TrenchText.Feet(f));
        Check(rows.Count == 12 && rows.Select(r => r.Color).Distinct().Count() == 12, "one legend row a colour");
    }
}

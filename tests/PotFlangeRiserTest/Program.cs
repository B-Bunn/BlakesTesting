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

// ---------------------------------------------------------------------------------------------------------------
// Which bend is the toilet's: the words Place Sleeves keys on (Quarter, 300 Fourth Bend) plus Medium Turn.
// ---------------------------------------------------------------------------------------------------------------
Check(PotFlangeRiserRules.IsToiletBend("Charlotte CI NH-4 Quarter Bend", "3\""), "a cast iron Quarter Bend is the toilet's bend");
Check(PotFlangeRiserRules.IsToiletBend("CLT 300 Fourth Bend", "3\" x 3\""), "a 300 Fourth Bend is");
Check(PotFlangeRiserRules.IsToiletBend("Sanitary Medium Turn", "3\""), "a Medium Turn is");
Check(PotFlangeRiserRules.IsToiletBend("sanitary", "MEDIUM TURN 3in"), "the type name counts, without case");
Check(!PotFlangeRiserRules.IsToiletBend("CLT Long Sweep 90", "3\""), "a long sweep is not");
Check(!PotFlangeRiserRules.IsToiletBend("Wye", "3\""), "a wye is not");
Check(!PotFlangeRiserRules.IsToiletBend(null, null), "no names is not");
Check(PotFlangeRiserRules.IsCoupling("Coupling NHxNH", "3\"") && PotFlangeRiserRules.IsCoupling("CLT Coupling NH x NH", null), "a no-hub coupling is looked through");
Check(!PotFlangeRiserRules.IsCoupling("Quarter Bend", "3\""), "a bend is not a coupling");

// ---------------------------------------------------------------------------------------------------------------
// What kind of pipe: 3" sanitary ABS / PVC / cast.
// ---------------------------------------------------------------------------------------------------------------
Check(PotFlangeRiserRules.IsSanitary("Sanitary") && PotFlangeRiserRules.IsSanitary("sanitary 4") && !PotFlangeRiserRules.IsSanitary("Vent") && !PotFlangeRiserRules.IsSanitary(null),
    "only a sanitary system");
Check(PotFlangeRiserRules.IsToiletSize(3.0) && PotFlangeRiserRules.IsToiletSize(2.9999999) && PotFlangeRiserRules.IsToiletSize(3.4), "3\" (and what rounds to it)");
Check(!PotFlangeRiserRules.IsToiletSize(4.0) && !PotFlangeRiserRules.IsToiletSize(2.0) && !PotFlangeRiserRules.IsToiletSize(double.NaN), "4\", 2\" and no size are not");
Check(PotFlangeRiserRules.IsToiletMaterial("PVC - DWV") && PotFlangeRiserRules.IsToiletMaterial("ABS") && PotFlangeRiserRules.IsToiletMaterial("Cast Iron - NH")
      && !PotFlangeRiserRules.IsToiletMaterial("Copper") && !PotFlangeRiserRules.IsToiletMaterial(null), "ABS, PVC or cast - not copper");

// ---------------------------------------------------------------------------------------------------------------
// The plan: every toilet pipe on a level goes to that level's longest.
// ---------------------------------------------------------------------------------------------------------------
List<ToiletLevelPlan> plans = PotFlangeRiserRules.Plan(new[]
{
    new ToiletPipe(1, "L1", 3.0), new ToiletPipe(2, "L1", 3.5), new ToiletPipe(3, "L1", 3.25),
    new ToiletPipe(4, "L2", 2.5), new ToiletPipe(5, "L2", 2.5),
    new ToiletPipe(6, "l1 ", 4.0),             // same level as L1, spelt differently
    new ToiletPipe(2, "L1", 9.0),              // pipe 2 again (two bends reached it): counted once, and its first length stands
    new ToiletPipe(7, "", 1.0),                // no level
    new ToiletPipe(8, "L3", double.NaN),       // nothing reads on L3 ...
    new ToiletPipe(9, "L3", 0.0),              // ... a zero is not a length
    new ToiletPipe(10, "L4", double.NaN), new ToiletPipe(11, "L4", 2.0),   // one read, one did not
});

ToiletLevelPlan Level(string name) => plans.Single(p => string.Equals(p.Level, name, StringComparison.OrdinalIgnoreCase));
Check(plans.Select(p => p.Level).SequenceEqual(new[] { "(no level)", "L1", "L2", "L3", "L4" }), "levels in name order, no-level first: " + string.Join(",", plans.Select(p => p.Level)));
Check(Level("L1").Pipes.Count == 4 && Level("L1").LongestFeet == 4.0 && Level("L1").LongestPipeId == 6, "L1: four pipes (2 once, 'l1 ' joined in), longest 4.0 ft from pipe 6");
Check(Level("L1").ShortestFeet == 3.0, "L1's shortest is 3.0 ft");
Check(Level("L2").Pipes.Count == 2 && Level("L2").LongestFeet == 2.5 && Level("L2").CanWrite, "L2: two pipes, tied at 2.5 ft (already the same)");
Check(Level("L2").LongestPipeId == 4, "a tie is the first pipe");
Check(Level("(no level)").Pipes.Count == 1 && Level("(no level)").CanWrite, "a pipe with no level is its own group, and writable");
Check(!Level("L3").CanWrite && Level("L3").LongestFeet == 0.0 && Level("L3").Pipes.Count == 2, "L3: no length read, so nothing to write");
Check(Level("L4").CanWrite && Level("L4").LongestFeet == 2.0 && Level("L4").Pipes.Count == 2, "L4: the pipe that would not read is still brought to 2.0 ft");
Check(PotFlangeRiserRules.Plan(Array.Empty<ToiletPipe>()).Count == 0, "no toilets, no plan");
Check(new ToiletPipe(1, null!, 1).Level == PotFlangeRiserRules.NoLevel, "a null level is no level");

// ---------------------------------------------------------------------------------------------------------------
// Feet and inches for the dialog.
// ---------------------------------------------------------------------------------------------------------------
void Fmt(double feet, string want) => Check(PotFlangeRiserRules.FormatFeetInches(feet) == want, $"{feet} ft reads {want} (got {PotFlangeRiserRules.FormatFeetInches(feet)})");
Fmt(3.375, "3'-4 1/2\"");
Fmt(4.0, "4'-0\"");
Fmt(0.25, "0'-3\"");
Fmt(2.0 + 5.0 / 12.0 + 1.0 / 96.0, "2'-5 1/8\"");
Fmt(1.0 + 7.75 / 12.0, "1'-7 3/4\"");
Fmt(11.0 / 12.0 + 0.0001, "0'-11\"");
Fmt(2.99999, "3'-0\"");
Check(PotFlangeRiserRules.FormatFeetInches(double.NaN) == "?", "NaN reads ?");

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) FAILED.");
return failures == 0 ? 0 : 1;

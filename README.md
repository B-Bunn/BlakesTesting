# BlakesTesting

Blake's scratch repo for Revit commands: tools being tried before they go into
[BIM Tools](https://github.com/2008NUB/BIM-Tools), new tools on their way there, and one-off jobs that will never be
anything more. Everything is a plain `IExternalCommand` in `BlakesTesting.dll`, so it runs three ways:

1. **Hot Load** (BIM Tools > Settings > Hot Load > *Run DLL...* or *Watch Window*) - point it at
   `src/BlakesTesting/bin/Debug/<year>/BlakesTesting.dll`. Rebuild and run again, no Revit restart.
2. **Add-Ins > External Tools** - copy `BlakesTesting.addin` and `BlakesTesting.dll` into
   `%AppData%\Autodesk\Revit\Addins\<year>\`.
3. **Promote it** - copy the command into `BIM-Tools/src/...` (and follow that repo's CLAUDE.md: ribbon line, Updates card).

## Just load it (no build)

Release builds of every year are committed in `dist\<year>\` (`BlakesTesting.dll`, `.pdb`, `.addin`). `git pull`, then load
`dist\2023\BlakesTesting.dll` in Hot Load or Add-in Manager. After changing code, refresh them with
`.\Build-All.ps1 -Configuration Release -Dist` and commit `dist\`.

## Build

    dotnet build src/BlakesTesting -c "Debug R23"      # R23 R24 R25 R26 R27, as in BIM Tools
    .\Build-All.ps1                                    # all five years into src\BlakesTesting\bin\Debug\<year>\
    .\Build-All.ps1 -Only 2023                         # just the Revit you run

In Visual Studio, `BlakesTesting.sln` has the configurations `Debug R23` ... `Debug R27` and `Release R23` ... `Release R27` in the
configuration dropdown (plain `Debug`/`Release` build 2025). Pick the one for the Revit you run, then **Build > Build Solution** (Ctrl+Shift+B) - the solution holds only
`BlakesTesting`, so the one DLL it makes is the one to load:
`src\BlakesTesting\bin\Debug\<year>\BlakesTesting.dll`. (The layout test is not in the solution, so it can never be built
or loaded by mistake; run it from a terminal, below.)

2023 and 2024 build for .NET Framework 4.8, 2025 and 2026 for .NET 8, 2027 for .NET 10 (needs the .NET 10 SDK).
The year table is `RevitYears.props`, copied from BIM Tools' Hot Load samples. Warnings are errors.

## Commands

| Command | What it does |
| --- | --- |
| `SpreadTags` | Un-piles overlapping tags. Only tags whose labels overlap another tag are moved - each to the free spot **nearest where it is now**, in a column to the left or right of the pile, level with its old height where it can be, leader on. Tags that are already readable stay put and moved tags keep clear of them. Uses the selected tags - **which are always re-laid out, even if they no longer overlap** (select tags you spread earlier to tidy them again) - or, with nothing selected, only the overlapping tags in the active view. One Undo. |
| `SpreadTagsLeft` / `SpreadTagsRight` | The same, but only to one side - for a view with no room on the other. |
| `CopyHostParametersToNested` | Copies every **instance parameter of a host family onto the families nested inside it** (and the ones nested in those), so a view filter, schedule or Isolate that reads a parameter sees the whole assembly, not just the hanger. Uses the selected families, or - with nothing selected - every family in the active view that has nested families. Shows how many values will change (and which host parameters a nested family does not have) and asks before writing; one Undo. Afterwards the hosts and their nested parts are left **selected**, so Temporary Isolate Element keeps all of it. |
| `RoundRiserPexLengths` | Writes **MFG.CutLength** on every pipe: the PEX serving a **water riser (tower)** - the vertical pipes **and the horizontal from each manifold back to them** - is rounded **up** by the Round Pex Lengths rules (3" steps to 9'-0", then whole feet); every other pipe - PEX distribution, mains, branches, other materials - gets its **real length**. The pipe's own length is never touched, so model groups stay whole. Finds the riser from the **4- and 6-port manifolds** (below), shows what it found, and asks before writing; one Undo. With nothing selected: the whole model. With **pipes** selected: only those are written. With **manifolds** selected: only those manifolds' risers. |
| `SelectRiserPex` | The preview: finds the riser pipes and **selects** them, writing nothing - Temporary Isolate the selection to check it is the tower. Also writes a per-manifold log to `%TEMP%\BlakesTesting-RiserPex.txt`. |
| `RoundSelectedPexAsRiser` | Manual override for a tower the trail missed: rounds the **selected** pipes up the pex ladder into MFG.CutLength, riser or not. |
| `SetSelectedExactCutLength` | The other override: puts the **selected** pipes' real length into MFG.CutLength. |
| `RiserPexColorSettings` | Opens the settings for the riser coloring (below): on/off, which pipes get painted, and the two colors. Saved per user in `%AppData%\BlakesTesting\RiserPexColors.ini`. |
| `ClearRiserPexColors` | Takes the riser colors back off the pipes in the active view (the selected pipes, or all of them). Only a pipe colored one of the two settings colors is touched. |
| `TrenchMap` | A **color-coded trench plan with a legend** in the active plan view: each trench is a filled rectangle along the pipe, as wide as BIM Tools' Calculate Spoils digs it at that depth, **colored by width and depth together**, with the legend off to the right of the trench (color, width, depth range, footage). Uses the selected pipes, or every pipe in the view. Details below. |
| `TrenchMapOptions` | The Trench Map settings: spoils depth or bottom of pipe, the floor, the width rule, the depths the colors change at, see-through. Saved per user in `%AppData%\BlakesTesting\TrenchMap.ini`. |
| `ClearTrenchMap` | Takes the trench map (colored regions and legend) out of the active view. |

`CopyHostParametersToNested` matches parameters the way Revit does: a shared parameter by GUID, a built-in by its id, a family parameter by name *and* kind of value (a text "Size" is never forced into a numeric one). Read-only targets (e.g. already driven by the host through an association) are skipped, and so are built-ins that place or host an element (level, offsets, host, phase, workset, design option, family/type) - copying those would move the nested part. Everything else, Mark and Comments included, is copied. Type parameters are not touched. **Only nested families set to *Shared* in the family editor exist as separate elements in a project**, so a nested part that still is not picked up is not shared - open the family, select the nested family, tick *Shared*, reload. A host parameter a nested family does not have at all cannot be copied; the dialog lists those (add the parameter to that family, or bind the project/shared parameter to its category). **Not verified in Revit.**

How it decides, in `Tags/TagLayout.cs`: tags are placed one at a time, highest first, at the nearest free spot, where a move up or
down costs `VerticalWeight` (1.0 = tightest blob; raise it to keep tags level with what they tag) times a move sideways, and no
tag goes further than `MaxDistance` (1 in of paper) from where it is - a tag with no room that close stays put and is counted in the report. Everything is in **paper inches**, so a
layout looks the same at any view scale: `LabelHeight` and `CharWidth` (label size, used when a tag's real size cannot be read -
a tag with a leader), `Padding`, `MinOffset` (how far off its old spot a moved tag sits), `ColumnStep`, `MaxColumns`, `MaxRows`, `MaxDistance`. The dialog after a run
says which label size it used and how many tags it measured rather than estimated - if that size looks wrong, tune `LabelHeight`/`CharWidth`.
Leaders are measured from the point on the tagged element they point at (the nearest point of a pipe's run, a fitting's own point), not from where the tag sits, so a tag moved earlier can come back. Two more things reduce overlap: **leaders are untangled** (pairs of tags are swapped wherever that removes a crossing - the dialog shows
`Leader crossings: before -> after`), and **labels keep off the drawing** - the pipes, fittings and other model elements in the view are read
(pipes as their run, fittings as their box) and a spot on top of one has to be `GeometryPenalty` (0.5 in of paper) nearer than a clear spot
to win; set it to 0 to ignore the drawing. Pinned tags are left alone. Known limits: pipes are straight runs between their ends (a bend in one run is not followed), walls and other large elements are
skipped, and a big label with a
leader is sized from its text, so set `LabelHeight` to match your tag text if big ones get overlapped.
The layout rule has no Revit types in it and is tested without Revit:

    dotnet run --project tests/TagLayoutTest

**Not verified in Revit** - the container compiles every year but cannot run Revit. Try it on a copy of a riser
view: run it with nothing selected, check the Undo, then with a few tags selected.

### How the riser PEX is found

Everything that gets rounded is connected to a 4- or 6-port manifold, so the trail starts there:

1. Leave the manifold by its **inlet** (its biggest port - the 3/4" on a 1/2" manifold; the outlets go to fixtures and are never followed).
2. Walk the **horizontal PEX** from the inlet until a vertical is reached. The pipes **on the way from the manifold to that vertical** are the manifold's own branch back to the riser, and they are **rounded too**. Horizontal pipe that is *not* on that way - a side branch off a tee, a run that leaves the riser for a main - is not.
3. The first **vertical PEX** pipe reached is the riser. Follow it through couplings, tees, valves and unions, marking every vertical PEX pipe - floor after floor, through the tees where each floor's branch leaves - until the trail turns horizontal (a tee's branch, the elbow into a main at the top or bottom). Those marked pipes are rounded, and the horizontal it ends on is not.

What stops or limits it, all in `Risers/RiserTrail.cs` and easy to change:

- A **manifold** is a pipe fitting with 5 or more pipe connectors (inlet + 4 outlets, or 6), or 4 named *Multiport*/*Manifold*. Another manifold is a wall to the walk.
- **PEX** means the pipe *type's name* contains "pex" (the same test Round Pex Lengths uses). Copper or any other pipe ends the trail.
- **Vertical** means steeper than 45 degrees, so an offset in the riser does not break it and a flat branch is never taken for one.
- A vertical trail must **rise at least 4 ft** (`MinRiseFeet`) to count; a shorter one is a stub into a manifold and is left at real length (reported, not rounded).
- A manifold whose **ports are all the same size** has no inlet to tell from an outlet, so **every port is tried** and the run says so ("inlet guessed", in the dialog and the log). Only a port whose walk reaches a real riser puts anything on a path, but an outlet that drops a long way to a fixture would pass for one - check those.
- The **confirm dialog** has a third choice when some manifolds found no riser: select those manifolds, to see which ones the trail could not follow. `%TEMP%\BlakesTesting-RiserPex.txt` says why for each (path, other horizontals touched, riser, stub).
- Flex pipe, fittings and valves are passed *through* by the trail and are never rounded; flex pipe gets its real length.

The rounding rules (`Risers/CutLengthRules.cs`) are Round Pex Lengths' ladder, working in inches and writing feet. One difference: a length within 0.00001" above a 3" or 1' mark is forgiven (a pipe drawn at exactly 4'-3" can read back as 51.00000000000001"), where the BIM Tools ladder would take it up one more step. MFG.CutLength is written as a Length (feet) or, on a job still bound as Text, as the same number as text; a value already there is left alone.

The overrides write only what you select, and **running the whole-model command afterwards works every pipe out again from the manifolds**, so it overwrites them. Nothing is stored on the pipe yet: if the overrides turn out to be needed on a job, the next step is an override flag on the pipe that the whole-model run honours.

### Coloring the result (testing aid)

So a wrong riser is obvious at a glance, each run paints the pipes it sorted **in the active view**: the pipes it **rounded** solid **green**, the ones it **did not** solid **red**. `SelectRiserPex` paints too (rounded vs not, before anything is written); the two override commands paint what they wrote. A pipe whose cut length could not be written is not painted.

It is a per-element graphic override in that one view (Override Graphics in View > By Element) - nothing in the model or the pipes' parameters changes, and one Undo takes it back. `ClearRiserPexColors` removes it later.

`RiserPexColorSettings` sets:

- **Color on/off** (master switch).
- **What to color:** PEX that was rounded (the riser); PEX that was not rounded (branches, mains); every other pipe too (copper etc., off by default - it is noise when the point is to check the riser PEX). Other pipes take the not-rounded color.
- **The two colors**, from a color picker (defaults green `#00C800` and red `#FF0000`).
- **Color the lines too** (on by default): a surface fill does not show in a wireframe view, and pipes at coarse detail are a single line, so the projection and cut lines take the color as well.

The fill is the project's solid fill pattern; if the project somehow has none, only the lines are colored. A pipe the view hides (a filter or a category switched off) cannot be overridden and is counted as "could not be colored here". A view that cannot take overrides (a sheet, schedule or legend) is not colored and the result says so. The settings logic is tested without Revit (`dotnet run --project tests/RiserTrailTest`); the painting is **not verified in Revit**.

The trail rule and the ladder have no Revit types in them and are tested without Revit:

    dotnet run --project tests/RiserTrailTest

**Not verified in Revit.** Try it on a copy of a job with two different riser setups: `SelectRiserPex` first and Temporary Isolate the selection; then `RoundRiserPexLengths`, and check MFG.CutLength in a schedule against the pipes' own length. Things to look for: a riser pipe left out, a horizontal or a fixture stub-out picked up, a manifold that found no riser or had its inlet guessed (the dialog and the log name them), and a tower through a non-PEX section.

### Trench Map

Built on BIM Tools' **Calculate Spoils** (`BIM-Tools/src/TPITools/TPITools/TopconSurface/SpoilsRules.cs`, `CalculateSpoils.cs`,
`SpoilsFloors.cs` and `TopconSurfaceRules.BottomOfPipe`), so the trench on the map is as deep and as wide as the Spoils tool digs
it. The rules are copied here, not referenced (this is a scratch DLL with no link to `BIMTools.dll`), in `Trench/` - minus Spoils'
volume count and its sluff (the map draws the bottom width):

- **Depth** is read at every pipe end and every foot along it: the top of the trench (the floor less the *floor offset*) down to the
  bottom of the pipe's outside (less any *dig below the pipe*). The floor is one **level**, or the **floors** a parameter picks, read at
  their real top by a vertical ray (holes bridged, slopes followed). Pipes steeper than 45 degrees are risers and have no trench.
- **Width** comes from that depth by the width rule: shallower than 4'-0" is 18", deeper is 24" (rows and the deeper width are settings).
- **Color** is one color per width x depth-band combination. The bands start at the *depth marks* (default 4'-0", 8'-0", 12'-0": under
  4', 4' to 8', 8' to 12', 12' and deeper). A combination is the same color from run to run (the table is fixed by the settings alone).
  A box splits where the depth crosses a band, so one drawn piece is one color.
- **Color the depth by** *Spoils depth* (top of trench to bottom of trench) or *Bottom of pipe* (the floor to the bottom of the pipe: the
  offset and the bedding left out - "how deep is the pipe"). The width always follows the spoils depth.
- The first run (or one whose level / floors the model does not have) opens the settings window to ask for the floor offset and the floor.
  Lengths are typed `4'-6"`, `54"` or feet (`4.5`).
- Drawn as **filled regions** (detail items: this view only) of filled region types named `Trench Map W18 D0`, one per combination, in the
  solid fill and the combination's color, **see-through** (30% by default, so the pipes and the plan show through) and with invisible
  outlines; the **legend** is the same regions as swatches beside text notes of the types `Trench Map Legend` / `Trench Map Legend Title`,
  to the right of the trench, sized from the view's scale. Everything is found again by those type names, so **Clear Trench Map** (and the
  next run) removes exactly the map and never a filled region you drew. One transaction: one Undo. The unused types stay in the project.

The rules have no Revit types in them and are tested without Revit:

    dotnet run --project tests/TrenchMapTest

**Not verified in Revit.** Try it on a copy of a plan with a trench run: `TrenchMapOptions` first (floor offset, level), then `TrenchMap`
with nothing selected, then with a few pipes selected; check the widths and bands against Calculate Spoils' review shapes, that the
legend sits clear of the trench at 1/8" and 1/4" scale, that the colors show through at 30% (and what a Revit that refuses the
transparency override looks like), a cropped view (the legend can fall outside the crop), a model with a sloped slab (Floors mode), and
Undo.

## Adding a scratch command

Add a class to `src/BlakesTesting/Commands/`, `[Transaction(TransactionMode.Manual)]`, `: IExternalCommand`; add an
`<AddIn>` to `BlakesTesting.addin` if you want it under External Tools. Keep logic that does not need Revit in its own
file so it can be tested like `TagLayout`.

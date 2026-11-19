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

## Adding a scratch command

Add a class to `src/BlakesTesting/Commands/`, `[Transaction(TransactionMode.Manual)]`, `: IExternalCommand`; add an
`<AddIn>` to `BlakesTesting.addin` if you want it under External Tools. Keep logic that does not need Revit in its own
file so it can be tested like `TagLayout`.

# BlakesTesting

Blake's scratch repo for Revit commands: tools being tried before they go into
[BIM Tools](https://github.com/2008NUB/BIM-Tools), new tools on their way there, and one-off jobs that will never be
anything more. Everything is a plain `IExternalCommand` in `BlakesTesting.dll`, so it runs three ways:

1. **Hot Load** (BIM Tools > Settings > Hot Load > *Run DLL...* or *Watch Window*) - point it at
   `src/BlakesTesting/bin/Debug/<year>/BlakesTesting.dll`. Rebuild and run again, no Revit restart.
2. **Add-Ins > External Tools** - copy `BlakesTesting.addin` and `BlakesTesting.dll` into
   `%AppData%\Autodesk\Revit\Addins\<year>\`.
3. **Promote it** - copy the command into `BIM-Tools/src/...` (and follow that repo's CLAUDE.md: ribbon line, Updates card).

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

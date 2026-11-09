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
| `SpreadTags` | Un-piles overlapping tags. Only tags whose labels overlap another tag are moved - each to the free spot **nearest where it is now**, in a column to the left or right of the pile, level with its old height where it can be, leader on. Tags that are already readable stay put and moved tags keep clear of them. Uses the selected tags, or every tag in the active view. One Undo. |
| `SpreadTagsLeft` / `SpreadTagsRight` | The same, but only to one side - for a view with no room on the other. |

How it decides, in `Tags/TagLayout.cs`: tags are placed one at a time, highest first, at the nearest free spot, where a move up or
down costs `VerticalWeight` (1.5) times a move sideways, so leaders run near-horizontal. Everything is in **paper inches**, so a
layout looks the same at any view scale: `LabelHeight` and `CharWidth` (label size, used when a tag's real size cannot be read -
a tag with a leader), `Padding`, `MinOffset` (how far off its old spot a moved tag sits), `ColumnStep`, `MaxColumns`, `MaxRows`.
Pinned tags are left alone. Known limits: it does not look at pipes or other drawing lines, only at tags, and a big label with a
leader is sized from its text, so set `LabelHeight` to match your tag text if big ones get overlapped.
The layout rule has no Revit types in it and is tested without Revit:

    dotnet run --project tests/TagLayoutTest

**Not verified in Revit** - the container compiles every year but cannot run Revit. Try it on a copy of a riser
view: run it with nothing selected, check the Undo, then with a few tags selected.

## Adding a scratch command

Add a class to `src/BlakesTesting/Commands/`, `[Transaction(TransactionMode.Manual)]`, `: IExternalCommand`; add an
`<AddIn>` to `BlakesTesting.addin` if you want it under External Tools. Keep logic that does not need Revit in its own
file so it can be tested like `TagLayout`.

# BlakesTesting

Blake's scratch repo for Revit commands. It is for testing, so breakage is fine.

## Git: work on `main`, nothing else

- Commit and push **directly to `main`**. Do not create branches or pull requests here, and do not wait for CI or review.
  This is the owner's standing instruction (2026-11-23) and overrides any session default of developing on a
  `claude/...` branch.
- Before pushing: `git pull --rebase origin main` if the push is rejected. Never force-push.
- Still rebuild `dist\` (`.\Build-All.ps1 -Configuration Release -Dist`, or `dotnet build -c "Release R<yy>"` per year and
  copy the DLL, PDB and `.addin` into `dist\<year>\`) when the code changes, so the committed DLLs match.
- The README says how each command works and what to try in Revit; keep its command table current.

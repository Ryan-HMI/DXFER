# DXFER Testing Handoff

Prepared: 2026-05-12

## Published Scope

The current `main` branch includes the latest origin work plus the FeatureScript standard-library reference tooling commit:

- `Add FeatureScript std reference tooling`
- Adds `tools/DXFER.FeatureScriptStd`
- Adds `scripts/Sync-FeatureScriptStd.ps1`
- Adds `src/DXFER.Core/References/FeatureScript`
- Adds `docs/dev/featurescript-std-reference.md`

## Codex Verification Completed

These checks were run on `main` before pushing:

```powershell
dotnet restore DXFER.slnx
dotnet build DXFER.slnx --no-restore
dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --no-build
node --test tests\DXFER.Blazor.Tests\*.test.mjs
dotnet run --no-build --project tools\DXFER.FeatureScriptStd\DXFER.FeatureScriptStd.csproj -- --help
.\scripts\Sync-FeatureScriptStd.ps1
git diff --check origin/main..HEAD
```

Observed results:

- Solution restore: up to date.
- Solution build: passed with 0 warnings and 0 errors after stopping a stale local `DXFER.Web.exe` process that had locked Web output DLLs.
- Core tests: 474 passed, 0 failed.
- Blazor/node tests: 229 passed, 0 failed.
- FeatureScript CLI help path: printed usage successfully.
- FeatureScript sync script: indexed 265 modules and wrote the ignored manifest at `artifacts/featurescript-std/manifest.json`.
- Diff check: clean.

## Manual Testing Needed

### FeatureScript Reference Tooling

- Run `scripts/Sync-FeatureScriptStd.ps1` on a fresh machine or clean checkout to confirm clone/pull/index behavior works outside this workspace.
- Inspect `artifacts/featurescript-std/manifest.json` for expected source root, module count, license path, imports, exports, built-in calls, hashes, and stable ordering.
- Confirm the generated manifest remains ignored and is not accidentally staged.

### User Retest Queue

Use `docs/dev/user-test-tracking.md` as the source of truth for user status. Rows still need user confirmation before moving to `USER-PASSED`, especially:

- Trim/Extend: `UT-TRIM-001`, `UT-TRIM-002`, `UT-TRIM-005`, `UT-TRIM-006`, and `UT-TRIM-007`.
- Spline editing: `UT-SPLINE-001` and `UT-SPLINE-002`.
- Ellipse keyed creation: `UT-ELLIPSE-001`.
- Solver and dimension drag: `UT-SOLVER-001`, `UT-SOLVER-002`, `UT-SOLVER-003`, `UT-SOLVER-004`, `UT-SOLVER-005`, `UT-SOLVER-006`, and `UT-SOLVER-007`.
- Constraints: `UT-CONSTRAINT-001` and `UT-CONSTRAINT-002`.
- Toolbar/dock/icon UI: `UT-UI-001`, `UT-ICON-001`, and `UT-ICON-002`.

### Roadmap Testing Still Open

- Continue the entity-agnostic drag matrix in `docs/dev/entity-drag-constraint-matrix.md`.
- Continue Stage 7G canvas module splits in `docs/superpowers/plans/2026-05-06-dxfer-stage7-architecture-gap-plan.md`.
- Record the Stage 7H desktop shell decision before creating any `DXFER.Desktop` project.

## Known Environment Caveat

If `dotnet build DXFER.slnx --no-restore` fails while copying `DXFER.Core.dll` or `DXFER.Blazor.dll` into `src/DXFER.Web/bin`, check for a running `DXFER.Web.exe` from this checkout. Stop that dev server and rerun the build before treating it as a repo failure.

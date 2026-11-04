# Cross-document solver replay

## Status

Reproduced on local application/mapper baseline `6d6e6e1` (public mapper
`46d24cb`) through real browser interactions. This commit adds evidence and
regressions, **not a production fix**. No deployment or existing drawing edits.

Two new native tests intentionally fail until the shared native solve path is
corrected. Recreating the UI or restarting the sidecar when opening a file would
mask the failure, not establish a root-cause fix.

## Reproduction

`PlaneGcsBrowserSessionFixturesTests` creates an unchanged native DXF baseline
with two six-sided polygons, one driving radius, and a whole-fixed vertical
mirror axis. It is synthetic, not Flux's unavailable exact overnight file.

1. Open `session-witness.dxf` in a fresh browser page.
2. Activate Symmetric; pick the two polygon edges and then the fixed axis.
3. The first solve passes, with three remaining DOF.
4. Open the exact original baseline again in the same page, without New or reload.
5. Repeat identical picks. Solve fails with status 2 / DOF 3 / redundant axis Vertical.
6. Reloading the browser and reopening the same baseline recovers the passing solve.

Alternate trigger: fresh page, open the synthetic rectangle, edit width 15 to 16,
open the polygon baseline for the first time, then add Symmetric. This also fails.
The rectangle operation itself succeeds, including its native rank check.

The browser verifies that each open restores the exact input entities,
constraints and dimensions. Successful and failed native requests are identical
after normalizing the new constraint ID and excluding document metadata.
Both operations run in the same native sidecar PID. The browser does not inject
solver requests or invoke internal workbench methods.

## Narrowing

- Ten ordinary browser replay controls pass on current and historical `6524dc5`
  mappers: inscribed/circumscribed pentagons, four same-page opens each, and an
  unrelated rectangle edit before first use. The previous ten native lifecycle
  tests also passed. Geometry-sensitive numerical behavior explains why those
  controls did not expose the failing case.
- A deterministic variation search found the six-sided witness. Both historical
  and current mappers reproduce it directly, without a browser or C# adapter.
- Native stage observation: hard constraints return Success on every call;
  adding temporary shape-preservation goals returns Success on the first call
  and Failed on subsequent calls. Applied hard-solution starting coordinates
  differ by no more than approximately `8.44e-15`.
- Retrying the failed stage with LM, DogLeg or BFGS does not recover it. The
  pinned native implementation routes combined hard/auxiliary systems through
  its constrained optimization path rather than the chosen single-system
  algorithm.
- A fresh WASM module consistently solves the identical request. Recreating
  `GcsSystem` inside a shared module, as current code already does, does not.
- This establishes numerical/runtime history sensitivity in the native
  preference solve. It does **not** establish that a particular C++ global,
  allocator ordering, or specific arithmetic expression is the cause.
- A diagnostic prototype that restores the already applied successful hard
  solution after preference failure, then performs a fresh hard-only native
  solve, passes five repeats without a module restart. All mapper result checks
  remain enabled. This is not implemented here. Any use of that recovery must
  distinguish shape-stability preferences from temporary pointer goals: silently
  dropping a drag target could report success without the requested movement.

`planeGcsSolver.js` is owned by the parent integration task and was not edited.
Any correction must preserve hard constraints, fixed geometry and normal drag
validation; do not simply accept a failed native result.

## Run

```sh
DOTNET_PROCESSOR_COUNT=2 DXFER_SESSION_FIXTURES=/tmp/dxfer-session-fixtures \
  dotnet test tests/DXFER.Core.Tests -m:1 -nodeReuse:false \
  --filter FullyQualifiedName~PlaneGcsBrowserSessionFixturesTests
DOTNET_PROCESSOR_COUNT=2 dotnet build src/DXFER.Web -m:1 -nodeReuse:false
node --experimental-default-type=module --test \
  tests/DXFER.Blazor.Tests/planeGcsSessionUiWitness.test.mjs
DXFER_SESSION_WITNESS=reopen DOTNET_PROCESSOR_COUNT=2 \
  PLAYWRIGHT_MODULE=/path/to/playwright/test.mjs \
  node --experimental-default-type=module tests/DXFER.Blazor.Tests/sessionReplay.browser.mjs
DXFER_SESSION_WITNESS=rectangle DOTNET_PROCESSOR_COUNT=2 \
  DXFER_SCREENSHOTS=/tmp/dxfer-session-ui-rectangle-witness \
  PLAYWRIGHT_MODULE=/path/to/playwright/test.mjs \
  node --experimental-default-type=module tests/DXFER.Blazor.Tests/sessionReplay.browser.mjs
```

Omit `DXFER_SESSION_WITNESS` to run the ten passing ordinary browser controls.
Set `DXFER_SESSION_MAPPER_REVISION=6524dc5` to run the historical mapper without
editing tracked runtime files. The runner copies historical JS assets into the
local evidence directory and uses the repository's pinned WASM library.

The runner starts/stops a localhost-only application with a random transient
key. `sessionTraceNode.cjs` forwards native frames unchanged and captures only
solver geometry/intent frames, not authentication headers, keys or cookies.

Evidence from the original investigation:

- `/tmp/dxfer-session-ui-witness-browser/`: first pass / repeated failure, DXFs,
  screenshots, native frames and browser result report.
- `/tmp/dxfer-session-ui-rectangle-witness/`: successful rectangle edit followed
  by first-use polygon failure.
- `/tmp/dxfer-session-native-stages.json`: hard and preference stage observations.
- `/tmp/dxfer-session-hard-recovery-probe.mjs` and
  `/tmp/dxfer-session-hard-recovery-results.json`: diagnostic recovery experiment.
- `/tmp/dxfer-session-vary-68.json`: exact reduced request under `.q`.
- `/tmp/dxfer-session-ui-browser/` and `/tmp/dxfer-session-ui-old-browser/`:
  ten passing ordinary controls under current/historical mappers.

These temporary evidence files are not a durable archive. The fixture generator,
native regression and browser replay preserve the reproducible case in source.

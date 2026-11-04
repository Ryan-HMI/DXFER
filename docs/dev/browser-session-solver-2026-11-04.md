# Cross-document solver replay

## Status

Reproduced on local application/mapper baseline `6d6e6e1` (public mapper
`46d24cb`) through real browser interactions, then corrected in the isolated
session worktree by integrating the complete-system native retry strategy from
`c179e05` and `84d25b2`. Both formerly failing native witnesses and both actual
browser triggers now pass. No deployment or existing drawing edits.

The initial evidence commit `97fc0b6` deliberately retained the two red native
regressions. They pass with the shared mapper using `solveNativeWithFallback`;
no browser/C# lifecycle reset, WASM module restart, or hard-only recovery is used.

## Original Reproduction

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

## Verified Correction

The parent authorized applying the proposed mapper integration only in this
isolated worktree. On primary failure, the shared strategy rebuilds a native
system from the original input primitives, solves the hard system with DogLeg,
then solves it with every original auxiliary preference still present. It never
publishes a failed partial solution. Normal residual, fixed-geometry and driving
rank checks still run afterward.

Observed actual native stages across four identical witness requests:

| Request | LM hard | LM with preferences | DogLeg hard | DogLeg with preferences |
| --- | --- | --- | --- | --- |
| First | 0 | 0 | Not needed | Not needed |
| Second through fourth | 0 | 2 | 0 | 0 |

All four requests share one WASM module. Unlike retrying only the failed
auxiliary stage, this complete retry re-solves hard intent from the original
seed before adding preferences. That recovers this numerical false refusal
without discarding goals. This is a shared solve-path correction, not an
affected-document repair. The exact native C++ numerical cause remains narrower
than a proven allocator/global-state defect; no such unsupported claim is made.

Verification on the integrated isolated worktree:

- Real browser reopen mode: all 14 symmetry cases pass, including the original
  baseline three times in one sidecar session and once after actual page reload.
- Real browser rectangle-first mode: all 11 symmetry cases pass. The unrelated
  width 15-to-16 edit and first witness share the same sidecar PID.
- Browser replay now asserts sidecar continuity until page reload, identical
  reopened documents and solve requests, exact fixed axis and driving radius,
  satisfied relations, and mirrored contour vertices rather than only centers.
- Four focused witness tests pass, including same-session constrained diagonal
  drag projection and contradictory-diameter refusal after the recovered solves.
  The failed request leaves geometry unchanged; another witness solve still works.
- Existing ten native session controls and five retry-strategy tests pass.
- Full JavaScript suite: 2,097 pass, no unexpected failures; the two known full-ellipse
  TODOs remain owned by the parent's separate integration.
- Full C# suite: 3,772 pass, one known full-ellipse skip, no failures. Focused
  native-runtime/large-step/session fixture checks pass 22/22.

The exact Flux native file remains unavailable; these are genuine UI replays of
the reduced synthetic witness with the same two failure triggers, not a claim
that Flux's original file has independently passed. Previously stored failed
intent is not automatically removed or repaired by these changes.

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

Passing integration evidence:

- `/tmp/dxfer-session-dogleg-reopen-final/`: repeated opens, actual reload,
  screenshots, exported native drawings and unchanged solver request frames.
- `/tmp/dxfer-session-dogleg-rectangle-final/`: unrelated rectangle edit followed
  by first-use polygon, with explicit sidecar continuity assertions.
- `/tmp/dxfer-session-dogleg-focused-final.log`: 19 witness/lifecycle/strategy tests.
- `/tmp/dxfer-session-dogleg-all-js.log` and
  `/tmp/dxfer-session-dogleg-all-csharp.log`: full suite output.

These temporary evidence files are not a durable archive. The fixture generator,
native regression and browser replay preserve the reproducible case in source.

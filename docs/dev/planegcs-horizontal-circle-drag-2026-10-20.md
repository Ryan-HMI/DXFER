# PlaneGCS Horizontal circle drag

## Report and reproduction

Ryan reported that Horizontal locked a circle's location. His Slack screenshot
attachment could not be downloaded with the available access; his original
native drawing has not been inspected. Do not equate synthetic coverage with
acceptance of that original drawing.

The basic origin and fixed-line-midpoint cases pass on deployed `65b56dd`,
including diameter-sized circles, horizontal/diagonal center drags, held
preview, release, Undo/Redo and native reopen.

A more constrained case reproduces on that same public build:

- Import circle center (1, 0.5), radius 2, and line (-2, -2) to (-2, 2).
- Add driving diameter 4 to the circle.
- Add Vertical and whole-entity Fix to the line.
- Add Horizontal from circle center to line midpoint; center moves to (1, 0).
- Drag center toward (2, 1).
- Preview and release refuse, falsely naming the compatible Vertical constraint
  as conflicting, with one mapped degree of freedom. Geometry remains unchanged.

The minimized native equivalent also reproduces with a horizontal fixed line,
with and without diameter, and with actual rectangle-factory relationships.

## Shared cause and correction

The native system was initialized with both hard intent and negative-tagged
pointer/stability preferences. PlaneGCS excludes negative-tagged constraints
from its rank Jacobian, but its redundancy-solving pass includes them in its
temporary solve. An infeasible pointer preference could therefore contaminate
the diagnosis of compatible redundant hard equations.

The adapter now initializes and diagnoses hard intent before adding preferences.
Adding negative-tagged constraints retains that native diagnosis. The final
native solve still has to converge, report no conflicts, and pass every mapped
hard-residual check before geometry is returned. Underdetermined hard-only
geometry may need the subsequent stability preferences to converge; failure
of that preliminary solve alone is not treated as mathematical inconsistency.

The same initialization is used for dimension-rank probes, with the same fixed
parameter flags. Curve drag seeds no longer overwrite whole-fixed radii or arc
angles before solving. No constraints are removed, no legacy solver fallback is
introduced, and the vendor solver algorithms/WASM are unchanged.

## Regression coverage

- `planeGcsDragDiagnosis.test.mjs`: 30 real-WASM cases covering fixed peers with
  redundant axes, selection order, center/perimeter drags, diameter and rank,
  rectangle peers, and genuinely contradictory hard intent.
- `PlaneGcsHorizontalCircleDragTests.cs`: 64 adapter cases using real native
  execution, origin/line/rectangle peers, diameter, preview/release requests,
  native reopen/edit-again, and whole-fixed refusal.
- `planeGcsIntegration.browser.mjs`: 12 real browser cases, with relations and
  dimensions created through the UI, checking held preview, release,
  Undo/Redo and native reopen.

Evidence before the fix:
`/tmp/dxfer-circle-horizontal-redundant-before`.
Local corrected browser evidence:
`/tmp/dxfer-circle-horizontal-redundant-fixed`.
These browser checks use the authorized API-key harness with LongPolling after
WebSocket upgrade rejection; they are not proof of an interactive Google login.

## Deployment and existing drawings

Deployed code `3d641db` to DXFer Dev on 2026-10-20 around 17:21 UTC.
The public tooltip independently reports `3d641db; sketch solver: PlaneGCS`.
All 32 installed browser workflows pass, including the 12 new circle cases,
actual-created rectangles, offsets, patterns, native curves, and drag snaps.
No page exceptions were recorded. The expected API-key WebSocket upgrade
rejection still falls back to LongPolling.

Final suites: 3,023 Core tests and 1,668 JavaScript tests pass.
Logs: `/tmp/dxfer-circle-hard-first-core-final.log`,
`/tmp/dxfer-circle-hard-first-all-js-final2.log`, and
`/tmp/dxfer-planegcs-installed-3d641db.log`.
Installed browser artifacts: `/tmp/dxfer-planegcs-installed-3d641db`.

Published assembly and solver-asset hashes match the built source. Dev process
2096323 retains PlaneGCS, full editing, disabled Sync callbacks and its isolated
Dev key directory. Public auth status returns 200; anonymous root redirects to
Google with the correct HTTPS Dev callback. Production/Test process IDs remain
3584417/3584475 respectively, unchanged. Sync application data is untouched.
Rollback publish is `/home/ryan/dxfer-runtime/dev-publish-before-3d641db`.

These reproduced failures refused atomically. No drawing/data repair is needed
for those cases, and no existing drawings have been modified. Ryan's original
fixture still needs replay or his confirmation.

## Follow-on coupled-freedom verification

The same installed `3d641db` also passes three additional canvas workflows:

- A fixed Point at (5,3), with a line Midpoint constrained to it: changing
  driving length 10 to 12 preserves that midpoint, Undo/Redo and native reopen;
  another edit to 14 after reopen also passes.
- A length-12 line with its start fixed, Parallel to a free peer which is
  Perpendicular to another free peer: dragging the endpoint rotates the chain.
  Both relation orders pass held preview, release, Undo/Redo, native reopen
  and another endpoint edit.

`planeGcsCoupledFreedom.test.mjs` adds 36 actual-WASM checks for those chains,
driving Angle/Parallel coupling, 90-degree rotations, off-length projection,
Fixed-midpoint growth, Fixed-peer refusal/projection and recovery after
removing Fix. Requests remain immutable. A solved projection that does not
rotate is explicitly not counted as successful rotation.

Sustained canvas dragging also passes again on `3d641db`: 43 distinct preview
frames during 1,850 ms of pointer movement, with latest-pointer release and
Escape cancellation covered. This is measured browser evidence, not inferred
from endpoint screenshots.

Evidence: `/tmp/dxfer-planegcs-fixed-midpoint-3d641db`,
`/tmp/dxfer-planegcs-coupled-3d641db`, and
`/tmp/dxfer-planegcs-sustained-3d641db`.
No further application change or drawing repair was needed for these cases.
The large radial-pattern timeout remains a separate open performance issue;
these passes do not close every legacy matrix row or all sketch combinations.

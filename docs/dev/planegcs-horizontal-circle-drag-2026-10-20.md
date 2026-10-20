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

Deployment verification is recorded below after rollout. Only DXFer Dev is in
scope. Production/Test and Sync application data must remain unchanged.

These reproduced failures refused atomically. No drawing/data repair is needed
for those cases, and no existing drawings have been modified. Ryan's original
fixture still needs replay or his confirmation.

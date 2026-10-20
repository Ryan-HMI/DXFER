# Perimeter Distance Dimensions

## Cause And Scope

Ryan reported that circle/arc perimeters could not be dimensioned. Both the
canvas dimension builder and server factory lacked a general perimeter-gap
dimension. Two circles were accepted only when they shared one editable
Offset relationship; point/line-to-curve bodies had no corresponding path.
This was a missing shared contract, not an affected drawing requiring repair.

`PerimeterDistance` now persists explicit curve references and uses PlaneGCS's
native `p2cdistance`, `c2ldistance` and `c2cdistance` equations. An arc maps to
a supporting native circle sharing its center and a native equal-radius
equation; validation additionally requires the witness to lie on the finite
arc sweep. Geometry is never corrected analytically after solving. Fixed
geometry and all other mapped constraints remain in the native system.

Supported: point-to-perimeter, straight-line-to-perimeter and independent
circle/arc perimeter pairs, including nested/concentric circles. Distances
are shortest radial clearances, not arbitrary far-side or horizontal/vertical
extents. Straight edges use the supporting line, consistent with the existing
point-to-line dimension. Intersecting perimeters and radial witnesses outside
an arc's sweep refuse; select a real arc endpoint for an endpoint distance.
Native dimension edits that leave the supported span refuse unchanged.

Linked circle offsets still edit their existing Offset distance. Ambiguous,
suppressed or inconsistent Offset relationships do not silently become a
second competing gap dimension. Explicit centers remain point references;
existing point/quadrant-to-line dimension behavior is retained. Single-curve
Radius/Diameter and Equal curve-picking behavior are unchanged.

No legacy solver implementation or fallback was added. Rollout is DXFer Dev
with PlaneGCS only; Production/Test and Sync are outside this change.

## Interaction Contract

Use the existing Dimension tool: pick two targets, place the annotation,
then key the distance. Existing input size, numeric editing, context menu,
dimension label/extension-line styling and panel composition remain intact.
No new panel, toolbar or help text is introduced. Explicit point picks must
not silently become centers. The new dimension uses existing anchor movement,
Undo/Redo and native persistence. Stable labels remain outside geometry and
are inspectable at the desktop QA viewport; no phone scope is claimed.

## Verification

- Six new point/line/circle and reverse-order preview tests failed before the
  change; ten native tests now cover those cases plus arcs and concentric
  circles. They check fixed-geometry refusal, immutable input and subsequent
  solve/edit using the returned geometry.
- 1,772 JavaScript tests and 3,033 Core tests passed. Existing refusal-only
  expectations for independent circle gaps were updated to assert the new
  perimeter kind, preserving Offset and center distinctions.
- Core fixtures verify native DXF dimension persistence, current geometric
  measurement, center references and exclusion of invisible arc spans.
- Isolated published-app Playwright QA passed circle-to-line and arc-to-point
  body selection, placement, distance 4 -> 5, Undo/Redo, native download/reopen
  and another edit to 4.5. Returned geometry was measured independently;
  no browser/circuit errors occurred. Both desktop screenshots were inspected.
- Evidence: `/tmp/dxfer-perimeter-browser`, `/tmp/dxfer-perimeter-browser.log`,
  `/tmp/dxfer-perimeter-core-final.log`, `/tmp/dxfer-perimeter-js-final.log`.

The missing dimension path is corrected. Existing documents are not rewritten;
there is no identified data repair. The separate large-pattern performance
limitation remains open and is not an acceptance claim of this change.

## Installed Verification

Installed and pushed `11634be6556d171c055cebadbb502730494fc453` to DXFer Dev.
The public browser tooltip independently reports `11634be; sketch solver:
PlaneGCS`. Both browser fixtures passed again against the installed endpoint,
including native reopen/edit. The installed solver and perimeter module
hashes match the worktree. Evidence: `/tmp/dxfer-perimeter-installed` and
`/tmp/dxfer-perimeter-installed.log`; the arc screenshot was inspected again.

Service PID 2186262 is active. Anonymous root access remains HTTP 302 and
auth status confirms normal Google authentication. Runtime configuration,
isolated keys and disabled Sync callbacks were retained. Rollback publication:
`/home/ryan/dxfer-runtime/dev-publish-before-11634be`. The isolated local QA
server was stopped. Flux received the bounded independent replay request.

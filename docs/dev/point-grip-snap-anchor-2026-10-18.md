# Point Grip Snap Anchoring

## Reproduction

Flux reported midpoint acquisition differing between creation and drag on Dev
92dfc24. Owner browser replay uses `implicit-snap-line.dxf`, free line start
(-2,1), source (2,3)-(6,3), a rounded 2px off-center grab and single-step drag
to the rounded screen location of (4,3). Held hover identifies the exact source
midpoint, but the released endpoint remains offset and no Midpoint is stored.
Two signed off-center pointer lifecycle regressions also fail before the fix.

## Interaction Contract

- A point-grip drag uses the exact picked geometric point as its world anchor,
  not a nearby mouse-down location within the existing hit tolerance.
- Body/edge drags retain their actual grabbed position. The existing screen-space
  click/drag threshold stays unchanged; clicks alone do not edit geometry.
- Snap radius, target priority, source geometry, constraint validation, and
  dimension/locked-peer protection are unchanged. No tolerance inflation.
- Preserve desktop palette/overlay alignment, wrapping, sizes, hit areas and
  overflow. No markup or styling changes. Existing hovered snap cue stays.
- Verify rounded/off-center pointer movement, held/released result, relation
  persistence, Undo/Redo, native reopen and subsequent coupled edits.

## Cause

The drag delta subtracted raw mouse-down coordinates from an exact snapped
destination, then added that delta to the original geometric grip. The initial
grab offset survived, so relation capture correctly found no exact contact.
Creation already uses exact target points and did not have this discrepancy.

## Verification

The shared pointer-down candidate now uses the selected point's exact position;
body picks still use raw coordinates. The versioned canvas module forces reload.

- 1,712 core and 872 JavaScript tests pass; Web build passes.
- Five pointer lifecycle regressions cover signed off-center and exact grabs,
  no-edit clicks, and retaining actual body-grab coordinates.
- Local real-browser exact and rounded/off-center single-move runs pass for
  line/polyline midpoints, ellipse center and spline endpoint. Held snap cue,
  exact committed endpoint (1e-9 tolerance), source unchanged, stored satisfied
  relation, Undo/Redo, native reopen, subsequent coupled edit and creation pass.
- Twelve rounded-run exports audit with zero errors/fixes; midpoint held
  screenshot inspected. The test now waits for Fit Extents to settle before
  mapping the next gesture; without that, a follow-up targeted the line body.
- Evidence: `/tmp/offset-snap-before.log`, `/tmp/point-grip-before.log`,
  `/tmp/offset-snap-local`, `/tmp/exact-snap-local` and matching logs.

No automatic repair of previously unsnapped drawings: endpoints and missing
relations must be deliberately resnapped after the code fix. This does not
complete unsupported virtual/quadrant/intersection snap persistence. Flux's
specific ellipse attempt still needs independent replay on the installed fix.

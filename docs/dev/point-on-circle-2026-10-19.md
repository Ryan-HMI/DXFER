# Coincident Point on Circle

## Cause and Correction

Coincident supported point/point, point/line and line/line, but not point/circle.
Tool-first picking also excluded circle bodies. The prior isolated browser build
reproduced the missing relation with a center/circumference selection.

Append `PointOnCircle` without changing existing enum values. Coincident maps a
point plus a whole circle to this relation; circle perimeter/quadrant picks use
the whole circumference rather than unresolved temporary canvas point keys.
Center/center still creates the existing Coincident relation. Self-relations,
invalid circles and unsupported arc/ellipse circumferences are refused.

Both core and canvas preview project a free point onto the circle. An explicitly
moved or fixed point can instead translate a free supporting circle. Neither
path resizes a circle. At coincident centers, retain the prior radial direction
when available, otherwise use positive X. Existing complete-system validation,
protected-layer acceptance and immutable failure rollback remain in force.

## Interaction Contract

Keep the current palette geometry, widths, wrapping and target sizes. Reuse the
Coincident command and glyph; expand its tooltip to include a circle target.
Both preselection and tool-first workflows support either pick order. Center
grips remain point selections; body picks remain circle references. No new modal,
panel, responsive layout, or automatic creation-snap behavior is introduced.
Version both the canvas entry point and nested target module to avoid stale picks.

## Verification

- 48 new core cases, including history snapshots, native reopen, edit again,
  radius changes, Fixed/locked protections and degenerate input.
- Full core: 2,189 passing. Full JavaScript: 1,046 passing.
- Two older assertions required updating: Coincident endpoint/quadrant now saves
  the supported PointOnCircle relation, not the previously unresolvable raw key.
  Equal itself remains unchanged and its geometry checks still pass.
- Actual browser regression: `tests/DXFER.Blazor.Tests/pointOnCircle.browser.mjs`.
  All 12 isolated cases pass: both orders/workflows, unchanged radii,
  history/reopen, repeat movement, fixed peers and center/center controls.
  Success/refusal screenshots inspected; no browser circuit errors.
  The first run exposed harness assumptions about hover reacquisition and raw
  versus canonical center references; corrected checks retain actual mouse
  events and strict new PointOnCircle references. Deployment results are recorded in the
  operational acceptance ledger after the installed build is verified.

## Boundaries and Existing Data

DXFer Dev only. No saved files or Sync records are rewritten. Reapply the intended
Coincident command where the old version created nothing; remove an old failed
relation first when present. General point-on-arc/ellipse support and new implicit
snap creation are not included. This adds a geometric incidence relationship,
not a fixed angular location on the target circle.

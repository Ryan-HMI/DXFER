# Manual Midpoint Axis Constraints

## Root Cause

The explicit constraint command had a separate path from hover-guide creation.
Preselecting a circle center and line midpoint saved a temporary canvas midpoint
key that the solver could not resolve. Starting Horizontal/Vertical first also
filtered the midpoint out of eligible picks, falling back to the entire edge.

## Correction

Only the Horizontal/Vertical command normalizes straight line/polyline midpoint
picks to live `:mid` references. Its other persistent point picks and active pick
use canonical references too. Current geometry, not coordinates embedded in a
canvas key, supplies the point. Midpoints are now eligible H/V point targets.
Coincident, Fix, dimensioning and direct Midpoint command semantics are unchanged.

The existing read-only midpoint model aligns the free counterpart without
reshaping the source. Both pick orders work. Already aligned midpoint pairs can
retain a relation, but incompatible pairs of derived midpoints refuse without
geometry changes; general reverse source reshaping is not added here.

## Interaction Contract

No layout, sizes, wrapping, palette placement, snap tolerances or icons change.
Both preselect-then-command and tool-first picking use the same command factory.
The canvas module and nested target module receive fresh cache versions so a
normal reload loads the correction. Fixed/locked acceptance guards remain.

## Verification

- Core baseline: 34 failures / 2 passing controls in 36 new manual-axis tests.
- Candidate: all 36 focused cases pass; full suite 2,141 core / 1,016 JavaScript.
- Tests cover H/V, line/polyline, both orders, stale canvas coordinates, free
  circle movement with unchanged radius/driving value/source, native reopen and
  source edits, Fixed conflict/aligned controls, and unchanged direct Midpoint.
- JavaScript eligibility regression fails before the fix and passes afterward.
- Browser replay: `tests/DXFER.Blazor.Tests/manualMidpointAxis.browser.mjs`.
- All 16 isolated browser cases pass: H/V, preselect/tool-first, both pick orders,
  and free/Fixed circle controls. Native references, unchanged radius/source,
  Undo/Redo, reopen and free-source following are checked from exports.
- Success and refusal screenshots inspected; no browser circuit errors.

## Existing Drawings

No saved drawings or business records are rewritten. Remove the old failed H/V
constraint before reapplying it through the corrected command. Do not infer
missing relationships from current positions. This is DXFer Dev only.

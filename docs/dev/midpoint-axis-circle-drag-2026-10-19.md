# Circle Center Axis Drag

## Cause

Ryan reported a circle with a Horizontal relationship to a rectangle midpoint
that could not slide horizontally. This is separate from creating the relation.
On baseline Dev `65938c3`, exact along-axis core drags succeed; diagonal pointer
movement fails because propagation cannot move the derived midpoint. The circle
drag fallback handled Tangent and point-to-line dimensions but not two-point H/V
relations. Even slight unwanted movement on the constrained axis could reject
an otherwise useful gesture.

## Fix

Extend the existing circle-translation fallback with H/V center candidates. Keep
the pointer's free coordinate and project the constrained coordinate onto the
live peer. Validate the complete candidate against all dimensions, constraints,
fixed geometry and the existing document/layer acceptance boundary. Both reference
orders are supported; no midpoint-source reshaping or dimension-value rewriting.
Radius-grip resizing remains distinct from center translation.

Apply the same candidate rules to the local JavaScript preview and bump the canvas
module URL. The first corrected-server browser run passed release but failed
held-pointer preview; this second path was corrected before deployment.

This does not implement general reverse-midpoint solving or free Point-entity
projection. It does not turn a failed prior constraint into a valid one or unlink
Offset/pattern geometry. Existing drawings are not rewritten; a valid saved
relation can be retried with the corrected drag path.

## Verification

Initial core baseline: 16 failed diagonal cases and 32 passing controls in the
first 48-case matrix. Expanded suite: 102 new core cases pass; all 2,513 core and
1,146 JavaScript tests pass, including 32 new preview cases. Coverage includes
line/polyline midpoint identity, raw/canonical picks, Radius/Diameter preservation,
Fixed source versus Fixed center, both-axis and driving-X conflicts, blocked-axis
no-op refusal, live source changes, missing references and the locked-layer
document acceptance boundary. Web build has zero warnings/errors.

Public baseline `65938c3`: pure X moves; pointer drift from (5,5) toward (8,5.6)
refuses both while held and after release. The corrected isolated browser passes
H/V in both reference orders, plus Fixed refusal and recovery. The held preview
and committed center project to (8,5) or (5,8), preserving radius and rectangle.
Undo/Redo, native download/reopen and another drag pass. No browser errors;
held-preview screenshot inspected. This verifies preview state before release,
not a general frame-rate guarantee. Local precommit tooltip still names the old
commit and is not presented as installed verification.

Evidence: `/tmp/dxfer-midpoint-axis-drag-baseline.log`,
`/tmp/dxfer-midpoint-circle-drag-baseline/`,
`/tmp/dxfer-midpoint-axis-drag-core.log`, `/tmp/dxfer-midpoint-axis-drag-js.log`,
`/tmp/dxfer-midpoint-axis-circle-browser.log`,
`/tmp/dxfer-midpoint-axis-circle-final/`.

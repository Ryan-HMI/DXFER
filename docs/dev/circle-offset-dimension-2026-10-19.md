# Circular Offset Gap Dimensioning

## Cause and Correction

The canvas did not recognize a linked circle pair as an Offset dimension. After
the first circle completed a Diameter draft, the second circle click placed that
diameter instead of extending the selection. The command factory also excluded
perimeter/quadrant picks because they carry a point as well as circle geometry.

Recognize the unique active directional Offset relationship before ordinary
point dimensions. Circle body and perimeter picks now select the linked gap in
either order. Placement creates or reuses one Offset dimension; editing it uses
the existing offset solver, preserving the source and concentric centers.
Preview and saved annotation witnesses use the same radial direction toward the
label anchor. Rectilinear offset witnesses remain unchanged.

Distinct circles without a unique editable Offset relationship explicitly
refuse, rather than placing the first circle's diameter or inventing a relation.
Center picks remain point references. Ordinary single-circle diameter placement
still works. No independent-circle radial-gap feature is introduced.
The Dimension footer now displays its operation status, like Offset and Trim,
instead of masking placement refusals with the static activation prompt.

Browser testing also exposed a shared precision bug: leaving an untouched,
auto-open dimension editor committed its rounded display text. Track actual
user input and preserve the stored value when the displayed value is unchanged.
Deliberately typing that rounded number still commits it. This is not a widened
numeric tolerance. Version the nested input parser as well as the canvas entry.

## Interaction Contract

Reuse Dimension, its existing key-in and annotation controls. No palette, panel,
width, wrapping, overflow or target-size changes. Pick both circumferences then
place the gap label. Existing gap dimensions retain their identity and mode.
Keep the source/dependent ordering regardless of pick order. Version the canvas
module so the installed selection behavior is not hidden by a stale import.

## Verification

- Baseline actual browser reproduced the premature Diameter commit; the linked
  pair could not retain two references in its dimension draft.
- 65 new core cases cover body/perimeter/quadrant picks, both orders, reuse,
  inward/outward gaps, native reopen, protected geometry and ambiguous links.
- Full core suite: 2,254 passed. Full JavaScript suite: 1,113 passed, including
  actual input focus/blur/Enter/change handlers and explicit rounded edits.
- Browser regression: `tests/DXFER.Blazor.Tests/circleOffsetDimension.browser.mjs`.
  Eight positive isolated cases passed: pointer/keyed offset creation, both pick
  orders, body/quadrant picks, unique editable gap, exact source preservation,
  Undo/Redo and native reopen, plus single-circle Diameter controls.
  The negative test exposed the hidden footer refusal. After correcting that
  shared display path, the independent-circle refusal and pointer/unedited-blur
  control both passed again. Geometry, dimensions and Undo history stayed exact.
  Success and refusal screenshots inspected; no browser circuit errors.
  Public installed-build results are recorded in the operational acceptance
  ledger after deployment verification.

## Boundaries and Existing Data

DXFer Dev only. No saved drawing or Sync record is rewritten. Delete an unwanted
diameter or competing dimension created by the old interaction before placing
the intended gap. Existing valid Offset relationships need no migration. A
missing or ambiguous relationship is not inferred or silently repaired.
Previously rounded values are not reconstructed automatically; use Undo or a
known intended value if an earlier edit already changed geometry.

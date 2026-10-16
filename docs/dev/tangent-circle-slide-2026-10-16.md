# Tangent Circle Translation

## Cause and Contract

A sized circle may slide along its tangent line without changing radius. The existing propagation tried moving the peer edge or changing radius when the mouse had any normal displacement; a fixed edge or dimensioned rectangle made that candidate fail. Only point-to-line dimensions supplied projected movement candidates, so the remaining tangent freedom was lost. Separately, propagation could restore a perimeter-resized radius before the driving check, disguising the user's drag as an unchanged edit. Sized-circle raw intent is now resolved before propagation can erase it.

On a rejected full drag, consider translation along the original active tangent edge, then validate the entire drawing: every driving dimension, constraint and fixed reference. Preserve valid existing full movements first. Pure normal motion against an immovable edge has no legal displacement and remains a no-op. This is a bounded circle/linear-edge correction, not a claim of arbitrary constraint convergence.

Preview and committed geometry must agree; diameter label follows actual center movement. No new controls or visual-layout changes. Failed moves preserve geometry and Undo history. Native save/reopen must retain intent and allow another legal edit. Existing files need no numeric repair.

## Fixtures

- Fixed line (0,0)-(20,0), circle (10,7.5), diameter15. Drag center toward (14,9): expected center (14,7.5), diameter15 unchanged.
- Aligned rectangle20x10 with welded corners, opposite Parallel relations, Perpendicular, driving width/height and top-edge Tangent circle at (10,17.5), diameter15. Drag center or dimensioned perimeter with delta(4,1.5): expected center (14,17.5), rectangle unchanged.
- Fixed-center variant refuses and creates no Undo entry.

Baseline regression evidence: 42 expected failures/12 passes in the added 54-case core matrix on `bdd0ebc`; the new actual-browser test also fails on the old held preview.

- 1,474 .NET tests and 700 JS tests pass, including 57 new core and 70 new JS cases. Core coverage adds center-position dimension blocking/recovery and negligible-displacement refusal. Existing free-radius Equal/Tangent/Fixed refusal remains intact.
- Actual browser: fixed-line center and rectangle center/perimeter drags pass during held preview and release, retain all diameter/width/height intent, preserve peer geometry, support Undo/Redo, native reopen and another edit. Fixed-center refuses without history changes.
- Ten saved browser DXFs audit with zero errors/fixes. Screenshot `/tmp/dxfer-tangent-slide-evidence/rectangle-perimeter-held.png` visually inspected.
- Updated two stale dock-width source assertions to include the independently browser-verified scrollbar gutter from the preceding layout correction; no new layout changes in this fix.
- Dev rollout pending. No existing drawing data repair required; no generic circle-circle orbit or whole-assembly rigid-motion behavior is claimed.

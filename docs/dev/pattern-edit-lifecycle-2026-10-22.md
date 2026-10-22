# Pattern edit lifecycle

## Contract

- Show pattern controls opens an editable draft immediately; no read-only parameter card or extra Edit action.
- Accept and Cancel hide the controls. Temporarily hiding/showing an active draft preserves its values.
- Existing radial-center picking remains available independently of the parameter card.
- Retain the floating card's drag position, compact width, wrapping, canvas clamping and desktop layout. Linear widgets keep their geometry anchors.
- Count changes stage removal of dimensions and constraints referencing generated members of that pattern. Source-only, center-only and unrelated intent remains.
- Cancel leaves the original document intact; Accept applies geometry and intent changes in one Undo step. Returning to the original count before Accept restores its original intent.
- Hidden/locked geometry and reference-only documents remain protected. No historical document rewrite.

## Verification

Root causes: Show only changed visibility and rendered a read-only branch; parameter updates retained intent attached to generated geometry even when the instance layout changed. Show now enters the existing staged editor. Explicit count edits detach member intent in an immutable candidate before regeneration, retaining the original document for Cancel and Undo. Generic reconciliation and non-count edits retain their existing guards.

Center selection from an unchanged existing draft transitions to selection without a parameter card. A changed/invalid/new draft cannot be discarded this way. The canvas transition is flushed before its selection guard; ordinary constraint/dimension picks do not reset their tool.

Candidate evidence:

- Core: 3,472 passing; JavaScript: 1,962 passing.
- 24 browser lifecycle/count cases passed: radial/linear entry and exit, invalid counts, U/V picker cancellation, radial/rectangular/staggered shrink/grow with member intent, one-step Undo/Redo, native reopen/edit again.
- Floating card checks at 1440x1000 and 1100x705 passed: drag, Escape rollback, pan/zoom anchoring, hide/show draft retention and viewport clamping. Screenshots reviewed.
- Core regressions cover source/center/unrelated intent, suppressed/reference member intent, reference-only and hidden/locked refusal, returning to the original count, and atomic commit/cancel.
- Candidate files: `/tmp/dxfer-pattern-edit-candidate-20261022-b`, `/tmp/dxfer-pattern-show-edit-layout-20261022-b`.
- Additional browser passes: origin snap ON, midpoint snap OFF, cancellation, and explicit center selection/Fix refusal. The last case verified the canvas edit-state transition correction in `/tmp/dxfer-pattern-show-edit-fixed-20261022-b`.

Existing documents are not rewritten. Generated-member intent is removed only when an explicit count change is accepted; no data repair is needed for this fix.

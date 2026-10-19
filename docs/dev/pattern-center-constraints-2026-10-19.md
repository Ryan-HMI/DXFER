# Pattern Instance Center References

## Cause and Correction

Generated circle centers were already present and pickable in both preselection
and tool-first workflows. The ordinary solver could move a generated member
instead of its free peer; directional pattern reconciliation then rejected that
independent edit. The opposite pick order could succeed. Source regeneration
also left attached external points behind and therefore refused source edits.

Build solve-time read-only references from pattern membership. They are not
saved Fix constraints and do not spread fixedness through free attached peers.
The ordinary constraint and dimension solvers now choose the free counterpart
without independently modifying generated geometry. Following source edits,
regenerate members and propagate Coincident/Horizontal/Vertical point
attachments before validating all constraints, driving dimensions and pattern
geometry. Other coupled feature solves remain guarded rather than guessed.

## Interaction Contract

No new UI or layout. Existing circle center grips, selection behavior and
constraint commands remain. Both pick orders must preserve the seed, generated
members, settings and IDs when attaching a free external point or circle center.
Native save/reopen retains references to the actual generated center. Fixed
incompatible peers and independent generated-member drags remain protected.

## Verification

- Baseline core: six pick-order failures among 22 cases; three additional source
  following failures reproduced independently.
- Baseline browser on installed17eb154: all four circle workflow/order cases
  failed either applying Coincident or moving the seed afterward. The center
  picks themselves succeeded.
- All 28 new core cases pass; full core2,282 and JavaScript1,113 pass. The first
  broad run caught an unintended expansion into Offset propagation and a changed
  refusal diagnostic. Point-attachment propagation was narrowed and the useful
  independent-member explanation retained; existing tests were not weakened.
- All11 isolated actual-browser cases pass: circle/Point peers, preselection and
  tool-first selection in both orders, original family/IDs/radii preservation,
  native reopen, source following, and generated-center selection. One case
  additionally verifies regenerated Undo/Redo, reopen and a second seed edit.
  Both Fixed-peer orders refuse unchanged; direct generated-center dragging
  remains refused without adding history. Success/refusal screenshots inspected;
  no browser circuit errors. Web build has zero warnings/errors.
- Browser: `tests/DXFER.Blazor.Tests/patternCenterConstraints.browser.mjs`.
  Local evidence: `/tmp/pattern-center-browser-final.log` and
  `/tmp/dxfer-pattern-center-final`. Installed-build verification is recorded in
  the operational acceptance ledger after deployment.

## Boundaries and Existing Data

DXFer Dev only. No saved files or Sync records are rewritten. Reapply a relation
that the old version rejected; no missing intent is guessed automatically.

This makes instances usable as directional references. It does not implement
inverse pattern solving: moving a constrained instance onto fixed geometry could
mean moving the whole pattern or changing the seed relative to its center.
That distinction has been raised with Ryan, not silently selected. Pattern
settings and explicit source edits remain the controls for changing the family.

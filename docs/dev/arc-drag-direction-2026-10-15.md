# Arc Drag Direction

Scope: DXFer Dev only, DRAG-ARC-001. No engine replacement or visual overhaul.

## Reproduction And Shared Fix

Shift-dragging an arc endpoint to its center retained the original radius but
computed an angle from a zero direction vector. Exact zero invented angle0;
near zero(1e-9,1e-9) invented45. Both core and JavaScript preview regressions
failed before correction. Check the target direction against the existing
geometry tolerance before deriving any angle, independently of Shift. Refuse
unchanged with an explicit explanation; a valid1e-4 target still succeeds.
The canvas module version changes so preview and commit use the same guard.

No data repair. Previously saved unintended geometry requires Undo or verified
originals; it cannot be reconstructed automatically from an arc's current shape.
This is undefined geometry, not a claim that a constraint system is inconsistent.

## Bounded Coverage

- Driving radius3 and sweep90, arc end welded to a line: supported endpoint
  movement translates the arc and moves its welded peer while preserving intent.
- Fix the welded line start: refuse unchanged. Normal/reversed constraints and
  dimension order agree in core and preview.
- Fix arc center and drive radius3: perimeter radius change refuses; Shift sweep
  edit succeeds. Exact/near-center Shift direction refuses, valid retry succeeds.
- Core1133 and JavaScript414 tests pass; Web build has no warnings/errors.
- Actual canvas replay uses the three disposable arc fixtures described in the
  drag-matrix README, including held-pointer pixel checks, release, no-Undo
  refusal, recovery, Undo/Redo, native download/reopen and edit again.
  This replay passed locally; the refusal screenshot was inspected for visible
  unchanged geometry and readable feedback. Translated dimension anchors move
  with the arc; their values and references remain unchanged.

No markup/CSS/layout changes. Reuse the existing wrapping status area for the
new refusal. Public deployment and independent acceptance are recorded separately.

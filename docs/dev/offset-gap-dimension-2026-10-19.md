# Post-creation Offset dimensions

## Cause and Fix

Dimensioning source and dependent edges created an ordinary driving
PointToLineDistance alongside the Offset relationship (and any keyed gap).
Changing that competing distance could not change the offset parameter.

The dimension factory now recognizes a matching normal gap on a direct,
active Offset relationship. It uses the complete ordered source/dependent
references and reuses an existing Offset dimension ID. Pointer-created offsets
gain a driving gap when first dimensioned. Existing driven mode and requested
values are preserved. Repositioning an existing gap label does not rerun the
solver or retry unrelated failed intent.

Single-edge length, opposite edges, diagonals, unrelated lines and indirect
offset-chain pairs keep their ordinary dimension meaning. No relationships are
inferred for historical geometry-only copies. Ambiguous relationships are not
converted to a guessed offset parameter. Fixed/locked geometry guards remain.

## Interaction Contract

Existing Dimension picks, placement, inline editing, context menu, history and
native persistence are reused. No panel, target, width, wrapping, overflow or
responsive layout changes. Repeated placement moves the existing parameter
label instead of creating another driving value.

## Verification

- Baseline regression: seven new factory tests failed, unrelated-line control
  passed, before the fix.
- Core tests cover line/contour gaps, inset/outset, both pick orders, full source
  references, chains, repeated placement, driven mode, failed intent and Fixed
  refusal. Existing Offset solver/persistence/locked-layer coverage retained.
- Browser replay: `tests/DXFER.Blazor.Tests/offsetGapDimension.browser.mjs`.
  Actual four-line rectangle picks, mouse/keyed offsets, gap editing, no duplicate
  dimensions, unchanged source, Undo/Redo and native reopen/edit. Screenshots
  and exported fixtures under `/tmp/offset-gap-dimension`.
- Core suite: 2042 passed. JavaScript suite: 1008 passed.

## Scope and Existing Drawings

DXFer Dev only. Production/Test and Sync callbacks are unchanged.
This fixes creation of the competing dimension; it does not silently remove
dimensions already saved by older builds. Delete the extra conflicting generic
gap dimension, then dimension the source/offset gap again. No existing files or
business records were rewritten. Derived-circle Tangent behavior is separate.

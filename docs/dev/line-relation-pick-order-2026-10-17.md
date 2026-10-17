# Symmetric line relation pick order

Flux Slack1792279898.664099: Vertical A(0,0)-(0,5), free B(4,5)-(7,0).
Parallel fails for one selection order while the other order rotates B correctly.
This is separate from curve point-pick normalization and Tangent coupling.

## Cause and correction

The sequential solver tried one driven peer, then rejected the transaction when
that choice broke an existing orientation. For a single new Parallel or
Perpendicular relation, it now tries the opposite driven peer from the original
snapshot when the first attempt fails. Both attempts run the full constraint and
driving-dimension validation; only a fully satisfied result commits. Stored
reference order/ID is retained. If both fail, original geometry and failed intent
remain unchanged. No retry loop, directed-angle reversal or replacement engine.

Scale regressions also exposed false success after iterative collapse: whole-line
Horizontal/Vertical validation compared endpoint coordinate differences using a
length tolerance. A near-collapsed tilted line could therefore appear vertical.
Whole-line axis validation now checks normalized direction, as Parallel and
Perpendicular already do, in core and preview. Two-point axis relations retain
their positional tolerance. Degenerate lines remain invalid.

## Coverage

Four of the initial ten core cases failed before editing. Coverage now includes
Horizontal/Vertical peers, Parallel/Perpendicular, both selection orders, existing
driving length, incompatible axes with unchanged geometry, removal/recovery,
scale0.00001 and10000, and small slanted-line rejection. JS covers coupled preview
validation at three scales. Full suites:1669 .NET and829 JS pass.

Browser fixture `line-relation-order-free.dxf` matches Flux's undimensioned case.
The `conflict` fixture adds a Horizontal B. Browser tests actual body picks, both
orders, Parallel/Perpendicular, preserved Vertical A and B length, Undo/Redo,
native reopen, and conflicting intent with visible unchanged feedback.

No data repair or silent re-solve of saved drawings. Reapply existing failed
relations deliberately. This closes the minimized single-relation selection-order
case only; general coupled rotation and angle editing remain separate open work.

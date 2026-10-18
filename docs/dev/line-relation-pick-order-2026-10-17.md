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

## Installed verification

Installed `2ac6db41520eeb0bd58bed99ed31415323dd4a73` on DXFer Dev at
2026-10-17T23:59Z. Local and public browser suites pass all six cases; public
browser verified the rendered2ac6db4 build. Six public exports audit0/0 and
screenshots were inspected.

Published/live assembly hashes match; ten tracked runtime settings retained,
health200 and anonymous root302. Normal Google auth, independent Dev state and
disabled Sync callbacks retained. Production/Test PIDs unchanged. Rollback
publish retained. Existing drawings were not rewritten.

## Independent acceptance

Flux delivery8fb7230b51a7a324225bfb84 confirms2ac6db4 passes the exact
Parallel fixture and Perpendicular in both pick orders. Vertical A and the
free peer's length remain unchanged. Conflicting Vertical/Horizontal axes
refuse Parallel in both orders without changing geometry or existing axes.
Undo/Redo and native reopen preserve geometry and constraint states;15 exports
audit clean. This closes the tested normal-scale pick-order cases only.
Nearly-collapsed small-scale coverage remains owner-tested, not independently
accepted. Broader coupled rotation and angle issues remain open.

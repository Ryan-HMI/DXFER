# Created Rectangle Drag Regression

## Reproduction

Flux independently failed build `25fb640`: blank drawing, Two-point rectangle,
viewport corners (480,450) and (650,560), Escape, Show constraints, then drag
bottom-right to (690,610) without Shift or snap setting changes.

Owner replay reproduces the same Parallel residual, 0.2821663239915504.
The exact rectangle has four coincident corners, two Parallel relations,
one Perpendicular relation, and Horizontal/Vertical on the first two edges.
All four entities remain unchanged after refusal. Initial degrees of freedom
is four; the failing seeded drag reports three.

Earlier browser acceptance used imported geometry plus four explicit H/V
relations. That was a real passing fixture, but insufficient coverage of the
actual drawing tool's constraint combination. This regression now creates the
rectangle through the actual UI instead of substituting that fixture.

## Shared Correction

- Keep direct point/body drag guesses only if mapped hard residuals remain
  satisfied. Otherwise retain the valid snapshot as the native starting guess;
  the pointer remains a soft native goal. Do not move welded endpoints apart
  before native redundancy analysis.
- Do not add old-axis-length preferences during dragging. They were weak goals,
  but still interfered with valid native dragging in this redundant system.
  Actual driving length dimensions remain hard equations.
- Preserve all user constraints, including the redundant Perpendicular relation.
  No rectangle-specific geometry repair, constraint removal, Legacy fallback,
  or post-solve coordinate correction.
- Curve-specific multi-parameter seed assembly remains unchanged. Applying the
  pointwise guard indiscriminately to intermediate ellipse/arc seeds caused
  regressions in isolated tests; the complete native suite caught that before
  deployment.

This corrects the shared point/body drag initialization policy. It does not
claim the exact upstream C++ rank-analysis implementation is defective.

## Verification

- Actual factory Core cases: before correction 40 failed / 45 passed; afterward
  all 85 pass. Includes two-point/aligned rectangles, all corners and eight
  directions, relation/entity/reference ordering, fixed/locked/driving controls,
  repeated solves and native reopening.
- Actual-WASM cases cover the emitted constraint combination across scales,
  translations, corner/edge gestures, ordering and repeated requests.
- Full suites: 2,959 Core and 1,638 JavaScript tests pass.
- Actual local browser: all four newly drawn corners pass held preview, release,
  Undo/Redo and native reopen, with Show constraints on and normal snap defaults.
- Evidence: `/tmp/dxfer-planegcs-created-rectangle-before`,
  `/tmp/dxfer-planegcs-created-rectangle-fixed`,
  `/tmp/dxfer-created-rectangle-core-red.log`,
  `/tmp/dxfer-created-rectangle-core-fix.log`,
  `/tmp/dxfer-created-rectangle-all-core.log`,
  `/tmp/dxfer-created-rectangle-all-js.log`.

No saved drawing or business record repair is required for this reproduced
failure: rejected drags left geometry and intent unchanged. Existing unrelated
saved-data issues are not repaired by this code change.

Dev deployment and independent replay are pending at this source commit.

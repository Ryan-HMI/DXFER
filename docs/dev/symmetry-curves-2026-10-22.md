# Exact Whole-Curve Symmetry

Ryan requested Symmetric across all drawing entity types, including splines.

## Shared Implementation

- Canvas eligibility and the ordered command factory now accept whole splines,
  ellipses and regular polygons in addition to existing point/line/circle/arc/
  straight-polyline support. The third pick remains the independent axis.
- PlaneGCS mirrors the full spline control or fit representation, including
  tangent handles. Degree, knots and rational weights are not rewritten or sampled.
- Full ellipses mirror both axes and center; partial ellipses mirror the native
  focal parameters and reverse eccentric endpoints. Equivalent angle turns are
  chosen near the current representation, including after DXF normalization.
- Each connected full-ellipse symmetry family keeps one independent orthogonal
  frame equation. Reflected peers are independently residual-checked, avoiding
  redundant equations without dropping all frame equations in a relation cycle.
- Unchanged focal ellipse parameters retain the original serialized axes exactly;
  fixed-geometry validation remains strict. No tolerance-based repair is applied.
- Regular polygons mirror center and guide point, retaining side count and mode.
- The C# result validator checks exact geometric relationships before accepting
  results. Native save/reopen uses the existing Symmetric constraint persistence.

## Explicit Limits

This covers every current native entity *type*, not every possible imported
representation or arbitrary topology conversion:

- Spline correspondence follows stored control/fit-point order. Native splines
  require matching degree/count, normalized knots and proportional weights.
  Fit-point splines require matching point and tangent-handle topology.
- Periodic/unclamped native splines remain outside the existing PlaneGCS mapper.
- Partial equal-axis ellipses and partial ellipses forced to the axis-parameter
  editing model explicitly refuse; ordinary noncircular partial ellipses work.
- Polygons require matching side count and circumscribed mode. No entities are
  silently exploded, resampled, converted or replaced to make a pair eligible.
- Explicit endpoint/center point symmetry is separate from whole-curve symmetry.

## Verification

- Targeted real Node/WASM adapter tests: both selection orders, exact native
  preservation, fixed conflicts, DXF reopen and subsequent edits for seven curve
  representations. Includes existing Normal and Symmetric regressions.
- JavaScript tests cover rational/scaled knot bases, incompatible-weight refusal,
  ellipse cycles, focal references and unsupported partial equal-axis refusal.
- Browser test `symmetryCurves.browser.mjs`: tool-first and preselection for
  control/fit/rational splines, full/equal-axis/partial ellipses and polygons;
  Undo/Redo, DXF reopen and edit again. Disposable fixtures only.
- Initial browser evidence: `/tmp/dxfer-symmetry-curves-browser-candidate-b`.
  Release candidate: `/tmp/dxfer-symmetry-21d4f5f-local` (14/14).
- Installed Dev `69cf11a` at `2026-10-22T14:49:26Z`, with all 14 public browser
  cases passing and the rendered build tooltip verified. Evidence:
  `/tmp/dxfer-symmetry-69cf11a-public` (112 native exports and 14 screenshots).
- Final core suite: 3,496 passed, zero failures. Final JS suite: 1,988 passed,
  zero failures. Targeted adapter coverage includes fractional fixed ellipses.
- Normal Google sign-in remains configured; Sync launch callbacks remain disabled.
  Production/Test PIDs stayed 3584417/3584475. Isolated local QA was stopped.
- Flux received the installed revision and a reconstructable spline fixture in
  `#all-agents`; independent acceptance is pending, not claimed by these checks.

## Data And Isolation

No historical drawings or Sync artifacts are rewritten. Existing failed
constraints are not silently removed or reinterpreted. This is DXFer Dev only;
Production and Test are not part of the rollout. The separate Parallel report
remains unresolved pending Ryan's original native drawing.

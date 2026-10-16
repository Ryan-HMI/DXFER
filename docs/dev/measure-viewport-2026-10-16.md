# Measure viewport contract

Measure shows a compact read-only three-row viewport annotation: signed Delta X, signed Delta Y, and shortest distance with document units. Colored orthogonal legs and a direct segment join the actual closest points. Position the annotation beside the measured endpoint, clamped into the existing unobscured viewport region; it cannot capture clicks or change geometry. Exit/clear removes feedback. One selected reference plus hovered second reference previews; two selected references retain their measurement. One selected line shows its endpoint span. More than two references do not invent a bounding-box gap.

Reuse the Core measurement engine and expose its closest-point witnesses. Analytic pairs retain exact distances; sampled arc/ellipse/spline paths must explicitly say approximate. Circle-line containment and intersection need actual boundary witnesses rather than the old blanket zero for inside segments. No dimensions or constraints are created by measuring. Production/Test unchanged; Dev only.

## Verification

- 1,542 .NET tests and 751 JavaScript tests pass; build and diff checks pass.
- Actual isolated browser verifies point hover preview and locked 3/4/5 deltas, parallel-line and line/circle boundary shortest distances, clear/exit, viewport placement and unchanged native entities/dimensions/constraints with no Undo entry.
- Desktop screenshots inspected at 1440 x 1000; readout and guides remain readable beside geometry. Evidence: `/tmp/dxfer-measure-evidence`.
- Core regression cases verify actual boundary witnesses, reversed selection, intersections, wholly contained and degenerate line segments, and explicit sampled-curve approximation.
- Shared calculation fixed; no persisted geometry or records changed and no existing-record repair required. This does not claim exact distances for sampled curve pairs.

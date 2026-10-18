# Persistent implicit snaps

Ryan delivery c1b217dea45721e412ddf440 requires every accepted implicit snap to
create its permanent geometric relation. This is broader than the origin fix.
This report distinguishes the implemented patch from remaining architectural work.

## Root cause

The canvas offers more snap features than the stored constraint/reference model
can represent. Most creation paths infer relations from final coordinates instead
of carrying the selected source feature through the commit. Origin and circle
tangency have explicit intent fields. Two independent point-enumeration lists
omitted spline endpoints and ellipse centers despite core point support. Drag
capture added only Coincident, not Midpoint, and excluded standalone Point entities.

## Implemented

- Shared supported point enumeration for creation and drag: line/polyline endpoints,
  circle/arc/ellipse/polygon centers, arc/spline endpoints and Point entities.
- Existing insertion inference now captures ellipse-center and spline-endpoint
  Coincident/Midpoint/origin relationships instead of dropping them.
- Dragging a supported point onto a stationary line/polyline midpoint saves Midpoint.
  Point entities also participate in automatic Coincident/Midpoint capture.
- Snap capture validates and appends the satisfied relation without re-solving or
  moving accepted geometry. A source moved by the same drag is not a newly acquired
  snap target: this avoids duplicate relations on welded closed-polyline aliases.

Eight initial regressions failed before editing. 1710 .NET and863 JavaScript tests
pass afterward, including new origin ellipse/spline and Point-drag coverage.
Native round trips and subsequent source edits preserve the relationships.
Existing closed-alias regressions remain passing. No UI/layout or solver-engine swap.

Actual browser harness: implicitSnapPersistence.browser.mjs, four synthetic native
fixtures named implicit-snap-{ellipse,spline,line,polyline}.dxf. It tests creation,
held drag/release, source stability, Undo/Redo, native reopen and coupled edit again.

## Remaining, Not Complete

| Snap family | Current state |
| --- | --- |
| Supported endpoints/centers/origin | Persistent Coincident; new omissions fixed here |
| Line/polyline midpoint | Creation and point drag now persist Midpoint |
| Line H/V, rectangle orientation | Existing creation constraints; not new acceptance of every draw tool |
| Analytic tangent/perpendicular | Existing bounded creation support; not universal snap provenance |
| Circle/arc quadrants and arc midpoint | No stable stored point target for these features |
| Curve/line intersections, body-point coincidence | Need point-on-curve/intersection relationships, not fixed coordinates |
| Acquired projection/virtual midpoint | Source identities/relationship provenance currently lost at commit |
| Virtual centers (e.g. center rectangle) | Not necessarily a persisted point entity/reference |
| Polar/grid-style positioning | No blanket persistent inference implemented |

Do not claim this matrix is fully implemented. Completing the rule needs typed
snap intent through creation/drag and stable geometric references for virtual
features, with serialization, propagation, preview and refusal tests. Do not
substitute Fix or delete useful snaps silently. Ryan has been asked whether a
quadrant snap should stay at that specific quadrant or merely stay on the circle;
these impose different freedoms. Intersections/projections remain open regardless.

Existing coordinate-only drawings cannot safely recover intent from geometry
alone. No historical files or manufacturing records are rewritten. Deliberate
resnapping/reapplication is needed for previously missing relations. Dev only;
Production/Test and Sync callbacks remain outside this rollout.

# Corner edits preserve driving intent

## Reproduction and policy

Base: `f38c50f`. Horizontal line `h=(0,0)->(10,0)`, vertical line
`v=(0,0)->(0,10)`, dimension `length=10` on `h:start/h:end`.
Before this change, fillet radius 2 and chamfer distance 2 both shortened `h`
to `(2,0)->(10,0)` and silently rewrote a driving dimension to 8. The same
operation on a polyline already rejected invalidated driving intent.

Current and legacy separate-line corner entry points now use a corner-only
finalizer. Affected driving dimensions retain their requested values, IDs,
kinds, anchors, and driving flags. Canvas-point coordinates are refreshed by
their existing logical labels before validation. Unsatisfied or unresolvable
affected dimensions reject the whole edit with the original document object.
Reference dimensions alone are remeasured, including valid zero axis distances;
an unresolvable reference dimension rejects rather than disappearing.

Fix uses before/after geometry comparison, including encoded canvas handles,
because ordinary Fix validation does not detect movement. Unmoved fixed points
and suppressed Fix constraints do not prevent otherwise valid operations.
Legacy line APIs also reject breaking previously satisfied constraints. They
retain their existing constraint collection rather than acquiring the current
API's generated corner constraints; a preserved coincident constraint that
would be broken therefore causes a conservative refusal.

No virtual original corner, automatic remote-endpoint movement, dimension
conversion, solver change, or UI change was introduced.

## Applicability

- Line: current and legacy fillet/chamfer covered.
- Polyline: existing same-path topology/policy unchanged and regression suite
  rerun; mixed line/polyline segments use the new corner finalizer.
- Polygon: native `PolygonEntity` edges are not supported by these corner APIs.
  Regression verifies unchanged refusal. This patch does not explode or convert
  a regular polygon. Polygon outlines represented as polylines use that policy.
- Circle, arc, ellipse, spline: not straight-edge corner targets; no new support
  or approximation. Arc remains the fillet output.
- Point: no point-entity corner editing added. Existing supported endpoint/vertex
  selection resolves to the underlying straight edges.
- Dimensions and constraint references: protected intent, not corner targets.

## Evidence

- New core regressions before implementation: 20 failed, 10 passed.
- Focused modify/polyline/corner suite after implementation: 245 passed.
- Full `DXFER.Core.Tests`: 951 passed, including 40 new cases (30 core and 10
  workbench). No JS files changed.
- Tests cover current/legacy fillet/chamfer, driving/reference dimensions,
  entity/endpoint/canvas references, unresolved references, valid zero axis
  measurements, Fix (including suppressed/unmoved references), Equal constraints,
  mixed line/polyline edges, and unsupported native polygon refusal.
- Headless Blazor tests invoke the actual current command handler: rejection
  preserves document identity, native export and empty Undo/Redo stacks.
  Successful reference edits through current/legacy APIs survive workbench
  Undo/Redo and native DXF reopen with dimensions/constraints intact.
- Parent independently reproduced both corrupting operations in installed Dev
  before the fix. Corrected installed-browser verification is parent-owned and
  pending at handoff; headless tests are not claimed as browser verification.

Run: `dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --nologo`.
Focused filter: `--filter 'FullyQualifiedName~CornerIntent|FullyQualifiedName~PolylineCornerModifyTests|FullyQualifiedName~DrawingModifyServiceTests'`.

## Explicit remaining issue and existing drawings

Parent local browser verification after integration: both Fillet2 and Chamfer2
reject the driving10-inch fixture with explicit unchanged-geometry feedback,
original native entities/dimensions/constraints and disabled Undo. Both reference
variants succeed with value8 and retain working Undo/Redo. Screenshots inspected;
installed Dev verification remains pending the combined rollout.

**OPEN: PowerTrim driving-dimension rewriting.** The non-corner
`RefreshDimensionsForReplacedEntities` helper and all its remaining callers are
unchanged. Existing PowerTrim tests explicitly expect driving values to be
rewritten. Trim/extend/split policy requires a separate bounded decision and
regression migration, not a blanket helper change in this corner patch.

This code prevents future corner corruption; it does not repair previously
saved drawings. A stored driving value of 8 cannot reveal whether 10 was the
original request. Any restoration requires a verified original, undo history,
or backup and a deliberate compatible geometry/intent decision. No user drawings
were inventoried or modified, and nothing was deployed.

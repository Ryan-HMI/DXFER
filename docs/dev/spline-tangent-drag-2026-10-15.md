# Spline Tangent Drag

UT-SPLINE-002; isolated base 20c5aeb. Parent reproduced the installed no-op with
/tmp/dxfer-drag-live-qa.cjs --dev --tangent --before and
/tmp/dxfer-spline-tangent.dxf. No services or application data changed here.

## Root Cause And Fix

The canvas advertised square endpoint tangent grips for fit splines, control
splines and even sample-only spline DTOs. Core and preview dispatch accepted
tangent labels only for fit splines. Control previews also substituted the
control polygon for the curve because the DTO omitted degree/knots/weights.

- Fit splines continue to edit the chosen explicit tangent handle, preserving
  all fit points and the other explicit handle.
- Supported control-spline grips now edit the adjacent native control point.
  The displayed handle is one quarter of the endpoint-to-control vector, so
  control = endpoint + 4 * (handle - endpoint). No conversion to fit points,
  sampled degree-1 geometry, or different knots/weights occurs.
- Core supplies per-endpoint editable-control indices and native spline
  parameters in the canvas DTO. Control-point preview samples the actual
  rational spline with the existing core de Boor recurrence, ported to the
  small splineGeometry.js module. It follows core's 24-256 sampling intervals
  and duplicate-point tolerance. These points are display samples only, never
  the persisted/exported spline representation. No new solver or dependency.
- Control-spline handles derive from controls before any explicit-handle
  override. Stale StartTangentHandle/EndTangentHandle metadata is retained but
  ignored for non-fit geometry and grip placement; it cannot mask the edit.
- Selected square-grip coordinates refresh from preview controls. Neighboring
  round control grips still move the selected control by the unscaled delta.

For the browser fixture, start grip (0.25,0) dragged to (0.25,0.4) changes only
control 1 from (1,0) to (1,1.6). Controls (0,0), (3,1), (4,1), degree 3 and
clamped knots remain unchanged. The curve endpoints stay fixed. A quadratic
with three controls has one shared middle control: either tangent edit can
change both endpoint tangent directions, not two independent tangent handles.

## Supported And Refused Cases

Control endpoint tangent editing requires degree >=2, a clamped endpoint with
an adjacent nonzero knot span, finite controls/nondecreasing knots, positive
finite weights, and a distinct adjacent control. Rational and multi-span
curves are supported. Each endpoint is checked separately.

Non-clamped/periodic endpoint representations, degree-1 curves, degenerate
adjacent controls and invalid/nonpositive-weight data do not advertise an
editable square handle. Direct unsupported requests return false with explicit
unchanged-geometry status. This limits this endpoint-edit mapping; it does not
claim those representations are mathematically invalid in general. Collapsing
a supported control tangent onto its endpoint is also refused. Round controls
remain available according to the existing DTO rules, allowing a degenerate
adjacent control to be repaired. Missing/invalid native preview parameters
decline optimistic preview instead of drawing a misleading control polygon.

Whole-spline Fix rejects both fit and control tangent changes. Existing shared
constraint validation and authoritative release rejection remain in force;
unsupported active constraint combinations still decline optimistic preview.
Rejected changes do not alter the original document or record a failed drag.
Line/polyline/polygon/circle/arc/ellipse/point, dimension and constraint-glyph
drag paths are unchanged. No ellipse dimension solver changes are included.

## Interaction Contract

Preserve the existing drawing/palette layout and all grip sizes, colors,
selection priorities and pointer thresholds. Square tangent and round control
grips remain distinct. No CSS, palette, tool inventory or command changes.
The only removed affordances are unsupported/misleading tangent handles.

## Verification And Integration

Two core and two JS start/end regressions failed against the original dispatch.
Final results on this branch: 23 focused .NET tangent/history cases, all 868
.NET tests, and all 352 JS tests pass (19 new JS cases). Coverage includes
start/end control and fit splines, exact browser coordinates, round-versus-square
selection, source/other-point preservation, rational non-unit knot domains,
repeated knots, fixed rejection, stale explicit metadata, quadratic shared
controls, unsupported cases, native degree/knots, and workbench Undo/Redo/DXF
reopen. JS preview is checked against rational Bernstein evaluation and a
native-verified multi-span sample; no control-polygon approximation is accepted.

```text
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj
node --test tests/DXFER.Blazor.Tests/*.test.mjs
```

Parent owns installed-browser retest, screenshots, integration and deployment.
Deploy the DTO/core changes and static module together through the normal
application publish; no runtime/configuration changes or package installation.
The shared dispatch/preview defect is fixed in code. The reported no-op saved
no bad geometry, so no existing-record repair is required or performed.

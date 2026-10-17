# Spline endpoint welds

Scope: DXFer Dev solver hardening, DRAG-COINCIDENT-001 and DRAG-SPLINE-001.
No Sync, Production/Test, machine operations, or automatic source-file repair.

## Reproduction and cause

The core point editor could not read/write spline endpoints and the propagation
queue omitted them. Preview had the same omission. Six initial core cases and
four preview cases failed before the fix: a cubic endpoint welded to a line and
point could not move in either direction, even without conflicting constraints.
An added driving-span case exposed a second preview-only omission: canonical
`spline:start/end` references were not measurable in dimension validation.

Shared point resolution, endpoint mutation, propagation and preview dimension
resolution now agree. Control splines retain degree/knots/weights and unrelated
controls. Fit-spline endpoint edits carry the endpoint tangent handle. Unclamped
endpoints refuse movement rather than pretending the endpoint is its control.
Dragging slightly off a grip retains the requested delta, not the pick offset.

## Verification

- Core suite: 1,597 passing cases; preview/JS suite: 785 passing cases.
- New focused coverage: both directions and constraint orders, start/end, fit,
  weighted and degree-one curves, small movements, Fixed-peer refusal/recovery,
  driving-span translation, origin coincidence, unsupported unclamped refusal.
- Eight real desktop-browser fixtures: fit/control x free/Fixed x driving/free
  endpoint span. Held preview peer pixels, release, exact controls/knots, driving
  intent, Undo/Redo, native download/reopen and repeat edit pass. Fixed cases
  preserve geometry and leave Undo disabled. Screenshots inspected.
- Fixtures: `tests/DXFER.Blazor.Tests/fixtures/drag-matrix/spline-weld-*.dxf`.
- Replay: `tests/DXFER.Blazor.Tests/splineEndpointWeld.browser.mjs`, using the
  existing QA URL/API-key environment contract. Local evidence is under
  `/tmp/dxfer-spline-weld-local`.

Exact fixture: cubic controls (0,0),(1,0),(3,1),(4,1), knots
[0,0,0,0,1,1,1,1]; line (4,1)-(8,1); point (4,1). Coincident
spline:end/line:start and line:start/point. Drag shared endpoint (4,1) to (5,2).
Free: only endpoint changes. Driving span sqrt(17): spline translates (1,1).
Fixed point: refuse unchanged. Fit fixture uses the same four fit points.

These are bounded passes, not exhaustive solver acceptance. Arbitrary coupled
spline systems, indexed-control constraints, and closed-polyline aliases remain
open. Independent Flux QA is separate. Existing files are untouched; previously
failed user intent is not automatically repaired or declared satisfied.

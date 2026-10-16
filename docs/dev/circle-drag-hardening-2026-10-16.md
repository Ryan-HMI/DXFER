# Dimensioned Circle Drag Hardening

Bounded DRAG-CIRCLE-001 on base `85d99b9`, existing feature branch. Parent owns
native fixtures, browser verification, matrix/ledger and deployment. No Slack,
deployment, phone QA or changes to parent polygon/ellipse files.

## Reproduction And Shared Fix

Before production edits, the three existing core circle drag tests passed.
Driving center/circumference translation, welded-center movement and Fixed-peer
refusal also passed JS probes. New core matrix: 50/50 passed without a core fix.

The reference-radius control reproduced a separate preview defect: a circle
resized from radius3 to4 while the held-pointer reference label still displayed3.
The registered-canvas test failed on `radius label 4 painted`. Both constraint
orders also failed a reference-length test on the welded peer: geometry changed,
but the preview retained length2 instead of sqrt(10). Core already refreshes
reference dimensions correctly after propagation; JS omitted that step.

`applyGeometryDragPreview` now refreshes measurable, non-driving dimension values
after propagation and successful constraint/driving-dimension validation. It uses
the existing measurement helper and tolerance, preserves anchors and intent,
supports both DTO casings, and leaves suppressed/unmeasurable references alone.
It does not rewrite driving values, repair failed candidates, or add solver
degrees of freedom. Rejected/cancelled previews restore original measurements.
The canvas module cache key is `20261016-reference-drag-measurements`.

## Exact Fixtures

Parent-owned files in `tests/DXFER.Blazor.Tests/fixtures/drag-matrix/`:

- `circle-free.dxf`: circle ID `circle`, center `(0,0)`, radius3; driving Radius3
  anchored `(4,-1)`.
- `circle-fixed.dxf`: same, Fix whole `circle`.
- `circle-weld-free.dxf`: same, line `peer` `(0,0)` to `(-2,0)`, Coincident
  `circle:center` and `peer:start`.
- `circle-weld-fixed.dxf`: same weld, Fix `peer:start`.
- `circle-radius-free.dxf`: same circle/anchor, reference Radius3, not driving.
- `circle-radius-center-fixed.dxf`: reference variant, Fix `circle:center`.

Driving fixtures: center key `circle|point|center|0|0`, `(0,0)->(1,1)`;
circumference key `circle|point|quadrant-0|3|0`, `(3,0)->(4,1)`. Both translate
center to `(1,1)`, retain radius/value exactly3, move anchor to `(5,0)` and move
the welded peer start to `(1,1)` without moving its end. Fixed variants refuse
unchanged; removing Fix permits the same gesture.

Reference controls: quadrant0 `(3,0)->(4,0)` yields center `(0,0)`, radius and
reference value4, unchanged anchor `(4,-1)`, even with Fix center. A second
outward drag reaches5. Center movement remains possible only without Fix center.

Additional automated checks: repeated live-coordinate drags; reversed constraint
order and weld reference direction; circumference targets `(0,0)` / `(1e-9,0)`
refuse; retry at `(-4,0)` succeeds after removing Fix where needed. Zero/epsilon
center movement refuses in core; `(1e-4,1e-4)` recovers. Registered pointer release
near its starting point sends no commit and restores the original drawing.

## Verification And Boundaries

- 50 new core tests; 71 new JS tests, including 19 registered-canvas tests using
  actual pointer handlers, hit testing, drawing calls and preview/release code.
  These cover held geometry/labels/anchors, welded peer rendering, exact release
  payloads, refusal, Fix removal, collapse/recovery and server-false rollback.
- Full retained suites: 1283 core and606 JS passed, zero failures/skips. Web build
  passed with zero warnings/errors. These are regression runs, not reopened
  independent acceptance of Equal-circle/Fixed, polygon or ellipse fixtures.
- Parent reported ten baseline Dev browser cases passing geometry/history/native
  reopen checks. That is not acceptance of this source change. The additional
  browser check is visible reference label4 BEFORE mouseup in both reference
  fixtures, then4 on release and3 after Undo/refusal. Parent owns that replay.
- Recorded canvas calls use a test-double context/interop; no real browser pixels,
  installed-build checks or native history verification are claimed here.

Layout contract: existing alignment, wrapping, sizing, overflow, targets, labels'
anchor positions, responsive behavior and viewport transforms remain unchanged.
No CSS/markup edits. The refresh is shared for existing measurable references;
the bounded new evidence covers circle radius/diameter and welded line length.
Other entity families and arbitrary coupled systems gain no new support claims.
Equal-circle/Fixed acceptance is retained, not reopened. Non-quadrant entity-edge
picks and other quadrant directions were not added to browser acceptance here.

## Commands

```bash
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --no-restore --filter FullyQualifiedName~CircleDragMatrixTests --verbosity minimal
node --test --test-reporter=spec tests/DXFER.Blazor.Tests/circleDragMatrix.test.mjs
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --no-restore --verbosity minimal
node --test --test-reporter=dot tests/DXFER.Blazor.Tests/*.test.mjs
dotnet build src/DXFER.Web/DXFER.Web.csproj --no-restore --verbosity minimal
git diff --check
```

Underlying cause fixed: missing shared preview reference-measurement refresh.
No saved documents or business records changed; no existing-record repair is
needed for this transient-label defect. Previously damaged documents and unrelated
solver failures still require separate diagnosis. No general engine replacement.

## Parent Browser Evidence

The actual Dev baseline reproduced the stale held label: radius geometry4 while
`input[data-dimension-id="radius"]` still displayed `R3` and value3. Corrected
local app passes all ten native-fixture cases, including visible held labelR4
and value4 (mouse-coordinate tolerance0.000001 inch), release, Undo/Redo and native
reopen/reedit. Driving values stay exact. Held canvas pixels, dimension anchors,
welded peer endpoint, Fixed refusal/no Undo and untouched peer end are checked.
Sixteen exports audit with zero errors/fixes. Parent reran1283core/606JS tests.
The committed `circleDrag.browser.mjs` is replayable with the six native fixtures.
No synthetic canvas hooks replace the actual DOM measurement/geometry checks.
Installed Dev verification follows deployment; local passes alone do not claim it.

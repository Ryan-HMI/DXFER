# Dimensioned Polygon Drag Coverage

Bounded DRAG-POLYGON-001 on base `91c881f`, branch
`feature/full-editing-workspace-20261012`. No deployment or Slack activity.

## Reproduction And Outcome

Before editing, the three existing core polygon translation tests and twelve
canvas-helper probes (three grips, free/fixed center weld, normal/reversed
constraint order) passed. The old core tests did not set `IsDriving: true`.
The new matrix uses driving radius/count dimensions and actual polygon vertices.

No shared solver or preview defect was reproduced. No production code or engine
was changed. This closes a bounded regression-coverage gap, not the overall
solver assignment or running-app acceptance. Intermediate test-authoring failures
were empty-collection assertion semantics, DTO alias comparisons, and harness
cleanup ordering; no remaining failures are attributed to product behavior.

## Exact Fixtures And Grips

Parent owns the four native files under
`tests/DXFER.Blazor.Tests/fixtures/drag-matrix/` and `polygonDrag.browser.mjs`.
Core/JS tests construct equivalent geometry independently:

- `polygon-free.dxf`: polygon ID `polygon`, center `(0,0)`, stored radius `5`,
  rotation `30` degrees, side count `6`, circumscribed false. Driving Radius `5`
  anchored `(6,-2)` and driving Count `6` anchored `(0,-6)`.
- `polygon-fixed.dxf`: same, Fix whole `polygon`.
- `polygon-weld-free.dxf`: same, line `peer` from `(0,0)` to `(-2,0)`,
  Coincident `polygon:center` and `peer:start`.
- `polygon-weld-fixed.dxf`: same weld with Fix `peer:start`.

Fresh-fixture gestures, each translated by `(1,1)`:

| Grip | Key | Start | End |
|---|---|---|---|
| Center | `polygon\|point\|center\|0\|0` | `(0,0)` | `(1,1)` |
| Side 0 midpoint | `polygon\|point\|mid-0\|<x>\|<y>` | `(2.165063509461097,3.75)` | `(3.165063509461097,4.75)` |
| Whole, side 0 at 30% | `polygon` | `(3.031088913245535,3.25)` | `(4.031088913245535,4.25)` |

Free results: center `(1,1)`, anchors `(7,-1)` / `(1,-5)`, radius/count/rotation
unchanged. Welded start follows to `(1,1)`; peer end stays `(-2,0)`. Fixed variants
refuse unchanged. Remove only Fix, repeat the same gesture, and it succeeds.

## Automated Scope

- 90 new core cases: all three grips across five configurations (the four above
  plus Fix whole peer), both polygon modes, forward/reversed constraint and
  dimension order with reversed weld references. Every case checks refusal or
  translation, retry after Fix removal, then another delta `(-1,2)` to cumulative
  center `(0,3)`, anchors `(6,1)` / `(0,-3)`, unchanged driving values/references,
  satisfied constraints, welded start, unchanged peer end and original document.
  Six cases additionally refuse pre-existing wrong driving radius/count values.
  Twenty-four cases refuse zero/`1e-9` deltas with free/fixed peers and both
  orders, then recover at delta `(1e-4,1e-4)` after removing Fix if present.
- 111 new JS cases: matching 90 helper cases plus 21 registered-canvas pointer
  tests. The latter use `createDrawingCanvas` and its real pointer handlers,
  hit testing, preview/release code, and recorded drawing calls at pixel ratio 2.
  They verify painted vertices, labels and welded line; selected grip; no release
  request while held; exact release coordinates; return-to-start/recovery;
  Fix-removal retry; server-false rollback; retry and pointer cancellation.
  Zero/`1e-9` release after a valid held preview restores the original drawing
  and sends no commit; a subsequent ordinary drag works. Helper previews below
  geometry tolerance may be identity-equivalent candidates; the pointer release
  threshold prevents committing that negligible motion.
- The JS canvas context and interop response are test doubles. These tests do
  not claim browser pixels, real server acceptance, Undo/Redo or native reopen.
  Those checks remain parent-owned, as does deployment verification. No phone QA
  and no independent ellipse fixture replay was performed.

Parent reported the initial Dev baseline passed all twelve fixture/grip cases,
held-preview pixels, driving anchors/values, Undo/Redo, native reopen/reedit;
evidence `/tmp/dxfer-polygon-evidence`. The parent's strengthened history rerun
is separate from this commit's automated claims. Reported second-drag coordinate
rounding (~`4.94e-7` inches) is within its `1e-6` browser tolerance, with driving
intent exactly preserved. This agent did not run that browser verification.

Layout contract: alignment, wrapping, sizes, overflow, interaction targets,
responsive layout and viewport transforms are unchanged. No UI markup/CSS edits.

Applicable here: polygon translation, line endpoint welding, attached dimensions,
Coincident/Fix references. Polyline, circle, arc, ellipse, spline, point, dimension
anchor dragging and constraint-glyph dragging are unchanged/outside this bounded
assignment. Polygon vertex/radius/apothem grip editing and arbitrary coupled
systems are not claimed supported by these tests. Circumscribed coverage here is
translation of its stored radius/apothem parameter, not resizing.

## Verification Commands

Run from the worktree root:

```bash
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --filter FullyQualifiedName~PolygonDragMatrixTests --no-restore --verbosity minimal
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --no-restore --filter 'FullyQualifiedName~PolygonDragMatrixTests|FullyQualifiedName~DraggingWholePolygon|FullyQualifiedName~DraggingPolygonCenter|FullyQualifiedName~DraggingPolygonMidpoint|FullyQualifiedName~CanvasDragResultTests' --verbosity minimal
node --test --test-reporter=spec tests/DXFER.Blazor.Tests/polygonDragMatrix.test.mjs
node --test --test-reporter=dot tests/DXFER.Blazor.Tests/polygonDragMatrix.test.mjs tests/DXFER.Blazor.Tests/dragPreviewSafety.test.mjs
dotnet build src/DXFER.Web/DXFER.Web.csproj --no-restore --verbosity minimal
git diff --check
```

Results: 90/90 new core; 96/96 with retained polygon/interop tests; 111/111 new JS;
142/142 with retained preview-safety tests; Web build zero warnings/errors.

Underlying defect status: none found in this bounded reproduction, so no solver
fix was warranted. No existing records were changed and no repair need was
identified. Intentionally inconsistent fixture dimensions refuse without silently
rewriting intent; previously damaged documents still need separate diagnosis and
verified recovery, not inferred repair.

# DXFER Audit Repair Game Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the 2026-05-06 architecture audit and latest sketch testing feedback into staged, test-gated fixes instead of one broad patch.

**Architecture:** Keep each patch scoped to one behavioral seam: tool status/UI truth, destructive operation safety, sketch creation constraints, geometry drag, solver diagnostics, glyph placement, and workspace/tool UI. Each stage must add focused tests before implementation and run the narrow test set before moving forward.

**Tech Stack:** .NET 8, Blazor components, DXFER.Core sketch/document services, JavaScript canvas module tests with `node --test`, xUnit/FluentAssertions.

---

## Current Inputs

- Audit file: `docs/dev/dxfer-architecture-audit-2026-05-06.md`.
- Latest testing feedback: creation tools need uniform snap/autoconstraint behavior; lines should create coincident, perpendicular, and tangent-adjacent constraints when continuing geometry; tangent arcs need persistent tangent constraints; ellipses are not draggable; glyphs block vertex clicks; dimensions should move with dragged geometry and still be respected; violated constraints/dimensions/entities should paint red; toolbar/icon state is not trustworthy; dockable toolbar work is incomplete; icon art needs another pass.

## Live Progress

- [x] Read audit and locate the active sketch, canvas, workbench, and test seams.
- [x] Create this staged game plan.
- [x] Stage 0: clear misleading green "confirmed working" icon state. Automated tests and running-app browser verification complete.
- [x] Stage 1: Power Trim functional behavior. Safety, trim, and extend matrix coverage is complete for line, polyline, polygon, circle, arc, ellipse, spline, and point where geometrically applicable. The trim matrix is `CORE+APP` for every cell, and the extend matrix is `CORE+APP` or deliberate `N/A`.
- [x] Stage 2: make sketch creation constraints explicit and repeatable for chained/snap-created relationships. Focused tests, running-app browser verification, and the full core suite are complete.
- [x] Stage 3: make drag behavior uniform for ellipses, dimensions, and constrained line relations. Core tests and running-app checks passed for the scoped drag/dimension slice and the reopened ellipse canvas behavior slice.
- [x] Stage 4: move constraint glyph defaults away from vertices without losing draggable glyph support. Focused JS and rebuilt-app canvas verification complete.
- [x] Stage 5: paint violated constraints, dimensions, and affected geometry red from solver state. Focused diagnostics tests, full core tests, JS canvas/dock tests, rebuild, and running-app canvas verification complete.
- [x] Stage 6a: dockable/free-floating tool groups. Integrated the subagent slice, kept confirmed-green icon styling removed, rebuilt/restarted the app, and verified left/right/top/bottom/floating dock behavior in the running browser.
- [x] Bug-log slice: spline tangent handles are endpoint-only during preview and persist after creation. Focused JS and DTO tests passed, the solution rebuilt cleanly, and the rebuilt app canvas verified endpoint handle pixels with no interior handle pixels.
- [x] Bug-log slice: constraint glyphs are selectable from the canvas in hover-only mode. Focused JS tests passed, the solution rebuilt cleanly, and the rebuilt app selected a `constraint:horizontal-...` key from a visible glyph while `Show constraints` was off.
- [x] Bug-log slice: three-point arc radius dimension input no longer lands at the top-left. Focused JS passed, the solution rebuilt cleanly, and the rebuilt app placed the radius input near the preview arc at `x=561.6, y=375.2`.
- [x] Bug-log slice: inscribed and circumscribed polygon previews draw the guide circle again. Focused JS passed, the solution rebuilt cleanly, and the rebuilt app verified guide-circle pixels in both polygon modes.
- [x] Bug-log slice: polygon auto radius dimensions no longer cluster on the picked corner. Focused core passed, the solution rebuilt cleanly, and the rebuilt app placed a typed `R10` input outside the top radius point.
- [x] Stage 6b icon slice: corrected the three-point circle and center-point arc icon geometry. Focused icon tests passed, full core passed 384/384, JS canvas plus dock passed 151/151, the app rebuilt cleanly, and the rebuilt running app verified the updated SVG geometry.
- [x] Stage 6b icon slice: corrected the inscribed and circumscribed polygon icon geometry. Focused icon tests passed 4/4, full core passed 386/386, JS canvas plus dock passed 151/151, the app rebuilt cleanly, and the rebuilt running app verified the updated SVG geometry.
- [x] Stage 6b icon slice: corrected the three-point arc icon so all picked points lie on the displayed arc. Focused icon tests passed 5/5, full core passed 387/387, JS canvas plus dock passed 151/151, the app rebuilt cleanly, and the rebuilt running app verified the updated SVG geometry.
- [x] Stage 6b: complete the remaining workspace/tool UI icon art pass. Evidence: `docs/dev/tool-icon-audit-matrix.md` tracks the creation/toolbar icon review; concrete icon defects from latest testing are fixed and verified in the rebuilt app.
- [ ] Stage 7: revisit the larger audit architecture gaps through the targeted breakdown in `docs/superpowers/plans/2026-05-06-dxfer-stage7-architecture-gap-plan.md`. Slices 7A through 7F are complete; 7G geometry, pure linear/radial dimension helper, target-classification helper, pure dimension-presentation/input-policy, pure dimension input-state, pure dimension input parsing, and raw target-key splits are complete, and the remaining dimension rendering/DOM lifecycle, nearest-target hit-test, and tool-state canvas splits are still open.

## Stage 0 - Status Truth Reset

**Files:**
- Modify: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs`
- Modify: `src/DXFER.Blazor/Components/WorkbenchToolPalette.razor`
- Modify: `src/DXFER.Blazor/Components/CadIconButton.razor.css`

- [x] Remove the `IsConfirmedWorkingCommand` green-state mapping from command construction.
- [x] Keep future/dashed styling for intentionally disabled future commands.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --no-build`
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`
- [x] Run the app in a browser and verify tool icons no longer render green confirmed styling. Evidence: `.dxfer-icon-button-confirmed` count was `0`; future/dashed marker count remained `4`.

Acceptance: no creation/modification tool is visually marked "good" until the behavior is proven by current tests.

## Stage 1 - Power Trim Safety And Functional Coverage

**Files:**
- Modify: `src/DXFER.Core/Operations/DrawingModifyService.cs`
- Modify: `tests/DXFER.Core.Tests/Operations/DrawingModifyServiceTests.cs`
- Modify: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs`
- Track: `docs/dev/power-trim-extend-test-matrix.md`

- [x] Replace the existing curve-deletion test with a failing test that unsupported Power Trim targets leave the document unchanged.
- [x] Change `TryPowerTrimOrExtendLine` so non-line targets fail unchanged until real curve trim support exists.
- [x] Add a failing test that endpoint clicks meant for extension do not delete a line when no valid cutter/extension span is resolved.
- [x] Keep valid line trim/extend behavior unchanged.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter DrawingModifyServiceTests`
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result: passed 258/258 after Stage 3 was completed.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`
- [x] Run the app in a browser and verify the original safety guard prevented unsupported curve deletion before curve support was added. Historical evidence: a circle click/drag was rejected without deleting geometry. Circle and arc support were added in the later Stage 1b slice below.
- [x] Add a focused circle trim test and implement circle targets trimmed by line cutters.
- [x] Add a focused line-to-circle cutter test and implement circle intersections as line target trim cutters.
- [x] Add a focused arc target test and implement open arc trim against line/circle cutters.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter DrawingModifyServiceTests`. 2026-05-06 result: passed 18/18.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result: passed 259/259.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result: passed 118/118.
- [x] Run the app in a browser and verify circle trim. Evidence: with line cutters present, clicked a circle span; entity count stayed `3`, first entity changed from `circle:` to `arc:`, and the canvas showed the picked span removed.
- [x] Run the app in a browser and verify line-to-circle trim. Evidence: clicked a line section inside a circle cutter; hover targeted the line, entity count changed from `2` to `3`, and the canvas showed the middle span removed at the circle intersections.
- [x] Run the app in a browser and verify arc trim. Evidence: converted a circle to an arc, clicked an arc span; hover targeted the arc point, entity count changed from `3` to `4`, and the canvas showed the picked arc span removed.
- [x] Add focused line-target cutter matrix tests for polyline, polygon, arc, ellipse, spline, and point cutters.
- [x] Implement line-target cutter collection through a single entity matrix path for line, polyline, polygon, circle, arc, ellipse, spline, and point cutters.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter DrawingModifyServiceTests`. 2026-05-06 result after this slice: passed 24/24.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after this slice: passed 265/265.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after this slice: passed 122/122.
- [x] Kill the existing `DXFER.Web` process, rebuild `DXFER.slnx`, restart the app at `http://127.0.0.1:5119`, and verify line-target Power Trim in the running canvas for UI-creatable cutters. Evidence: line cutter changed entity count `3 -> 4`; circle, arc, ellipse, polygon, and spline cutters each changed entity count `2 -> 3`; point cutters changed entity count `3 -> 4`; every trim request returned `OnPowerTrimRequested:ok`.
- [x] Verify polyline as a line-target cutter in the running app through a loaded/imported polyline document. Evidence: loaded/imported polyline cutter paths were exercised in the running canvas, and line target plus polyline cutter is now `CORE+APP` in the matrix.
- [x] Add a focused ellipse-target test for a full ellipse trimmed by horizontal and vertical line cutters.
- [x] Implement ellipse target line-cutter trim using line/ellipse intersections and ellipse parameter ranges.
- [x] Update the workbench Power Trim guard so click and drag Power Trim routes ellipse targets to the modify service.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after ellipse-target slice: passed 265/265.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after ellipse-target slice: passed 122/122.
- [x] Kill/rebuild/restart and verify ellipse target trim in the running canvas. Evidence: before trim, the picked ellipse span hovered as the ellipse; after trim, the picked span had no hovered target, while a kept span still hovered as the ellipse; callback was `OnPowerTrimRequested:ok`.
- [x] Add a focused spline-target test for a spline trimmed by two line cutters.
- [x] Implement spline target line-cutter trim using sampled path distances and kept spline spans.
- [x] Update the workbench Power Trim guard so click and drag Power Trim routes spline targets to the modify service.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after spline-target slice: passed 265/265.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after spline-target slice: passed 122/122.
- [x] Kill/rebuild/restart and verify spline target trim in the running canvas. Evidence: before trim, the picked spline span hovered as the spline; after trim, entity count changed `3 -> 4`, the picked span had no hovered target, and a kept span still hovered as the spline; callback was `OnPowerTrimRequested:ok`.
- [x] Add row/column matrix tracking for trim target/cutter coverage and extend target/boundary coverage. Evidence: `docs/dev/power-trim-extend-test-matrix.md` records all line, polyline, polygon, circle, arc, ellipse, spline, and point combinations, with current core/app/open/todo status.
- [x] Add focused arc-target tests for arc cutters, including tangent/touch intersections that do not visually cross.
- [x] Implement arc-target cutter collection from other arc entities.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter DrawingModifyServiceTests`. 2026-05-06 result after arc/arc slice: passed 26/26.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after arc/arc slice: passed 267/267.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after arc/arc slice: passed 122/122.
- [x] Kill/rebuild/restart and verify arc target trim against arc cutters in the running canvas. Evidence: crossing arc cutter changed entity count `2 -> 3`, the clicked target span stopped hovering, and a kept target span still hovered.
- [x] Verify tangent/touch arc cutter behavior in the running canvas. Evidence: tangent arc touch plus a line cutter changed entity count `3 -> 4`, which confirms the non-crossing touch point split the target arc; removed-span probes at angles 110 and 115 no longer hovered, while tangent endpoint and kept arc spans still hovered.
- [x] Add focused circle-target tests for circle, arc, polyline, polygon, ellipse, spline, and point cutters, including a tangent/touch arc cutter.
- [x] Implement circle-target cutter collection for all listed cutter types.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "FullyQualifiedName~DrawingModifyServiceTests.PowerTrimCircleUses"`. 2026-05-06 result after circle-target slice: passed 8/8 after initially failing for every missing cutter type.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter DrawingModifyServiceTests`. 2026-05-06 result after circle-target slice: passed 34/34.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after circle-target slice: passed 275/275.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after circle-target slice: passed 122/122.
- [x] Kill/rebuild/restart and verify circle targets against UI-creatable cutters in the running canvas. Evidence: line, circle, arc, ellipse, polygon, spline, and point cutters each converted the target circle to an arc; the picked top span no longer hovered as the target, and the kept bottom span still hovered as the target. Polyline remains core-only until loaded/imported polyline canvas verification.
- [x] Add focused arc-target tests for polyline, polygon, ellipse, spline, and point cutters.
- [x] Implement arc-target cutter collection for sampled/split segment cutters and point cutters.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "FullyQualifiedName~DrawingModifyServiceTests.PowerTrimArcUsesApplicableEntityTypesAsCutters|FullyQualifiedName~DrawingModifyServiceTests.PowerTrimArcUsesPointEntitiesAsCutters"`. 2026-05-06 result after arc-target remaining-cutter slice: passed 5/5 after initially failing for all newly covered cutter types.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter DrawingModifyServiceTests`. 2026-05-06 result after arc-target remaining-cutter slice: passed 39/39.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after arc-target remaining-cutter slice: passed 280/280.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after arc-target remaining-cutter slice: passed 122/122.
- [x] Kill/rebuild/restart and verify arc targets against UI-creatable cutters in the running canvas. Evidence: ellipse, polygon, spline, and point cutters trimmed the picked arc span; the non-quadrant picked span no longer hovered as the target, and a kept side span still hovered as the target. Polyline remains core-only until loaded/imported polyline canvas verification.
- [x] Add focused ellipse-target tests for circle, arc, polyline, polygon, ellipse, spline, and point cutters.
- [x] Implement ellipse-target cutter collection for sampled/split curve cutters, segment cutters, and point cutters.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "FullyQualifiedName~DrawingModifyServiceTests.PowerTrimEllipseUsesApplicableEntityTypesAsCutters|FullyQualifiedName~DrawingModifyServiceTests.PowerTrimEllipseUsesPointEntitiesAsCutters"`. 2026-05-06 result after ellipse-target remaining-cutter slice: passed 7/7 after initially failing for all newly covered cutter types.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter DrawingModifyServiceTests`. 2026-05-06 result after ellipse-target remaining-cutter slice: passed 46/46.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after ellipse-target remaining-cutter slice: passed 287/287.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after ellipse-target remaining-cutter slice: passed 122/122.
- [x] Kill/rebuild/restart and verify ellipse targets against UI-creatable cutters in the running canvas. Evidence: circle, arc, ellipse, polygon, spline, and point cutters trimmed the picked ellipse span; the removed top span no longer hovered as the target, and kept ellipse spans still hovered. Polyline remains core-only until loaded/imported polyline canvas verification.
- [x] Add focused spline-target tests for circle, arc, polyline, polygon, ellipse, spline, and point cutters, including a tangent/touch arc cutter.
- [x] Implement spline-target cutter collection for analytic circle/arc/ellipse cutters, sampled segment cutters, and point cutters.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "FullyQualifiedName~PowerTrimSpline"`. 2026-05-06 result after spline-target remaining-cutter slice: passed 9/9 after initially failing for every newly covered cutter type.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter DrawingModifyServiceTests`. 2026-05-06 result after spline-target remaining-cutter slice: passed 54/54.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after spline-target remaining-cutter slice: passed 295/295.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after spline-target remaining-cutter slice: passed 122/122.
- [x] Kill/rebuild/restart and verify spline targets against UI-creatable cutters in the running canvas. Evidence: line cutters changed entity count `3 -> 4`; circle, arc, ellipse, polygon, and spline cutters changed entity count `2 -> 3`; point cutters changed entity count `3 -> 4`; the picked middle span no longer hovered as the target, and both kept spline spans still hovered. Tangent/touch arc plus a line cutter changed entity count `3 -> 4`, proving non-crossing touch points split spline targets. Polyline remains core-only until loaded/imported polyline canvas verification.
- [x] Reopen ellipse target from latest testing and add a focused near-edge canvas-pick regression test.
- [x] Change ellipse pick parameter resolution so canvas near-edge clicks are accepted while exact cutter intersections remain strict.
- [x] Change Power Trim canvas requests to send the projected edge point instead of the raw mouse point.
- [x] Reopen spline target from latest testing and add a focused regression proving trimmed spline spans are not reinterpolated through Catmull-Rom fit points.
- [x] Change trimmed spline target spans to preserve the sampled display path as degree-1 spline spans.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "FullyQualifiedName~PowerTrimEllipseAcceptsNearEdgeCanvasPick|FullyQualifiedName~PowerTrimSplinePreservesSampledPathInsteadOfReinterpolatingTrimmedSpan"`. 2026-05-06 result: passed 2/2 after both tests failed before implementation.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter DrawingModifyServiceTests`. 2026-05-06 result after reopened trim quality slice: passed 64/64.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after reopened trim quality slice: passed 305/305.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after reopened trim quality slice: passed 123/123.
- [x] Kill/rebuild/restart and verify reopened trim quality fixes in the running canvas. Evidence: off-edge ellipse target click with line cutters returned `OnPowerTrimRequested:ok`, removed span no longer hovered, and kept ellipse span still hovered; curved spline target with two line cutters changed entity count `3 -> 4`, removed middle span no longer hovered, and both kept spans were hoverable.
- [x] Add focused endpoint-coincidence tests for curved spline cutters against line, circle, arc, and ellipse targets, and for curved spline targets against circle cutters. Evidence: each test failed first against sampled-polyline intersections, then passed after refined Catmull-Rom root solving.
- [x] Implement refined Catmull-Rom intersection paths for line/circle/arc/ellipse targets using fit-point spline cutters, and for spline targets trimmed by circle cutters.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "FullyQualifiedName~DrawingModifyServiceTests"`. 2026-05-06 result after spline precision slice: passed 69/69.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after spline precision slice: passed 310/310.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after spline precision slice: passed 123/123.
- [x] Kill/rebuild/restart and verify the spline precision slice in the running canvas. Evidence: circle target + curved spline cutter converted the circle to an arc and removed the picked top span; ellipse target + curved spline cutter returned `OnPowerTrimRequested:ok`, removed the picked top span, and kept a split ellipse span hoverable; spline target + circle cutter changed entity count `2 -> 3`, removed the picked middle span, and kept both endpoint spans hoverable.
- [x] Add focused endpoint-coincidence tests for spline targets trimmed by line cutters and line-segment spline cutters. Evidence: both tests failed first against sampled target intersections, then passed after refined Catmull-Rom target roots were used.
- [x] Implement refined Catmull-Rom target intersections for spline targets trimmed by line, polyline, polygon, and line-segment spline cutters. Curved spline cutters remain open because the cutter side is still sampled.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "FullyQualifiedName~PowerTrimSplineTargetUsesRefinedLineCutterIntersections|FullyQualifiedName~PowerTrimSplineTargetUsesRefinedSplineSegmentCutterIntersections"`. 2026-05-06 result: passed 2/2 after both tests failed before implementation.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "FullyQualifiedName~DrawingModifyServiceTests"`. 2026-05-06 result after spline target precision expansion: passed 73/73.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after spline target precision expansion: passed 314/314.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after spline target precision expansion: passed 123/123.
- [x] Kill/rebuild/restart and verify the expanded spline target precision in the running canvas. Evidence: spline target with line cutters changed entity count `3 -> 4`; spline target with line-segment spline cutters changed `3 -> 4`; spline target with an arc cutter changed `2 -> 3`; spline target with an ellipse cutter changed `2 -> 3`; ellipse target with a crossing spline cutter changed `2 -> 3`. Picked spans stopped hovering and kept endpoint spans remained hoverable.
- [x] Add a focused endpoint-coincidence test for spline targets trimmed by curved spline cutters. Evidence: the test failed first with a sampled cutter endpoint offset of `1.4e-05`, then passed after Catmull-Rom curve-curve refinement.
- [x] Implement refined fit-point spline/spline intersections for spline targets and spline cutters, with sampled fallback for non-fit splines.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "FullyQualifiedName~PowerTrimSplineTargetUsesRefinedCurvedSplineCutterIntersections|FullyQualifiedName~PowerTrimSplineTargetUsesRefinedSplineSegmentCutterIntersections|FullyQualifiedName~PowerTrimSplineTargetUsesRefinedLineCutterIntersections"`. 2026-05-06 result: passed 3/3 after the curved spline-cutter test failed before implementation.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "FullyQualifiedName~DrawingModifyServiceTests"`. 2026-05-06 result after curved spline-vs-spline precision: passed 74/74.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after curved spline-vs-spline precision: passed 315/315.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after curved spline-vs-spline precision: passed 123/123.
- [x] Kill/rebuild/restart and verify curved spline-vs-spline trim in the running canvas. Evidence: spline target with two curved spline cutters changed entity count `3 -> 4`, the picked middle span stopped hovering, and both kept endpoint spans remained hoverable.
- [x] Add focused tests for polygon targets with line, circle, arc, ellipse, polyline, polygon, spline, and point cutters, and for point target deletion.
- [x] Implement polygon Power Trim as closed-perimeter trimming that replaces the polygon with the kept open polyline remainder. Implement point Power Trim as deliberate point deletion.
- [x] Update the workbench Power Trim target gate and instructional/status text to include polygon and point targets.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "FullyQualifiedName~PowerTrimPolygon|FullyQualifiedName~PowerTrimPointDeletesPointTarget"`. 2026-05-06 result after polygon/point trim target support: passed 10/10.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "FullyQualifiedName~DrawingModifyServiceTests"`. 2026-05-06 result after polygon/point trim target support: passed 83/83.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after polygon/point trim target support: passed 324/324.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after polygon/point trim target support: passed 123/123.
- [x] Kill/rebuild/restart and verify polygon/point trim in the running canvas. Evidence: inscribed polygon plus line cutter kept entity count `2`, changed first entity from `polygon` to `polyline`, removed the picked top span from hover, and left the kept lower span hoverable; point target trim changed entity count `1 -> 0`.
- [x] Reproduce the line extend/trim disambiguation bug in JS: off-end line click just outside point-snap tolerance but inside edge-hit tolerance returned endpoint `10,0` instead of raw off-end point `10.9,0`.
- [x] Preserve raw off-end line picks for Power Trim requests when the request is an edge hit beyond a line endpoint.
- [x] Add service coverage for line target extension to a line boundary.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "FullyQualifiedName~PowerTrimExtendsLineEndToLineBoundaryWhenPickedPastEndpoint|FullyQualifiedName~PowerTrimDoesNotDeleteLineWhenEndpointIsPickedForExtend"`. 2026-05-06 result after extend disambiguation: passed 2/2.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after extend disambiguation: passed 124/124.
- [x] Kill/rebuild/restart and verify line extension in the running canvas. Evidence: target line plus vertical line boundary kept entity count `2`; clicking off the target end at `10.35,0` returned `OnPowerTrimRequested:ok`; hovering `15,0` hit the target endpoint, and hovering old `10,0` hit the line body.
- [x] Reproduce the latest ellipse target report as a Power Trim hit-priority bug: an off-quadrant line cutter endpoint coincident with an ellipse edge targeted the cutter line instead of the ellipse.
- [x] Change the Power Trim-only request/hover hit path to prefer edge targets over coincident line/polyline/polygon point hits, while leaving normal selection and snapping behavior unchanged.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after ellipse target hit priority: passed 125/125.
- [x] Kill/rebuild/restart and verify ellipse targets in the running canvas. Evidence: at a line cutter endpoint coincident with the ellipse edge, hover targeted the ellipse, click returned `OnPowerTrimRequested:ok`, the top span no longer hovered, and a kept span remained hoverable. The rebuilt-app ellipse target row also passed for line, circle, arc, ellipse, polygon, spline, and point cutters.
- [x] Reproduce the latest spline precision report for control-point/no-fit splines. Evidence: line targets cut by control-point spline cutters failed against exact cubic intersections, and control-point spline targets cut by lines failed to resolve exact endpoint cuts.
- [x] Add knot-parameter spline evaluation and refined root solving for control-point/no-fit splines used as line/circle/arc/ellipse cutters or targets.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "PowerTrimLineTargetUsesRefinedControlPointSplineCutterIntersections|PowerTrimControlPointSplineTargetUsesRefinedLineCutterIntersections"`. 2026-05-06 result: passed 2/2 after both tests failed before implementation.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "PowerTrimSpline"`. 2026-05-06 result after control-point spline precision: passed 16/16.
- [x] Kill/rebuild/restart and verify control-point spline precision in the running canvas. Evidence: control-point spline cutters trimmed a line target with `0` pixel endpoint error at the refined intersection; control-point spline target plus two line cutters returned `OnPowerTrimRequested:ok`, changed entity count `3 -> 4`, removed the picked span from hover, and left both kept spans hoverable.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after control-point spline precision: passed 327/327.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result after control-point spline precision: passed 125/125.
- [x] Run: `dotnet build DXFER.slnx`. 2026-05-06 result after control-point spline precision: passed with 0 warnings and 0 errors.
- [x] Reproduce the open-entity no-cutter regression in focused tests. Evidence: spline, polyline, arc, and elliptical arc no-cutter deletion tests initially failed with unchanged documents.
- [x] Restore no-cutter interior deletion for open polylines, arcs, elliptical arcs, and splines while keeping closed full ellipses unchanged.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "PowerTrimSplineDeletesOpenTargetWhenPickedWithoutCutters|PowerTrimPolylineDeletesOpenTargetWhenPickedWithoutCutters|PowerTrimArcDeletesOpenTargetWhenPickedWithoutCutters|PowerTrimEllipticalArcDeletesOpenTargetWhenPickedWithoutCutters"`. 2026-05-06 result: passed 4/4 after all four tests failed before implementation.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "PowerTrim"`. 2026-05-06 result after open-entity deletion: passed 85/85.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result after open-entity deletion: passed 331/331.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\*.test.mjs`. 2026-05-06 result after open-entity deletion: passed 125/125.
- [x] Kill the locked `DXFER.Web` process, rebuild `DXFER.slnx`, restart the app at `http://127.0.0.1:5119`, and verify no-cutter behavior in the running canvas. Evidence: open spline, center-point arc, and elliptical arc each changed entity count `1 -> 0`; closed full ellipse stayed `1 -> 1`.
- [x] Use the built-in sample document to verify selected polyline matrix cells in the running canvas. Evidence: sample `bend-path` polyline target plus two line cutters changed entity count `9 -> 10`, removed the picked segment, and left both kept spans hoverable; a line target trimmed against the sample polyline kept entity count `8`, removed the picked lower span, and left the kept span hoverable; circle target plus sample polyline kept entity count `9`, removed the picked top span, and left the kept bottom span hoverable; ellipse target plus sample polyline changed `8 -> 9`, removed a clean picked-span probe, and left a kept ellipse span hoverable; arc target plus sample polyline changed `10 -> 11`, removed the picked arc span, and left both kept arc spans hoverable.
- [x] Add line extend boundary coverage for line targets against line, polyline, polygon, circle, arc, ellipse, spline, and point boundaries. Evidence: focused core boundary tests passed 7/7, PowerTrim core passed 92/92, `DXFER.slnx` rebuilt cleanly, and rebuilt-app checks verified circle, arc, ellipse, spline, and point boundaries by probing the extended line body between the old endpoint and boundary. Polyline and polygon boundaries remain core-only until app-verified from a loaded/imported boundary setup.
- [x] Add open-polyline extend coverage for line, polyline, polygon, circle, arc, ellipse, spline, and point boundaries. Evidence: focused core tests passed 8/8 after failing before implementation, PowerTrim core passed 100/100, JS canvas tests passed 126/126, full core passed 346/346, `DXFER.slnx` rebuilt cleanly, and a rebuilt browser-canvas check confirmed the shipped canvas sends the raw off-end polyline point to `OnPowerTrimRequested` instead of the snapped endpoint. Full workbench canvas verification passed for the line-boundary cell after the sample document loaded: before extension the probe at `10,18` had no hover, the off-start click returned `OnPowerTrimRequested:ok`, entity count stayed `7`, and the probe hovered `bend-path|segment|0` after extension.
- [x] Fix the app menu command routing blocker found during polyline app verification. Evidence: a focused guard test first failed on the duplicate page render boundary, the page now relies on the root `Routes` render mode, externally invoked workbench commands call `StateHasChanged`, render/menu focused tests passed 3/3, and rebuilt-app verification loaded `Open sample` to 7 entities with bounds `120 x 80`.
- [x] Add open-spline extend coverage for line, polyline, polygon, circle, arc, ellipse, spline, and point boundaries. Evidence: focused core tests passed 8/8 after failing before implementation, PowerTrim core passed 108/108, JS canvas tests passed 127/127, `DXFER.slnx` rebuilt cleanly, and a rebuilt browser-canvas check confirmed the shipped canvas sends the raw off-end spline point `10.2,0` to `OnPowerTrimRequested` instead of the snapped endpoint `10,0`.
- [x] Add open-arc and open-elliptical-arc extend coverage for line, polyline, polygon, circle, arc, ellipse, spline, and point boundaries. Evidence: focused core tests passed 18/18 after failing before implementation, PowerTrim core passed 126/126, JS canvas tests passed 129/129, full core passed 373/373, `DXFER.slnx` rebuilt cleanly, and a rebuilt browser-canvas check confirmed the shipped canvas sends raw off-end arc and elliptical arc points to `OnPowerTrimRequested` instead of snapped endpoints.
- [x] Classify closed/non-open extend target rows. Evidence: polygon, circle, full ellipse, and point rows are now deliberate `N/A` in `docs/dev/power-trim-extend-test-matrix.md`; added canvas request coverage proving closed circle and polygon near-edge picks project to trim points instead of raw extension picks; JS canvas tests passed 131/131.
- [x] Add full workbench line-boundary verification for open arc and open elliptical-arc extension. Evidence: after killing the main app process, rebuilding `DXFER.slnx`, and restarting `http://127.0.0.1:5119`, toolbar-created arc and elliptical-arc targets each extended to a toolbar-created line boundary through Power Trim; entity count stayed `2`, and a span probe beyond the old endpoint changed from the boundary line hit to the extended target entity.
- [x] Add full workbench line-boundary verification for open spline extension. Evidence: toolbar-created spline target plus toolbar-created line boundary extended through Power Trim; entity count stayed `2`, and the probe at `12,0` changed from no hover to the extended spline entity.
- [x] Add full workbench polyline-boundary verification for line target extension. Evidence: loaded the sample `bend-path` polyline, added a target line from `0,18` to `10,18`, clicked just past the endpoint at `10.35,18`, entity count stayed `8`, and the probe at `15,18` changed from no hover to the extended line entity.
- [x] Add full workbench polygon-boundary verification for line target extension. Evidence: toolbar-created target line and inscribed polygon boundary, clicked just past the endpoint at `10.35,0`, entity count stayed `2`, and the probe at `11,0` changed from no hover to the extended line entity.
- [x] Add full workbench circle-boundary verification for open polyline target extension. Evidence: loaded sample `bend-path`, placed a toolbar-created circle on the endpoint extension ray, clicked just past the polyline endpoint, entity count stayed `8`, and the probe beyond the old endpoint changed from no hover to `bend-path|segment|4`.
- [x] Add full workbench point-boundary verification for open polyline target extension. Evidence: loaded sample `bend-path`, placed a toolbar-created point on the endpoint extension ray, clicked just past the polyline endpoint, entity count stayed `8`, and the probe beyond the old endpoint changed from no hover to `bend-path|segment|4`.
- [x] Add full workbench spline-boundary verification for open polyline target extension. Evidence: loaded sample `bend-path`, placed a toolbar-created spline across the endpoint extension ray, clicked just past the polyline endpoint, entity count stayed `8`, and the probe beyond the old endpoint changed from no hover to `bend-path|segment|4`.
- [x] Add full workbench arc-boundary verification for open polyline target extension. Evidence: loaded sample `bend-path`, placed a toolbar-created center-point arc across the endpoint extension ray, clicked just past the polyline endpoint, entity count stayed `8`, and the probe beyond the old endpoint changed from no hover to `bend-path|segment|4`.
- [x] Add full workbench ellipse-boundary verification for open polyline target extension. Evidence: loaded sample `bend-path`, placed a toolbar-created ellipse with its major axis on the endpoint extension ray, clicked just past the polyline endpoint, entity count stayed `8`, and the probe beyond the old endpoint changed from no hover to `bend-path|segment|4`.
- [x] Add full workbench polygon-boundary verification for open polyline target extension. Evidence: loaded sample `bend-path`, placed a toolbar-created inscribed polygon on the endpoint extension ray, clicked just past the polyline endpoint, entity count stayed `8`, and the probe beyond the old endpoint changed from no hover to `bend-path|segment|4`.
- [x] Add full workbench boundary-row verification for open spline target extension. Evidence: toolbar-created spline targets extended to toolbar-created circle, polygon, arc, ellipse, spline, and point boundaries, and a sample `bend-path` polyline boundary; every case kept entity count stable and changed the probe beyond the old endpoint to the extended spline entity.
- [x] Add full workbench boundary-row verification for open arc target extension. Evidence: toolbar-created arc targets extended to toolbar-created circle, polygon, arc, ellipse, spline, and point boundaries, and a sample `bend-path` polyline boundary; every case kept entity count stable and changed the probe beyond the old endpoint to the extended arc entity.
- [x] Add full workbench boundary-row verification for open elliptical-arc target extension. Evidence: toolbar-created elliptical-arc targets extended to toolbar-created circle, polygon, arc, ellipse, spline, and point boundaries, and a sample `bend-path` polyline boundary; every case kept entity count stable and changed the probe beyond the old endpoint to the extended elliptical arc entity.
- [x] Fix header file-open requests to rerender the workbench after loading a file. Evidence: focused source guard failed first, then passed after `OpenFileAsync` scheduled `StateHasChanged`; rebuilt-app DXF import showed the two imported polylines immediately without requiring a separate UI rerender.
- [x] Add full workbench polyline-boundary verification for open polyline target extension. Evidence: imported a two-polyline DXF through the visible file chooser, clicked just past the target polyline endpoint with Power Trim, entity count stayed `2`, and the probe at `12,0` changed from no hover to `polyline-1|segment|2`.
- [x] Add final trim-matrix app verification. Evidence: rebuilt and restarted the app, then verified the remaining `CORE` trim cells in the running canvas. Open polyline target passed polyline, polygon, circle, arc, ellipse, spline, and point cutters; polygon target passed polyline, polygon, circle, arc, ellipse, spline, and point cutters; spline target passed a real polyline cutter. Each successful case returned `OnPowerTrimRequested:ok`, removed the picked span from hover, and left the kept target span hoverable.

Acceptance: Power Trim cannot delete arcs, circles, ellipses, splines, polygons, or polylines through the line-only path.

Remaining acceptance before Stage 1 is complete:
- [x] Circle trim works in the running app.
- [x] Arc trim works in the running app for line/circle cutters.
- [x] Spline trim works in the running app for line cutters.
- [x] Ellipse trim works in the running app for line cutters.
- [x] Every entity type is evaluated as a Power Trim cutter and target: line, polyline, polygon, circle, arc, ellipse, spline, and point targets accept all listed cutter types in core tests or have documented cutter-independent behavior. Running-app verification is complete for every trim matrix cell.
- [x] All applicable Power Trim cutter/target combinations are covered by focused tests and running-app verification. Evidence: `docs/dev/power-trim-extend-test-matrix.md` has no remaining `CORE`-only trim cells; the extend matrix is `CORE+APP` or deliberate `N/A`.
- [x] Arc targets trim against arc cutters, and tangent/touching intersections count as cutters even when the entities do not visually cross.
- [x] Unsupported Power Trim cutter/target combinations fail safely without deleting geometry and are documented as deliberate limitations. Current matrix state has no unsupported trim cells; extend-ineligible closed/non-open targets are documented as deliberate `N/A`.
- [x] Line-target extend attempts do not trim the source entity for line, polyline, polygon, circle, arc, ellipse, spline, and point boundary paths. The full line extend row is now `CORE+APP`.
- [x] Open-polyline target extend attempts are core-covered for all boundary types, preserve raw off-end canvas picks, and are full-workbench app-verified for all boundary types.
- [x] Open-spline target extend attempts are core-covered for all boundary types, preserve raw off-end canvas picks, and are full-workbench app-verified for all boundary types.
- [x] Open-arc target extend attempts are core-covered for all boundary types, preserve raw off-end canvas picks, and are full-workbench app-verified for all boundary types.
- [x] Open-elliptical-arc target extend attempts are core-covered for all boundary types, preserve raw off-end canvas picks, and are full-workbench app-verified for all boundary types.
- [x] Closed/non-open extend target rows are deliberately `N/A`: polygon, circle, full ellipse, and point do not have an open endpoint/span to extend, while remaining valid trim targets or boundaries where applicable.
- [x] Trimming open entities deletes/trims the intended open section where applicable. Evidence: core tests cover open polyline, arc, elliptical arc, and spline no-cutter deletion; rebuilt-app checks cover open spline, arc, and elliptical arc.
- [x] Keep `docs/dev/power-trim-extend-test-matrix.md` current after each targeted patch; do not move a cell to `CORE+APP` without automated and running-canvas evidence.

## Stage 2 - Creation Autoconstraints

**Files:**
- Modify: `src/DXFER.Blazor/Sketching/SketchCreationConstraintFactory.cs`
- Modify: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs`
- Modify: `tests/DXFER.Core.Tests/Sketching/SketchCreationConstraintFactoryTests.cs`
- Modify: `tests/DXFER.Core.Tests/Sketching/SketchConstraintServiceTests.cs` if validation needs tighter coverage.

- [x] Add tests for a chained line starting from an existing line endpoint creating a persistent `Coincident` constraint.
- [x] Add tests for a new line drawn perpendicular from an existing endpoint creating `Coincident` plus `Perpendicular`.
- [x] Add tests for a new line drawn from an existing midpoint creating `Midpoint`; add `Perpendicular` when the direction is perpendicular to that source segment.
- [x] Add tests for tangent-arc creation creating `Coincident` at the shared point and `Tangent` to the source line when geometry is already tangent.
- [x] Implement a document-insertion constraint factory method that sees existing entities plus new entities, instead of only the new tool output.
- [x] Update workbench tool commit to use the insertion-aware factory.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter SketchCreationConstraintFactoryTests`
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result: passed 258/258 after Stage 3 was completed.
- [x] Run the app in a browser and verify chained/continued line creation produces visible persistent constraints without breaking canvas interaction. Evidence: drew a continued perpendicular line; entity count became `2`, visible constraint glyph groups became `4`.
- [x] Run the app in a browser and verify tangent-arc chain creation produces visible persistent constraints without breaking canvas interaction. Evidence: line chain switched to `tangentarc`, created an arc, entity count became `2`, visible constraint glyph groups became `2`.

Acceptance: creation-time constraints are generated from actual snapped/coincident geometry, not from toolbar labels or one-off tool paths.

## Stage 3 - Drag Semantics And Dimension Respect

**Files:**
- Modify: `src/DXFER.Core/Sketching/SketchGeometryDragService.cs`
- Modify: `src/DXFER.Core/Sketching/SketchConstraintPropagationService.cs`
- Modify: `src/DXFER.Core/Sketching/SketchGeometryEditor.cs`
- Modify: `tests/DXFER.Core.Tests/Sketching/SketchGeometryDragServiceTests.cs`
- Modify: `tests/DXFER.Core.Tests/Sketching/SketchDimensionSolverServiceTests.cs`

- [x] Add failing tests for `Perpendicular` propagation during rectangle/line dragging.
- [x] Add failing tests for ellipse center, major-axis, and minor-axis drag.
- [x] Add failing tests showing dimension anchors move when all referenced geometry translates.
- [x] Add failing tests showing driving dimensions are reapplied or violations are surfaced after drag.
- [x] Implement the smallest drag service changes needed for those tests.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter SketchGeometryDragServiceTests`. 2026-05-06 result: passed.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result: passed 258/258.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result: passed 118/118.
- [x] Run the app in a browser and verify ellipse center, major-axis, and minor-axis drags on a created ellipse. Evidence: inspector statuses were `Moved ellipse center.`, `Changed ellipse major axis.`, and `Changed ellipse minor axis.`, with bounds updating as expected and entity count staying `1`.
- [x] Run the app in a browser and verify a driving dimension stays respected during endpoint drag. Evidence: after dragging the endpoint of a dimensioned line, the displayed dimension stayed `10` and live bounds stayed `10 x 0`.
- [x] Run the app in a browser and verify a chained perpendicular line remains perpendicular after a diagonal endpoint drag. Evidence: created a chained line with visible constraint group count `4`; after dragging the vertical endpoint diagonally, the vertical relation remained visually intact and constraint glyphs stayed visible.
- [x] Reopen from latest ellipse screenshots and add JS tests for first-stage major diameter preview, sampled ellipse edge hover, ellipse handle drag preview, and whole-ellipse preview dimension-anchor movement.
- [x] Implement centered first-stage ellipse major diameter preview in `drawingCanvas.js`.
- [x] Implement sampled ellipse edge hit testing so ellipse edges hover as entity targets.
- [x] Implement ellipse-aware canvas drag preview for center/whole-entity translation, major-axis handles, and minor-axis handles.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`. 2026-05-06 result: passed 122/122.
- [x] Run: `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj`. 2026-05-06 result: passed 259/259.
- [x] Run the app in a browser and verify ellipse creation preview. Evidence: after first center click and pointer move, active tool was `ellipse`, draft point count was `1`, and the active dimension input was `Major diameter` with value `10`; after Enter and minor preview, inputs showed `Major diameter` `10` and `Minor diameter` `5`.
- [x] Run the app in a browser and verify ellipse hover. Evidence: moving over the ellipse edge set `data-hovered-id` to the ellipse entity id and `data-hovered-kind` to `entity`.
- [x] Run the app in a browser and verify ellipse handle drag. Evidence: major handle hover targeted `point|start`; drag reported `Changed ellipse major axis.` with bounds changing to `15.833 x 7.917`; minor handle hover targeted `point|quadrant-90`; drag reported `Changed ellipse minor axis.` with bounds changing to `15.833 x 11.667`.

Acceptance for this scoped slice: ellipse creation preview uses centered diameter semantics, ellipse edge hover works, ellipse point/center drags work with canvas preview support, perpendicular line relations are propagated during drag, dimension anchors follow full-entity translation, and driving dimensions do not silently stretch during endpoint drag. Broader uniform drag behavior remains part of the open audit backlog.

## Stage 4 - Constraint Glyph Placement

**Files:**
- Modify: `src/DXFER.Blazor/wwwroot/drawingCanvas.js`
- Modify: `tests/DXFER.Blazor.Tests/drawingCanvasSelection.test.mjs`

- [x] Add tests proving default glyph rectangles do not contain their anchor vertex/snap point.
- [x] Offset default glyph placement along a deterministic normal from the referenced geometry, with a leader only when the user drags the glyph farther away.
- [x] Preserve manual glyph dragging and hit testing.
- [x] Run: `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs`
- [x] Run the rebuilt app in a browser and verify a shared vertex remains clickable while constraint glyphs are visible. Evidence: visible constraint group count was `4`; hovering/clicking the shared endpoint selected `line-...|point|end|...`, not a `constraint:` key.

Acceptance: vertex targets remain clickable without first moving a constraint glyph.

## Stage 5 - Solver Diagnostics Rendering

**Files:**
- Modify: `src/DXFER.Core/Sketching/SketchSolveResult.cs`
- Modify: `src/DXFER.Core/Sketching/SketchConstraintPropagationService.cs`
- Modify: `src/DXFER.Blazor/Interop/CanvasDocumentDto.cs`
- Modify: `src/DXFER.Blazor/wwwroot/drawingCanvas.js`
- Modify: `tests/DXFER.Core.Tests/Sketching/SketchSolverAbstractionTests.cs`
- Modify: `tests/DXFER.Blazor.Tests/drawingCanvasSelection.test.mjs`

- [x] Add affected-reference diagnostics for unsatisfied constraints and dimensions.
- [x] Expose unsatisfied state through the canvas DTO.
- [x] Render affected entities, glyphs, and dimensions with the existing red error color when state is unsatisfied.
- [x] Run core solver tests and canvas tests. Evidence: focused solver/DTO tests passed 2/2; full core tests passed 382/382; JS canvas plus dock tests passed 151/151; `DXFER.slnx` rebuilt with 0 warnings and 0 errors after killing the main web process.
- [x] Run the rebuilt app in a browser and verify actual canvas behavior. Evidence: an intentionally unsatisfied running-app document produced red affected geometry pixels (`edgeRedCount=23`), red dimension segment pixels (`maxDimensionSegmentRedCount=14`), a red unsatisfied constraint glyph (`glyphRedCount=37`), and a red persistent dimension input class/color.

Acceptance: broken solves are visible in the drawing area, not only in a status line.

## Stage 6 - Workspace UI And Icon Pass

**Files:**
- Modify: `src/DXFER.Blazor/Components/DrawingWorkbench.razor`
- Modify: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.css`
- Modify: `src/DXFER.Blazor/Components/WorkbenchToolPalette.razor`
- Modify: `src/DXFER.Blazor/Components/WorkbenchToolPalette.razor.css`
- Modify: `src/DXFER.Blazor/Components/CadIcon.razor`
- Modify: `src/DXFER.Blazor/Components/CadIcon.razor.css`
- Modify: `docs/dev/workspace-tool-ui-acceptance.md`

- [x] Define and implement the dock model: tool panels can free-float or dock to any side of the window: left, right, top, and bottom. Bottom docking must sit above the instruction/status panel; right docking must sit left of the inspector. Evidence: `WorkbenchToolPalette` now owns per-group dock state; `DrawingWorkbench` mounts the palette in a canvas-row overlay; cleanup commands are included as a dockable group.
- [x] Add tests or manual acceptance notes for dock state and placement. Evidence: `node --test tests\DXFER.Blazor.Tests\workbenchDockModel.test.mjs` passed 13/13, `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs` passed 128/128, `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter WorkbenchRenderBoundaryTests --no-build` passed 2/2, and `dotnet build .\DXFER.slnx --no-restore` passed.
- [x] Run the app in a browser and verify every dock state. Evidence: after killing the main app process, rebuilding, and restarting `http://127.0.0.1:5119`, Playwright dragged the `View` group to floating, top, bottom, right, and left; bottom group bounds stayed above the command/status bar, right group bounds stayed left of the open inspector, and `.dxfer-icon-button-confirmed` count was `0`.
- [x] Redraw the three-point circle icon so all three picked points sit on the circle.
- [x] Redraw the three-point arc icon so all three picked points sit on the displayed arc.
- [x] Redraw the center-point arc icon so the displayed center is concentric with the arc.
- [x] Redraw inscribed/circumscribed polygon icons so the guide circle relationship is geometrically accurate.
- [x] Continue the remaining icon art pass for other icons that imply wrong geometry.
- [x] Keep icons as inline SVG in `CadIcon.razor`; do not add raster assets for tool icons.
- [x] Run Blazor build and JS tests for the dockable-panel slice.

Acceptance: toolbar behavior matches the documented scope, and icons communicate the actual geometry operations.

## Stage 7 - Larger Audit Gaps

**Files:** see `docs/superpowers/plans/2026-05-06-dxfer-stage7-architecture-gap-plan.md`.

- [x] 7A. Reproduce and fix parallel line-to-parallel line dimensioning in the running app.
- [x] 7B1. Complete scalar entity and bounds measurement coverage behind `MeasurementService`.
- [x] 7B2. Add entity-to-entity shortest distance measurement coverage.
- [x] 7C. Add document metadata model for units, source/trust mode, hashes, warnings, and unsupported entity accounting.
- [x] 7D. Add `*.dxfer.json` sidecar schema/export path.
- [x] 7E. Move DXF IO behind a `DXFER.CadIO` adapter boundary without changing behavior.
- [x] 7F. Add trusted/reference guardrails and unsupported entity warnings to import/open flows.
- [ ] 7G. Split `drawingCanvas.js` into behavior-preserving modules.
- [ ] 7H. Record the desktop shell decision and defer or plan `DXFER.Desktop`.

Acceptance: each architecture gap gets its own plan and testable slice; these are not mixed into sketch-interaction patches.

## Verification Rules

- Before coding or fixing any tool/feature, create an entity applicability checklist: line, polyline, polygon, circle, arc, ellipse, spline, point, dimension, and constraint glyph/reference.
- For each applicable entity type, add focused automated coverage where practical and running-app canvas verification for canvas-facing behavior.
- For each non-applicable entity type, document the deliberate unsupported path and verify it fails safely without deleting or corrupting geometry.
- Run the narrow test command at the end of each stage.
- Run the broader affected suite before marking a stage complete.
- Run the app in a browser and verify the actual canvas/workbench behavior before marking any canvas-facing stage complete.
- Update `Live Progress` immediately after each stage.
- Do not combine unrelated stages in the same patch.

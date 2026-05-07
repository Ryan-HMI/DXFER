# DXFER Stage 7 Architecture Gap Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Break the remaining audit gaps into small, test-gated patches, starting with the reopened parallel line-to-parallel line dimension regression.

**Architecture:** Stage 7 is not one feature. Each slice owns one boundary: dimension interaction, measurement coverage, document metadata, sidecar export, CAD IO adapters, trust/warning guardrails, canvas module split, and desktop-shell decision. Every canvas-facing slice requires a rebuilt running-app verification before it can move to done.

**Tech Stack:** .NET 8, Blazor, `DXFER.Core`, JavaScript canvas facade tests with `node --test`, xUnit/FluentAssertions, PowerShell app rebuild/restart workflow.

---

## Current Rules

- New bugs/features reported during active work go into `docs/dev/bug-feature-log.md`; do not switch tasks unless the report directly affects the active slice.
- Before coding a feature/tool behavior, evaluate applicability for: line, polyline, polygon, circle, arc, ellipse, spline, point, dimension, and constraint glyph/reference.
- Mark a slice complete only after focused automated tests, affected broad tests, a clean `DXFER.slnx` rebuild after killing the existing main web process, and running-app canvas verification when the behavior is visible in the canvas.
- Do not combine CAD IO, sidecar, measurement, and canvas module-split changes in the same patch.

## Active Stage 7 Tracking

- [x] 7A. Reproduce and fix parallel line-to-parallel line dimensioning in the running app.
- [x] 7B1. Complete scalar entity and bounds measurement coverage behind `MeasurementService`.
- [x] 7B2. Add entity-to-entity shortest distance measurement coverage.
- [x] 7C. Add document metadata model for units, source/trust mode, hashes, warnings, and unsupported entity accounting.
- [x] 7D. Add `*.dxfer.json` sidecar schema/export path.
- [x] 7E. Move DXF IO behind a `DXFER.CadIO` adapter boundary without changing behavior.
- [x] 7F. Add trusted/reference guardrails and unsupported entity warnings to import/open flows.
- [ ] 7G. Split `drawingCanvas.js` into behavior-preserving modules.
- [ ] 7H. Record the desktop shell decision and defer or plan `DXFER.Desktop`.

## 7A - Parallel Line Dimension Regression

**Why first:** Latest testing says "DIM FROM PARALLEL LINE TO PARALLEL LINE IS NOT IMPLEMENTED." Current core and JS descriptor tests already claim support exists, so this must be reproduced in the rebuilt workbench before changing code.

**Files:**
- Investigate: `src/DXFER.Blazor/wwwroot/drawingCanvas.js`
- Investigate: `src/DXFER.Blazor/Sketching/SketchCommandFactory.cs`
- Investigate: `src/DXFER.Core/Sketching/SketchDimensionSolverService.cs`
- Modify if needed: `tests/DXFER.Blazor.Tests/drawingCanvasSelection.test.mjs`
- Modify if needed: `tests/DXFER.Core.Tests/Sketching/SketchCommandFactoryTests.cs`
- Modify if needed: `tests/DXFER.Core.Tests/Sketching/SketchDimensionSolverServiceTests.cs`
- Track: `docs/dev/bug-feature-log.md`

- [x] **Step 1: Reproduce in the rebuilt running app**

Kill the existing app process, rebuild, restart, then use the visible canvas at `http://127.0.0.1:5119`.

Expected reproduction script behavior:
- Create two separate parallel lines through the toolbar.
- Activate Dimension.
- Click the first line body.
- Click the second line body.
- Move to a dimension anchor location and click to place.
- Inspect the app document and persistent input.

Observed failure before the fix: midpoint clicks on both parallel lines hovered as `point` targets and produced a persistent `lineardistance` dimension between midpoint snap points instead of a line-to-line `PointToLineDistance`.

- [x] **Step 2: Write the smallest failing automated test for the reproduced layer**

Preferred JS test if the defect is selection/placement:

```javascript
test("dimension tool can extend a selected line with a parallel line target", () => {
  const state = createHitTestState([
    {
      id: "base",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    },
    {
      id: "offset",
      kind: "line",
      points: [{ x: 2, y: 4 }, { x: 8, y: 4 }]
    }
  ]);

  assert.equal(canExtendDimensionSelection(state, ["base"], "offset"), true);
});
```

Preferred core test if the defect is command construction:

```csharp
[Fact]
public void BuildsPointToLineDimensionFromParallelLinesInEitherSelectionOrder()
{
    var document = new DrawingDocument(new DrawingEntity[]
    {
        new LineEntity(EntityId.Create("base"), new Point2(0, 0), new Point2(10, 0)),
        new LineEntity(EntityId.Create("offset"), new Point2(2, 4), new Point2(8, 4))
    });

    SketchCommandFactory.TryBuildDimension(
            document,
            new[] { "offset", "base" },
            "dim-1",
            out var dimension,
            out var status)
        .Should().BeTrue();

    status.Should().Be("Added parallel line distance dimension.");
    dimension.Kind.Should().Be(SketchDimensionKind.PointToLineDistance);
    dimension.ReferenceKeys.Should().Equal("offset", "base");
}
```

Run the focused command and verify it fails for the expected reason before editing production code.

- [x] **Step 3: Patch only the failing layer**

Use the existing `PointToLineDistance` model. Do not add a new dimension kind unless reproduction proves the current model cannot represent line-line distance.

Applied fix: `getDimensionSelectionKey` now promotes line midpoint snap targets to the line entity reference and polyline segment midpoint snap targets to the segment reference for Dimension-tool selection. Endpoint and unrelated point targets still remain point references.

- [x] **Step 4: Verify automated tests**

Run:

```powershell
dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter "SketchCommandFactoryTests|SketchDimensionSolverServiceTests"
node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs
```

Expected: all focused tests pass with no unexpected failures.

Evidence: the focused red test first failed because `getDimensionSelectionKey` was not exported and the behavior was missing; after the fix, `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs` passed 139/139 and `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchDockModel.test.mjs` passed 152/152.

- [x] **Step 5: Verify in the rebuilt running app**

Run:

```powershell
$main = Get-Process -Name DXFER.Web -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*\source\repos\DXFER\src\DXFER.Web\bin\Debug\net8.0\DXFER.Web.exe' }
if ($main) { $main | Stop-Process -Force }
dotnet build .\DXFER.slnx --no-restore
dotnet run --project src\DXFER.Web\DXFER.Web.csproj --no-build --urls http://127.0.0.1:5119
```

Then repeat the reproduction steps in the canvas. Evidence must include document dimension kind/reference keys and visible persistent dimension input placement.

Evidence: after killing the main web process, rebuilding `DXFER.slnx`, and restarting `http://127.0.0.1:5119`, the running canvas created two separate toolbar lines. Dimension midpoint clicks still hovered as line midpoint snap points, but placement produced one persistent dimension input with `data-dimension-kind="pointtolinedistance"`, label `Point-to-line distance`, and value `4` between the lines.

## 7B - V1 Measurement Coverage

**Files:**
- Modify: `src/DXFER.Core/Operations/MeasurementService.cs`
- Modify: `src/DXFER.Core/Operations/MeasurementResult.cs`
- Test: `tests/DXFER.Core.Tests/Operations/MeasurementServiceTests.cs`
- Consider DTO/UI only after the core measurement matrix is green.

- [x] **Step 1: Create a measurement applicability matrix**

Track rows for line, polyline, polygon, circle, arc, ellipse, spline, point, selection bounds, full drawing bounds, dimensions, and constraints. Mark each as `MEASURE`, `BOUND`, or deliberate `N/A`.

Evidence: `docs/dev/measurement-coverage-matrix.md` now tracks scalar/bounds coverage and leaves entity-to-entity shortest distance as 7B2.

- [x] **Step 2: Add failing core tests for V1 required scalar/bounds measurements**

Required tests:
- point-to-point distance with X/Y delta.
- line length.
- full polyline length, not only the first segment.
- arc length.
- selected bounds.
- full drawing bounds.
- units/precision formatting once metadata exists.

Evidence: added `MeasurementServiceTests` for point, line-adjacent existing path, full polyline path, arc length, circular ellipse arc length, spline sample length, polygon perimeter, point entity coordinates, and entity bounds. Added `DrawingPrepServiceTests.ReportsSelectedEntityBoundsWhenMultipleEntitiesAreSelected`.

- [x] **Step 3: Implement only the missing scalar/bounds measurement primitives**

Keep `MeasurementService` pure and independent of Blazor. Add methods only where tests define a consumer need, for example:

```csharp
public static bool TryMeasureEntity(DrawingEntity entity, out MeasurementResult measurement)
public static MeasurementResult MeasureBounds(IEnumerable<DrawingEntity> entities)
```

Evidence: `MeasurementService` now measures polyline full path, arc length, ellipse/elliptical-arc sampled length, spline sampled length, polygon perimeter, point entity coordinates, and bounds width/height/diagonal. `DrawingPrepService.TryGetMeasurement` returns selected bounds for multi-entity selection.

- [x] **Step 4: Run scalar/bounds verification**

Run:

```powershell
dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --filter MeasurementServiceTests
dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --no-build
```

Running-app verification is required only after these measurements are wired to visible UI.

Evidence: focused measurement tests passed 9/9; full core passed 396/396; JS canvas plus dock passed 152/152; `DXFER.slnx` rebuilt cleanly after killing the main web process. Rebuilt-app inspector verified a selected arc measured `X -5, Y 5, D 7.854`, and a selected line plus circle measured bounds `X 9, Y 4, D 9.849`.

- [x] **Step 5: Plan and implement entity-to-entity shortest distance**

Do this as 7B2, not as part of scalar/bounds measurement. Start with line-line, line-circle, circle-circle, point-entity, and sampled curve fallback tests, then decide which pairs are exact and which are sampled.

Evidence: added `MeasurementService.TryMeasureShortestDistance` with exact point/line/circle paths and segment-sampled fallback for other entities. Focused `MeasurementServiceTests` passed 14/14, full core passed 402/402, JS canvas plus dock passed 152/152, and `DXFER.slnx` rebuilt cleanly after killing the main web process. This is a core API slice; visible UI wiring for a shortest-distance command/readout is intentionally not mixed into it.

## 7C - Document Metadata Model

**Files:**
- Modify: `src/DXFER.Core/Documents/DrawingDocument.cs`
- Create: `src/DXFER.Core/Documents/DrawingDocumentMetadata.cs`
- Create: `src/DXFER.Core/Documents/DrawingDocumentWarning.cs`
- Test: `tests/DXFER.Core.Tests/Documents/DrawingDocumentTests.cs`
- Test: `tests/DXFER.Core.Tests/Documents/DrawingDocumentMetadataPreservationTests.cs`
- Modify DTOs only in a separate follow-up if Blazor needs metadata display.

- [x] Add immutable metadata records for units, source filename/hash, normalized filename/hash, trusted-source marker, reference-only mode, warnings, and unsupported entity counts.
- [x] Preserve existing `DrawingDocument` constructors by defaulting metadata to an empty/default value.
- [x] Add tests proving entity/dimension/constraint constructors store metadata and common document-rebuild services preserve metadata.
- [x] Run focused document tests and full core tests.

Evidence: added `DrawingDocumentMetadata` and `DrawingDocumentWarning`, kept existing constructors defaulting to `DrawingDocumentMetadata.Empty`, and passed metadata through workbench commits, selection deletion, split, construction, modify, sketch constraint, sketch dimension, and geometry-drag document rebuilds. Focused metadata tests passed 12/12, full core passed 411/411, `DXFER.slnx` rebuilt cleanly after killing the main web process, and the rebuilt app canvas smoke check verified a live nonblank canvas at `1098 x 718`.

## 7D - Sidecar Schema And Export Path

**Files:**
- Create: `src/DXFER.Core/IO/DxferSidecarDocument.cs`
- Create: `src/DXFER.Core/IO/DxferSidecarWriter.cs`
- Test: `tests/DXFER.Core.Tests/IO/DxferSidecarWriterTests.cs`
- Modify later: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs`

- [x] Add sidecar model with schema version, source/normalized filename/hash, trusted source, units, bounds, normalization summary, grain metadata, and warnings.
- [x] Add deterministic JSON serialization tests.
- [x] Add hash tests using stable fixture strings.
- [x] Wire Blazor export only after core sidecar serialization is green.
- [x] Rebuild and verify the running app downloads or exposes both normalized DXF and `.dxfer.json` when the UI path is wired.

Evidence: added `DxferSidecarDocument` and `DxferSidecarWriter` with deterministic LF-normalized JSON, SHA-256 calculation for supplied source/normalized text, and metadata hash fallback. `Save DXF` now downloads both the normalized DXF and matching `.dxfer.json` sidecar. Focused sidecar/export tests passed 13/13, full core passed 419/419, `DXFER.slnx` rebuilt cleanly after killing the main web process, and the rebuilt app emitted `Untitled.dxf` plus `Untitled.dxfer.json` with parseable schema version 1 sidecar JSON.

## 7E - CAD IO Adapter Boundary

**Files:**
- Create: `src/DXFER.CadIO/DXFER.CadIO.csproj`
- Move or wrap: `src/DXFER.Core/IO/DxfDocumentReader.cs`
- Move or wrap: `src/DXFER.Core/IO/DxfDocumentWriter.cs`
- Create interfaces in Core or CadIO based on dependency direction.
- Update: `DXFER.slnx`
- Update tests or add `tests/DXFER.CadIO.Tests` only if the project split requires it.

- [x] Decide the dependency direction before coding: Core must not depend on third-party IO packages.
- [x] Add adapter boundary and keep current behavior byte-for-byte where practical.
- [x] Move hand-written DXF parser/writer behind the adapter before introducing IxMilia or richer IO.
- [x] Run IO round-trip tests and full build.
- [x] Do not mix sidecar schema changes into this patch.

Evidence: added `src/DXFER.CadIO/DXFER.CadIO.csproj`, moved `DxfDocumentReader` and `DxfDocumentWriter` out of `DXFER.Core` into the CadIO project, updated Blazor/Web/test project references, and added boundary tests so Core no longer owns the DXF reader/writer files. Focused CadIO/DXF tests passed 7/7, full core passed 422/422, `DXFER.slnx` rebuilt cleanly with `DXFER.CadIO`, and the rebuilt app verified Save DXF still emits `Untitled.dxf` and parseable `Untitled.dxfer.json`.

## 7F - Trust, Reference, And Unsupported Entity Guardrails

**Files:**
- Modify: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs`
- Modify: `src/DXFER.Web/Components/Layout/MainLayout.razor`
- Modify metadata and IO files created by 7C/7E.
- Test: component/source guard tests under `tests/DXFER.Core.Tests/Components`.

- [x] DWG open should be explicit reference/external-viewer behavior, not silent no-op editing.
- [x] Unsupported DXF entities should generate document warnings with counts and names.
- [x] Missing units should show warnings without corrupting geometry.
- [x] Reference-only mode must block edit/trim/dimension mutations and explain why.
- [x] Verify warning display in the running app.

Evidence: `DxfDocumentReader` now records unsupported entity counts and emits `unsupported-entity` plus `missing-units` warnings in document metadata. File open overlays source filename/hash metadata, the inspector metadata shows warning/count fields, and `ApplyDocumentChange` blocks mutations while the active document is `ReferenceOnly`. Focused 7F tests passed 2/2, full core passed 424/424, `DXFER.slnx` rebuilt cleanly, and the rebuilt app imported a DXF with a supported line plus `3DSOLID`/`HATCH` warnings while keeping the line. The rebuilt app also showed the explicit DWG external-viewer handoff status for a `.dwg` selection.

## 7G - Canvas Module Split

**Files:**
- Keep facade: `src/DXFER.Blazor/wwwroot/drawingCanvas.js`
- Create modules in `src/DXFER.Blazor/wwwroot/canvas/`, one slice at a time.
- Test: `tests/DXFER.Blazor.Tests/drawingCanvasSelection.test.mjs`

- [x] Split pure geometry math first; no behavior changes.
- [x] Split pure linear/radial dimension screen geometry second; no behavior changes.
- [x] Split pure target classification helpers out of the hit-test area; no behavior changes.
- [x] Split pure dimension presentation/input-policy helpers; no behavior changes.
- [x] Split pure dimension input-state helpers; no behavior changes.
- [x] Split pure dimension input parsing/commit-policy helpers; no behavior changes.
- [x] Split raw target-key constants/parsers; no behavior changes.
- [ ] Split remaining dimension rendering/input state; no behavior changes.
- [ ] Split nearest-target hit testing/target resolution third; no behavior changes.
- [ ] Split tool interaction state only after the first three splits are green.
- [ ] After each module move, run JS tests and one running-app smoke test for drawing, selecting, dimensioning, and Power Trim.

Evidence for geometry split: added `src/DXFER.Blazor/wwwroot/canvas/geometry.js`, moved pure world/screen geometry helpers out of `drawingCanvas.js`, and kept `drawingCanvas.js` as the facade. Added `canvasGeometry.test.mjs`; JS geometry/canvas/dock suites passed 153/153, `DXFER.slnx` rebuilt cleanly, and the rebuilt app loaded a live nonblank canvas with no module-load console errors.

Evidence for dimension geometry split: added `src/DXFER.Blazor/wwwroot/canvas/dimensions.js`, moved pure linear/radial dimension screen-geometry helpers out of `drawingCanvas.js`, and re-exported them through the facade. Added `canvasDimensions.test.mjs`; JS dimension/geometry/canvas/dock suites passed 155/155, `DXFER.slnx` rebuilt cleanly, and the rebuilt app loaded a live nonblank canvas with no module-load console errors.

Evidence for target-classification split: added `src/DXFER.Blazor/wwwroot/canvas/targets.js`, moved selection-overlay filtering, point marker classification, dynamic target currency, pan-button detection, and constraint-tool target eligibility out of `drawingCanvas.js`, and re-exported them through the facade. Added `canvasTargets.test.mjs`; JS target/dimension/geometry/canvas/dock suites passed 160/160, `DXFER.slnx` rebuilt cleanly, and after killing/restarting the main app the rebuilt canvas smoke found one nonblank `1098 x 718` canvas with no fresh console/page errors. The in-app browser DOM check also found the canvas, but its screenshot capture timed out, so the pixel assertion used Playwright fallback.

Evidence for dimension presentation split: added `src/DXFER.Blazor/wwwroot/canvas/dimensionPresentation.js`, moved dimension display text/labels, render styles, input CSS class construction, input refresh/commit predicates, and numeric formatting out of `drawingCanvas.js`, and re-exported the existing public helper surface through the facade. Added `canvasDimensionPresentation.test.mjs`; JS presentation/target/dimension/geometry/canvas/dock suites passed 165/165, `DXFER.slnx` rebuilt cleanly, and after killing/restarting the main app the rebuilt canvas smoke found one nonblank `1098 x 718` canvas with no fresh console/page errors.

Evidence for dimension input-state split: added `src/DXFER.Blazor/wwwroot/canvas/dimensionInputState.js`, moved active-key selection, pending-focus resolution, input cycling, visible descriptor ordering, dimension-key extraction, and new persistent dimension detection out of `drawingCanvas.js`, and re-exported the existing public helper surface through the facade. Added `canvasDimensionInputState.test.mjs`; JS input-state/presentation/target/dimension/geometry/canvas/dock suites passed 169/169 and `DXFER.slnx` rebuilt cleanly. The first rebuilt-app canvas smoke correctly failed on a missing `getDimensionKeys` import; after exporting/importing that helper and rerunning tests/build/restart, the rebuilt canvas smoke found one nonblank `1098 x 718` canvas with no fresh console/page errors.

Evidence for dimension input parsing split: added `src/DXFER.Blazor/wwwroot/canvas/dimensionInputParsing.js`, moved persistent dimension commit parsing and positive-value commit policy out of `drawingCanvas.js`, and re-exported the existing public commit helper through the facade. Added `canvasDimensionInputParsing.test.mjs`; JS parsing/input-state/presentation/target/dimension/geometry/canvas/dock suites passed 171/171, `DXFER.slnx` rebuilt cleanly, and after killing/restarting the main app the rebuilt canvas smoke found one nonblank `1098 x 718` canvas with no fresh console/page errors.

Evidence for raw target-key split: added `src/DXFER.Blazor/wwwroot/canvas/targetKeys.js`, moved shared point/segment/constraint key separators plus raw point and segment key parsing out of `drawingCanvas.js`, while leaving current-entity resolution in the facade. Added `canvasTargetKeys.test.mjs`; JS target-key/parsing/input-state/presentation/target/dimension/geometry/canvas/dock suites passed 174/174, `DXFER.slnx` rebuilt cleanly, and after killing/restarting the main app the rebuilt canvas smoke found one nonblank `1098 x 718` canvas with no fresh console/page errors.

## 7H - Desktop Shell Decision

**Files:**
- Create or modify: `docs/dev/desktop-shell-decision.md`
- Do not create `src/DXFER.Desktop` until the decision doc identifies a concrete runtime target and acceptance tests.

- [ ] Document whether V1 needs Blazor Hybrid/WebView now or after web prototype stabilization.
- [ ] If deferred, record the production-file-access workaround and remaining risks.
- [ ] If approved, create a separate implementation plan for the shell project.

## Completion Checklist

- [ ] Every Stage 7 sub-slice has its own focused tests.
- [ ] Every canvas-visible sub-slice has rebuilt running-app verification.
- [ ] `docs/dev/bug-feature-log.md` is current after each slice.
- [ ] `docs/superpowers/plans/2026-05-06-dxfer-audit-repair-game-plan.md` links back to this Stage 7 breakdown.
- [ ] No Stage 7 patch mixes unrelated architecture gaps.

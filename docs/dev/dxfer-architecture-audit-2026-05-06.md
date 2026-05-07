# DXFER Architecture Audit - 2026-05-06

Scope: read-only audit of the current DXFER workspace against the active architecture and design documents. The source tree is actively being changed by another agent; this audit intentionally does not modify source or test files.

Docs reviewed:

- `docs/superpowers/specs/2026-05-03-dxfer-v1-design.md`
- `docs/superpowers/plans/2026-05-03-canvas-prototype.md`
- `docs/dev/canvas-module-boundaries.md`
- `docs/dev/workspace-tool-ui-acceptance.md`
- `docs/dev/cad-polish-implementation-passes.md`
- `docs/dev/agent-workflow.md`

## Executive Summary

DXFER has a substantial interactive canvas/workbench prototype, but it is still not aligned with the V1 architecture described in the design docs. The largest missing areas are the adapter-based CAD IO boundary, Sync sidecar schema/export, trusted/reference document guardrails, document metadata, and full V1 measurement coverage.

The highest-risk partial behavior is Power Trim: the docs require unsupported entities to fail clearly, but the current implementation captures curve targets and deletes non-line entities when no curve trim solver exists.

Several UI controls are explicit stubs or disabled future commands. Some are expected by the workspace UI acceptance doc as future work, but they should not be treated as implemented V1 functionality.

## Active Worktree Note

Early in the audit, `git status --short` showed another agent's uncommitted changes in:

- `src/DXFER.Blazor/Components/DrawingCanvas.razor.css`
- `src/DXFER.Blazor/wwwroot/drawingCanvas.js`
- `tests/DXFER.Blazor.Tests/drawingCanvasSelection.test.mjs`

Those entries disappeared before final verification. Final `git status --short` showed only this audit file as untracked. This audit file is the only file intentionally added by this pass.

## Verification Run During Audit

- `Test-Path docs\dev\dxfer-architecture-audit-2026-05-06.md`: `True`.
- `git status --short`: only `?? docs/dev/dxfer-architecture-audit-2026-05-06.md`.
- `dotnet test tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --no-build`: passed, 237 passed, 0 failed.
- `node --test tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs tests\DXFER.Blazor.Tests\workbenchHotkeys.test.mjs`: passed, 112 passed, 0 failed.
- `dotnet build DXFER.slnx --no-restore`: `DXFER.Core`, `DXFER.Blazor`, and `DXFER.Core.Tests` compiled, but `DXFER.Web` failed copy steps because running process `DXFER.Web (29848)` had `DXFER.Core.dll` and `DXFER.Blazor.dll` locked. The process was not stopped because another agent is actively working.

## Architecture Findings

### A1. Solution/project split does not match the documented architecture

Docs expect separate projects for `DXFER.Core`, `DXFER.CadIO`, `DXFER.Blazor`, `DXFER.Canvas`, `DXFER.Desktop`, and `DXFER.Tests` (`docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:88-95`).

Current solution only includes:

- `src/DXFER.Blazor/DXFER.Blazor.csproj`
- `src/DXFER.Core/DXFER.Core.csproj`
- `src/DXFER.Web/DXFER.Web.csproj`
- `tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj`

Evidence: `DXFER.slnx:1-10`, `src` contains only `DXFER.Blazor`, `DXFER.Core`, and `DXFER.Web`.

Impact: CAD IO, canvas, desktop shell, and test boundaries are still prototype-local rather than isolated as described.

### A2. CAD IO is not behind a CadIO adapter project or IxMilia-based reader/writer interface

Docs recommend `IxMilia.Dxf` behind DXFER-owned reader/writer interfaces and keeping third-party IO types inside adapter projects (`docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:99-115`).

Observed:

- `src/DXFER.Core/DXFER.Core.csproj` has no package references.
- Searches for `IxMilia`, `IDxf`, `IDrawing`, `DrawingReader`, and `DrawingWriter` in `src`/`tests` had no matches.
- Current DXF IO is a static hand parser/writer inside Core: `src/DXFER.Core/IO/DxfDocumentReader.cs:7-109`, `src/DXFER.Core/IO/DxfDocumentWriter.cs:8-57`.

Impact: the IO boundary is not replaceable, unsupported data cannot be preserved reliably, and future DWG/commercial/OpenCascade/other IO adapters would force Core/UI churn.

### A3. Sidecar schema and sidecar export are absent

Docs require exporting normalized DXF plus `*.dxfer.json` sidecar (`docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:15-16`, `docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:297-298`). The sidecar should include schema version, source filename/hash, normalized filename/hash, trusted-source marker, units, bounds, normalization summary, and grain metadata (`docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:147-190`).

Observed:

- Searches for `sidecar`, `schemaVersion`, `sha256`, `trustedSource`, `OnshapeFlatPatternArtifact`, and `dxfer.json` in `src`/`tests` had no matches.
- The UI "Sync Metadata" panel serializes only file name, entity count, grain direction, and bounds: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:406-428`, displayed at `src/DXFER.Blazor/Components/DrawingWorkbench.razor:177-180`.
- Save/export writes only DXF text: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:1546-1564`.

Impact: HMI-Sync cannot validate trusted source hashes, normalized hashes, units, or grain metadata from DXFER output.

### A4. Trusted/reference document guardrails are mostly missing

Docs require DWG opening to launch an external viewer without editable geometry, reference files to be labeled reference-only, missing units/suspicious bounds warnings, and unsupported entity reporting (`docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:292-304`).

Observed:

- Header accepts `.dxf,.dwg`: `src/DXFER.Web/Components/Layout/MainLayout.razor:24-28`.
- `.dwg` input only sets a status and returns: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:436-439`.
- Searches found no document mode/trust model such as `DocumentMode`, `Trusted`, `ReferenceOnly`, source hash, suspicious bounds, or unsupported-entity warning model in application code.

Impact: the UX says DWG is an external handoff, but no actual external viewer handoff or reference-only model exists. Trusted flat-pattern workflow cannot be enforced.

### A5. Drawing document model lacks V1 metadata

Docs assign Core ownership of units, stable IDs, measurements, sidecar schema, and grain metadata (`docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:90`). They also require layer/entity warnings and units for guardrails/measurement.

Observed:

- `DrawingDocument` only stores `Entities`, `Dimensions`, and `Constraints`: `src/DXFER.Core/Documents/DrawingDocument.cs:6-48`.
- Entities carry IDs, construction state, kind, bounds, transform, and construction toggle behavior, but not layers, colors, linetypes, source metadata, units, trust mode, unsupported payload, or warning state.

Impact: document-level V1 workflows have nowhere to store required source/trust/unit/sidecar data, and IO cannot round-trip or report unsupported CAD data as designed.

### A6. Measurement service is partial against V1 measurement requirements

Docs require point distance, X/Y delta, shortest distance between supported entities, line length, arc length, polyline length, selected bounds, full-drawing bounds, units display, and precision control (`docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:238-251`). Required tests include distance, deltas, line length, arc length, and selected bounds (`docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:313-315`).

Observed:

- `MeasurementService` supports point-to-point measure and `TryMeasureEntity` for line, first polyline segment, and circle diameter only: `src/DXFER.Core/Operations/MeasurementService.cs:6-34`.
- No arc length, full polyline length, selected bounds, full bounds, entity-to-entity shortest distance, units, or precision control implementation was found.

Impact: measurement should be treated as prototype-level, not V1-complete.

### A7. Power Trim captures curves but deletes non-line targets

Docs say Power Trim should trim drag-crossed sections, support lines first then arcs/circles/polylines/polygons, and unsupported entities should fail clearly (`docs/dev/cad-polish-implementation-passes.md:128-151`).

Observed:

- JS drag-crossing targets include `line`, `polyline`, `circle`, `arc`, `ellipse`, `spline`, and `polygon`: `src/DXFER.Blazor/wwwroot/drawingCanvas.js:9398-9407`.
- Core method `TryPowerTrimOrExtendLine` removes any selected non-line entity: `src/DXFER.Core/Operations/DrawingModifyService.cs:292-313`.
- Current Core test encodes curve deletion as expected behavior: `tests/DXFER.Core.Tests/Operations/DrawingModifyServiceTests.cs:169-188`.
- Current JS test confirms curve targets are captured: `tests/DXFER.Blazor.Tests/drawingCanvasSelection.test.mjs:1900-1917`.

Impact: crossing or clicking a curve under Power Trim can delete the curve instead of trimming it or failing clearly. This is inconsistent with the design docs and is the most dangerous behavioral gap found.

### A8. PlaneGCS adapter shell exists but is not production wired

Docs for Pass 1 require a replaceable solver interface, fallback adapter, and a PlaneGCS adapter that is discoverable as optional/unavailable until WASM assets are wired (`docs/dev/cad-polish-implementation-passes.md:38-63`).

Observed:

- `ISketchSolver`, `LegacySketchSolverAdapter`, `PlaneGcsSketchSolverAdapter`, request/result/status types, and tests exist.
- `PlaneGcsSketchSolverAdapter` returns `SketchSolveStatus.Unavailable` with a diagnostic that the WASM bridge is not wired: `src/DXFER.Core/Sketching/PlaneGcsSketchSolverAdapter.cs:3-15`.
- Production workbench paths still call `SketchConstraintService` and `SketchDimensionSolverService` directly: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:658`, `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:1184`, `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:1207`, `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:1253`, `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:1299`.

Impact: this matches the "adapter shell/unavailable" slice, but real solver integration is not complete and runtime UI does not yet consume the solver abstraction.

### A9. Constraint command system is still partial

Docs require modal constraint post-selection, hover filtering by active constraint kind, selectable/deletable glyphs, and persistent constraints from creation/snap relations (`docs/dev/cad-polish-implementation-passes.md:65-92`).

Observed:

- Modal constraint tooling exists and repeats after apply: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:1214-1268`.
- `Normal` and `Symmetric` constraints are disabled future commands: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:266-267`.
- `Tangent` is exposed and listed as confirmed working: `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:260`, `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:2105-2114`.
- `SketchConstraintService.ApplyConstraintGeometry` does nothing for `SketchConstraintKind.Tangent`: `src/DXFER.Core/Sketching/SketchConstraintService.cs:83-119`.

Impact: constraint UI is further along than the initial prototype, but tangent can be created/validated without geometry solving, and advertised "confirmed working" status overstates the behavior.

### A10. Canvas module boundary plan has not been executed

Docs say `drawingCanvas.js` should remain a stable facade that imports smaller sibling modules, separating state, rendering, hit testing, snaps, viewport, interaction, tools, selection, targets, and document adapters (`docs/dev/canvas-module-boundaries.md:5-35`, `docs/dev/canvas-module-boundaries.md:37-180`).

Observed:

- Current `src/DXFER.Blazor/wwwroot/drawingCanvas.js` is still a single ES module with 9,182 lines.
- It still owns rendering, state, hit testing, snapping, target resolution, pointer interaction, power trim, selection, geometry math, viewport, and document DTO adapters in one file.

Impact: the public facade is stable, but the internal boundary split has not happened. This increases risk for concurrent agents and makes behavior-preserving changes harder.

### A11. Desktop shell is absent

Docs expect `DXFER.Desktop` as a Windows desktop shell using Blazor Hybrid/WebView for local file access and production use (`docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:94`).

Observed: no `src/DXFER.Desktop` project exists and the solution hosts a web app prototype instead.

Impact: production local-file workflow remains a future architecture step.

## Explicit Stubs And Disabled/Future Features

| Area | Current evidence | Status |
| --- | --- | --- |
| Text tool | `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:222` | Disabled future command |
| Normal constraint | `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:266` | Disabled future command |
| Symmetric constraint | `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:267` | Disabled future command |
| Remove duplicates | `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:280`, `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:614-615`, `src/DXFER.Web/Components/Layout/MainLayout.razor:83` | Disabled/planned, not implemented |
| Command parameter distance/angle inputs | `src/DXFER.Blazor/Components/DrawingWorkbench.razor:69-80` | Visible but disabled |
| Panels menu | `src/DXFER.Web/Components/Layout/MainLayout.razor:55` | Disabled |
| Units setting | `src/DXFER.Web/Components/Layout/MainLayout.razor:96` | Disabled; no document units model found |
| Polar snap setting | `src/DXFER.Web/Components/Layout/MainLayout.razor:97` | Disabled |
| Theme setting | `src/DXFER.Web/Components/Layout/MainLayout.razor:98` | Disabled |
| Default unimplemented command status | `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:617-625` | Catch-all stub path |
| PlaneGCS solve | `src/DXFER.Core/Sketching/PlaneGcsSketchSolverAdapter.cs:3-15` | Adapter intentionally unavailable |
| Tangent constraint solve | `src/DXFER.Core/Sketching/SketchConstraintService.cs:107-108` | Validation exists, geometry application empty |
| Auto Orient Bounds | Design stretch at `docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:228-236`; no implementation found | Not implemented, documented stretch |
| Sidecar export | Design requires sidecar at `docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:147-190`; no implementation found | Missing |
| DWG external viewer handoff | Design requires viewer launch at `docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:292`; current code only status/return at `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs:436-439` | Partial UI-only behavior |

## Test Coverage Gaps Against Docs

The required V1 test list includes transform/bounds, Auto Orient when implemented, grain metadata export choices, measurement coverage, Onshape DXF fixtures, DXF round-trip, sidecar schema, and guardrails (`docs/superpowers/specs/2026-05-03-dxfer-v1-design.md:306-318`).

Observed gaps:

- No sidecar schema/hash/trusted-source tests were found.
- No guardrail tests for reference-only DWG or unsupported entity warnings were found.
- No Onshape fixture-based DXF IO tests were found.
- Measurement tests do not cover the full V1 set called out by the docs.
- Existing Power Trim tests currently lock in non-line deletion behavior, which conflicts with "unsupported entities fail with a clear status."

## Priority Recommendations

1. Stop treating curve Power Trim deletion as acceptable behavior. Unsupported curve trim requests should leave the document unchanged and return a clear status until real curve splitting exists.
2. Add the missing sidecar model/export path and tests before wiring DXFER output into HMI-Sync.
3. Establish the document metadata model for units, source hash, trust/reference mode, warnings, and unsupported entity accounting.
4. Move CAD IO behind DXFER-owned interfaces and a `DXFER.CadIO` boundary before adding richer DXF/DWG behavior.
5. Decide whether the canvas module split is a hard architecture requirement for V1 or an internal cleanup plan, then schedule it before more agents add behavior to the monolithic file.
6. Reconcile UI "confirmed working" labels with actual solver behavior, especially Tangent.

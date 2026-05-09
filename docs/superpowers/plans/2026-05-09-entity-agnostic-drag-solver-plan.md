# Entity-Agnostic Drag Solver Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace rectangle-only drag behavior with a staged, constraint-driven drag system that applies uniformly to every applicable entity and grip.

**Architecture:** Keep the current public drag entry points, but route each drag through a common candidate-evaluation pipeline: parse the selected grip, generate entity-specific edit and translation candidates, propagate coincident constraints, validate constraints and driving dimensions, then commit the first valid candidate. The canvas preview must mirror the same candidate order so live drag never shows a solve that the server rejects.

**Tech Stack:** C# `DXFER.Core` sketch services, Blazor canvas interop in `drawingCanvas.js`, Node `node:test` canvas tests, xUnit core tests, and running-app browser/canvas verification.

---

## Ground Rules

- Do not add new rectangle-only fixes for behavior that follows from constraints, dimensions, or grip degrees of freedom.
- A rectangle remains just a line group with coincident vertices, orthogonal constraints, and dimensions.
- Each stage must include a focused failing test, implementation, focused passing tests, solution build, and running-app/canvas verification before the tracking row moves forward.
- Keep `docs/dev/bug-feature-log.md`, `docs/dev/user-test-tracking.md`, and `docs/dev/entity-drag-constraint-matrix.md` current after every stage.

## File Map

- Modify `STARTUP.md` when the standing agent rule changes.
- Modify `docs/dev/bug-feature-log.md` and `docs/dev/user-test-tracking.md` for live user status.
- Modify `docs/dev/entity-drag-constraint-matrix.md` for entity/grip coverage.
- Modify `src/DXFER.Core/Sketching/SketchGeometryDragService.cs` for server-side drag candidate evaluation.
- Modify `src/DXFER.Core/Sketching/SketchConstraintPropagationService.cs` only when welded/coincident propagation needs a general solver rule.
- Modify `src/DXFER.Core/Sketching/SketchDimensionSolverService.cs` only when dimension satisfaction or overconstraint diagnostics are wrong.
- Modify `src/DXFER.Blazor/wwwroot/drawingCanvas.js` for canvas preview parity.
- Modify `src/DXFER.Blazor/Components/DrawingCanvas.razor.cs` only when the canvas module cache key needs a bump.
- Add or update tests in `tests/DXFER.Core.Tests/Sketching/SketchGeometryDragServiceTests.cs`.
- Add or update tests in `tests/DXFER.Core.Tests/Sketching/SketchDimensionSolverServiceTests.cs` when dimension state is involved.
- Add or update tests in `tests/DXFER.Blazor.Tests/drawingCanvasSelection.test.mjs`.

## Stage 0: Tracking And Baseline

- [x] Add UT-SOLVER-005 to the bug log and user-test tracker.
- [x] Add the standing constraint-driven drag rule to `STARTUP.md`.
- [x] Add `docs/dev/entity-drag-constraint-matrix.md`.
- [x] Run the current focused solver/canvas tests once before the first code refactor:
  - `dotnet test .\tests\DXFER.Core.Tests\DXFER.Core.Tests.csproj --no-restore --filter "FullyQualifiedName~SketchGeometryDragServiceTests|FullyQualifiedName~SketchDimensionSolverServiceTests"`
  - `node --test .\tests\DXFER.Blazor.Tests\drawingCanvasSelection.test.mjs`
  - Evidence: core focused tests passed 57/57 and canvas selection tests passed 169/169 before the first UT-SOLVER-005 code change.

## Stage 1: Core Candidate Evaluator

- [ ] Add a small internal candidate helper in `SketchGeometryDragService.cs` that takes the original document, a mutated entity array, candidate dimension anchors, and a status string, then returns a validated document only if constraints and driving dimensions remain satisfied.
- [ ] Replace ad hoc post-mutation dimension validation in `TryApplyDrag` with the candidate helper.
- [ ] Keep existing rectangle paths temporarily, but make them call the same validation helper instead of doing private validation logic.
- [x] Add `DRAG-LINE-001` as a failing core test: dragging a length-dimensioned line endpoint in the length-changing direction must not break the dimension or leave red stale state.
- [x] Implement the minimal fallback candidate: if an edit candidate violates a driving dimension, try translating the selected entity by the blocked drag component and move attached dimension anchors.
- [x] Run focused core tests and record pass counts in the bug log.
- [x] Run the rebuilt app and canvas-test the `DRAG-LINE-001` path before marking this stage beyond `CODED`.
  - Evidence: focused core `DraggingDimensionedLineEndpointTranslatesLineWhenLengthWouldChange` passed 1/1; focused canvas preview line/whole-ellipse/whole-polygon tests passed; broader core drag/dimension tests passed 57/57; full `drawingCanvasSelection.test.mjs` passed 170/170; `DXFER.slnx` built cleanly after stopping locked PID `27136`; cache-busted rebuilt app on `127.0.0.1:5119` verified the line endpoint drag translated the line and dimension anchor while keeping dimension value `10`.

## Stage 2: Canvas Preview Candidate Parity

- [x] Add the same candidate order to `applyGeometryDragPreview`: edit candidate first, then dimension-preserving translation candidate, then stable failure.
- [x] Add `DRAG-LINE-001` preview coverage to `drawingCanvasSelection.test.mjs`.
- [x] Verify preview and release agree for the same line endpoint drag in the running app.
- [x] Keep `previewDrivingDimensionsRemainSatisfied` as the preview guard until the canvas module is split enough for a smaller helper.

## Stage 3: Coincident Weld Generalization

- [ ] Extend the matrix with mixed coincident pairs: line-line, line-polyline, polyline-polyline, line-arc endpoint, arc-arc endpoint, spline endpoint-line, and point-entity point.
- [ ] Add core and preview tests for at least one mixed-entity coincident endpoint pair before implementation.
- [ ] Generalize `propagatePreviewCoincidentConstraints` and `SketchConstraintPropagationService.PropagateFromChanges` only where the failing mixed case proves a gap.
- [ ] App-test a visible mixed-entity welded drag before marking the row `APP-VERIFIED`.

## Stage 4: Entity Grip Rows

- [ ] Lines: endpoint, midpoint, whole-line, dimensioned endpoint, and coincident endpoint.
- [ ] Polylines: vertex, segment midpoint, segment, whole polyline, dimensioned segment, and coincident vertex.
- [ ] Polygons: center, midpoint, whole polygon, radius/apothem edit where supported, and dimensioned radius/count behavior.
- [ ] Circles: center translation, circumference radius edit, dimensioned circumference fallback or stable failure, and coincident center.
- [ ] Arcs: center/whole translation, endpoint sweep/radius edits, dimensioned radius/sweep behavior, and coincident endpoints.
- [ ] Ellipses: center translation, major quadrant, minor quadrant, dimensioned axis behavior, and hover/edit parity.
- [ ] Splines: fit point, control point, endpoint tangent handle, whole spline, dimensioned handle behavior, and coincident endpoints.
- [ ] Points: point translation, point dimensions, and coincident references.
- [ ] Dimensions: anchor drag, value edit, deletion cleanup, and no synthetic value recommit after anchor drag.
- [ ] Constraint glyph/reference: selection and drag behavior where supported; otherwise document stable unsupported behavior.

## Stage 5: Diagnostics And Red State

- [ ] Add a focused overconstraint or unsatisfied-dimension test for a non-rectangle entity.
- [ ] Confirm `CanvasDocumentDto` exposes affected references for the failed entity, dimension, and constraint.
- [ ] Confirm the running canvas paints affected geometry/glyphs/dimensions red without moving unrelated geometry.

## Stage 6: Matrix Verification Pass

- [ ] Run all focused core drag/dimension tests.
- [ ] Run all focused canvas drag/selection tests.
- [ ] Run `dotnet build .\DXFER.slnx --no-restore`.
- [ ] Restart the app and complete the matrix rows in `docs/dev/entity-drag-constraint-matrix.md`.
- [ ] Update `docs/dev/user-test-tracking.md` with only Codex statuses; leave `USER-FAILED` until the user reports pass.

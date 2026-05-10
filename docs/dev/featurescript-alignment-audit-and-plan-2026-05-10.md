# FeatureScript Alignment Audit And Plan - 2026-05-10

Scope: audit DXFER against the local Onshape FeatureScript Standard Library reference corpus for sketch entities, solving, constraints, dimensions, and tool workflows. This is an audit and planning pass only; no source implementation changes are included.

FeatureScript corpus state: `artifacts/featurescript-std/manifest.json` exists and was generated at `2026-05-09T21:52:41.1292243+00:00`. I did not run `scripts/Sync-FeatureScriptStd.ps1` because the local corpus was present and fresh for this pass.

## Executive Summary

DXFER is now much closer to a parametric 2D sketch editor than the May 6 architecture audit snapshot. Current source includes point, line, circle, arc, ellipse/elliptical arc, polyline, polygon, and spline entities; persistent sketch constraints and dimensions; sidecar metadata writing; a `DXFER.CadIO` project; split-at-point and broad Power Trim coverage; construction geometry; dimension overlays; constraint glyphs; drag/edit workflows; and a local FeatureScript std indexer.

The highest architecture gap is not entity coverage anymore. It is the runtime solve boundary. FeatureScript has a clear lifecycle: create sketch, add entities, optionally set initial guesses, add constraints/dimensions, call `skSolve`, then consume diagnostics/results. DXFER has `ISketchSolver`, `SketchSolveRequest`, `SketchSolveResult`, `LegacySketchSolverAdapter`, and a PlaneGCS unavailable adapter, but the live workbench still calls `SketchConstraintService`, `SketchDimensionSolverService`, and `SketchGeometryDragService` directly. That leaves solving behavior spread across UI and ad hoc services instead of a single DXFER-owned solver contract.

The strongest semantic gaps against FeatureScript are:

- DXFER has no initial-guess model in `SketchSolveRequest`.
- `SketchSolveStatus` has only `Solved`, `Unavailable`, and `Failed`; there is no first-class underconstrained, overconstrained, unsupported, or invalid-input status.
- DXFER supports a useful subset of `ConstraintType`, but FeatureScript exposes a broader vocabulary including mirror, projected, offset, pattern, pierce, major/minor diameter, quadrant, rho, equal curvature, bezier degree, freeze, and centerline dimension.
- Tangent is validated and created, but `SketchConstraintService.ApplyConstraintGeometry` does not solve tangent geometry.
- Bezier and conic are represented through `SplineEntity` rather than typed Bezier/conic entities or typed submodes.
- Rectangle generation aligns with FeatureScript's "generated line segments plus constraints" pattern, while regular polygon differs: FeatureScript `skRegularPolygon` generates an unconstrained polyline, but DXFER keeps a parametric `PolygonEntity` with radius/count semantics.
- Elliptical arc is represented as `EllipseEntity` with start/end parameters, not as a distinct `EllipticalArcEntity`.
- Runtime failure handling is improving, but unsupported or approximate workflows still need explicit safety gates, especially where exact curve math is not yet available.

The plan below prioritizes safety and architecture: unsupported geometry must fail clearly and leave documents unchanged, and all runtime solving should route through DXFER-owned solver request/result contracts before more tool math is added.

## FeatureScript Reference Map

Primary local reference files used:

| File | What it contributed |
| --- | --- |
| `artifacts/featurescript-std/onshape-std-library-mirror/sketch.fs` | Sketch lifecycle, entity constructor vocabulary, constraint wrapper, rectangle/polyline/polygon generation patterns, builtin delegation boundary. |
| `artifacts/featurescript-std/onshape-std-library-mirror/constrainttype.gen.fs` | Complete `ConstraintType` enum used by `skConstraint`. |
| `artifacts/featurescript-std/onshape-std-library-mirror/dimensionalignment.gen.fs` | Dimension display/alignment vocabulary re-exported by `sketch.fs`. |
| `artifacts/featurescript-std/onshape-std-library-mirror/dimensionhalfspace.gen.fs` | Dimension side/half-space vocabulary re-exported by `sketch.fs`. |
| `artifacts/featurescript-std/onshape-std-library-mirror/radiusdisplay.gen.fs` | Radius/diameter display vocabulary re-exported by `sketch.fs`. |
| `artifacts/featurescript-std/onshape-std-library-mirror/fixedparameterposition.gen.fs` | Fixed parameter positioning vocabulary re-exported by `sketch.fs`. |
| `artifacts/featurescript-std/onshape-std-library-mirror/sketchtooltype.gen.fs` | Tool-type vocabulary re-exported by `sketch.fs`. |
| `artifacts/featurescript-std/onshape-std-library-mirror/sketchsilhouettedisambiguation.gen.fs` | Projection/silhouette sketch vocabulary re-exported by `sketch.fs`. |
| `docs/dev/featurescript-std-reference.md` | DXFER guidance for using the std corpus as a semantic reference, not as vendored runtime code. |
| `THIRD_PARTY_NOTICES.md` | MIT attribution and non-vendoring constraints for the FeatureScript std corpus. |

`sketch.fs` exports used as the main comparison surface:

- Lifecycle: `newSketch`, `newSketchOnPlane`, `skSetInitialGuess`, `skSolve`.
- Primitive entities: `skPoint`, `skLineSegment`, `skCircle`, `skEllipse`, `skArc`, `skEllipticalArc`.
- Curve entities: `skSpline`, `skSplineSegment`, `skInterpolatedSpline`, `skInterpolatedSplineSegment`, `skFitSpline`, `skBezier`, `skConicSegment`.
- Generated/composite entities: `skRectangle`, `skRegularPolygon`, `skPolyline`.
- Constraints/dimensions: `skConstraint` with `ConstraintType`, optional `length`, and optional `angle`.
- Additional exports/re-exports: `Sketch`, `DimensionDirection`, `SketchProjectionType`, `DimensionAlignment`, `DimensionHalfSpace`, `RadiusDisplay`, `FixedParameterPosition`, `SketchToolType`, and `SketchSilhouetteDisambiguation`.

Builtin calls and portability boundary:

- `sketch.fs` delegates `skSolve` to `@skSolve`.
- `sketch.fs` delegates `skSetInitialGuess` to `@skSetInitialGuess`.
- `sketch.fs` delegates `skConstraint` to `@skConstraint`.
- Most primitive constructors delegate to builtins, including `@skPoint`, `@skLineSegment`, `@skCircle`, `@skEllipse`, `@skArc`, `@skEllipticalArc`, `@skSpline`, `@skFitSpline`, `@skBezier`, and `@skConicSegment`.
- `skRectangle`, `skRegularPolygon`, and `skPolyline` are useful semantic patterns because they show generated entity/constraint shape in visible FeatureScript code. The actual solver and many geometry constructors remain Onshape runtime behavior, not portable implementation code.

Constraint vocabulary from `constrainttype.gen.fs`:

`NONE`, `COINCIDENT`, `PARALLEL`, `VERTICAL`, `HORIZONTAL`, `PERPENDICULAR`, `CONCENTRIC`, `MIRROR`, `MIDPOINT`, `TANGENT`, `EQUAL`, `LENGTH`, `DISTANCE`, `ANGLE`, `RADIUS`, `NORMAL`, `FIX`, `PROJECTED`, `OFFSET`, `CIRCULAR_PATTERN`, `PIERCE`, `LINEAR_PATTERN`, `MAJOR_DIAMETER`, `MINOR_DIAMETER`, `QUADRANT`, `DIAMETER`, `SILHOUETTED`, `CENTERLINE_DIMENSION`, `INTERSECTED`, `RHO`, `EQUAL_CURVATURE`, `BEZIER_DEGREE`, `FREEZE`.

## DXFER Current-State Inventory

### Exists

- FeatureScript reference tooling:
  - `scripts/Sync-FeatureScriptStd.ps1`
  - `tools/DXFER.FeatureScriptStd`
  - `src/DXFER.Core/References/FeatureScript/*`
  - `tests/DXFER.Core.Tests/References/FeatureScriptStdIndexerTests.cs`
- Entity model:
  - `PointEntity`, `LineEntity`, `CircleEntity`, `ArcEntity`, `EllipseEntity`, `PolylineEntity`, `PolygonEntity`, `SplineEntity`.
  - Construction flag on `DrawingEntity`.
  - Stable `EntityId`.
  - Transform and bounds support on entities.
- Document model:
  - `DrawingDocument` holds entities, sketch dimensions, sketch constraints, and `DrawingDocumentMetadata`.
  - Metadata includes source/normalized names and hashes, units, mode, trusted-source flag, warnings, and unsupported entity counts.
- Cad IO boundary:
  - `src/DXFER.CadIO` exists.
  - Core no longer owns `DxfDocumentReader` or `DxfDocumentWriter`.
  - Reader/writer support DXF lines, circles, arcs, points, lightweight/classic polylines, splines, and ellipses.
- Sidecar:
  - `DxferSidecarWriter` writes schema version, source/normalized file references, units, mode, trusted source, bounds, normalization summary, warnings, and unsupported entity counts.
  - Save flow downloads DXF plus `.dxfer.json`.
- Constraints:
  - `SketchConstraintKind`: coincident, horizontal, vertical, parallel, perpendicular, tangent, concentric, equal, midpoint, symmetric, fix.
  - Constraint states: unknown, satisfied, unsatisfied, suppressed.
  - Constraint glyph rendering and selection exist in canvas tests.
  - Creation factories add constraints for rectangles, slots, insertion snaps, midpoint/perpendicular line insertion, and tangent arc insertion.
- Dimensions:
  - `SketchDimensionKind`: linear distance, horizontal distance, vertical distance, point-to-line distance, radius, diameter, angle, count.
  - Driving dimensions can mutate geometry.
  - Dimension overlays, editing, parsing, and persistent canvas state have tests.
- Solver contracts:
  - `ISketchSolver`, `SketchSolveRequest`, `SketchSolveResult`, diagnostics, `LegacySketchSolverAdapter`, and `PlaneGcsSketchSolverAdapter`.
  - Legacy adapter returns failed status when unsatisfied constraints/dimensions remain.
  - PlaneGCS adapter is isolated and reports unavailable.
- Tools:
  - Draw tools for line, midpoint line, rectangle variants, circle variants, ellipse, arcs, elliptical arc, conic, polygons, spline, Bezier, point, and slot.
  - Modify tools include translate, rotate, scale, mirror, offset, fillet/chamfer for lines, linear/circular patterns, split at point, add spline point, construction toggle, and Power Trim.
  - Canvas supports snapping, acquired inference points, tangent snap markers, selection, hover, drag preview, dimension input, and constraint glyph groups.
- Measurement:
  - Point-to-point delta/distance.
  - Entity path length/bounds for lines, polylines, arcs, ellipses, polygons, splines, and points.
  - Shortest-distance measurements for point/line/circle exact cases and sampled curve fallback.

### Partial

- Runtime solving:
  - Solver abstraction exists, but `DrawingWorkbench.razor.cs` still calls `SketchConstraintService.ApplyConstraint`, `SketchDimensionSolverService.ApplyDimension`, and `SketchGeometryDragService.TryApplyDrag` directly.
  - Solve lifecycle is service-by-service, not `SketchSolveRequest -> ISketchSolver -> SketchSolveResult`.
- Initial guesses:
  - FeatureScript exposes `skSetInitialGuess`.
  - DXFER tool previews, drag start/end data, and existing geometry can serve as guesses, but no first-class initial-guess map exists in `SketchSolveRequest`.
- Solve status:
  - DXFER status is `Solved`, `Unavailable`, `Failed`.
  - Underconstrained, overconstrained, unsupported, and invalid input are inferred through unsatisfied items or status strings, not first-class result states.
- Constraint parity:
  - DXFER covers the practical early subset.
  - Tangent can be created and validated but is not solved by `ApplyConstraintGeometry`.
  - Symmetric exists in enum/tool command vocabulary but is disabled in the UI and not applied in the service.
  - Normal, pierce, curvature, and other FeatureScript constraints are command placeholders or absent from the core enum.
- Entity parity:
  - Elliptical arc is encoded as `EllipseEntity` with parameter start/end.
  - Bezier and conic are encoded as `SplineEntity` with degree/control points.
  - Rectangle is encoded as four lines plus constraints.
  - Regular polygon is a parametric `PolygonEntity`, not an unconstrained generated polyline.
  - No text/image sketch entity support.
- Curve operations:
  - Power Trim covers lines, polylines, polygons, circles, arcs, ellipses, splines, and point removal.
  - Some spline trim output is converted to degree-1 sampled spline segments, which is useful but not exact FeatureScript/Onshape spline semantics.
- Canvas architecture:
  - Some modules were extracted under `wwwroot/canvas`, but `drawingCanvas.js` remains large at 10,672 lines and still owns much high-frequency behavior.
- CAD IO:
  - The reader/writer are isolated in `DXFER.CadIO`, but they are still hand-rolled ASCII DXF readers/writers rather than IxMilia-backed adapters.
  - Unsupported entities are counted and warned, but not preserved as raw round-trip payloads.

### Missing

- A single runtime sketch lifecycle entrypoint equivalent to FeatureScript's create/add/guess/constrain/dimension/solve flow.
- First-class initial guesses in solver requests.
- First-class underconstrained/overconstrained/unsupported solve statuses.
- A solver capability matrix that determines which entity/constraint/dimension combinations are supported by each adapter.
- Tangent geometry solving.
- Explicit supported/unsupported behavior tests for every FeatureScript `ConstraintType` that DXFER chooses not to implement yet.
- Separate typed Bezier/conic/elliptical-arc semantics or a documented subtype contract inside `SplineEntity`/`EllipseEntity`.
- Text and image sketch entities.
- FeatureScript-style major/minor diameter, rho, equal curvature, Bezier degree, quadrant, offset/projected/pattern/freeze constraint handling.
- Exact curve split behavior for every spline/conic case.
- User-facing solve diagnostics grouped by affected sketch references for all tool workflows.
- Round-trip preservation of unsupported DXF payloads.

## Gap Table

| Area | FeatureScript behavior/reference | DXFER behavior | Risk | Recommended fix | Suggested milestone |
| --- | --- | --- | --- | --- | --- |
| Sketch lifecycle | `newSketch`/`newSketchOnPlane`, add entities, optional `skSetInitialGuess`, `skConstraint`, `skSolve`. | Documents are mutated directly by workbench services; `ISketchSolver` exists but is not the runtime path. | Solver behavior spreads across UI, making failures hard to diagnose and adapters hard to swap. | Route all runtime sketch mutations through `ISketchSolver` with a complete request/result contract. | M1, M3 |
| Initial guesses | `skSetInitialGuess` accepts entity-dependent guess data before solve. | No first-class guess model; drag/tool preview data is not captured in `SketchSolveRequest`. | Future solver adapters lose the best available starting points and may become unstable. | Add `SketchInitialGuess` data keyed by entity/reference and pass it through the solver contract. | M1, M3 |
| Solve status | `@skSolve` reports runtime solve/failure behavior through Onshape internals. | `Solved`, `Unavailable`, `Failed` only. | Underconstrained/overconstrained/unsupported states collapse into generic failure or UI text. | Add first-class `Underconstrained`, `Overconstrained`, `Unsupported`, and `InvalidInput` statuses plus diagnostics. | M1 |
| Builtin dependency boundary | `@skSolve`, `@skConstraint`, and constructors are Onshape builtins. | DXFER has local C# services and a PlaneGCS placeholder. | Copying visible std code would not provide the solver. | Treat FeatureScript as vocabulary/semantics only; keep implementations behind DXFER adapters. | M1 |
| Point entity | `skPoint` creates a point body after solve. | `PointEntity` exists, is drawable/selectable/snappable, and can be dragged. | Low. | Keep point references explicit and route point edits through solver request diagnostics. | M2, M3 |
| Line entity | `skLineSegment` with start/end and construction flag. | `LineEntity` matches start/end/construction and is well covered. | Low. | Preserve current model; use it as canonical reference shape in solver requests. | M2 |
| Circle entity | `skCircle` with center/radius/construction and center point. | `CircleEntity` has center/radius/construction; center references exist. | Low. | Add radius bounds/capability diagnostics rather than silently accepting invalid radii. | M2, M5 |
| Arc entity | `skArc` is three-point creation semantics. | `ArcEntity` stores center/radius/start/end angles; tools include three-point and center-point arcs. | Medium: input model differs from FeatureScript creation semantics. | Keep stored center/radius model but document creation conversion and add regression tests for three-point degeneracy. | M2, M6 |
| Ellipse and elliptical arc | `skEllipse` uses center, major/minor radii, major axis; `skEllipticalArc` adds start/end parameters. | `EllipseEntity` stores center, major-axis endpoint, minor-radius ratio, start/end parameters. Full ellipse and elliptical arc share one type. | Medium: type name and parameter interpretation can drift. | Document `EllipseEntity` as ellipse/elliptical arc carrier; add explicit DTO and solver tests for partial sweeps. | M2, M5 |
| Spline | `skFitSpline`, `skInterpolatedSpline`, `skSpline`, and segment variants rely on fit/control/guess data. | `SplineEntity` supports control splines and fit-point splines with tangent handles and sampled evaluation. | Medium: exact spline solving/splitting is only partially modeled. | Add spline mode metadata and capability flags for exact vs sampled operations. | M2, M7 |
| Bezier | `skBezier` is a named constructor. | Bezier tool creates a cubic `SplineEntity`; no Bezier subtype. | Medium: Bezier degree constraints and control handle behavior are not explicit. | Add `SplineKind` or typed `BezierEntity` contract; mark `BEZIER_DEGREE` unsupported until implemented. | M2, M4 |
| Conic | `skConicSegment` has start/control/end/rho and optional fixed rho. | Conic tool creates a degree-2 `SplineEntity`; no rho model. | High for conic edit/solve parity. | Either add `ConicEntity` with rho/fixed-rho or disable conic as unsupported beyond visual prototype. | M2, M4 |
| Rectangle | `skRectangle` creates four line segments plus coincident, parallel, vertical, and horizontal constraints. | Rectangle tools create four lines plus constraints; aligned rectangle omits global axes. | Low to medium. | Keep generated-line model; verify exact constraint set and no geometry movement after creation. | M6 |
| Regular polygon | `skRegularPolygon` computes vertices and calls unconstrained `skPolyline`. | DXFER uses parametric `PolygonEntity` with center/radius/side count/circumscribed. | Medium: DXFER is richer but less FeatureScript-like. | Decide whether export/solve treats polygon as parametric entity or generated constrained segments. | M2, M7 |
| Polyline | `skPolyline` creates line segments, optionally constrained at joints and closure. | `PolylineEntity` is a single vertex list; split/trim can emit polyline or line segments. | Medium: references to individual segments are synthetic. | Add formal segment reference contract and optional joint constraints for creation/import. | M2, M6 |
| Constraint enum coverage | `ConstraintType` includes 34 values. | DXFER core enum has 11 values; disabled/future commands cover some more. | High if UI suggests unsupported constraints are real. | Maintain explicit supported/unsupported matrix with tests and clear UI disabled states. | M4 |
| Coincident | `ConstraintType.COINCIDENT`. | Supported for points/endpoints and propagation. | Medium for non-line/curve points. | Extend point reference resolver to cover all supported curve handles and diagnostics. | M4 |
| Horizontal/Vertical | `ConstraintType.HORIZONTAL`, `VERTICAL`. | Supported for lines and point pairs; used in rectangle creation. | Low. | Route through solver adapter and add fixed-reference conflict diagnostics. | M3, M4 |
| Parallel/Perpendicular | `ConstraintType.PARALLEL`, `PERPENDICULAR`. | Supported for lines and rectangle/group propagation. | Medium. | Add adapter-level overconstraint reporting and line-pair diagnostics. | M3, M4 |
| Tangent | `ConstraintType.TANGENT`. | Creation/validation exists for line-circle-like and circle-like pairs; application does not move geometry. | High. | Either implement tangent solving in solver adapter or mark tangent as validation-only/unsupported when not already satisfied. | M4 |
| Concentric | `ConstraintType.CONCENTRIC`. | Applies to circle-like entities including polygon radius carrier. | Medium. | Verify arc/ellipse/polygon semantics and unsupported combinations with diagnostics. | M4 |
| Equal | `ConstraintType.EQUAL`. | Applies line length and circle-like radii. | Medium. | Add entity-pair capability tests and unsupported diagnostics for splines/ellipses/conics. | M4 |
| Fix | `ConstraintType.FIX`. | Supported through `SketchFixedReferences`. | Medium. | Surface fixed-reference conflicts as `Overconstrained` or `Failed` with affected refs. | M1, M4 |
| Distance dimensions | `LENGTH`, `DISTANCE`; `skConstraint` accepts optional `length`. | DXFER has linear, horizontal, vertical, point-to-line distance and line length behavior. | Medium. | Unify dimension kinds with FeatureScript names while keeping UX-friendly labels. | M5 |
| Radius/Diameter dimensions | `RADIUS`, `DIAMETER`, `MAJOR_DIAMETER`, `MINOR_DIAMETER`. | Radius/diameter for circle-like and polygon; ellipse axis dimensions are represented as linear axis dimensions. | Medium. | Add explicit major/minor diameter dimensions or document current linear-axis mapping. | M5 |
| Angle dimensions | `ANGLE`; FeatureScript constrains angles through builtin solve. | DXFER handles line-line angle and arc sweep; overconstraint detection is partial. | Medium. | Add solver-contract diagnostics for fixed lines/rectangle conflicts and arc sweep bounds. | M5 |
| Rho/conic dimension | `RHO` and conic `fixedRho`. | No rho model. | High if conic remains enabled. | Add conic rho support or disable conic editing with unchanged-document failures. | M2, M4 |
| Diagnostics | FeatureScript builtin solver reports failures through Onshape feature errors/solve behavior. | DXFER has unsatisfied states and affected reference keys for canvas, but not all workflows use one solve result. | High. | Centralize diagnostics in `SketchSolveResult` and display them consistently. | M1, M3 |
| Drag/edit | Onshape sketch solve updates constrained geometry after edits. | DXFER has a large `SketchGeometryDragService` with local rules. | High as constraints grow. | Treat drag as a solve request with initial guesses and adapter diagnostics. | M3, M6 |
| Snapping | FeatureScript API itself is not UI snap behavior, but sketch references imply endpoints/centers/curve handles. | Canvas supports endpoint, midpoint, center, intersection/inference, tangent snaps, and acquired points. | Medium. | Freeze snap reference key contracts and test each supported entity. | M2, M6 |
| Construction geometry | Sketch entity constructors accept `construction` on many entities. | `DrawingEntity.IsConstruction` exists and writer skips construction geometry. | Low. | Add sidecar/export checks that construction stays non-manufacturing. | M6, M8 |
| Unsupported entity behavior | FeatureScript unsupported behavior is runtime/builtin; DXFER policy says fail clearly and leave document unchanged. | IO warnings exist; unknown runtime sketch combinations often return false/status, but support matrix is not explicit. | High. | Add capability checks before mutation and regression tests proving unchanged documents. | M4, M7 |
| Power Trim | FeatureScript does not provide portable trim implementation; semantics come from CAD expectations. | DXFER supports many curve targets, but some spline outputs are sampled degree-1 splines. | Medium to high. | Keep exactness/capability labels; fail unchanged for unsupported exact split cases. | M7 |
| CAD IO | FeatureScript sketch bodies become wires/surfaces/points after solve. | DXFER reads/writes DXF entities but does not preserve unsupported raw payloads. | Medium for trusted manufacturing workflows. | Add adapter preservation or explicit irreversible-warning gate. | M8 |
| Canvas architecture | FeatureScript is not a UI architecture reference; DXFER docs call for split modules. | Some modules exist, but `drawingCanvas.js` remains 10,672 lines. | Medium. | Continue behavior-preserving split around solver/tool contracts. | M6, M9 |

## Milestone Plan

Each milestone is independently testable and has a user testing gate. Do not start later milestones by weakening earlier safety behavior. If a feature cannot be made safe, defer it and keep the document unchanged on unsupported input.

### M1 - Solver Contract And Diagnostics Baseline

Objective:

Create the DXFER-owned solver contract that all future runtime solving must use. Extend `SketchSolveRequest` and `SketchSolveResult` so they can represent initial guesses, capability failures, underconstrained/overconstrained states, unsupported combinations, and affected references.

Code areas likely affected:

- `src/DXFER.Core/Sketching/ISketchSolver.cs`
- `src/DXFER.Core/Sketching/SketchSolveRequest.cs`
- `src/DXFER.Core/Sketching/SketchSolveResult.cs`
- `src/DXFER.Core/Sketching/SketchSolveStatus.cs`
- `src/DXFER.Core/Sketching/LegacySketchSolverAdapter.cs`
- `src/DXFER.Core/Sketching/PlaneGcsSketchSolverAdapter.cs`
- `src/DXFER.Blazor/Interop/CanvasDocumentDto.cs`
- `tests/DXFER.Core.Tests/Sketching/SketchSolverAbstractionTests.cs`
- `tests/DXFER.Core.Tests/References/FeatureScriptStdIndexerTests.cs`

Automated tests to add/update:

- `SketchSolveRequest` carries entities, constraints, dimensions, fixed refs, and initial guesses without leaking UI types.
- `SketchSolveResult` distinguishes `Solved`, `Failed`, `Unavailable`, `Unsupported`, `InvalidInput`, `Underconstrained`, and `Overconstrained`.
- Legacy adapter reports overconstrained fixed-distance and fixed-angle conflicts with affected reference keys.
- PlaneGCS adapter still reports unavailable and preserves the input document unchanged.
- FeatureScript manifest test asserts `sketch.fs` still exposes `skSetInitialGuess`, `skSolve`, and `skConstraint`.

User testing gate:

The user must see clear solver status and diagnostics in the workbench for one solvable case, one unsupported case, and one overconstrained case.

Exact user actions to test:

1. Open DXFER and load the sample drawing.
2. Draw a line and add a length dimension; edit the dimension to a valid value.
3. Fix both endpoints of another line and try to change its length dimension.
4. Try to apply a constraint that is not supported by the current solver capability matrix.
5. Confirm the status panel names the result and highlights affected geometry where applicable.

Edge cases to test:

- Empty document solve.
- Missing reference key.
- Duplicate constraint IDs.
- Fixed whole entity vs fixed endpoint.
- Suppressed constraints ignored by conflict detection.

Pass/fail criteria:

- Pass: all solver requests go through the contract, diagnostics identify affected refs, unsupported/overconstrained inputs leave the document unchanged.
- Fail: a failed solve mutates entities, UI receives only a generic string, or unsupported cases silently no-op without a diagnostic.

Rollback or defer criteria:

- If result-status expansion creates too much UI churn, keep the expanded core contract and defer richer canvas highlighting.
- If a solver adapter cannot classify underconstrained vs overconstrained reliably, return `Failed` with a specific diagnostic and do not claim that status is supported.

### M2 - Entity And Reference Contract Alignment

Objective:

Make DXFER's entity and reference vocabulary intentionally align with FeatureScript sketch entities while preserving useful DXFER-specific types. Document and test which FeatureScript constructors map to native entities, generated entities, or unsupported/deferred carriers.

Code areas likely affected:

- `src/DXFER.Core/Documents/*Entity.cs`
- `src/DXFER.Core/Sketching/SketchReference.cs`
- `src/DXFER.Core/Sketching/SketchReferenceResolver.cs`
- `src/DXFER.Core/Sketching/SketchGeometryEditor.cs`
- `src/DXFER.Blazor/Interop/CanvasDocumentDto.cs`
- `src/DXFER.Blazor/Sketching/SketchCreationEntityFactory.cs`
- `src/DXFER.Blazor/wwwroot/drawingCanvas.js`
- `src/DXFER.Blazor/wwwroot/canvas/targets.js`
- `tests/DXFER.Core.Tests/Documents/*`
- `tests/DXFER.Core.Tests/Interop/CanvasDocumentDtoTests.cs`
- `tests/DXFER.Blazor.Tests/canvasTargets.test.mjs`

Automated tests to add/update:

- Entity map tests for `skPoint`, `skLineSegment`, `skCircle`, `skArc`, `skEllipse`, `skEllipticalArc`, `skFitSpline`, `skBezier`, `skConicSegment`, `skRectangle`, `skRegularPolygon`, and `skPolyline`.
- Reference tests for entity, start, end, center, midpoint, segment, curve point, quadrant, and tangent handle keys.
- DTO tests for ellipse partial sweeps, spline fit/control modes, Bezier/conic subtype metadata, polygon parametric metadata, and construction flags.
- Unsupported text/image tests that produce capability diagnostics and unchanged documents.

User testing gate:

The user must be able to draw each supported entity and inspect/select its expected edit handles without ambiguous selection keys.

Exact user actions to test:

1. Draw a point, line, circle, arc, full ellipse, elliptical arc, fit spline, Bezier, conic, rectangle, polygon, and polyline.
2. Hover each entity and verify endpoint/center/midpoint/fit/control/tangent handles appear only where meaningful.
3. Toggle construction mode before drawing an entity and confirm it renders as construction and does not export as cut geometry.
4. Try the disabled text tool and confirm it is clearly unavailable.

Edge cases to test:

- Zero-length line.
- Zero-radius circle.
- Degenerate arc through collinear points.
- Ellipse with zero major/minor axis.
- Spline with duplicate fit points.
- Polygon sides below three or above the max.
- Polyline with repeated start/end closure.

Pass/fail criteria:

- Pass: every supported entity has deterministic reference keys and unchanged behavior through DTO round trips.
- Fail: Bezier/conic/elliptical arc behavior is indistinguishable from a generic spline/ellipse when solver/tool code needs subtype semantics.

Rollback or defer criteria:

- If adding typed Bezier/conic entities is too broad, add subtype metadata to `SplineEntity` first and mark exact conic rho solving unsupported.
- If a reference key would break existing canvas tests, add a compatibility parser before changing emitted keys.

### M3 - Runtime Solve Lifecycle Routing

Objective:

Move live tool, constraint, dimension, and drag workflows onto `ISketchSolver` so the workbench follows the same lifecycle shape as FeatureScript: prepare entities and guesses, solve through an adapter, then apply or reject the result.

Code areas likely affected:

- `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs`
- `src/DXFER.Core/Sketching/LegacySketchSolverAdapter.cs`
- `src/DXFER.Core/Sketching/SketchConstraintService.cs`
- `src/DXFER.Core/Sketching/SketchDimensionSolverService.cs`
- `src/DXFER.Core/Sketching/SketchGeometryDragService.cs`
- `tests/DXFER.Core.Tests/Sketching/SketchSolverAbstractionTests.cs`
- `tests/DXFER.Core.Tests/Sketching/SketchGeometryDragServiceTests.cs`
- `tests/DXFER.Blazor.Tests/drawingCanvasSelection.test.mjs`

Automated tests to add/update:

- Workbench command handlers call a solver abstraction for adding constraints and dimensions.
- Drag requests produce a solve request with current document, selected reference, drag start/end, and initial guesses.
- Failed solver results do not update `_document`.
- Successful solver results update dimensions, constraints, diagnostics, and history as one transaction.
- Legacy adapter parity tests prove current successful behaviors still work after routing.

User testing gate:

The user must verify that normal sketch actions still work and that failed solves are transactional.

Exact user actions to test:

1. Draw a rectangle, add width/height dimensions, and drag one edge.
2. Apply horizontal/vertical/parallel/perpendicular constraints to lines.
3. Apply a dimension that the fixed constraints cannot satisfy.
4. Undo and redo after both successful and failed solve attempts.
5. Confirm failed attempts do not add an undo step or mutate geometry.

Edge cases to test:

- Repeated modal constraint command.
- Dimension edit while a drag preview is active.
- Solver unavailable adapter selected in a dev/test configuration.
- Multiple constraints added by one creation tool.
- History update after partial failure.

Pass/fail criteria:

- Pass: all live sketch mutations use `ISketchSolver`, success/failure is transactional, and current tests remain behaviorally equivalent.
- Fail: workbench still calls constraint/dimension services directly for live operations.

Rollback or defer criteria:

- If full drag routing is too risky, route add-constraint/add-dimension first and leave drag behind an explicit compatibility adapter with tests.

### M4 - Constraint Capability Matrix And Solver Parity

Objective:

Define the exact supported constraint set, implement missing high-value constraints, and make every unsupported FeatureScript `ConstraintType` fail clearly with no document mutation.

Code areas likely affected:

- `src/DXFER.Core/Sketching/SketchConstraintKind.cs`
- `src/DXFER.Core/Sketching/SketchConstraintService.cs`
- `src/DXFER.Core/Sketching/SketchConstraintPropagationService.cs`
- `src/DXFER.Blazor/Sketching/SketchCommandFactory.cs`
- `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs`
- `tests/DXFER.Core.Tests/Sketching/SketchConstraintServiceTests.cs`
- `tests/DXFER.Core.Tests/Sketching/SketchCreationConstraintFactoryTests.cs`
- `tests/DXFER.Blazor.Tests/drawingCanvasSelection.test.mjs`

Automated tests to add/update:

- Capability table covers all FeatureScript `ConstraintType` names and maps each to supported, deferred, or not applicable.
- Tangent solve tests for line-circle, line-arc, circle-circle external/internal tangent, and already-satisfied tangent.
- Unsupported constraints such as projected, offset, pierce, rho, equal curvature, Bezier degree, freeze, and pattern constraints return unsupported diagnostics and unchanged documents.
- Fix conflicts report affected fixed references.
- Constraint deletion removes only selected constraints and not geometry.

User testing gate:

The user must be able to apply each supported constraint from the toolbar and see unsupported constraints disabled or rejected with a clear message.

Exact user actions to test:

1. Draw two lines and apply parallel, perpendicular, horizontal, vertical, equal, and fix.
2. Draw a circle and line, apply tangent, then drag the line and confirm tangent behavior or an explicit unsupported diagnostic.
3. Draw two circles, apply concentric and equal.
4. Try disabled/future constraints and confirm they cannot create misleading sketch data.
5. Delete a constraint glyph and confirm only that relation is removed.

Edge cases to test:

- Tangent line through circle center.
- Equal between line and circle.
- Concentric between ellipse and circle if ellipse support is deferred.
- Fix on whole entity vs endpoint vs center.
- Suppressed constraints.
- Duplicate constraints with reversed reference order.

Pass/fail criteria:

- Pass: supported constraints either solve or diagnose cleanly; unsupported constraints never mutate geometry.
- Fail: tangent remains advertised as applied when geometry is unsolved, or unsupported constraints can be persisted as unknown failures.

Rollback or defer criteria:

- If tangent solving cannot be implemented robustly, downgrade tangent to validation-only and make unsatisfied tangent return unsupported/failed without moving geometry.

### M5 - Dimension Semantics And Edit Parity

Objective:

Align DXFER dimensions with FeatureScript constraint/dimension behavior while preserving DXFER's UI-friendly dimension overlay. Driving dimensions must be solved through the adapter and rejected transactionally on unsupported combinations.

Code areas likely affected:

- `src/DXFER.Core/Sketching/SketchDimensionKind.cs`
- `src/DXFER.Core/Sketching/SketchDimensionSolverService.cs`
- `src/DXFER.Core/Sketching/DimensionValueFormatter.cs`
- `src/DXFER.Blazor/Sketching/SketchCommandFactory.cs`
- `src/DXFER.Blazor/Sketching/SketchCreationDimensionFactory.cs`
- `src/DXFER.Blazor/wwwroot/canvas/dimensions.js`
- `src/DXFER.Blazor/wwwroot/canvas/dimensionPresentation.js`
- `tests/DXFER.Core.Tests/Sketching/SketchDimensionSolverServiceTests.cs`
- `tests/DXFER.Blazor.Tests/canvasDimensions.test.mjs`

Automated tests to add/update:

- Linear, horizontal, vertical, point-to-line, radius, diameter, and angle driving dimensions produce solver requests and diagnostics.
- Major/minor ellipse diameter behavior is either implemented or explicitly unsupported.
- Arc sweep angle edits enforce valid sweep bounds.
- Polygon side count dimension remains intentional DXFER behavior and is documented as not a FeatureScript `ConstraintType` equivalent.
- Dimension parsing rejects invalid, negative where unsupported, NaN, infinity, and unit-suffixed inputs until units parsing exists.

User testing gate:

The user must be able to create, edit, drag, and delete dimensions without corrupting geometry.

Exact user actions to test:

1. Draw a line and edit its length.
2. Draw a rectangle and edit width/height.
3. Draw a circle and edit diameter/radius.
4. Draw an arc and edit sweep angle.
5. Draw an ellipse and test major/minor diameter behavior or confirm a clear unsupported message.
6. Enter invalid dimension text and confirm no geometry changes.

Edge cases to test:

- Dimension value zero.
- Negative input.
- Overconstrained rectangle diagonal.
- Angle dimension between already constrained rectangle sides.
- Fixed entity with radius edit.
- Dimension anchor drag during edit.

Pass/fail criteria:

- Pass: valid dimension edits solve through the adapter; invalid/unsupported edits leave the document unchanged with diagnostics.
- Fail: dimension changes partially mutate entities or leave unsatisfied dimensions without a visible warning.

Rollback or defer criteria:

- If ellipse major/minor diameter solving is too broad, keep ellipse axis dimensions read-only and return unsupported diagnostics for driving edits.

### M6 - Tool Workflow, Snapping, And Construction Gate

Objective:

Make tool behavior deterministic and solver-aware: creation tools should emit the right entities, persistent logical constraints, optional dimensions, construction flags, and snap-derived references, then solve through the shared lifecycle.

Code areas likely affected:

- `src/DXFER.Blazor/Sketching/SketchCreationEntityFactory.cs`
- `src/DXFER.Blazor/Sketching/SketchCreationConstraintFactory.cs`
- `src/DXFER.Blazor/Sketching/SketchCreationDimensionFactory.cs`
- `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs`
- `src/DXFER.Blazor/wwwroot/drawingCanvas.js`
- `src/DXFER.Blazor/wwwroot/canvas/targets.js`
- `src/DXFER.Blazor/wwwroot/canvas/targetKeys.js`
- `tests/DXFER.Core.Tests/Sketching/SketchCreation*Tests.cs`
- `tests/DXFER.Blazor.Tests/drawingCanvasSelection.test.mjs`

Automated tests to add/update:

- Every draw tool declares required point count, entity output, optional dimension keys, and persistent constraints.
- Rectangle creation exactly preserves geometry after constraints are applied.
- Snap-created coincident/midpoint/perpendicular/tangent relations are persisted only when intended.
- Construction mode affects created entities and conversion tool only affects selected/targeted geometry.
- Canvas reference keys remain stable for point, segment, dynamic point, and curve targets.

User testing gate:

The user must run a sketch creation smoke pass and confirm no tool creates surprising hidden geometry or misleading constraints.

Exact user actions to test:

1. Draw each sketch tool in normal mode.
2. Toggle construction mode and draw a line, circle, spline, and rectangle.
3. Draw a chained line/tangent arc sequence.
4. Use endpoint, midpoint, center, intersection, and tangent snaps.
5. Box select, crossing select, and delete selected geometry/constraints.
6. Export and confirm construction geometry is omitted from cut DXF.

Edge cases to test:

- Double-click to finish spline.
- Escape during each modal tool.
- Scroll polygon side count.
- Chained line/tangent arc rearm.
- Snap to dynamic point then cancel.
- Constraint visibility off with hovered glyph.

Pass/fail criteria:

- Pass: tool outputs match factories, persistent constraints are visible/deletable, and construction/export behavior is clear.
- Fail: a tool adds constraints that move the just-created geometry unexpectedly or emits references the solver cannot resolve.

Rollback or defer criteria:

- If a tool's persistent constraints are unstable, keep the geometry creation but defer automatic constraints for that tool.

### M7 - Trim, Split, And Modify Safety

Objective:

Harden modify operations so every operation either performs a supported, testable edit or fails clearly with the original document unchanged. This milestone focuses on Power Trim, split, add spline point, offset, fillet/chamfer, and pattern operations.

Code areas likely affected:

- `src/DXFER.Core/Operations/DrawingModifyService.cs`
- `src/DXFER.Core/Operations/CurveSplitService.cs`
- `src/DXFER.Core/Operations/LineSplitService.cs`
- `src/DXFER.Core/Sketching/SketchGeometryDragService.cs`
- `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs`
- `src/DXFER.Blazor/wwwroot/drawingCanvas.js`
- `tests/DXFER.Core.Tests/Operations/DrawingModifyServiceTests.cs`
- `tests/DXFER.Core.Tests/Operations/CurveSplitServiceTests.cs`
- `tests/DXFER.Blazor.Tests/drawingCanvasSelection.test.mjs`

Automated tests to add/update:

- For each modify tool, unsupported target types return false/unsupported diagnostics and document reference equality or deep equality is preserved.
- Power Trim supports exact line/circle/arc/ellipse cases and labels sampled spline/polyline cases as approximate where applicable.
- Spline trim does not pretend to preserve exact spline degree/knot semantics when it emits sampled degree-1 geometry.
- Split at point handles line, arc, circle two-point split, and unsupported ellipse/spline cases explicitly.
- Constraints/dimensions referencing removed or split geometry are either remapped or removed with diagnostics.

User testing gate:

The user must verify that trim/split failures are safe and successes are visible and undoable.

Exact user actions to test:

1. Trim a line between two cutter lines.
2. Extend a line to a cutter.
3. Trim a circle into arcs.
4. Trim an arc and an ellipse.
5. Trim a spline and confirm whether the result is exact or explicitly approximate.
6. Try trim on text/unsupported geometry if present.
7. Undo every successful modify operation.

Edge cases to test:

- Pick exactly on a cutter.
- Pick exactly on endpoint.
- Tangent cutter intersection.
- Multiple crossings in drag trim.
- Closed polygon trim.
- Spline with too few fit points.
- Constraints/dimensions on target geometry.

Pass/fail criteria:

- Pass: unsupported/unsafe operations leave the document unchanged and report why; supported operations produce valid geometry and history entries.
- Fail: any unsupported target is deleted or partially transformed without a precise capability check.

Rollback or defer criteria:

- If exact spline/conic trim cannot be guaranteed, keep sampled trim behind an explicit "approximate" status or defer it.

### M8 - CAD IO, Sidecar, And Trust Guardrails

Objective:

Ensure the stronger sketch model still supports DXFER's trusted flat-pattern workflow: supported sketch geometry exports cleanly, unsupported CAD payloads are reported or preserved, and sidecar metadata remains trustworthy.

Code areas likely affected:

- `src/DXFER.CadIO/DxfDocumentReader.cs`
- `src/DXFER.CadIO/DxfDocumentWriter.cs`
- `src/DXFER.Core/IO/DxferSidecarWriter.cs`
- `src/DXFER.Core/Documents/DrawingDocumentMetadata.cs`
- `src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs`
- `tests/DXFER.Core.Tests/IO/*`

Automated tests to add/update:

- Onshape-like DXF fixtures for line, circle, arc, ellipse, polyline, spline, and point.
- Export/reload tests preserve entity count, bounds, construction exclusion, source/normalized hashes, units, warnings, and sidecar schema.
- Unsupported DXF entities are counted and warned; if raw preservation is deferred, export must include an irreversible-normalization warning.
- Reference-only DWG remains non-editable and does not create manufacturing geometry.

User testing gate:

The user must run a trusted flat-pattern workflow and verify both files are produced and warnings are visible.

Exact user actions to test:

1. Open an Onshape-generated DXF.
2. Confirm entity count, warnings, units, and bounds are visible.
3. Normalize origin/vector/grain.
4. Save/export.
5. Inspect that both `.dxf` and `.dxfer.json` download.
6. Reopen the exported DXF and compare bounds/entity count.
7. Try opening a DWG and confirm no editable geometry is created.

Edge cases to test:

- Missing units.
- Suspiciously large coordinates.
- Unsupported entities on named layers.
- Construction geometry in document.
- Reference-only mode.
- Source hash mismatch.

Pass/fail criteria:

- Pass: trusted exports include normalized DXF plus sidecar, warnings are explicit, and reference inputs cannot write manufacturing metadata.
- Fail: unsupported CAD data disappears without warning or sidecar hashes/metadata are missing.

Rollback or defer criteria:

- If raw unsupported payload preservation is not feasible yet, fail export or mark the sidecar with a clear irreversible-normalization warning.

### M9 - Canvas Boundary And Browser Regression Harness

Objective:

Continue the behavior-preserving canvas split so solver/tool responsibilities are testable in smaller modules. Keep the Blazor-facing `drawingCanvas.js` facade stable while moving pure geometry, references, hit testing, snaps, viewport, selection, tools, rendering, and interop into modules.

Code areas likely affected:

- `src/DXFER.Blazor/wwwroot/drawingCanvas.js`
- `src/DXFER.Blazor/wwwroot/canvas/*.js`
- `tests/DXFER.Blazor.Tests/*.test.mjs`
- `src/DXFER.Blazor/Components/DrawingCanvas.razor.cs`

Automated tests to add/update:

- Module-level tests for hit testing, snap generation, power-trim crossing requests, geometry drag previews, dimension overlays, and constraint glyph grouping.
- Facade compatibility tests for exported functions and callback names.
- Browser smoke test or Playwright flow for render, hover, selection, draw, dimension edit, and trim.

User testing gate:

The user must verify the app visually after each extraction phase because canvas regressions can be subtle.

Exact user actions to test:

1. Start the web host.
2. Load the sample drawing.
3. Fit extents, pan, zoom, hover, select, box select, and delete.
4. Draw line/circle/arc/spline/rectangle.
5. Create and edit a dimension.
6. Apply and delete a constraint glyph.
7. Use Power Trim drag crossing.

Edge cases to test:

- High DPI display.
- Very small and very large drawings.
- Touch/pointer capture cancellation.
- Escape during modal tools.
- Canvas disposal/reload.
- Constraint visibility toggles.

Pass/fail criteria:

- Pass: public import path and callbacks remain stable, tests pass, and manual visual smoke shows no render/selection regressions.
- Fail: module extraction changes selection keys, callback timing, or visual draw order.

Rollback or defer criteria:

- If a module extraction causes circular dependencies or visual regressions, revert only that extraction phase and keep prior pure modules.

## Priority Order

1. M1 Solver Contract And Diagnostics Baseline.
2. M3 Runtime Solve Lifecycle Routing.
3. M4 Constraint Capability Matrix And Solver Parity.
4. M2 Entity And Reference Contract Alignment.
5. M5 Dimension Semantics And Edit Parity.
6. M6 Tool Workflow, Snapping, And Construction Gate.
7. M7 Trim, Split, And Modify Safety.
8. M8 CAD IO, Sidecar, And Trust Guardrails.
9. M9 Canvas Boundary And Browser Regression Harness.

The ordering intentionally puts solve/result architecture before adding more geometry intelligence. DXFER already has substantial entity and tool coverage; the next risk is inconsistent behavior if each tool continues to solve locally.

## Verification Notes For This Audit Pass

- No source or test implementation files were intentionally changed.
- No FeatureScript source was vendored into DXFER.
- I did not quote or adapt FeatureScript implementation code into DXFER source.
- I did not run build or test commands for this audit pass. The pass relied on source inspection and the existing local FeatureScript manifest.
- The only intended artifact from this pass is this markdown file.

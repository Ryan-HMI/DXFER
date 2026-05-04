# Canvas Module Boundary Plan

Date: 2026-05-04

## Scope

This note prepares `src/DXFER.Blazor/wwwroot/drawingCanvas.js` for a behavior-preserving split. It does not propose changing the Blazor public import path, callback names, selection key format, snap labels, visual styles, draw order, or the serialized document DTO shape.

The current file is a single ES module imported by `DrawingCanvas.razor.cs` at `./_content/DXFER.Blazor/drawingCanvas.js`. When the split happens, keep that file as the stable facade that exports `createDrawingCanvas(canvas, dotnetRef)` and imports smaller sibling modules with relative paths.

## Current Source Map

- Facade, lifecycle, state creation, public instance methods: lines 13-152.
- Resize and rendering pipeline: lines 154-615.
- Pointer, keyboard, pan, zoom, sketch tool, and selection event handlers: lines 617-936.
- View transform and fit-to-extents helpers: lines 938-991.
- Hit testing and rubber-band selection: lines 993-1460.
- Snap points, dynamic snaps, tangent snaps, and target creation: lines 1462-1779.
- Selection target resolution, hover/selection state, acquired snaps, debug data attributes: lines 1781-1987.
- DOM input helpers, tool normalization, and .NET callback helper: lines 1989-2190.
- Document/entity DTO adapters and geometry math: lines 2192-2525.

## Proposed Modules

### `drawingCanvas.js`

Owns the stable Blazor-facing facade.

- Export `createDrawingCanvas(canvas, dotnetRef)`.
- Create the mutable state object.
- Register and unregister DOM/window/resize-observer listeners.
- Return the public instance methods currently used by Blazor: `setDocument`, `fitToExtents`, `setGrainDirection`, `clearSelection`, `setOriginAxesVisible`, `setActiveTool`, and `dispose`.
- Orchestrate calls into rendering, interaction, viewport, and interop modules.

Do not move the Blazor import path unless the Razor component changes in the same feature. The least risky split keeps `drawingCanvas.js` as the compatibility shell.

### `canvas/state.js`

Owns state shape and constants that define behavior.

- Initial state factory for `document`, `view`, `selectedKeys`, `hoveredTarget`, `toolDraft`, `acquiredSnapPoints`, pointer state, and disposal state.
- Behavior constants such as `HIT_TEST_TOLERANCE`, `SNAP_POINT_TOLERANCE`, `CLICK_MOVE_TOLERANCE`, min/max zoom, `MAX_ACQUIRED_SNAP_POINTS`, key separators, and geometry tolerance.

Keep constants centralized so rendering, snaps, hit testing, and key parsing share exactly the same values.

### `canvas/rendering.js`

Owns all canvas drawing and `CanvasRenderingContext2D` style decisions.

- Main `draw(state)` ordering.
- Entity drawing and path construction for line, polyline, circle, and arc entities.
- Origin axes, grain direction, selection highlight, hover highlight, point target markers, acquired snap markers, inference guides, rubber-band selection box, and sketch previews.

Inputs should be state plus pure helpers: viewport transforms, document/entity accessors, target resolution, selection-box normalization, and tool lookup. Rendering should not mutate selection, hover, tool state, or invoke .NET callbacks.

Behavior to preserve when extracting:

- Background color and all stroke/fill colors.
- Draw order: background, axes, entities, selected targets, acquired snaps, inference guides, hovered target, selection box, tool preview, grain direction.
- Context `save`/`restore` balance and pixel-ratio transform.
- Existing arc rendering by sampled polyline rather than native arc commands.

### `canvas/hitTesting.js`

Owns screen-space target discovery.

- Nearest target selection: point snap priority, dynamic sketch snap priority, then edge hit priority.
- Whole-entity double-click hit testing.
- Edge hit testing for line, polyline segment, circle, and arc.
- Rubber-band target discovery for window and crossing selection.
- Screen bounds, screen segments, and sampled screen points used by selection.

Hit testing can return targets and distances, but it should not mutate `state.selectedKeys`. Keep `applyBoxSelection` in interaction or selection state, or rename it clearly if it remains mutating.

Dependencies:

- `viewport.js` for `worldToScreen` and `screenToWorld`.
- `documentAdapter.js` for entity shape reads.
- `targets.js` for target creation and key formats.
- `snaps.js` for point and dynamic snap candidates.
- `geometry.js` for distances, projections, rectangle checks, arc checks, and point sampling.

### `canvas/snaps.js`

Owns snap candidate generation and acquired-snap inference rules.

- Entity snap points for lines, polylines, circles, and arcs.
- Tangent snap points between linear and curve entities.
- Dynamic sketch snap candidates from acquired points, midpoint/inference intersections, and draft-line tangent points.
- Snap labels and priorities.

Keep acquired snap list mutation separate from pure candidate generation if possible. A good split is:

- `snaps.js`: pure snap candidate calculations.
- `interactionState.js`: `rememberAcquiredSnapPoint(state, target)` because it mutates `state.acquiredSnapPoints` and depends on active tool state.

Behavior to preserve:

- Static snap points prefer points within `SNAP_POINT_TOLERANCE`.
- Dynamic candidates are only available during sketch creation tools.
- Tangent snaps keep their higher priority and are allowed to duplicate coordinates when the label starts with `tangent-`.
- Only two acquired snap points are retained.

### `canvas/geometry.js`

Owns pure coordinate and geometry math with no DOM or state mutation.

- Point math: midpoint, mirror point, distances.
- Angle math: degrees/radians, arc sweep, angle-on-arc.
- Segment and rectangle math: closest point on segment, segment intersection, orientation, rectangle overlap.
- Bounds inclusion.
- Numeric helpers: clamp, positive finite checks, stable key number formatting.

Keep `worldToScreen`, `screenToWorld`, and `fitToExtents` out of this module unless they are parameterized by an explicit view object. They are viewport behavior because they depend on `state.view` and canvas size.

### `canvas/viewport.js`

Owns view, canvas size, and screen/world coordinate conversion.

- `resizeCanvas(state)`.
- `fitToExtents(state)`.
- `worldToScreen(state, point)` and `screenToWorld(state, point)`.
- `getCanvasCssSize(state)`, pointer-to-screen conversion, wheel delta normalization, and zoom clamping.

This module may mutate `state.view`, `state.pixelRatio`, and canvas dimensions. It should not decide selection, hover, snap, or .NET callback behavior.

### `canvas/interaction.js`

Owns event handling and state transitions.

- Pointer move/down/up/cancel/leave.
- Keyboard escape/delete behavior.
- Wheel zoom and double-click selection.
- Panning, click candidates, selection boxes, hover transitions, and draw scheduling.
- Calls into tool, hit-testing, selection, viewport, rendering, and interop modules.

This should be the main mutating coordinator below the facade. It is acceptable for it to invoke `draw(state)` after mutations because today rendering is immediate and synchronous.

### `canvas/tools.js`

Owns tool-name normalization and sketch draft behavior.

- `normalizeToolName`.
- `getSketchCreationTool`.
- `getSketchWorldPoint`.
- Sketch click state machine and commit coordinate generation for line, midpoint line, two-point rectangle, and center circle.

Keep visual preview drawing in `rendering.js`. Tool code should decide draft points and commit payloads, while rendering decides how a draft looks.

### `canvas/selectionState.js`

Owns selection and target state mutation.

- Toggle and clear selected targets.
- Apply box selection mutation.
- Resolve/prune selection after document replacement.
- Clear interaction state.
- Hover target updates and notification decision for dynamic vs entity-backed targets.

Keep key creation and parsing in `targets.js`, but keep selected-key mutation here so selection behavior has one owner.

### `canvas/targets.js`

Owns target contracts and selection key formats.

- Entity target, polyline segment target, point target, and dynamic point target construction.
- `withSnapPoint`.
- Target-key parsing and resolution.
- Key constants: `SEGMENT_KEY_SEPARATOR`, `POINT_KEY_SEPARATOR`, `DYNAMIC_POINT_KEY_PREFIX`.
- `formatKeyNumber`, `sanitizeKeyPart`, and string equality if not kept in `geometry.js`.

This is a contract boundary with Blazor because selected keys are sent back to .NET and stored by `DrawingCanvas.razor.cs`. Preserve exact key strings during the first split.

### `canvas/documentAdapter.js`

Owns JavaScript reads of the serialized Blazor DTO.

- `getDocumentEntities`.
- `getDocumentBounds`.
- Entity property accessors: id, kind, points, center, radius, start angle, end angle.
- CamelCase/PascalCase tolerance in `readProperty`.
- Fallback bounds computation.

This module should stay intentionally small and defensive. It is the only canvas module that should understand both camelCase JSON and PascalCase object shapes.

### `canvas/interop.js`

Owns .NET callback invocation and callback names.

- `invokeDotNet(state, methodName, ...args)`.
- Export named wrappers for current callbacks if the split benefits from stronger contracts:
  - `notifySelectionChanged(state)`.
  - `notifySelectionCleared(state)`.
  - `notifyEntityClicked(state, key)`.
  - `notifyEntityHovered(state, key)`.
  - `notifySketchToolCommitted(state, toolName, coordinates)`.
  - `notifySketchToolCanceled(state)`.
  - `notifyDeleteSelectionRequested(state)`.

Preserve debug dataset updates for callback state: `lastDotnetCallback` must continue to move through `missing`, `pending`, `ok`, and `error`.

### `canvas/debugAttributes.js`

Owns `canvas.dataset` diagnostics.

- Entity count, selected keys, hovered target, snap count, selection box mode, active tool, draft point count, view transform, canvas rect, first entity, and first entity screen points.

This is cross-cutting but should not remain embedded in selection or rendering. Keeping it separate makes behavior-preserving extraction easier because tests and manual checks can use the same dataset attributes after every phase.

## Extraction Order

1. Extract `geometry.js` with only pure helpers. Import it back into `drawingCanvas.js` and verify no behavior changes.
2. Extract `documentAdapter.js` and `targets.js`. Preserve exact selection-key strings and camelCase/PascalCase DTO tolerance.
3. Extract `viewport.js`. Keep `resizeCanvas` calling `draw(state)` from the facade or pass a draw callback to avoid circular imports.
4. Extract `debugAttributes.js`. Call it from the same places as today before moving interaction code.
5. Extract `snaps.js` as pure candidate generation. Leave acquired-snap mutation in the original file until selection/interaction state is split.
6. Extract `hitTesting.js`. Keep mutating selection operations out of it except for a temporary compatibility export if needed.
7. Extract `selectionState.js` and `tools.js`. Preserve callback timing and immediate draw calls.
8. Extract `rendering.js`. Verify draw order, styles, and context transform behavior visually.
9. Extract `interaction.js`. At this point `drawingCanvas.js` should mostly be lifecycle wiring and public facade methods.

Commit each extraction phase separately. A failed phase should be easy to revert without disturbing earlier pure-module moves.

## Behavior Verification Checklist

Run after each extraction phase that changes imports or moves code:

- `dotnet build DXFER.slnx`.
- Load the Blazor host and confirm the canvas renders the sample entities.
- Fit-to-extents centers the document and updates `data-scale`, `data-offset-x`, and `data-offset-y`.
- Hover highlights entity edges and snap points and updates `data-hovered-id`.
- Single click toggles selection and calls `OnEntityClicked`.
- Drag left-to-right performs window selection; right-to-left performs crossing selection; Ctrl/Command drag deselects.
- Shift-left, middle, and right pointer panning still work.
- Wheel zoom keeps the cursor world point stable.
- Double-click selects the nearest whole entity.
- Delete requests deletion for the current selection.
- Escape during a sketch tool cancels and calls `OnSketchToolCanceled`.
- Sketch tools still commit the same coordinate payloads through `OnSketchToolCommitted`.
- Static snaps, acquired projection snaps, midpoint/intersection snaps, and tangent snaps still show the same marker and guide behavior.
- Dispose removes event listeners and restores the previous canvas `touchAction`.

## Caveats

- There is no JavaScript test harness or bundler in the repo right now. The first split should stay plain ES modules under `wwwroot` so Blazor static web assets can serve relative imports without adding tooling.
- Circular dependencies are the main risk. Avoid them by keeping `drawingCanvas.js` as the orchestration layer during early phases and passing callbacks where viewport or interaction code needs to request a redraw.
- `targets.js` and `documentAdapter.js` are contracts with .NET behavior, not just internal helpers. Do not change target keys, DTO property reads, or callback payloads during the split.
- The current file mixes debug dataset writes with behavior. Move those writes intact before relying on them as regression checks.

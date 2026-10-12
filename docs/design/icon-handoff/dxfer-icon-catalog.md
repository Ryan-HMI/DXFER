# DXFer Command and Icon Catalog

Source: `7e9368692f0d77a371b44ac5f11e8d1f9b2267d8`. Generated from the current application, not the old `dxfer-buttons.json`.

**75 command IDs; 76 named icon assets; 65 commands and 64 unique icons in the palettes; 4 additional chrome assets.**

Full-editor and cleanup-only appearances are both included. Repeated labels/groups reuse one asset. 'Future' is the code's explicit flag; selection/document-dependent disabled buttons are NOT classified as unimplemented. Existing known editor defects are outside this inventory.

## Image-generation handoff

Generate one transparent PNG per unique asset ID, using the filenames below. Work in small families so the style stays consistent. Do not generate a single large atlas as the only deliverable. Keep labels and button backgrounds outside the bitmap. Use a consistent square canvas, centered silhouette, generous consistent padding, crisp high-contrast marks and minimal detail. No shadows, bevels, decorative backgrounds or tiny text. Create 256px masters, then inspect/export at 24px and 32px (plus 2x sizes); a large attractive rendering is not enough if it becomes muddy at toolbar size. Preserve genuine symbol differences, especially midpoint/endpoints, CW/CCW, inscribed/circumscribed, tangent/concentric and X/Y. Selected/disabled styling belongs to the application. Provide appropriate light/dark foreground variants if the assets cannot adapt to theme. This is an asset brief, not an instruction to replace the app UI yet.

## View

| Command ID | Button | Asset | State | Purpose / interaction |
|---|---|---|---|---|
| `Measure` | Measure | `measure.png` | Exposed; may require context | Measure: select points or geometry for live deltas. Esc: exit measure. |
| `FitExtents` | Fit extents | `fit.png` | Exposed; may require context | Fit extents. |
| `OriginAxes` | Origin axes | `origin-axes.png` | Exposed; may require context | Origin axes. |
| `ShowConstraints` | Show constraints | `show-constraints.png` | Exposed; may require context | Show constraints. When off, constraints appear only while hovering referenced geometry. |
| `ToolHotkeys` | Tool hotkeys | `hotkeys.png` | Exposed; may require context | Tool hotkeys. |

## Sketch

| Command ID | Button | Asset | State | Purpose / interaction |
|---|---|---|---|---|
| `Line` | Line | `line.png` | Exposed; may require context | Line: click start point, then end point. Shift: polar snap. Double-click: start a fresh line. Esc: cancel. |
| `MidpointLine` | Midpoint line | `midpoint-line.png` | Exposed; may require context | Midpoint line: click midpoint, then endpoint. Shift: polar snap. Esc: cancel. |
| `TwoPointRectangle` | Two-point rectangle | `rectangle.png` | Exposed; may require context | Corner rectangle: click first corner, then opposite corner. Shift: polar snap. Esc: cancel. |
| `CenterRectangle` | Center rectangle | `center-rectangle.png` | Exposed; may require context | Center rectangle: click center, then corner. Shift: polar snap. Esc: cancel. |
| `AlignedRectangle` | Aligned rectangle | `aligned-rectangle.png` | Exposed; may require context | Aligned rectangle: click baseline start, baseline end, then depth. Esc: cancel. |
| `CenterCircle` | Center circle | `circle.png` | Exposed; may require context | Center circle: click center, then radius point. Shift: polar snap. Esc: cancel. |
| `ThreePointCircle` | Three-point circle | `three-point-circle.png` | Exposed; may require context | Three-point circle: click three points on the circumference. Esc: cancel. |
| `Ellipse` | Ellipse | `ellipse.png` | Exposed; may require context | Ellipse: click center, major radius point, then minor radius point. Esc: cancel. |
| `ThreePointArc` | Three-point arc | `arc.png` | Exposed; may require context | Three-point arc: click start point, through point, then end point. Esc: cancel. |
| `CenterPointArc` | Center point arc | `center-point-arc.png` | Exposed; may require context | Center point arc: click center, start radius point, then end angle point. Esc: cancel. |
| `EllipticalArc` | Elliptical arc | `elliptical-arc.png` | Exposed; may require context | Elliptical arc: click center, major radius point, minor radius point, then end parameter point. Esc: cancel. |
| `Conic` | Conic | `conic.png` | Exposed; may require context | Conic: click start, control point, then end point. Esc: cancel. |
| `InscribedPolygon` | Inscribed polygon | `inscribed-polygon.png` | Exposed; may require context | Inscribed polygon: click center, scroll for sides, then vertex radius. Esc: cancel. |
| `CircumscribedPolygon` | Circumscribed polygon | `circumscribed-polygon.png` | Exposed; may require context | Circumscribed polygon: click center, scroll for sides, then side apothem. Esc: cancel. |
| `Spline` | Spline | `spline.png` | Exposed; may require context | Spline: click fit points, double-click to finish. Esc: cancel. |
| `SplineControlPoint` | Add spline point | `spline-control-point.png` | Exposed; may require context | Add spline point: click a fit-point spline where the new fit point belongs. Esc: cancel. |
| `Point` | Point | `point.png` | Exposed; may require context | Point: click to place a persistent sketch point. Esc: cancel. |
| `Text` | Text | `text.png` | Future / disabled | Text. Not implemented yet. |
| `Slot` | Slot | `slot.png` | Exposed; may require context | Slot: click first center, second center, then radius point. Esc: cancel. |

## Modify

| Command ID | Button | Asset | State | Purpose / interaction |
|---|---|---|---|---|
| `Undo` | Undo | `undo.png` | Exposed; may require context | Undo. Hotkey: Ctrl+Z. |
| `Redo` | Redo | `redo.png` | Exposed; may require context | Redo. Hotkey: Ctrl+Shift+Z. |
| `Construction` | Construction | `construction.png` | Exposed; may require context | Construction: click geometry to toggle construction state. Esc: cancel. |
| `DeleteSelection` | Delete selected geometry | `delete.png` | Exposed; may require context | Delete selected geometry. |
| `PowerTrim` | Power trim/extend | `power-trim.png` | Exposed; may require context | Power trim/extend: click a line, polyline, polygon, circle, arc, ellipse, spline, or point section to trim, or click past a line endpoint to extend. Esc: cancel. |
| `SplitAtPoint` | Split at point | `split.png` | Exposed; may require context | Split at point: click a line or arc, or pick two points on a circle. Esc: cancel. |
| `AddSplinePoint` | Add spline point | `spline-control-point.png` | Exposed; may require context | Add spline point: select one fit-point spline, then click the spline where the new fit point belongs. Esc: cancel. |
| `Offset` | Offset | `offset.png` | Exposed; may require context | Offset: select whole geometry, then click the through side or radius point. Esc: cancel. |
| `Fillet` | Fillet | `fillet.png` | Exposed; may require context | Fillet: select two converging edges, or one vertex with exactly two adjacent edges. Esc: cancel. |
| `Chamfer` | Chamfer | `chamfer.png` | Exposed; may require context | Chamfer: select two converging edges, or one vertex with exactly two adjacent edges. Esc: cancel. |
| `Dimension` | Dimension | `dimension.png` | Exposed; may require context | Dimension: click geometry or points, move to place, then click to set. Shift: diameter for arcs. Esc: cancel. |

## Transform

| Command ID | Button | Asset | State | Purpose / interaction |
|---|---|---|---|---|
| `Translate` | Translate | `move.png` | Exposed; may require context | Translate: select whole geometry, click from point, then to point. Esc: cancel. |
| `Rotate` | Rotate | `rotate.png` | Exposed; may require context | Rotate: select whole geometry, click center, reference point, then target point. Esc: cancel. |
| `Scale` | Scale | `scale.png` | Exposed; may require context | Scale: select whole geometry, click center, reference radius, then target radius. Esc: cancel. |
| `Mirror` | Mirror | `mirror.png` | Exposed; may require context | Mirror: select whole geometry, click two points for the mirror axis. Esc: cancel. |

## Pattern

| Command ID | Button | Asset | State | Purpose / interaction |
|---|---|---|---|---|
| `LinearPattern` | Linear pattern | `linear-pattern.png` | Exposed; may require context | Linear pattern: select whole geometry, click from point, then spacing point. Creates three instances. Esc: cancel. |
| `CircularPattern` | Circular pattern | `circular-pattern.png` | Exposed; may require context | Circular pattern: select whole geometry, click center, reference point, then angle point. Creates three instances. Esc: cancel. |

## Constraints

| Command ID | Button | Asset | State | Purpose / interaction |
|---|---|---|---|---|
| `Coincident` | Coincident | `coincident.png` | Exposed; may require context | Coincident. Select two editable points. Works with preselection or post-selection; Esc cancels. |
| `Concentric` | Concentric | `concentric.png` | Exposed; may require context | Concentric. Select two circles or arcs. Works with preselection or post-selection; Esc cancels. |
| `Parallel` | Parallel | `parallel.png` | Exposed; may require context | Parallel. Select two lines or polyline segments. Works with preselection or post-selection; Esc cancels. |
| `Tangent` | Tangent | `tangent.png` | Exposed; may require context | Tangent. Select a line/circle/arc plus another circle/arc or line. Works with preselection or post-selection; Esc cancels. |
| `Horizontal` | Horizontal | `horizontal.png` | Exposed; may require context | Horizontal. Select one line or two editable points. Works with preselection or post-selection; Esc cancels. |
| `Vertical` | Vertical | `vertical.png` | Exposed; may require context | Vertical. Select one line or two editable points. Works with preselection or post-selection; Esc cancels. |
| `Perpendicular` | Perpendicular | `perpendicular.png` | Exposed; may require context | Perpendicular. Select two lines or polyline segments. Works with preselection or post-selection; Esc cancels. |
| `Equal` | Equal | `equal.png` | Exposed; may require context | Equal. Select two lines or two circle/arc radii. Works with preselection or post-selection; Esc cancels. |
| `Midpoint` | Midpoint | `midpoint.png` | Exposed; may require context | Midpoint. Select one line and one editable point. Works with preselection or post-selection; Esc cancels. |
| `Normal` | Normal | `normal.png` | Future / disabled | Normal. Not implemented yet. |
| `Symmetric` | Symmetric | `symmetric.png` | Future / disabled | Symmetric. Not implemented yet. |
| `Fix` | Fix | `fix.png` | Exposed; may require context | Fix. Select geometry or editable points to fix. Works with preselection or post-selection; Esc cancels. |

## Cleanup

| Command ID | Button | Asset | State | Purpose / interaction |
|---|---|---|---|---|
| `AutoCleanup` | Auto | `auto-cleanup.png` | Exposed; may require context | Find the minimum-area rotation with the long side on X, then move bounds minimum to origin. |
| `Rotate` | Free rotate | `rotate.png` | Exposed; may require context | Rotate: select whole geometry, click center, reference point, then target point. Esc: cancel. |
| `Rotate90Clockwise` | Rotate 90 CW | `rotate90-clockwise.png` | Exposed; may require context | Rotate 90 CW. |
| `Rotate90CounterClockwise` | Rotate 90 CCW | `rotate90-counter-clockwise.png` | Exposed; may require context | Rotate 90 CCW. |
| `BoundsToOrigin` | Bounds to origin | `bounds-to-origin.png` | Exposed; may require context | Move drawing bounds minimum to global origin. |
| `PointToOrigin` | Point to origin | `point-to-origin.png` | Exposed; may require context | Move the selected point to global origin. |
| `VectorToX` | Vector to X | `vector-to-x.png` | Exposed; may require context | Align the selected line, segment, or two selected points to global X. |
| `VectorToY` | Vector to Y | `vector-to-y.png` | Exposed; may require context | Align the selected line, segment, or two selected points to global Y. |
| `RemoveDuplicates` | Remove duplicates | `remove-duplicates.png` | Exposed; may require context | Remove exact duplicate cut entities. Different layers and referenced sketch geometry are preserved. |

## Grain

| Command ID | Button | Asset | State | Purpose / interaction |
|---|---|---|---|---|
| `GrainNone` | Grain none | `grain-none.png` | Exposed; may require context | Clear the grain annotation. |
| `GrainX` | Grain X | `grain-x.png` | Exposed; may require context | Mark grain along global X on the GRAIN layer. |
| `GrainY` | Grain Y | `grain-y.png` | Exposed; may require context | Mark grain along global Y on the GRAIN layer. |
| `GrainFromVector` | Grain vector | `grain-vector.png` | Exposed; may require context | Mark grain along the selected line, segment, or two selected points on the GRAIN layer. |

## Complete Asset Checklist

One row per CadIconName, including retained icons that are not currently exposed. A reserved icon is not proof that its command works.

| Asset ID | Filename | Current use |
|---|---|---|
| `NewFile` | `new-file.png` | Retained icon; not exposed by current palettes/chrome |
| `FolderOpen` | `folder-open.png` | Retained icon; not exposed by current palettes/chrome |
| `Sample` | `sample.png` | Retained icon; not exposed by current palettes/chrome |
| `Export` | `export.png` | Retained icon; not exposed by current palettes/chrome |
| `Hotkeys` | `hotkeys.png` | Tool hotkeys |
| `CollapseLeft` | `collapse-left.png` | Retained icon; not exposed by current palettes/chrome |
| `CollapseRight` | `collapse-right.png` | Retained icon; not exposed by current palettes/chrome |
| `ChevronUp` | `chevron-up.png` | Collapse docked tool group |
| `ChevronDown` | `chevron-down.png` | Expand docked tool group |
| `Measure` | `measure.png` | Measure |
| `Undo` | `undo.png` | Undo |
| `Redo` | `redo.png` | Redo |
| `Line` | `line.png` | Line |
| `MidpointLine` | `midpoint-line.png` | Midpoint line |
| `Rectangle` | `rectangle.png` | Two-point rectangle |
| `CenterRectangle` | `center-rectangle.png` | Center rectangle |
| `AlignedRectangle` | `aligned-rectangle.png` | Aligned rectangle |
| `Circle` | `circle.png` | Center circle |
| `ThreePointCircle` | `three-point-circle.png` | Three-point circle |
| `Ellipse` | `ellipse.png` | Ellipse |
| `Arc` | `arc.png` | Three-point arc |
| `TangentArc` | `tangent-arc.png` | Retained icon; not exposed by current palettes/chrome |
| `CenterPointArc` | `center-point-arc.png` | Center point arc |
| `EllipticalArc` | `elliptical-arc.png` | Elliptical arc |
| `Conic` | `conic.png` | Conic |
| `InscribedPolygon` | `inscribed-polygon.png` | Inscribed polygon |
| `CircumscribedPolygon` | `circumscribed-polygon.png` | Circumscribed polygon |
| `Spline` | `spline.png` | Spline |
| `Bezier` | `bezier.png` | Retained icon; not exposed by current palettes/chrome |
| `SplineControlPoint` | `spline-control-point.png` | Add spline point |
| `Point` | `point.png` | Point |
| `Text` | `text.png` | Text |
| `Construction` | `construction.png` | Construction |
| `Delete` | `delete.png` | Delete selected geometry |
| `Slot` | `slot.png` | Slot |
| `PowerTrim` | `power-trim.png` | Power trim/extend |
| `Split` | `split.png` | Split at point |
| `Offset` | `offset.png` | Offset |
| `Fillet` | `fillet.png` | Fillet |
| `Chamfer` | `chamfer.png` | Chamfer |
| `Dimension` | `dimension.png` | Dimension |
| `Move` | `move.png` | Translate |
| `Rotate` | `rotate.png` | Rotate; Free rotate |
| `Rotate90Clockwise` | `rotate90-clockwise.png` | Rotate 90 CW |
| `Rotate90CounterClockwise` | `rotate90-counter-clockwise.png` | Rotate 90 CCW |
| `Scale` | `scale.png` | Scale |
| `Mirror` | `mirror.png` | Mirror |
| `LinearPattern` | `linear-pattern.png` | Linear pattern |
| `CircularPattern` | `circular-pattern.png` | Circular pattern |
| `Coincident` | `coincident.png` | Coincident |
| `Concentric` | `concentric.png` | Concentric |
| `Parallel` | `parallel.png` | Parallel |
| `Tangent` | `tangent.png` | Tangent |
| `Horizontal` | `horizontal.png` | Horizontal |
| `Vertical` | `vertical.png` | Vertical |
| `Perpendicular` | `perpendicular.png` | Perpendicular |
| `Equal` | `equal.png` | Equal |
| `Midpoint` | `midpoint.png` | Midpoint |
| `Normal` | `normal.png` | Normal |
| `Pierce` | `pierce.png` | Retained icon; not exposed by current palettes/chrome |
| `Symmetric` | `symmetric.png` | Symmetric |
| `Fix` | `fix.png` | Fix |
| `Curvature` | `curvature.png` | Retained icon; not exposed by current palettes/chrome |
| `Fit` | `fit.png` | Fit extents; Fit |
| `AutoCleanup` | `auto-cleanup.png` | Auto |
| `BoundsToOrigin` | `bounds-to-origin.png` | Bounds to origin |
| `PointToOrigin` | `point-to-origin.png` | Point to origin |
| `OriginAxes` | `origin-axes.png` | Origin axes |
| `ShowConstraints` | `show-constraints.png` | Show constraints |
| `VectorToX` | `vector-to-x.png` | Vector to X |
| `VectorToY` | `vector-to-y.png` | Vector to Y |
| `GrainNone` | `grain-none.png` | Grain none |
| `GrainX` | `grain-x.png` | Grain X |
| `GrainY` | `grain-y.png` | Grain Y |
| `GrainVector` | `grain-vector.png` | Grain vector |
| `RemoveDuplicates` | `remove-duplicates.png` | Remove duplicates |

## Controls Outside the Palette

- `chevron-up.png`: **Collapse docked tool group**. Upward chevron; collapse the group's commands, retaining its header. Source: `WorkbenchToolPalette.razor`.
- `chevron-down.png`: **Expand docked tool group**. Downward chevron; show the group's commands. Source: `WorkbenchToolPalette.razor`.
- `application-menu.png`: **Open application menu**. Three horizontal menu bars; currently CSS, not CadIconName. Source: `MainLayout.razor`.
- `submenu-right.png`: **Open File/Cleanup submenu**. Right chevron; currently a text greater-than symbol. Source: `MainLayout.razor`.
- `close.png`: **Close dialog / dismiss error**. Simple X; shared asset across dismiss/close controls. Currently text. Source: `MainLayout.razor; ToolHotkeyOptions.razor`.
- `drag-grip.png`: **Move or dock tool group**. Compact grip marks; currently CSS, not a command enum. Source: `WorkbenchToolPalette.razor.css`.

Current text-only actions also exist: Open local DXF/DWG, Connect to Sync, Download DXF, Save to Sync, Connect, Cancel, Reset hotkeys and Close. They do not all have icon assignments; do not mistake proposed future artwork for existing bindings. Canvas geometry, grip points, measurement arrows and constraint annotations are dynamic drawing content, not static toolbar image assets.

## Command IDs Not in Either Palette

- `NewBlankDocument` (NewBlankDocument): not in either palette; see the audited notes below. No icon binding is inferred by name.
- `LoadSample` (LoadSample): not in either palette; see the audited notes below. No icon binding is inferred by name.
- `ExportDxfText` (ExportDxfText): not in either palette; see the audited notes below. No icon binding is inferred by name.
- `SaveDxf` (SaveDxf): not in either palette; see the audited notes below. No icon binding is inferred by name.
- `SendToSync` (SendToSync): not in either palette; see the audited notes below. No icon binding is inferred by name.
- `ReturnToSync` (ReturnToSync): not in either palette; see the audited notes below. No icon binding is inferred by name.
- `TangentArc` (Tangent arc): not in either palette; see the audited notes below. No icon binding is inferred by name.
- `Bezier` (Bezier): not in either palette; see the audited notes below. No icon binding is inferred by name.
- `Pierce` (Pierce): not in either palette; see the audited notes below. No icon binding is inferred by name.
- `Curvature` (Curvature): not in either palette; see the audited notes below. No icon binding is inferred by name.

Audited notes: SaveDxf is Download DXF in the File menu. SendToSync is Save to Sync in the menu/Sync bar. NewBlankDocument and LoadSample retain handlers but are absent from the current menu. ExportDxfText is retired and points users to Download DXF. ReturnToSync is a retained ID, not a current visible control. TangentArc/Bezier/Pierce/Curvature are absent from the current palette; retained names/icons must not be taken as validated tool availability.

# Tool Icon Audit Matrix

Use this matrix to track icon semantics separately from tool behavior. Mark an icon complete only when the SVG communicates the command accurately enough to avoid misleading the sketch workflow.

## Creation Icons

| Tool | Expected icon signal | Status | Evidence |
| --- | --- | --- | --- |
| Line | Two endpoint picks connected by a line. | Reviewed | Existing SVG has line plus endpoint markers. |
| Midpoint line | Center-origin or midpoint-driven line. | Reviewed | Existing SVG has midpoint marker and endpoints. |
| Two-point rectangle | Opposite corner picks. | Reviewed | Existing SVG has rectangle plus opposite corner markers. |
| Center rectangle | Center-driven rectangle. | Reviewed | Existing SVG has rectangle plus center cross. |
| Aligned rectangle | Baseline plus angled rectangle. | Reviewed | Existing SVG shows rotated rectangle and baseline pick points. |
| Center circle | Center plus circle perimeter. | Reviewed | Existing SVG has outer circle plus center marker. |
| Three-point circle | Three circumference picks. | Fixed | `CadIconGeometryTests.ThreePointCircleIconPlacesPickedPointsOnTheCircle`; running app DOM marker distances verified. |
| Ellipse | Center, major, and minor axis relationship. | Reviewed | Existing SVG shows ellipse, center, and dashed axes. |
| Three-point arc | Start, through, and end picks on the arc. | Fixed | `CadIconGeometryTests.ThreePointArcIconPlacesPickedPointsOnTheArc`; running app DOM marker distances verified. |
| Tangent arc | Tangent continuation from prior segment. | Reviewed | Existing SVG shows source line and tangent arc start. |
| Center-point arc | Center plus radial start/end points concentric with arc. | Fixed | `CadIconGeometryTests.CenterPointArcIconUsesDisplayedCenterAsArcCenter`; running app DOM path verified. |
| Elliptical arc | Partial ellipse with endpoints and axis hint. | Reviewed | Existing SVG shows dashed full ellipse, partial arc, axis line, and endpoints. |
| Conic | Three defining control/pick points and conic curve. | Reviewed | Existing SVG shows conic curve plus dashed construction triangle. |
| Inscribed polygon | Polygon vertices on guide circle. | Fixed | `CadIconGeometryTests.InscribedPolygonIconPlacesVerticesOnGuideCircle`; running app DOM distances verified. |
| Circumscribed polygon | Polygon sides tangent to guide circle. | Fixed | `CadIconGeometryTests.CircumscribedPolygonIconPlacesGuideCircleTangentToSides`; running app DOM distances verified. |
| Spline | Fit curve with endpoint handles only. | Reviewed | Existing SVG shows smooth curve and endpoint markers. |
| Spline control point | Spline curve plus editable control point. | Reviewed | Existing SVG shows spline curve and a marked control point. |
| Point | Single sketch point. | Reviewed | Existing SVG is a single filled point. |
| Slot | Slot outline with centerline. | Reviewed | Existing SVG shows a pill slot and centerline. |

## Modify And Constraint Icons

| Tool group | Status | Evidence |
| --- | --- | --- |
| Modify tools | Reviewed | Icons are symbolic command glyphs rather than geometry-construction previews; no concrete geometry mismatch was found in the current testing notes. |
| Transform tools | Reviewed | Icons use conventional move/rotate/scale/mirror symbols. |
| Pattern tools | Reviewed | Icons use repeated elements and circular arrow motifs. |
| Constraint tools | Reviewed | Icons use standard CAD relation symbols and the green confirmed-working styling remains absent. |

## Current Result

- Concrete icon defects from latest testing are fixed: three-point circle, three-point arc, center-point arc, inscribed polygon, and circumscribed polygon.
- Future subjective icon preferences should be logged as named icon defects before coding, then verified with this matrix plus running-app DOM or screenshot evidence.

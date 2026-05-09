# Entity Drag Constraint Matrix

Use this matrix for UT-SOLVER-005. Drag behavior is accepted only when the core result, canvas preview, and running app all follow the same constraint-driven rule.

Status values:

- `OPEN` means not yet covered for this matrix.
- `AUTO-PASSED` means focused automated tests pass.
- `APP-VERIFIED` means Codex verified it in the running app/canvas.
- `USER-PASSED` means the user reported it working.
- `N/A` means the grip or operation does not apply to that entity.

## Drag Rule

For every entity and grip, classify the requested drag into allowed degrees of freedom. Apply the candidate that satisfies active constraints and driving dimensions:

- If the grip is a translation grip, move the entity or welded entity group and move attached dimension anchors.
- If the grip is an edit grip, change only the dragged geometric degree of freedom when constraints allow it.
- If an edit component would violate a driving dimension, translate the constrained geometry and attached dimensions for that component instead.
- Coincident references move as welded points in preview and after release.
- Failed solves paint affected geometry, constraints, and dimensions red without corrupting geometry.

## Entity Coverage

| Entity | Translation Grips | Edit Grips | Dimensioned Drag | Coincident Weld | Core | Canvas Preview | Running App | User |
|---|---|---|---|---|---|---|---|---|
| Line | midpoint, whole line, blocked endpoint component | start, end | PARTIAL `DRAG-LINE-001` app-verified | PARTIAL line-chain endpoint weld | PARTIAL | PARTIAL | PARTIAL | USER-FAILED |
| Polyline | whole polyline, segment midpoint, segment, blocked vertex component | vertices | OPEN | OPEN | PARTIAL | PARTIAL | OPEN | USER-FAILED |
| Polygon | center, side midpoint, whole polygon | radius/apothem or vertex where supported | OPEN | OPEN | PARTIAL | PARTIAL | APP-VERIFIED for center/mid/whole | USER-FAILED |
| Circle | center | circumference/quadrant radius | OPEN | OPEN | PARTIAL | PARTIAL | APP-VERIFIED for center/circumference | USER-FAILED |
| Arc | center or whole arc where supported | start, end, perimeter radius/sweep | OPEN | OPEN | PARTIAL | PARTIAL | OPEN | USER-FAILED |
| Ellipse | center or whole ellipse | major/minor quadrant axes | OPEN | OPEN | PARTIAL | PARTIAL | OPEN | USER-FAILED |
| Spline | whole spline | fit points, control points, endpoint tangent handles | OPEN | OPEN | PARTIAL | PARTIAL | APP-VERIFIED for edit handles | USER-FAILED |
| Point | point location | N/A | OPEN | OPEN | PARTIAL | PARTIAL | OPEN | OPEN |
| Dimension | anchor | value edit through input | PARTIAL skip synthetic value commits | N/A | PARTIAL | PARTIAL | APP-VERIFIED for anchor skip-commit | USER-FAILED |
| Constraint glyph/reference | glyph anchor where supported | reference-driven geometry through solver | OPEN | OPEN | OPEN | OPEN | OPEN | USER-FAILED |

## Test Rows To Add

| ID | Scenario | Expected Result | Status |
|---|---|---|---|
| DRAG-LINE-001 | Drag endpoint of a line with a driving length dimension in the length-changing direction. | Length remains satisfied; blocked component translates the line and its dimension anchor or fails without geometry corruption if another constraint fixes translation. | APP-VERIFIED: core focused test passed, canvas preview test passed, rebuilt app verified line `605,418 -> 845,418` shifted to `701,418 -> 941,418`, dimension value stayed `10`, and dimension input anchor moved from `left: 725px` to `left: 821px`. |
| DRAG-LINE-002 | Drag endpoint of a line with a driving length dimension perpendicular to the current line and no angle constraint. | Solver chooses a valid dimension-preserving movement and paints no false failed state. | OPEN |
| DRAG-POLYLINE-001 | Drag one vertex of a partially dimensioned polyline segment. | Only allowed free geometry changes; constrained dimension component stays satisfied. | OPEN |
| DRAG-POLYGON-001 | Drag a dimensioned polygon center, midpoint, and whole entity. | Polygon translates, radius/count dimensions remain satisfied, anchors travel. | PARTIAL |
| DRAG-CIRCLE-001 | Drag a dimensioned circle center and circumference. | Center drag translates with dimensions; circumference drag changes radius only when driving dimensions allow it. | OPEN |
| DRAG-ARC-001 | Drag dimensioned arc endpoint and perimeter. | Radius/sweep changes only when driving dimensions and constraints allow them. | OPEN |
| DRAG-ELLIPSE-001 | Drag dimensioned ellipse center and major/minor quadrants. | Center translates with dimensions; axis grips change only allowed axis degrees of freedom. | OPEN |
| DRAG-SPLINE-001 | Drag dimensioned spline fit/control/tangent handles. | Only the selected spline handle moves unless constraints/dimensions require a stable failure. | OPEN |
| DRAG-COINCIDENT-001 | Drag coincident endpoints across mixed entities. | Preview and release keep references welded. | OPEN |
| DRAG-DIM-001 | Drag persistent dimension anchors after geometry edits and deleted constraints. | Anchor moves without recommitting value or reviving deleted constraints. | PARTIAL |

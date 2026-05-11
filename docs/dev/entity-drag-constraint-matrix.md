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
| Line | midpoint, whole line, blocked endpoint component | start, end | PARTIAL `DRAG-LINE-001` app-verified; point-to-line free direction auto-passed | PARTIAL line-chain endpoint weld | PARTIAL | PARTIAL | PARTIAL | USER-FAILED |
| Polyline | whole polyline, segment midpoint, segment, blocked vertex component | vertices | OPEN | OPEN | PARTIAL | PARTIAL | OPEN | USER-FAILED |
| Polygon | center, side midpoint, whole polygon | radius/apothem or vertex where supported | OPEN | OPEN | PARTIAL | PARTIAL | APP-VERIFIED for center/mid/whole | USER-FAILED |
| Circle | center | circumference/quadrant radius | PARTIAL driving radius-dimension edge drag auto-passed | OPEN | PARTIAL | PARTIAL | APP-VERIFIED for center/circumference | USER-FAILED |
| Arc | center or whole arc where supported | start, end, perimeter radius/sweep | PARTIAL arc sweep-dimension translation auto-passed | OPEN | PARTIAL | PARTIAL | OPEN | USER-FAILED |
| Ellipse | center or whole ellipse | major/minor quadrant axes | PARTIAL driving major-axis drag auto-passed | OPEN | PARTIAL | PARTIAL | OPEN | USER-FAILED |
| Spline | whole spline | fit points, control points, endpoint tangent handles | OPEN | OPEN | PARTIAL | PARTIAL | APP-VERIFIED for edit handles | USER-FAILED |
| Point | point location | N/A | OPEN | OPEN | PARTIAL | PARTIAL | OPEN | OPEN |
| Dimension | anchor | value edit through input | PARTIAL skip synthetic value commits | N/A | PARTIAL | PARTIAL | APP-VERIFIED for anchor skip-commit | USER-FAILED |
| Constraint glyph/reference | glyph anchor where supported | reference-driven geometry through solver | PARTIAL Fix/Equal/Concentric/Midpoint/Tangent auto-passed | PARTIAL coincident/axis/relation plus driven constraint propagation | PARTIAL | PARTIAL | OPEN | USER-FAILED |

## Test Rows To Add

| ID | Scenario | Expected Result | Status |
|---|---|---|---|
| DRAG-LINE-001 | Drag endpoint of a line with a driving length dimension in the length-changing direction. | Length remains satisfied; blocked component translates the line and its dimension anchor or fails without geometry corruption if another constraint fixes translation. | APP-VERIFIED: core focused test passed, canvas preview test passed, rebuilt app verified line `605,418 -> 845,418` shifted to `701,418 -> 941,418`, dimension value stayed `10`, and dimension input anchor moved from `left: 725px` to `left: 821px`. |
| DRAG-LINE-002 | Drag endpoint of a line with a driving length dimension perpendicular to the current line and no angle constraint. | Solver chooses a valid dimension-preserving movement and paints no false failed state. | OPEN |
| DRAG-DIM-002 | Drag a line constrained by a driving point-to-line/parallel-line distance along the free parallel direction. | The selected line moves only along the free direction, the distance remains satisfied, and the dimension anchor travels with the referenced entity. | AUTO-PASSED: core and canvas-preview focused tests passed. |
| DRAG-POLYLINE-001 | Drag one vertex of a partially dimensioned polyline segment. | Only allowed free geometry changes; constrained dimension component stays satisfied. | OPEN |
| DRAG-POLYGON-001 | Drag a dimensioned polygon center, midpoint, and whole entity. | Polygon translates, radius/count dimensions remain satisfied, anchors travel. | PARTIAL |
| DRAG-CIRCLE-001 | Drag a dimensioned circle center and circumference. | Center drag translates with dimensions; circumference drag changes radius only when driving dimensions allow it. | PARTIAL AUTO-PASSED: circumference drag with a driving radius dimension translates the circle and dimension anchor while preserving radius. |
| DRAG-ARC-001 | Drag dimensioned arc endpoint and perimeter. | Radius/sweep changes only when driving dimensions and constraints allow them. | PARTIAL AUTO-PASSED: endpoint drag against a driving arc sweep dimension translates the arc and dimension anchor instead of breaking the sweep. |
| DRAG-ELLIPSE-001 | Drag dimensioned ellipse center and major/minor quadrants. | Center translates with dimensions; axis grips change only allowed axis degrees of freedom. | PARTIAL AUTO-PASSED: major-axis drag with a driving axis dimension translates the ellipse and dimension anchor while preserving axis length. |
| DRAG-SPLINE-001 | Drag dimensioned spline fit/control/tangent handles. | Only the selected spline handle moves unless constraints/dimensions require a stable failure. | OPEN |
| DRAG-COINCIDENT-001 | Drag coincident endpoints across mixed entities. | Preview and release keep references welded. | OPEN |
| DRAG-DIM-001 | Drag persistent dimension anchors after geometry edits and deleted constraints. | Anchor moves without recommitting value or reviving deleted constraints. | PARTIAL |
| DRAG-CONSTRAINT-001 | Drag geometry fixed by a Fix constraint. | Fixed references do not preview or commit movement. | AUTO-PASSED: core whole-line drag and canvas-preview regressions passed. |
| DRAG-CONSTRAINT-002 | Drag Equal-constrained line/circle-like geometry. | Peer length/radius updates or the drag safely fails if fixed. | AUTO-PASSED: core equal line/radius and canvas equal-line preview regressions passed. |
| DRAG-CONSTRAINT-003 | Drag Concentric, Midpoint, or Tangent constrained geometry. | The driven peer reference updates to preserve the active relation or safely fails if fixed. | AUTO-PASSED: core and canvas-preview regressions passed for concentric, midpoint, and tangent propagation. |

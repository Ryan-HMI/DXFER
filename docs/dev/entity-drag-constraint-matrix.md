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
| Polyline | whole polyline, segment midpoint, segment, blocked vertex component | vertices | PARTIAL indexed horizontal/linear dimensions app-verified | PARTIAL line/polyline/point weld | PARTIAL | PARTIAL | PARTIAL | USER-FAILED |
| Polygon | center, side midpoint, whole polygon | radius/apothem or vertex where supported | PARTIAL driving radius/count translation app-verified | PARTIAL center/line-start weld and Fixed refusal | PARTIAL | PARTIAL | APP-VERIFIED bounded center/mid/whole fixtures | USER-FAILED |
| Circle | center | circumference/quadrant radius | PARTIAL driving radius translation and reference radius resizing app-verified | PARTIAL center/line-start weld and Fixed refusal | PARTIAL | PARTIAL | APP-VERIFIED bounded center/quadrant fixtures | USER-FAILED |
| Arc | center or whole arc where supported | start, end, perimeter radius/sweep | PARTIAL driving radius/sweep and fixed-center Shift sweep app-verified | PARTIAL arc-end/line-start weld and Fixed refusal | PARTIAL | PARTIAL | APP-VERIFIED bounded fixtures | USER-FAILED |
| Ellipse | center or whole ellipse | major/minor quadrant axes | PARTIAL independent orthogonal driving diameters/Fixed center app-verified | PARTIAL center/line-start app-verified | PARTIAL | PARTIAL | APP-VERIFIED bounded axis/weld fixtures | USER-FAILED |
| Spline | whole spline | fit points, control points, endpoint tangent handles | PARTIAL live indexed span/Fixed cases independently accepted on3547ab4 | OPEN | PARTIAL | PARTIAL | APP-VERIFIED for edit handles | USER-FAILED |
| Point | point location | N/A | OPEN | OPEN | PARTIAL | PARTIAL | OPEN | OPEN |
| Dimension | anchor | value edit through input | PARTIAL skip synthetic value commits | N/A | PARTIAL | PARTIAL | APP-VERIFIED for anchor skip-commit | USER-FAILED |
| Constraint glyph/reference | glyph anchor where supported | reference-driven geometry through solver | PARTIAL Fix/Equal/Concentric/Midpoint/Tangent auto-passed | PARTIAL coincident/axis/relation plus driven constraint propagation | PARTIAL | PARTIAL | OPEN | USER-FAILED |

## Test Rows To Add

| ID | Scenario | Expected Result | Status |
|---|---|---|---|
| DRAG-LINE-001 | Drag endpoint of a line with a driving length dimension in the length-changing direction. | Length remains satisfied; blocked component translates the line and its dimension anchor or fails without geometry corruption if another constraint fixes translation. | APP-VERIFIED: core focused test passed, canvas preview test passed, rebuilt app verified line `605,418 -> 845,418` shifted to `701,418 -> 941,418`, dimension value stayed `10`, and dimension input anchor moved from `left: 725px` to `left: 821px`. |
| DRAG-LINE-002 | Drag endpoint of a line with a driving length dimension perpendicular to the current line and no angle constraint. | Solver chooses a valid dimension-preserving movement and paints no false failed state. | APP-VERIFIED bounded free translation/fixed-start rotation, preview/release/history/native reopen/reedit. See anchored-and-welded-drag-2026-10-15.md. |
| DRAG-DIM-002 | Drag a line constrained by a driving point-to-line/parallel-line distance along the free parallel direction. | The selected line moves only along the free direction, the distance remains satisfied, and the dimension anchor travels with the referenced entity. | AUTO-PASSED: core and canvas-preview focused tests passed. |
| DRAG-POLYLINE-001 | Drag one vertex of a partially dimensioned polyline segment. | Only allowed free geometry changes; constrained dimension component stays satisfied. | APP-VERIFIED bounded horizontal-width/fixed-endpoint fixture AND actual toolbar-created LinearDistance 10 L-shape, history/native reopen/reedit. Stale click-coordinate validation fixed; old corrupted fixture shows failure without auto-repair. Closed aliases/coupled dimensions remain open. |
| DRAG-POLYGON-001 | Drag a dimensioned polygon center, midpoint, and whole entity. | Polygon translates, radius/count dimensions remain satisfied, anchors travel. | APP-VERIFIED bounded driving radius5/count6, free/Fixed/center-weld/Fixed-peer for all three grips; held preview, refusal/no Undo, history/native reopen/reedit. Core/JS reversed order and tiny/zero-delta cases pass. Fix-removal recovery is automated, not independently browser-accepted. See polygon-drag-hardening-2026-10-16.md. No source defect reproduced; arbitrary coupled and edit-grip cases remain open. |
| DRAG-CIRCLE-001 | Drag a dimensioned circle center and circumference. | Center drag translates with dimensions; circumference drag changes radius only when driving dimensions allow it. | APP-VERIFIED bounded center/quadrant drivingR3 translation, center-weld/Fixed-peer refusal and referenceR3->4 with free/Fixed center; held preview, history/native reopen/reedit. Shared preview reference-label refresh fixes stale measurements before release. See circle-drag-hardening-2026-10-16.md for installed status. Other quadrant/edge picks and arbitrary coupled systems remain open. |
| DRAG-ARC-001 | Drag dimensioned arc endpoint and perimeter. | Radius/sweep changes only when driving dimensions and constraints allow them. | APP-VERIFIED bounded radius3/sweep90 welded-line translation, Fixed-peer refusal, fixed-center Shift sweep and zero-direction refusal/recovery; held-pointer/release/history/native reopen/reedit. Core/preview reversed order and near-zero tests pass. See arc-drag-direction-2026-10-15.md. Broader coupled arcs remain open. |
| DRAG-ELLIPSE-001 | Drag dimensioned ellipse center and major/minor quadrants. | Center translates with dimensions; axis grips change only allowed axis degrees of freedom. | APP-VERIFIED independent free axis with opposite driving diameter/Fixed center, both-axis refusal, degenerate refusal/recovery, center weld/fixed-peer and preview/release/history/native reopen/reedit. Reverse-order core/preview pass. See ellipse-independent-drag-2026-10-15.md. Partial ellipses/arbitrary coupling remain open. |
| DRAG-SPLINE-001 | Drag dimensioned spline fit/control/tangent handles. | Preserve driving intent through a supported movement or refuse unchanged. | APP-VERIFIED bounded indexed fit/control/tangent span preservation through translation, Fixed refusal, held-pointer/release/history/native reopen/reedit. See dimensioned-spline-references-2026-10-15.md. Arbitrary coupled spline solving remains open. |
| DRAG-COINCIDENT-001 | Drag coincident endpoints across mixed entities. | Preview and release keep references welded. | APP-VERIFIED dimensioned line -> polyline -> point weld, fixed-peer refusal; normal/reversed order in core/preview. Other mixed families remain open. |
| DRAG-DIM-001 | Drag persistent dimension anchors after geometry edits and deleted constraints. | Anchor moves without recommitting value or reviving deleted constraints. | APP-VERIFIED deleted-Vertical line case retained: endpoint and label drag, no revived constraint; Undo/Redo. Not reopened by this batch. |
| DRAG-CONSTRAINT-001 | Drag geometry fixed by a Fix constraint. | Fixed references do not preview or commit movement. | AUTO-PASSED: core whole-line drag and canvas-preview regressions passed. |
| DRAG-CONSTRAINT-002 | Drag Equal-constrained line/circle-like geometry. | Peer length/radius updates or the drag safely fails if fixed. | APP-VERIFIED: independent Flux Equal-circle/Fixed regression accepted on e94f6ee; refusal restores preview/no Undo, remove Fix allows both radii, history/export pass. Core/preview equal-line tests retained. Broader combinations remain open. |
| DRAG-CONSTRAINT-003 | Drag Concentric, Midpoint, or Tangent constrained geometry. | The driven peer reference updates to preserve the active relation or safely fails if fixed. | PARTIAL APP-VERIFIED: fixed midpoint symmetric recovery, fixed-line tangent with/without diameter, near-zero radius refusal, Equal/Tangent/Fixed and history/native reopen/reedit. Constraint-order core/preview tests pass. Flux independently accepted tangent collapse/recovery and Concentric fixed-peer radius freedom/center refusal/removeFix shared-center movement, history/native reopen on b45d9e5. Arbitrary combinations remain open. |

Prior PowerTrim driving-value protection is accepted independently on reported
`0eca667`: Trim/Extend/Split driving 10 refuse unchanged/no Undo; reference
fixtures update measurements, with history/native reopen and clean DXF audits.
That acceptance is retained, not put back on the open drag backlog.

Flux independently accepted all six spline fit/control/tangent free/Fixed
fixtures on3547ab4, plus Equal-opposite-edge rectangle conflict/recovery,
Undo/Redo and native reopen/reedit. Held-pointer evidence remains owner-only.
The separate redundant-versus-conflicting diagnostic correction is installed
on Dev da62081 with actual browser history/reopen verification. These are
bounded acceptances, not a blanket pass of the entity coverage table.

The next Equal/Midpoint/Tangent + Fixed results and concrete fixtures are in
`live-polyline-references-2026-10-15.md`. Fixed-midpoint symmetric endpoint
recovery is now implemented and has paired valid/incompatible fixtures. Arbitrary
coupled projection and cases without a unique changed endpoint remain outside
that bounded solve. Do not equate safe refusal with mathematical inconsistency.

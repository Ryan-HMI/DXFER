# User Test Tracking

Use this file to separate what the user has actually tested from what is only automated-tested or app-verified by Codex. Do not mark an item `USER-PASSED` unless the user reports it working. Do not mark an item `APP-VERIFIED` until it has been tested in the running app or canvas.

## Legend

- `USER-FAILED` - user reported the behavior still fails.
- `USER-REQUESTED` - user requested a behavior/UI change that still needs implementation.
- `USER-PASSED` - user reported the behavior works.
- `CODED` - implementation exists, but final verification is not complete.
- `AUTO-PASSED` - focused automated tests pass.
- `APP-VERIFIED` - Codex verified the behavior in the running app/canvas.
- `OPEN` - not fixed yet.

## Active Lane: Trim And Extend

| ID | Behavior | User Status | Automated Status | App Status | Current Stage |
|---|---|---|---|---|---|
| UT-TRIM-001 | Polygon/poly trim explodes trimmed polygon into line entities and does not leave stale solver references. | USER-FAILED | AUTO-PASSED | APP-VERIFIED | Coded and app-verified; awaiting user retest |
| UT-TRIM-002 | Power Trim hover has enough off-end tolerance and shows when the click will extend instead of trim. | USER-REQUESTED | AUTO-PASSED | APP-VERIFIED | Coded and app-verified; awaiting user retest |
| UT-TRIM-003 | Spline trim responsiveness is acceptable. | USER-FAILED | AUTO-PASSED | APP-VERIFIED | Coded and app-verified; awaiting user retest |
| UT-TRIM-004 | Arc extend reaches ellipse/elliptical-arc boundaries across larger angular spans. | USER-FAILED | AUTO-PASSED | APP-VERIFIED | Coded and app-verified; awaiting user retest |

## Needs User Testing Handoff - 2026-05-07

These rows are coded, automated-tested, and verified in the running canvas by Codex, but still need user retest before they can be marked `USER-PASSED`.

| ID | User Retest Focus |
|---|---|
| UT-TRIM-001 | Trim polygons/polys against cutters and confirm the result is exploded line geometry without solver breakage. |
| UT-TRIM-002 | Hover just past open entity endpoints in Power Trim and confirm the cyan extend indicator appears before extending instead of trimming. |
| UT-TRIM-003 | Trim splines with common cutters and confirm interaction latency is acceptable and kept spans land on the cutter. |
| UT-TRIM-004 | Extend arcs to ellipse/elliptical-arc boundaries, especially larger angular spans. |
| UT-SPLINE-001 | Select and drag spline fit points plus endpoint tangent handles after creation. |
| UT-LINEARC-001 | Continue a line into tangent-arc mode by hovering the last vertex, verify no mirrored preview line, and confirm tangent-arc key-in creates a radius dimension. |

These rows are still open and should not be treated as fixed yet.

| ID | Open Work |
|---|---|
| UT-SOLVER-001 | Dimensioned geometry drag/deletion robustness. |
| UT-SOLVER-002 | Coincident constraint vertex welding. |
| UT-ELLIPSE-001 | Keyed ellipse creation solve state. |
| UT-SOLVER-003 | Deleted vertical constraint reasserting through dimension dragging. |
| UT-UI-001 | Dock width and collapsible left/right docked panels. |
| UT-ICON-001 | Larger icons and removed hotkey labels. |
| UT-ICON-002 | Requested individual icon redraws. |

## Queued User-Tested Failures

| ID | Behavior | User Status | Automated Status | App Status | Current Stage |
|---|---|---|---|---|---|
| UT-SPLINE-001 | Spline tangent handles and fit points draw, select, and drag-edit correctly. | USER-FAILED | AUTO-PASSED | APP-VERIFIED | Coded and app-verified; awaiting user retest |
| UT-LINEARC-001 | Line/tangent-arc preview mode switching, vertex drawing, and keyed tangent-arc dimension creation are coherent. | USER-FAILED | AUTO-PASSED | APP-VERIFIED | Coded and app-verified; awaiting user retest |
| UT-SOLVER-001 | Dragging dimensioned geometry respects dimensions and deleting dimensions clears stale broken solve state. | USER-FAILED | OPEN | OPEN | Queued |
| UT-SOLVER-002 | Coincident constraints weld vertices for drag/solve behavior. | USER-FAILED | OPEN | OPEN | Queued |
| UT-ELLIPSE-001 | Keyed-in ellipse creation does not create broken solves. | USER-FAILED | OPEN | OPEN | Queued |
| UT-SOLVER-003 | Deleted vertical constraints do not reassert through associated dimension dragging. | USER-FAILED | OPEN | OPEN | Queued |
| UT-UI-001 | Left/right docked toolbars have no stray horizontal scrollbar and can collapse. | USER-REQUESTED | OPEN | OPEN | Queued |
| UT-ICON-001 | Toolbar icons are larger and hotkey labels no longer block icon art. | USER-REQUESTED | OPEN | OPEN | Queued |
| UT-ICON-002 | Construction, cut-at-point, scale, pattern, circular pattern, rotate, and text icons match requested artwork. | USER-REQUESTED | OPEN | OPEN | Queued |

## Update Rules

- Keep user status separate from Codex verification. A Codex app check can move `App Status` to `APP-VERIFIED`, but only the user can move `User Status` to `USER-PASSED`.
- When a new user report arrives mid-task, add it here and to `docs/dev/bug-feature-log.md`, then continue the active task unless it directly applies to that task.
- When a patch is coded, update the row to `CODED`; after focused tests pass, update to `AUTO-PASSED`; after running-app/canvas verification, update to `APP-VERIFIED`.
- After finishing one active row, pick the next open row from this tracker before going idle.

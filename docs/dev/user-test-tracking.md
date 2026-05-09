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
| UT-TRIM-001 | Polygon/poly trim explodes trimmed polygon into line entities, removes only the picked exploded segment, and does not leave stale solver references. | USER-FAILED | AUTO-PASSED | CODED | Follow-up picked-side fix coded and auto-passed; cutter hits on non-picked polygon sides should no longer add extra splits |
| UT-TRIM-002 | Power Trim hover has enough off-end tolerance and shows when the click will extend instead of trim without drawing stale/off-tolerance preview lines. | USER-FAILED | CODED | CODED | Curve/conic extend indicator no longer draws the cursor connector; needs app/user hover retest |
| UT-TRIM-003 | Spline trim responsiveness is acceptable. | USER-FAILED | AUTO-PASSED | APP-VERIFIED | Coded and app-verified; awaiting user retest |
| UT-TRIM-004 | Arc extend reaches ellipse/elliptical-arc boundaries across larger angular spans. | USER-FAILED | AUTO-PASSED | APP-VERIFIED | Coded and app-verified; awaiting user retest |
| UT-TRIM-005 | Extending conics does not leave persistent sample/control points or stale conic trim vertices behind. | USER-FAILED | AUTO-PASSED | CODED | Generated sampled splines hide leaked control points; needs app/user retest |
| UT-TRIM-006 | Deleted conic targets/cutters are removed from trim hit-testing so later ellipse trims cannot snap to old conic vertices. | USER-FAILED | CODED | CODED | Likely stale generated-point source suppressed; needs focused app/user retest and deeper cache work if still reproducible |
| UT-TRIM-007 | Exploded polygon trim output preserves coincident constraints at shared exploded vertices. | USER-FAILED | AUTO-PASSED | CODED | Coded and auto-passed; local-boundary fix avoids extra constraints from unrelated overlaps |
| UT-SPLINE-002 | Moving spline tangent handles changes handle geometry only, not the adjacent fit point placement, and moving adjacent fit points does not rewrite endpoint handle vectors. | USER-FAILED | AUTO-PASSED | CODED | Independent endpoint tangent handles and smoother fit-spline sampling coded and auto-passed; needs app/user retest |

## Needs User Testing Handoff - 2026-05-07

These rows are coded, automated-tested, and verified in the running canvas by Codex, but still need user retest before they can be marked `USER-PASSED`.

| ID | User Retest Focus |
|---|---|
| UT-TRIM-001 | Trim polygons/polys against cutters and confirm only the clicked exploded segment disappears, with no extra splits from unrelated overlapping geometry. |
| UT-TRIM-002 | Hover just past open entity endpoints in Power Trim and confirm the cyan extend indicator appears before extending instead of trimming. |
| UT-TRIM-003 | Trim splines with common cutters and confirm interaction latency is acceptable and kept spans land on the cutter. |
| UT-TRIM-004 | Extend arcs to ellipse/elliptical-arc boundaries, especially larger angular spans. |
| UT-TRIM-005 | Extend conics/splines and confirm sampled/control-point dots do not remain stuck on the extended curve. |
| UT-TRIM-006 | Delete a conic after extension/trim, then trim an ellipse nearby and confirm no old conic vertex is targetable. |
| UT-TRIM-007 | Trim a polygon into exploded line segments and confirm shared vertices carry coincident constraints. |
| UT-SPLINE-002 | Drag spline endpoint tangent handles and confirm adjacent fit points do not move; drag adjacent fit points and confirm endpoint handle vector/magnitude stays put. |
| UT-SOLVER-004 | Retest polygon center/midpoint/whole free-drag, dimensioned rectangle edge/vertex rigid translation with dimensions staying satisfied, undimensioned rectangle edge/corner resize including the live drag preview, and circle center/circumference drag behavior. |
| UT-SOLVER-005 | Retest drag behavior by constraint/dimension logic across all applicable entities, not just rectangles: point, edge, center, circumference/quadrant, fit/control/tangent handle, dimension-anchor, and coincident-weld drags. |
| UT-SOLVER-006 | Add a diagonal dimension across width/height constrained rectangle geometry and confirm it renders red/unsatisfied as an overconstrained derived dimension. |
| UT-SOLVER-001 | Resize/drag a rectangle, add/edit width and height dimensions, retroactively dimension a rectangle side from a midpoint/edge pick, and add an angle dimension between already-constrained rectangle sides; side dimensions should stay satisfied and the redundant angle should render red/unsatisfied. Broader dimensioned drag/deletion checks still remain open. |
| UT-ELLIPSE-001 | Create keyed ellipses and confirm persistent dimensions/geometry do not immediately draw red. |
| UT-SPLINE-001 | Select and drag spline fit points plus endpoint tangent handles after creation. |
| UT-LINEARC-001 | Continue a line into tangent-arc mode by hovering the last vertex, verify no mirrored preview line, and confirm tangent-arc key-in creates a radius dimension. |
| UT-UI-001 | Retest left/right side-docked toolbar groups: compact/expand buttons should collapse vertically to a header-only group with a single chevron, double-clicking the drag header should not trigger collapse or leave a drag state, and top/bottom/floating groups should not show dead collapse controls. |
| UT-ICON-001 | Confirm toolbar icon art size feels right and no visible hotkey labels obscure the icons. |
| UT-ICON-002 | Visually evaluate the reworked construction, split/scissors, scale, linear pattern, circular pattern, rotate, and text icon motifs. |

These rows are still open and should not be treated as fixed yet.

| ID | Open Work |
|---|---|
| UT-SOLVER-001 | Dimensioned geometry drag/deletion robustness. Rectangle side dimension edits after resize/drag, retroactive rectangle side dimensioning, redundant rectangle-side angle failure painting, and one-/two-dimension rectangle edge and vertex drag anchor travel are app-verified. Broader dimension dragging and deletion cleanup still remain open. |
| UT-SOLVER-004 | Uniform entity drag behavior for all applicable entity geometry. Rectangle partial-dimension edge drag, partial-dimension vertex decomposition, and full-dimension drag coherence are app-verified, and polygon center/midpoint/whole drag plus circle center/circumference drag are app-verified. Remaining entity classes still need entity-by-entity passes. |
| UT-SOLVER-005 | Entity-agnostic constraint-driven drag planner. `DRAG-LINE-001` line endpoint dimension-preserving fallback is app-verified; current rectangle-specific decomposition must still be generalized so every applicable entity and grip follows the same constraint/dimension decision model. |
| UT-SOLVER-006 | Diagonal derived dimensions should be overconstrained when the referenced geometry is already fixed by width/height dimensions. |
| UT-SOLVER-007 | After editing a driving dimension value, dragging the associated geometry keeps dimension graphics/anchors traveling with that geometry. |
| UT-SOLVER-002 | Coincident constraint vertex welding. Line-chain endpoint welding is app-verified for preview and release; broader mixed-entity coincidence coverage remains open. |
| UT-SOLVER-003 | Deleted vertical constraint reasserting through dimension dragging. Persistent dimension anchor drag skip-commit is app-verified; exact deleted-vertical workflow still needs user retest. |
| UT-CONSTRAINT-001 | Fix constraints pin/fix referenced geometry during drag/solve. |
| UT-CONSTRAINT-002 | Equal constraints enforce equal geometry during drag/solve. |

## Queued User-Tested Failures

| ID | Behavior | User Status | Automated Status | App Status | Current Stage |
|---|---|---|---|---|---|
| UT-SPLINE-001 | Spline tangent handles and fit points draw, select, and drag-edit correctly. | USER-FAILED | AUTO-PASSED | APP-VERIFIED | Coded and app-verified; awaiting user retest |
| UT-LINEARC-001 | Line/tangent-arc preview mode switching, vertex drawing, and keyed tangent-arc dimension creation are coherent. | USER-FAILED | AUTO-PASSED | APP-VERIFIED | Coded and app-verified; awaiting user retest |
| UT-SOLVER-001 | Dragging dimensioned geometry respects dimensions, retroactive side dimensions can be added, redundant constrained dimensions paint failed, and deleting dimensions clears stale broken solve state. | USER-FAILED | PARTIAL | PARTIAL | Rectangle partial-dimension edge drag, partial-dimension vertex decomposition, and two-dimension anchor travel are coded, auto-passed, and app-verified. Retroactive rectangle side dimensioning and redundant rectangle-side angle failure painting remain app-verified; broader non-rectangle dimension drag/deletion robustness remains queued |
| UT-SOLVER-004 | Entity-specific drag grips behave uniformly: centers/midpoints translate, circle circumference edits diameter/radius, polygons can be free-dragged, dimensioned rectangle vertex drags do not break the solve, and point/edge drags edit only the dragged geometry. | USER-FAILED | PARTIAL | PARTIAL | Rectangle partial-dimension edge drag, partial-dimension vertex decomposition, and full-dimension drag coherence are coded, auto-passed, and app-verified. Polygon center/midpoint/whole drag and circle center/circumference drag are coded, auto-passed, and app-verified; broader entity matrix still open |
| UT-SOLVER-005 | Drag behavior is determined by current solver constraints and grip degrees of freedom across every applicable entity type, not by rectangle-only special cases. | USER-REQUESTED | PARTIAL | PARTIAL | `DRAG-LINE-001` line endpoint dimension-preserving fallback coded, auto-passed, and app-verified; broader entity/grip matrix remains open |
| UT-SOLVER-006 | Diagonal point-to-point driving dimensions over already width/height constrained geometry are marked overconstrained/unsatisfied. | USER-FAILED | OPEN | OPEN | Logged; focused solver reproduction next |
| UT-SOLVER-007 | Persistent dimension graphics/anchors travel with geometry after the driving dimension value has been edited. | USER-FAILED | OPEN | OPEN | Logged; queued behind active UI worker |
| UT-SOLVER-002 | Coincident constraints weld vertices for drag/solve behavior. | USER-FAILED | PARTIAL | PARTIAL | Line-chain coincident endpoint drag is coded, auto-passed, and app-verified for live preview plus server release; broader mixed-entity coincidence matrix remains queued |
| UT-ELLIPSE-001 | Keyed-in ellipse creation does not create broken solves or red failed-state geometry on creation. | USER-FAILED | AUTO-PASSED | CODED | Custom keyed ellipse axis dimensions resolve as satisfied; needs app/user retest |
| UT-SOLVER-003 | Deleted vertical constraints do not reassert through associated dimension dragging. | USER-FAILED | PARTIAL | PARTIAL | Persistent dimension anchor drag now skips synthetic blur/change value commits, is auto-passed, and is app-verified with no value-change callback; exact deleted-vertical workflow still queued for user retest |
| UT-CONSTRAINT-001 | Fix constraints pin/fix referenced geometry during drag/solve. | USER-FAILED | OPEN | OPEN | Logged; queued behind active UI worker |
| UT-CONSTRAINT-002 | Equal constraints enforce equal geometry during drag/solve. | USER-FAILED | OPEN | OPEN | Logged; queued behind active UI worker |
| UT-UI-001 | Left/right docked toolbars have no stray horizontal scrollbar and can collapse. | USER-REQUESTED | AUTO-PASSED | APP-VERIFIED | Header-only vertical side collapse with single chevrons coded and app-verified; double-click header no longer triggers compaction or leaves drag state; awaiting user retest |
| UT-ICON-001 | Toolbar icons are larger and hotkey labels no longer block icon art. | USER-REQUESTED | AUTO-PASSED | APP-VERIFIED | Visible hotkey labels removed and icon art enlarged; awaiting user visual eval |
| UT-ICON-002 | Construction, cut-at-point, scale, pattern, circular pattern, rotate, and text icons match requested artwork. | USER-REQUESTED | AUTO-PASSED | APP-VERIFIED | Requested motifs coded and DOM-verified; awaiting user visual eval |

## Update Rules

- Keep user status separate from Codex verification. A Codex app check can move `App Status` to `APP-VERIFIED`, but only the user can move `User Status` to `USER-PASSED`.
- When a new user report arrives mid-task, add it here and to `docs/dev/bug-feature-log.md`, then continue the active task unless it directly applies to that task.
- When a patch is coded, update the row to `CODED`; after focused tests pass, update to `AUTO-PASSED`; after running-app/canvas verification, update to `APP-VERIFIED`.
- After finishing one active row, pick the next open row from this tracker before going idle.

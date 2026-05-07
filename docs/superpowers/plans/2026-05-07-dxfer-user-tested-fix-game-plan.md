# DXFER User-Tested Fix Game Plan

Created: 2026-05-07

Source: latest user testing pass. This plan supersedes any stale "complete" claim for reopened user-tested failures. Work must proceed as incremental targeted patches, with automated tests and running-app/canvas verification before moving each item to done.

## Ground Rules

- Log every new user bug/feature in `docs/dev/bug-feature-log.md` and `docs/dev/user-test-tracking.md`.
- Do not switch away from the active patch unless the new input directly applies to it.
- For each tool/feature, evaluate every applicable entity type: line, polyline, polygon, circle, arc, ellipse, spline, point, dimension, and constraint glyph/reference.
- Do not mark a row complete from automated tests alone. Canvas-facing work needs running-app verification.
- Keep user status distinct from Codex status. `USER-PASSED` only comes from the user.

## Live Progress

- [x] UT0 tracking docs: create this plan, update the bug/feature log, and add user-test status tracking.
- [x] UT1a polygon trim explosion: trimming a polygon/poly target now replaces the affected polygon with line entities, not an open polyline, and removes stale sketch references to the deleted polygon. User retest remains pending in `docs/dev/user-test-tracking.md`.
- [x] UT1b extend hover affordance: increased usable off-end tolerance and shows a visual extension indicator before the click. User retest remains pending in `docs/dev/user-test-tracking.md`.
- [x] UT1c arc-to-ellipse/elliptical-arc extend: large-span arc extension to ellipse-family boundaries now records both valid before-start and after-end unwrapped boundary parameters instead of dropping the far-side candidate. User retest remains pending in `docs/dev/user-test-tracking.md`.
- [x] UT1d spline trim performance: reduced spline sample recomputation and degree-1 oversampling in spline trim/render paths. User retest remains pending in `docs/dev/user-test-tracking.md`.
- [x] UT2 spline edit handles: fit points and endpoint tangent handles now render as selectable point targets, drag previews update the spline locally, server-side point drag persists the edit, and whole-spline translations are supported. Evidence: focused JS spline selection/preview tests passed, focused core spline drag tests passed, `SketchGeometryDragServiceTests|CanvasDocumentDtoTests` passed 28/28, `DXFER.slnx` rebuilt cleanly, and the rebuilt app verified a fresh toolbar-created spline with `fit-1` dragging `(4,0) -> (4,2)`, then `tangent-start` dragging `(1,0.5) -> (1,1)` and moving the adjacent fit point to `(4,4)`. User retest remains pending in `docs/dev/user-test-tracking.md`.
- [x] UT3 line/tangent-arc creation: fixed chained endpoint hover mode switching, removed the mirrored tangent-control fallback preview, suppressed the tangent-control point marker during tangent-arc preview, and wired tangent-arc radius key-in to persistent radius dimension creation. Evidence: focused JS canvas tests passed 149/149, keyed arc dimension factory tests rebuilt and passed 3/3 including tangent arc, `DXFER.slnx` rebuilt cleanly, and the rebuilt app verified line-to-tangent-arc hover switching, `0` yellow pixels on the old mirrored segment and tangent-control marker samples, visible yellow pixels on the real arc preview, and a committed persistent `R5` radius input. User retest remains pending in `docs/dev/user-test-tracking.md`.
- [ ] UT4 solver/dimension robustness: dimension-preserving drag, stale broken-solve cleanup after dimension delete, coincident welding, keyed ellipse solve, and deleted vertical constraint reassertion.
- [ ] UT5 dock/icon UI pass: left/right dock width and collapse controls, remove hotkey labels from buttons, enlarge icon art, and redraw requested icons.

## Handoff Status - Needs User Testing

The following completed rows are ready for user retest at work. They remain marked `USER-FAILED` in `docs/dev/user-test-tracking.md` until the user reports them fixed:

- UT-TRIM-001 polygon/poly trim explosion.
- UT-TRIM-002 Power Trim off-end extend hover tolerance and visual indicator.
- UT-TRIM-003 spline trim responsiveness.
- UT-TRIM-004 arc extend to ellipse/elliptical-arc boundaries.
- UT-SPLINE-001 spline fit-point and endpoint tangent-handle editing.
- UT-LINEARC-001 line/tangent-arc preview switching and tangent-arc radius key-in.

The following rows are still intentionally open and should not be used as acceptance targets for this push:

- UT-SOLVER-001 dimensioned geometry drag and stale solve cleanup.
- UT-SOLVER-002 coincident constraint vertex welding.
- UT-ELLIPSE-001 keyed ellipse creation broken solves.
- UT-SOLVER-003 deleted vertical constraint reassertion through dimension drag.
- UT-UI-001 dock width and collapsible side docks.
- UT-ICON-001 icon size and hotkey-label removal.
- UT-ICON-002 requested icon redraws.

## UT1 Trim/Extend Patch Order

### UT1a Polygon Trim Explosion

Files expected:
- `src/DXFER.Core/Operations/DrawingModifyService.cs`
- `src/DXFER.Core/Operations/CurveSplitService.cs`
- `tests/DXFER.Core.Tests/Operations/DrawingModifyServiceTests.cs`
- `docs/dev/power-trim-extend-test-matrix.md`

Checklist:
- [x] Reproduce current polygon trim output and stale solver reference behavior in a focused failing core test.
- [x] Change polygon trim target output to line entities for kept spans.
- [x] Remove stale constraints/dimensions that reference the deleted polygon entity.
- [x] Run focused Power Trim tests. Evidence: `PowerTrimPolygon` passed 10/10; broader `PowerTrim` passed 127/127.
- [x] Rebuild/restart the app and canvas-test polygon trim. Evidence: line-cutter polygon trim changed first entity from `polygon:*` to `line:polygon-line-*`, increased entity count `4 -> 9`, removed the picked-span hover, and left kept line spans hoverable. Circle-cutter polygon trim changed first entity from `polygon:*` to `line:polygon-line-*`, increased entity count `2 -> 7`, and left no stale red dimension inputs.
- [x] Update `docs/dev/user-test-tracking.md`, `docs/dev/bug-feature-log.md`, and the trim matrix.

Entity applicability:

| Entity | Applies To UT1a? | Note |
|---|---|---|
| Line | Yes | Polygon explosion output is lines; line cutters must still work. |
| Polyline | Yes | User said "poly"; verify whether both polyline and polygon need explosion semantics. |
| Polygon | Yes | Primary target. |
| Circle | Yes | Must remain a polygon cutter. |
| Arc | Yes | Must remain a polygon cutter. |
| Ellipse | Yes | Must remain a polygon cutter. |
| Spline | Yes | Must remain a polygon cutter. |
| Point | Yes | Must remain a polygon cutter. |
| Dimension | Yes | Stale dimension references must not break the solver. |
| Constraint glyph/reference | Yes | Stale constraint references must not break the solver. |

### UT1b Extend Hover Affordance

- [x] Add JS tests for off-end hover tolerance and extend-vs-trim visual classification.
- [x] Implement the smallest canvas hover/request change. Evidence: Power Trim open-path extend candidates use an 18 px off-end hit tolerance, expose `getPowerTrimRequestMode`, publish `data-power-trim-mode`, and render a cyan dashed endpoint-to-pointer cue.
- [x] Canvas-test line off-end picks in the rebuilt app. Evidence: toolbar-created line plus line boundary hovered 14 px past endpoint as `powerTrimMode=extend`, cyan indicator pixels were present, the click kept entity count `2`, and the post-click probe at `14,0` hovered the extended line. Polyline/arc/elliptical arc/spline request paths are covered by existing JS extend-preservation tests; run broader app rows again when those target-specific trim/extend issues are active.

### UT1c Arc-To-Ellipse Extend

- [x] Add focused core tests for arc extension to full ellipse and elliptical arc boundaries when the next intersection requires a larger angular extension. Evidence: `PowerTrimExtendsArcEndToEllipseBoundaryPastLargeAngle` failed first, then passed; broader `PowerTrimExtendsArc|PowerTrimExtendsEllipseArc|PowerTrimExtendsLineEndToApplicableBoundaryTypes|PowerTrim` passed 128/128.
- [x] Add running-app verification for a toolbar-created arc target and ellipse/elliptical-arc boundary. Evidence: toolbar-created center-point arc plus full ellipse boundary; Power Trim hover reported `powerTrimMode=extend`, click returned `OnPowerTrimRequested:ok`, entity count stayed `2`, and a probe at 250 degrees changed to hovering the extended arc.

### UT1d Spline Trim Performance

- [x] Measure current spline trim hot path. Evidence from the running app after the first cache change showed trim succeeded around 190 ms but exposed 257 points for the first trimmed degree-1 spline span.
- [x] Add a performance guard or representative regression fixture if practical. Evidence: `SplineEntityTests` verifies repeated sample calls reuse the cached path and degree-1 spline paths use control points directly.
- [x] Optimize without sacrificing exact endpoint coincidence. Evidence: spline Power Trim precision tests passed 19/19 after the cache and degree-1 sampling changes; rebuilt-app trim completed around 174 ms, removed the picked span, left both kept spans hoverable, and reduced the first trimmed span to 27 exposed points.

## Queued Non-Trim Work

The following are intentionally queued until the active trim/extend lane is handled:

- UT-SOLVER-001, UT-SOLVER-002, UT-SOLVER-003 dimension/constraint solver robustness.
- UT-ELLIPSE-001 keyed ellipse solve creation.
- UT-UI-001 dock width/collapse.
- UT-ICON-001 and UT-ICON-002 icon size/label/art pass.

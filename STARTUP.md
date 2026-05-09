# DXFER Agent Startup

Read this before starting DXFER work.

## Bug And Feature Intake

When the user gives a bug report or feature request while another task is active:

- Do not switch tasks midstream.
- Log the new item in `docs/dev/bug-feature-log.md`.
- Track whether the item is user-tested, automated-tested, and app-verified in `docs/dev/user-test-tracking.md`.
- Continue the active task unless the new input immediately pertains to the work already in progress.
- When the active task is coded and verified in the running app, return to `docs/dev/bug-feature-log.md`.
- Work the next logged item in order, and mark it complete only after the code is implemented and verified in app.
- Do not go idle while open items remain in the log.

Use the staged plan for the current repair run at `docs/superpowers/plans/2026-05-06-dxfer-audit-repair-game-plan.md`.

## Entity Applicability Rule

When coding or fixing a tool/feature, explicitly evaluate every drawing entity type for applicability before implementing the patch:

- Line
- Polyline
- Polygon
- Circle
- Arc
- Ellipse
- Spline
- Point
- Dimension
- Constraint glyph/reference

For every applicable entity type, add coverage or document a deliberate unsupported path in the active plan/log. Do not mark the work complete until all applicable entity types are coded and verified in the running app, or explicitly logged as unsupported/future work with safe behavior.

## Constraint-Driven Drag Rule

Dragging behavior is not rectangle-specific. Treat rectangles as one composition of lines, coincident vertices, horizontal/vertical/parallel/perpendicular constraints, and dimensions. For every entity and grip, decide the result from the active constraints and driving dimensions:

- Free point or handle drags move only the dragged degree of freedom when constraints allow it.
- Drag components blocked by a driving dimension should translate the constrained geometry and attached dimensions instead of breaking the dimension.
- Coincident references must move as welded points in preview and after release.
- Dimension anchors attached to moved geometry must travel with that geometry; dragging a dimension anchor must not recommit the dimension value.
- If a requested drag cannot satisfy constraints or dimensions, keep the geometry stable and render the affected entities, constraints, and dimensions as failed instead of silently corrupting the solve.

Do not add a rectangle-only fix for behavior that logically applies to other entity types. If a temporary entity-specific slice is unavoidable, log the broader entity-agnostic follow-up and keep it open until line, polyline, polygon, circle, arc, ellipse, spline, point, dimension, and constraint-glyph behavior have been evaluated and verified where applicable.

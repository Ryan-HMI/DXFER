# Constraint Hover Timing

Ryan requested half the current hover duration. Reduce the shared hold from
2500ms to 1250ms and fade from 500ms to 250ms: 1500ms total after departure.

Interaction contract: no layout, hit-target or styling changes. Hovering an icon
or selecting its constraint still pins visibility. Drawing and hit testing retain
the same shared visibility filter. Geometry, history and saved data are unaffected.

Regression checks: exact hold/fade boundaries, reentry, selection pinning,
deletion cleanup, and real browser pixel checks plus delete/Undo/Redo.
Deployment scope is DXFer Dev only. No existing drawing repair required.

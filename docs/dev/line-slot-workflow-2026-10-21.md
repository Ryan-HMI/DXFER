# Existing-Line Slot

## Interaction Contract

Slot uses one existing straight LINE as its centerline, via selection-first or tool-first picking. It no longer starts a three-point slot from empty space. The existing line keeps its identity, endpoints and intent and becomes construction geometry only when the slot commits. Locked/hidden or generated sources refuse without edits.

After picking, pointer motion previews the full slot width locally and continuously. The existing inline dimension input accepts a positive full width with ordinary decimal typing. Enter or a placement click commits; Escape discards the draft without history. Invalid input cannot commit a stale accepted width. A keyed width produces one persistent driving width dimension. Both arcs remain centered on the source endpoints, with four permanent tangent joins and closed endpoints.

Use the existing canvas input/preview styling and placement rules: compact fixed-width numeric field, no new sidebar or panel, keep field readable within the canvas and clear of docked controls at desktop widths. No palette or theme changes. Preview must not alter the document or refit the camera. Source changes and width edits must preserve the relationships through Undo/Redo and native DXF reopen.

## Verification

- Core/component suite: 3301 passed. New real PlaneGCS slot tests exercise creation, width/source edits, protected-source refusal and native persistence. The added runtime tests use the existing serialized resource collection rather than competing for the eight-process solver budget.
- JavaScript suite: 1896 passed, including native rank checks with reversed constraint/reference order and fixed-geometry refusal. Existing-line midpoint/end selections normalize to the owning line; unrelated points remain invalid sources.
- Isolated browser candidate passed four workflow groups: selection-first decimal key-in/Enter, tool-first pointer/click, width/source edits with native reopen and Undo/Redo, and Escape/invalid-width/locked-source refusal. Ten consecutive moving-pointer samples show both preview boundaries changing before motion stops. Screenshots inspected; zero browser errors. Evidence: `/tmp/dxfer-line-slot-qa-r2`.
- Browser source-end drag retained exact width and tangent/center relationships; its solved endpoint was 0.47 screen pixels from the observed pointer target. This verifies linked source editing, not exact pointer tracking for every solver drag.
- Deployment pending.

## Shared Solver Correction

The first real-engine width-edit tests exposed a singular line/arc tangent distance equation when the line and arc already have an explicit shared endpoint. Creation looked correct, but a subsequent driving width edit failed the native rank check. For declared Coincident line/arc endpoint joins, the mapper now uses the native perpendicular radius/line equation. It retains angular and distance validation, rather than dropping tangent intent or bypassing rank checks. Unjoined tangencies retain the existing mapping. Equal end-arc radii plus anchored centers and four joined tangencies imply parallel slot sides, so creation does not add a redundant Parallel relation.

No historical drawing conversion or data repair is included. Existing native endpoint-joined tangencies use the corrected mapping on future solves.

# Floating Pattern Card Layout Contract

## Scope

Replace the source-anchored radial parameter card with one floating radial card.
Keep the radial center handle and direction guides geometry-anchored. Preserve
all existing linear U/V, stagger, and action group anchors and layout unchanged.
Do not change geometry, snapping, validation, or history.

## Layout and Interaction

- Initialize the card slightly right/below the visible canvas center, independent
  of source geometry. Prefer unobscured viewport space around existing palettes.
- Keep radial width at 216px, compact count/checkmark/cancel controls, Full Circle
  checkbox and conditional angle. Linear controls are outside this change.
- Use existing theme tokens, 12px text, 28px controls and 4px corner radius.
  Inputs retain native editing and decimal-entry behavior.
- A separate move-only header is the sole pointer-drag target. Inputs, labels,
  checkboxes and action buttons never initiate movement. The header also supports
  arrow-key movement; Escape during movement restores the pre-drag position.
- Clamp the entire card to the canvas viewport with an 8px margin. Constrain
  width/height on small viewports; scroll contents vertically rather than overflow.
- Store position in the canvas session, not the drawing document or localStorage.
  Preserve it across camera changes, draft rerenders, hide/show and pattern edits;
  clamp only when dimensions/viewport change. Dispose pointer listeners/capture
  with the canvas. No interop calls, geometry mutations or history from card drag.
- Preserve successful Accept/full Cancel hiding and explicit context Show/Edit.
  Card movement does not select geometry or move the radial center.

## Ownership and Verification

UI: PatternViewportControls markup/CSS, new canvas/patternFloatingCard.js,
drawingCanvas card placement/disposal. Center snapping, patternViewport.js and
the DrawingCanvas module cache token remain parent-owned.

Focused tests cover initialization, bounds, resize, pointer capture/cancellation,
non-header exclusions, keyboard movement, rerender/hide/show persistence, and
rendered compact radial controls and unchanged linear anchors. Candidate screenshots at
1440x1000 and 1100x705 must verify placement and unchanged center/action behavior;
no deployment is authorized by this task.

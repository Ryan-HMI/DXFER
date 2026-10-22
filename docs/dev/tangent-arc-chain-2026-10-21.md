# Tangent Arc/Line Continuation

## Scope

Ryan requested a line plus quarter-circle icon, automatic return to Line after
one tangent arc, and quadrant snaps around the draft arc center.
DXFER Dev only; no Sync, Test or Production changes.

## Layout And Interaction Contract

- Keep the existing 24-unit icon view box and compact tool-button dimensions.
- Use the existing accessible tool name, tooltip, active state and theme color.
- Show center-to-quadrant guides through the existing canvas snap renderer;
  reuse its viewport-clamped cue, rather than adding another floating panel.
- Return both canvas and toolbar to Line, with the arc endpoint retained as the
  next start. Require pointer exit before another hover-toggle at that vertex.
- Keyed radius remains authoritative. A quadrant must preserve the starting
  tangent and remain on the same draft circle.
- With automatic constraints enabled, store the endpoint/center axis relation;
  with it disabled, retain the snapped position without adding that relation.

## Verification

- Full core/component suite: 3324 passed.
- Full JavaScript suite: 1909 passed.
- Unit coverage includes clockwise and counterclockwise quarter/half arcs,
  radius key-in, zoom-dependent pixel tolerance, invalid drafts, automatic mode
  change and truthful snap cues.
- Real PlaneGCS component checks cover persistent incoming/outgoing tangency,
  quadrant relations, Auto OFF, history, native reopen and invalid-intent refusal.
- Isolated browser checks pass: near-quadrant 90/180-degree previews, keyed
  radius 2.125, return to Line with pressed palette state, outgoing tangent
  continuation, Auto ON/OFF, Undo/Redo and native DXF reopen. Nineteen exports
  inspected, zero browser errors. Actual palette icon and preview screenshots
  inspected. Evidence: `/tmp/dxfer-tangent-arc-qa-r3-final/results.json`.
- Installed browser replay is pending.

## Underlying Changes

The post-commit state previously retained Tangent Arc, and draft snap candidates
had no arc-center quadrant points. Both paths are corrected. Explicit quadrant
intent is resolved against the newly created arc ID, with endpoint/center
validation before adding a permanent relation. Clockwise arcs account for the
reversed stored start/end ordering. The shared Line palette command now uses the
quarter-arc icon and stays active during its tangent-arc submode.

No existing drawing data is rewritten by this change.

# Invert Wheel Zoom

## Interaction Contract

Add one checkbox row to the existing File > Settings dialog using its current alignment, spacing, responsive width, wrapping and focus styles. Default is off. Persist the choice per browser independently of automatic snap constraints. Apply it immediately without changing geometry, history, pan, zoom sensitivity or cursor anchoring. Polygon side-count scrolling remains unchanged. No new dialog or viewport control.

## Verification

- All 1,803 JavaScript tests and 306 component tests passed. Targeted coverage includes default-off storage, blocked storage, independent snap preferences, all wheel delta modes, cursor anchoring, sensitivity and the polygon side-count branch.
- Isolated candidate browser replay passed eight wheel checks, both reload states, independent automatic-constraint behavior, exact native geometry comparison and Undo/Redo. No browser errors.
- Settings screenshots checked at 1440x1000 and 1100x705. Evidence: `/tmp/dxfer-invert-wheel-qa-r2`.
- Dev rollout/public verification pending. Production/Test remain outside scope. Flux/Ryan independent acceptance is not claimed.

The shared canvas wheel handler applies the preference, not individual tools. This preference needs no existing drawing repair or file-format migration.

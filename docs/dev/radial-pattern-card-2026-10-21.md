# Radial Pattern Card

## Layout Contract

Keep the exact draggable center handle. Remove viewport center X/Y fields, not stored center coordinates or the sidebar precision controls. Group count, checkmark Confirm and icon Cancel in a compact source-anchored card. Place Full Circle beneath that row, checked for new patterns; show the angle input only when unchecked. Preserve existing saved spacing choices. Reuse the delivered checkmark asset and themed form styling.

Card width is 216px maximum, with fixed 28px command targets, flexible count input, 6px spacing and 4px corners. Existing canvas obstacle placement and context-menu Show/Hide remain in use. Error text wraps inside the card. No separate radial angle or action groups, nested cards, changes to linear controls, solver behavior or persistence format.

## Verification

All 305 component tests and nine pattern viewport JavaScript tests pass. Rendered-component regressions cover the checkbox, conditional angle field, single action group and retained center handle. Full real-browser replay at 1440x1000 and 1100x705 passes 20 layouts, 18 held-center samples and four active-draft Show/Hide cases, including Confirm/Cancel, Undo/Redo and native reopen. Linear pattern regressions pass. No browser errors. Screenshots inspected: card is 216x86px checked and 216x137px with angle. Evidence: `/tmp/dxfer-radial-card-candidate-qa/pattern-viewport-results.json`.

Installed verification pending. Existing drawings require no repair.

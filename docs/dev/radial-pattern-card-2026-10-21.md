# Radial Pattern Card

## Layout Contract

Keep the exact draggable center handle. Remove viewport center X/Y fields, not stored center coordinates or the sidebar precision controls. Group count, checkmark Confirm and icon Cancel in a compact source-anchored card. Place Full Circle beneath that row, checked for new patterns; show the angle input only when unchecked. Preserve existing saved spacing choices. Reuse the delivered checkmark asset and themed form styling.

Card width is 216px maximum, with fixed 28px command targets, flexible count input, 6px spacing and 4px corners. Existing canvas obstacle placement and context-menu Show/Hide remain in use. Error text wraps inside the card. No separate radial angle or action groups, nested cards, changes to linear controls, solver behavior or persistence format.

## Verification

All 305 component tests and nine pattern viewport JavaScript tests pass. Rendered-component regressions cover the checkbox, conditional angle field, single action group and retained center handle. Full real-browser replay at 1440x1000 and 1100x705 passes 20 layouts, 18 held-center samples and four active-draft Show/Hide cases, including Confirm/Cancel, Undo/Redo and native reopen. Linear pattern regressions pass. No browser errors. Screenshots inspected: card is 216x86px checked and 216x137px with angle. Evidence: `/tmp/dxfer-radial-card-candidate-qa/pattern-viewport-results.json`.

## Installed Verification

Deployed `c4687cb20e7d411dc9d16f6829e7064fe225ad1f` to DXFer Dev at 2026-10-21T19:26:07Z. Public replay passes all 20 layouts, 18 held-center samples and four draft-context cases at both desktop sizes, with no browser errors. Tooltip revision and new CSS tokens verified; served scoped CSS matches publication. Evidence: `/tmp/dxfer-radial-card-installed/pattern-viewport-results.json`.

Normal Google sign-in retained; Production/Test PIDs 3584417/3584475 unchanged. Rollback: `/home/ryan/dxfer-runtime/dev-publish-before-c4687cb`. The isolated 5038 host is stopped and replay sessions are finished. This corrects the requested presentation rather than changing pattern geometry; existing drawings require no repair. Flux/Ryan visual acceptance remains separate from owner/worker verification.

## Source Clearance Follow-up

Flux independently accepted the functional controls on c4687cb (delivery998da5bec3fa9d87ce860d4a), but reported source-circle overlap at1180x757. Placement previously considered only UI controls. The follow-up contract adds the real source bounds, projected with current pan/zoom and an8px clearance, as an obstacle for the radial card. Keep the exact center handle and all other control avoidance. If a zoomed source occupies the entire available canvas, retain accessible controls using the existing UI-only placement instead of hiding all actions. General avoidance of every generated instance is not claimed. Sidebar precision X/Y inputs remain unchanged.

All305 component tests and the full1798 JavaScript tests pass. Browser baseline reproduces actual circle overlap by12.66px. Candidate replay passes50 layouts,18 held moves and4 draft cases at1440/1180/1100, including eight zoom levels and pan per viewport, history and native reopen. At1180x757 the source circle has13.08px clearance after zoom and15.99px after pan. Screenshots inspected; no browser errors. Evidence: `/tmp/dxfer-source-clearance-qa/pattern-viewport-results.json`; baseline: `/tmp/dxfer-radial-source-overlap-baseline/actual-circle-overlap-1180.png`. Installed follow-up verification pending.

# Moving Creation Clicks

Ryan reports geometry-creation clicks are lost while moving the mouse. The shared
selection/drag discriminator cancels a click after five pixels of travel, even
when a sketch tool has no drag action.

## Interaction Contract

- Preserve layout, alignment, sizing, wrapping, overflow, palette docking and
  desktop targets. No CSS or markup changes beyond module cache invalidation.
- A primary press/release during sketch creation places one point at the press
  position even when the mouse moves before release. Use existing snap/locked
  dimension handling at that position; do not shift it to the release location.
- Pointer cancellation, Escape or a tool change must not commit a stale point.
- Selection drags, panning, dimension-label moves, PowerTrim strokes and modify
  tool gestures retain their existing handlers and movement thresholds.

## Verification

Pre-fix registered-canvas tests reproduced missed commits for nine creation tools
and failed cancellation-recovery; the Escape test mock was corrected separately.
Creation candidates now retain tool/press position, skip selection's movement
cutoff and cannot hover-switch chain tools during the held gesture. Release
revalidates the active tool and picks/snaps at the pressed location. Stale hover
from the release location is cleared. Add-spline-control-point editing retains
the modify-style threshold.

1730 core and 910 JavaScript tests pass; Web build passes. Fourteen focused input
tests cover the nine tools, cancellation, Escape, panning, origin snap, duplicate
release and tool-change protection. Local browser checks pass Point, Line,
Rectangle, Circle, Arc and Ellipse: move 18x13 pixels while pressed, compare to
stationary geometry, Undo/Redo, native download/reopen. Moving origin clicks
retain a Satisfied Coincident. Screenshots inspected; no page errors.

Evidence: `/tmp/moving-click-before.log`, `/tmp/moving-click-js.log`,
`/tmp/moving-click-core.log`, `/tmp/moving-click-build.log`,
`/tmp/moving-click-browser-local.log`, `/tmp/moving-click-local`.

Shared input handling is corrected; no saved drawings need repair. Previously
ignored clicks produced no geometry and must be clicked again deliberately.
Dev deployed `0548a7bc2f4e40e21ab5b855b1248575533d7949` at 2026-10-18T23:27Z.
Public browser repeats all six creation/history/reopen cases and origin snapping,
with rendered build verified, no page errors and screenshots inspected. Thirteen
exported DXFs audit with zero errors/fixes. Evidence:
`/tmp/moving-click-browser-dev.log` and `/tmp/moving-click-dev`.

Installed four assembly hashes match, ten runtime settings retained, health200,
anonymous root302, callbacks disabled; Production/Test processes unchanged.
Rollback: `/home/ryan/dxfer-runtime/dev-publish-before-0548a7b`.
Owner verification complete; independent Flux/Ryan replay remains separate.

# Canvas Focus Return

## Interaction Contract

- Final Enter still commits the active drawing operation and restores canvas keyboard handling.
- Programmatic focus returns must not draw a viewport-wide outline or change pan/zoom.
- Native keyboard navigation into the canvas retains a visible, inset focus indicator.
- Leaving the canvas clears the programmatic-focus marker so later keyboard entry remains visible.
- Field Tab/Shift+Tab cycling, dimension values, geometry, panel widths and layout stay unchanged at desktop sizes.

## Cause

The canvas relied on the browser's default focus styling. Returning focus after a keyboard commit inherited keyboard focus-visible state and outlined the entire canvas. This is separate from camera auto-fit on opening a pattern editor.

## Verification

All 1,796 JavaScript and 3,149 core tests pass, including 303 component cases. New focus-lifecycle regressions cover dimension Enter and component-driven canvas focus, with marker clearing on blur and later keyboard entry. Release publication succeeds.

Isolated real-browser replay at 1440x1000 passes the exact Width 15, Tab, Height 10, Enter sequence. The native drawing contains the expected dimensions and rectangle bounds; scale and pan do not change. Persistent-dimension Enter and component-driven context-menu Close also return focus without an outline. Three native Tab entries restore the visible 2px inset indicator after blur. Screenshots inspected; no browser errors. Evidence: `/tmp/dxfer-canvas-focus-candidate-qa-final/results.json`.

Public deployment verification remains pending. No stored drawing repair is required.

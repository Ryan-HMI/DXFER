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

## Installed Verification

Deployed `4201a5c3feef020719b910afed00bd05e9e27385` to DXFer Dev at 2026-10-21T19:20:26Z. The public browser replay passes the same focus/camera/geometry checks with tooltip-verified 4201a5c and PlaneGCS. Screenshots inspected. Evidence: `/tmp/dxfer-canvas-focus-installed/results.json`. Served JavaScript and scoped CSS match the publication, with new cache tokens. Normal Google sign-in remains configured; Production/Test PIDs 3584417/3584475 are unchanged. Rollback: `/home/ryan/dxfer-runtime/dev-publish-before-4201a5c`.

The underlying focus styling is corrected; no stored drawing repair is required. This is owner/worker browser verification, not independent Flux acceptance. Isolated 5038 stopped and all test sessions finished.

## Independent Acceptance

Flux delivery `7b1a095721651a2d03d1cbfa` independently verified 4201a5c: final Enter commits the 15x10 rectangle without the unwanted outline or camera movement; Tab field cycling works; deliberate keyboard entry retains the cyan indicator; dimension editing and context Close also avoid the unwanted outline. This closes the reported focus-outline issue. The newer radial-card layout remains a separate acceptance scope.

# Independent palette labels

## Bounded layout contract

Continue the reviewed independent-palette composition, not a combined toolbox.
Preserve every group identity, command, handler, shortcut, docking order, preferred
position and collapse state. Add a per-palette label toggle using the existing Text
icon, accessible name and tooltip. Header actions remain separate from dragging.
No geometry, solver, document or Undo change is caused by this preference.

Expanded side/floating palettes retain their existing width. Labeled commands use
two equal minmax(0,1fr) columns and stable 3.5rem targets with two-line wrapped
labels; icons stay 1rem and cannot shrink. Icon-only mode retains the existing
grid. Top/bottom palettes keep compact icons; the saved label preference returns
when redocked to a side or floated. Headers use sentence case, zero letter spacing
and 12px semibold type. The toggle is 24px square with visible keyboard focus.

New layouts show labels. Existing v1 layouts missing this preference retain their
icon-only appearance; a malformed label preference must not erase valid positions.
Preferences remain independent and local to the browser. Storage denial keeps
controls usable. No automatic layout reset or hiding on resize.

Verify desktop 1440x900 and 1280x720, mixed floating/docked layouts, label toggles,
reload, resize, collapse/expand, command activation and unchanged document/history.
Full document header and active-operation inspector remain a subsequent slice.

## Verification and related fixes

- 1,036 .NET and 355 JS tests passed before spline-feedback integration.
- Real local browser: independent label modes, two floating palettes, collapse,
  unchanged command inventory, keyboard toggle, reload, legacy preference upgrade,
  actual Fillet activation, and unchanged synthetic document/dimensions/Undo.
- Committed palette browser suite passed mixed docking, order, restore, corrupt
  preferences and denied storage. Desktop 1440x900, 1280x720 and 1000x700 tested.
- Scaling coverage uses available CSS viewports equivalent to 1920x1080 at
  125/150/200%; not a claim of native OS/browser zoom testing. CSS zoom alone also
  exposed a layout/client-pixel mismatch in floating bounds. The observer now
  converts client bounds back to layout pixels, with a focused 2x regression.
- Floating bodies now scroll within the remaining canvas height, with sticky
  headers, so long labeled palettes do not hide their last commands under the
  status bar. Screenshot and final Slot target checks cover this path.
- Evidence: /tmp/dxfer-palette-labels-*.png and /tmp/dxfer-palette-evidence/.
- No drawing or business records changed; no existing-data repair required.

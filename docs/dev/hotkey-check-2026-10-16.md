# Hotkey Check

Ryan reported missing hotkeys. No general binding/listener failure reproduced on public DXFer Dev bc98628.

- Real browser keyboard events activate C/Center circle, R/Rectangle, M/Midpoint line, Shift+A/Line and Q/Measure. Escape returns to selection.
- View's keyboard button opens Tool hotkeys. Changing Line to L in the disposable browser works and survives reload. Ryan's browser storage is neither inspected nor changed.
- Escape from a radius field restores canvas tool shortcuts. Ctrl+Z and Ctrl+Shift+Z undo/redo a newly created circle.
- Screenshot: `/tmp/dxfer-hotkeys-evidence/settings.png`. Regression: `tests/DXFER.Blazor.Tests/hotkeys.browser.mjs`.
- Existing dispatch deliberately ignores editable targets. Custom bindings are browser-local under `dxfer.toolHotkeys.v2`, scoped to the site origin. No recent source changes removed the default bindings.
- Exact failing key/context requested from Ryan. This is investigation evidence, not a claimed bug fix or acceptance of every customized shortcut. No runtime, user settings, drawing files or records changed.

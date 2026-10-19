# Remove generic Information panel

## Layout contract

- Remove the generic Information section, selection/metadata readouts, and its show/hide toggle, including during ordinary drawing tools.
- Keep the existing right-side Layers and pattern management controls, aligned to the top with their current widths and scrolling behavior.
- Show an operation section only for actual Fillet/Chamfer or Pattern editing inputs and confirmation/cancellation. Remove its redundant selection readouts and duplicated prompt.
- Preserve bottom status/error messages, cut totals and contours. Only the Measure tool adds numeric measurement feedback there; do not relocate the removed generic readouts to the footer.
- Keep desktop canvas/palette bounds and keyboard focus behavior. Exit/Escape returns focus to the canvas without leaving a dead info-toggle target.
- DXFer Dev only. No changes to geometry, history or stored drawings.

## Verification

- Build passes without warnings/errors; 2,006 core tests and 1,005 JavaScript tests pass.
- Six local browser scenarios pass at 1188x761 and 1100x705: no generic Information section/toggle for idle, Line, Measure or Escape; Layers create/rename; Chamfer key-in, cancel, confirm, Undo/Redo; Pattern editor access/cancel.
- Fourteen native exports have valid checksums and zero ezdxf audit errors/fixes. No browser errors. Idle and Chamfer screenshots inspected for top alignment, preserved controls and readable layout.
- Evidence: `/tmp/dxfer-no-information-panel-local`. Runner stopped, disposable local listener removed.
- Shared rendering/lifecycle changed, not a CSS-only hide. No drawing or record repair required. Historical QA that expected an Information show/hide toggle is superseded by this explicit removal request.
- Deployed `10c51770c0d7e2b6408411e8479d56172cfabecf` to https://dxferdev.sync.harrisonmetals.com at approximately 2026-10-19T06:17Z. Installed assembly hashes match; all ten tracked settings, normal sign-in and disabled Sync callbacks remain intact. Production/Test process IDs unchanged.
- Public replay: all six scenarios pass on the rendered revision, fourteen native exports pass checksum and ezdxf audit (zero errors/fixes), no browser errors or Dev warning journal entries. Idle screenshot inspected. Evidence: `/tmp/dxfer-no-information-panel-public`. Runner and browser stopped.

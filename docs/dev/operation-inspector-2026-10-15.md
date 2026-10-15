# Active-operation inspector layout contract

DXFer full-editing workspace only. Keep the existing independent palette IDs,
membership, docking and saved layout. Reserve a separate 18.5rem right grid column
while an operation is active; canvas and palette host share only the remaining
first column. No overlay across drawing geometry or palette controls. Inactive
selection retains the existing full-width canvas. User can collapse the inspector
without changing the operation, typed values, geometry or history; the footer then
retains the prompt, corner input and an icon button to reopen it.

Inspector: compact tool-name header with collapse icon; existing operation feedback
and corner fields with actual document units; independently scrollable selection
readouts; persistent Exit tool command. Text wraps, long identifiers break inside
the panel, numeric fields use min-width:0. Routine feedback uses exactly one status
region, either panel or footer, never two announcements. Preserve current invalid
input and rejection messages. No invented preview/apply controls, selection slots,
solver claims or new CAD behavior. Exit ends the active tool via the existing cancel
handler; previously committed edits remain. Escape inside the panel does the same.

Canvas sizing uses the actual grid width. Floating palette coordinates remain saved
unchanged and are clamped only for presentation. Fit must use the reduced viewport.
No phone workflow added. Verify 1440x900,1280x720,1188x761 and desktop equivalent
scaled CSS viewports, including field focus, collapse/reopen, Escape, invalid input,
real tool edits, Undo/Redo, native reopen and mixed docked/floating palettes.

Document header is a parallel change in the existing 48px application bar: truthful
filename, units, dirty/read-only state. Parent publishes existing state, never new
storage or inferred units. Header and inspector changes must not alter native DXF.

## Implementation and Verification

- Full-editing tools use the existing active tool, selection, feedback and corner
  input state. No staging or Apply action was invented. Collapse preserves invalid
  input; Exit/Escape preserve completed edits and return focus to the canvas.
- Header publication runs after workbench renders, with equality-deduplicated
  notifications. Empty drawings remain open documents. Download resets the existing
  local baseline, labeled Unchanged rather than claiming a server save.
- Shared palette docking now measures actual canvas bounds and client/layout scale,
  instead of assuming fixed right/bottom screen coordinates. Final drops use their
  release position. Existing preferences are preserved; Sync connection controls,
  if enabled elsewhere, remain outside the inspector. Dev callbacks stay disabled.
- Integrated .NET suite: 1,070 pass. JavaScript suite: 364 pass. Web build: no
  warnings/errors. Changed Blazor C# format verification passes.
- Local real-browser checks pass: blank/open/long filenames; in/mm/unspecified and
  read-only display; dirty state through edit/Undo/Redo/download/New/reopen; corner
  invalid input/collapse/reopen and driving rejection; reference edit/Exit/history;
  Escape/focus; right docking against the reduced canvas with unchanged preferences.
- Screenshots inspected at 1440x900,1280x720,1188x761,1100x705 and960x540. These are
  desktop window/equivalent available viewport checks, not native OS scaling.
  Fit Extents is used after resizing; opening the inspector does not auto-zoom.
- Existing independent palette drag/order/reload/corrupt-storage browser contract
  passes. Exact cubic trim/refusal/degree-one extension/history/reopen and ellipse
  creation/independent dimensions/crossed axes/history/reopen also pass with the
  inspector installed locally. No CAD algorithm changed in this slice.

Repeatable UI driver: `tests/DXFER.Blazor.Tests/operationInspector.browser.mjs`.
Supply an isolated local or explicitly approved Dev URL/API key through
`DXFER_QA_URL`/`DXFER_QA_API_KEY`, and `PLAYWRIGHT_MODULE` when needed. No credentials
are stored. Driving/reference fixtures are native DXFs with horizontal line
(0,0)-(10,0), vertical line(0,0)-(0,10), and linear dimension `length` referencing
`horizontal:start`/`horizontal:end`, value10, anchor(5,-1), respective IsDriving.
Units are Unspecified. Read-only fixture is line(0,0)-(10,0), metadata Millimeters
and ReferenceOnly. Create them using `DxfDocumentWriter` with IncludeNativeDocument.
Fixture paths can be supplied with `DXFER_DRIVING_FIXTURE`, `DXFER_REFERENCE_FIXTURE`
and `DXFER_READONLY_FIXTURE`; defaults point to disposable `/tmp` QA files. All
downloads and edits are synthetic. Installed verification is recorded below only
after deployment; independent QA and full visual acceptance remain separate.

Underlying layout/state integration is implemented, including the shared docking
cause exposed by a narrower canvas. No user documents, preferences or business
records require repair. Previously damaged geometry from older defects is outside
this UI change and is not reconstructed here.

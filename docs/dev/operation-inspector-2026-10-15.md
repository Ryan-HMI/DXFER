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

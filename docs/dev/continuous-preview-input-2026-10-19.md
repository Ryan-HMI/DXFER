# Continuous preview and dimension input

## Contract

Preserve existing desktop canvas/panel alignment, widths, wrapping, overflow and interaction targets. No new controls or layout changes. Offset preview must advance while pointer events continue, with at most one shared-core request in flight. Display completed results at their computed positions, never relabel them as the latest pointer result. Changed selection, document, tool/draft or released drag must invalidate late results. Dimension inputs retain their DOM identity, focus, raw typed text and caret while recalculation is pending. Clear/retype and decimal entry must not be overwritten by redraw. Enter/Tab/blur retain existing deliberate commit semantics; Escape cancels.

## Root cause

The Offset renderer returned no geometry or dimension descriptor unless the completed server result exactly matched the latest pointer key. Continuous movement starved the preview; changed typed distances removed and recreated the input before the next result. Shared-core drag previews similarly discarded every response if a newer pointer position existed. Draft input handling additionally ended editing on each input event, allowing redraw to overwrite incomplete text.

## Applicability

The scheduling and draft-input fix is independent of entity type. Existing Offset applicability for lines, polylines/polygons, circles and arcs is unchanged; no new ellipse, spline, point, dimension or constraint-glyph offset operation is introduced. Shared-core drag scheduling covers every entity/grip already routed there, including linked Offset/Pattern geometry. Dimension fields across creation tools use the same raw-edit protection. Persistent dimension and constraint-glyph drag semantics are unchanged. Geometry calculations, solver acceptance and serialization remain in the existing shared core.

## Verification in progress

- Two pre-fix unit failures reproduce completed-preview starvation. Fixed scheduling now passes the focused tests, including changed draft/selection, rejected inset input retention, modifier changes and late drag-release results.
- Old served canvas code reproduced four actual-browser failures: previews absent during motion; typed `1.25` leaves a detached unfocused input containing only `1`; clearing/retyping line and rectangle dimensions is overwritten. Baseline evidence: `/tmp/dxfer-continuous-baseline/`.
- Core 1852 tests and JavaScript 942 tests pass; Web build zero warnings/errors. The installed SDK does not support the solution's slnx format, so verification uses the individual project files.
- Corrected local replay `/tmp/dxfer-continuous-current2/` passes all five scenarios: continuously moving Offset with 180ms added response latency, sequential decimal input/focus/node identity, exact keyed commit/history/native reopen, collapsed inset refusal/recovery, and Line/Rectangle decimal clear/retype/commit/history/reopen. Ten DXFs audit with zero errors/fixes; screenshots inspected. The earlier current run's selection setup race was corrected by allowing input/render state to settle before box-selection; its four functional passes and one setup failure are retained separately.
- Public deployment verification pending. No source drawings or business records modified; no existing record repair is needed.

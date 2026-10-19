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
- Final unmodified-JavaScript local replay `/tmp/dxfer-continuous-unmodified-final/` also passes all five scenarios with no browser errors. Pixel sampling spans dash periods so dashed preview gaps are not mistaken for absent geometry. Delayed replay injects only 180ms at the interop response boundary; unmodified replay installs no route/proxy hook.
- No source drawings or business records modified; no existing record repair is needed.

## Deployment

Dev `5a21cd48b5e652c6dd5d792caebbcecc19351ec5` installed 2026-10-19T04:55Z at https://dxferdev.sync.harrisonmetals.com. Four assembly hashes match the candidate, ten runtime settings are unchanged, health is 200 and anonymous root remains 302. Normal sign-in, independent Dev state and disabled Sync callbacks retained; Production/Test runtime PIDs unchanged. Rollback: `/home/ryan/dxfer-runtime/dev-publish-before-5a21cd4`.

- Public normal and 180ms-delayed replays pass all five scenarios: `/tmp/continuous-preview-dev-normal/` and `/tmp/continuous-preview-dev-delayed/`. Twenty DXFs audit with zero errors/fixes. Screenshots inspected; browser errors empty. Rendered build is `5a21cd4`, and fetched canvas SHA256 matches working/published source `379d5910df58a653155759f8bb34557b77d275b644de5aaeed9efe798a1962a2`.
- Existing public Offset regression also passes: rectangle, polyline, polygon, open contour, unsupported-selection refusal, exact keyed inset/refusal/recovery, persistent gap editing, held-drag pixels, Undo/Redo/native reopen/edit again. Its additional twenty DXFs audit with zero errors/fixes (forty public exports total). Evidence: `/tmp/continuous-preview-offset-regression/`.
- No warning/error journal entries during public replay. Owner verified, Flux/Ryan independent retest pending. No existing geometry was rewritten. Previously committed unintended numeric values cannot be inferred or automatically repaired; review and correct those deliberately.

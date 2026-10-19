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

## Continued report on 2026-10-19

Ryan reported that live dragging and key entry still fail (Slack delivery `3bf5e3bd899dee446ad30fe3`). This report remains open: the affected tool, loaded build and original drawing have not yet been established. Do not infer that the user's tab is stale or close this report from synthetic passes.

The browser regression now measures changing preview positions during motion, rather than mere visible pixels. It also directly replaces the field with Ctrl+A and individual `1`, `.`, `2`, `5` keys without clearing first, checks every prefix and the original focused DOM node, and verifies the committed driving value. Numeric inputs expose a trailing decimal as `1` while retaining `1.` in their editor; that browser behavior is not counted as lost input. Additional cases interleave pointer movement with typing and track the dependent offset during a held source drag, before pointer-up.

Initial public runs on tooltip-verified `4551c41` passed all six then-current cases with normal and 180ms-delayed preview responses (`/tmp/dxfer-continuous-4551-direct-normal/`, `/tmp/dxfer-continuous-4551-direct-delayed/`). The fetched canvas hash matches the installed fix above. The original direct typing sequence retained `1.25` and its input node. During sustained motion, sampled preview positions advanced through 13 normal and seven delayed distinct positions.

Expanded public replay passed all eight cases in both normal and delayed runs: `/tmp/dxfer-continuous-4551-held-normal/` and `/tmp/dxfer-continuous-4551-held-delayed/`. This includes held linked-source dragging and interleaved movement/typing, exact committed values, Undo/Redo and native reopen. All 42 exported DXFs audit with zero errors/fixes. Both runs verified rendered build `4551c41` and the same canvas hash; browser errors are empty. Normal screenshots were inspected, 17 focused input/preview JavaScript tests passed, and the Dev warning/error journal was empty during the replay. These synthetic passes do not close Ryan's report or establish the behavior of his existing browser session/drawing.

No application-code change or new deployment is justified by these passes alone. A read-only review separately reproduced cached RPC rejection at an unchanged request key with mocked interop; this requires a rejected RPC and has not been linked to Ryan's report. No saved drawings or records were modified or repaired.

### Decimal-specific follow-up

Ryan additionally reported that all decimals drop (delivery `2f6c011c564128ee8bc8cafc`). Expanded public `4551c41` checks passed six Fillet/Chamfer cases and ten canvas/pattern cases: leading `.25`, `0.25`, `1.25`, 650ms pauses after the decimal point, movement during entry, existing driving-dimension edits, keypad decimal input, pattern pitch and signed radial center `-.25`. Exported geometry/dimension/pattern values match the entries. Evidence: `/tmp/dxfer-decimal-4551-inspector/`, `/tmp/dxfer-decimal-4551-inspector-paused/`, `/tmp/dxfer-decimal-4551-surfaces-final/`. Screenshots and key/input-event traces are retained.

The initial simulated Shift+NumpadDecimal sequence produced no decimal text-input event and also resulted in `125` in a plain browser input outside DXFer (`/tmp/dxfer-decimal-4551-numpad-control/`); it is not accepted as an application reproduction. The final keypad checks dispatch an actual decimal key/text event with keypad location and verify both events before checking the committed value. That control produces `1.25` both outside and inside DXFer. All 19 exports from the final 16 app cases audit with zero errors/fixes. These checks do not establish Ryan's physical keyboard/browser behavior. His loaded build and exact transformation (for example `1.25` becoming `125` versus replacement of whole characters) were requested. The report remains open; no new runtime fix or automatic drawing repair is claimed.

### Renewed Offset preview report

Ryan confirmed he was using the newest build, then reported again that Offset preview geometry appears only after mouse movement stops (Slack delivery `c7867448a493874df197f61f`). Dev was at `9ac6ad2` when this renewed investigation began. Do not attribute this to stale assets without evidence.

Registered pointer-handler checks retain completed Offset results during continuing movement. Code review has not reproduced a new cache invalidation or movement-idle gate. Immediate redraw on pointer events is a performance hypothesis, not an established root cause. Earlier tests inspected canvas pixels with short pauses between moves, which does not independently prove compositor presentation under uninterrupted input.

Requested the affected native drawing and whether the gesture chooses a new Offset distance or drags already-linked geometry. Root cause remains unresolved; no speculative runtime change or existing drawing repair has been made for this renewed report.

Public `9ac6ad2` compositor replay now covers one rectangle Offset-creation path with uninterrupted real pointer input: 153 trusted pointer events over 3.01 seconds, 180 captured movement-time screen frames, preview visible in all 180 and advancing through 24 measured positions before stopping. No route/latency instrumentation or canvas-pixel reads ran during movement; analysis used the captured PNGs afterward. Evidence: `/tmp/dxfer-offset-unpaused-dev/`; frame 090 visually inspected. No browser errors or committed geometry/history changes. This does not cover the unidentified user's drawing, held-button creation, or already-linked geometry dragging; it does not close the report. Reusable diagnostic: `tests/DXFER.Blazor.Tests/offsetMotionDiagnostic.browser.mjs`.

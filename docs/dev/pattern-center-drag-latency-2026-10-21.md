# Radial Center Drag Latency

## Contract

Keep the existing center handle, card, docking and desktop layout. Pointer movement must update the actual radial copies as well as the guide and handle; it must not wait for mouse stop. Coalesce painting to animation frames. The camera must not refit during a held drag. Confirm/Cancel and native document history remain owned by the existing pattern editor.

Local preview is transient display geometry only. Enable it only when the server can prove moving the center and members has no protected geometry, attached sketch intent or cross-pattern dependencies. Source geometry stays unchanged. Other cases retain validated server previews. Releasing still validates the exact final center before allowing confirmation; cancel/errors discard transient geometry. No change to saved geometry or solver acceptance rules is authorized by a display optimization.

## Investigation

The existing client moves the center handle/guides immediately, but draws members from server-delivered `cornerPreview`. Each center update invokes a full server pattern preview and component render. The first server preview also fits the camera during a held gesture.

Public 9a0678f baseline, two sustained 121-event drags: copies lagged by 85-85.6 ms at p95, with 230.7-251.8 ms maxima. Handle p95 error stayed below 0.02 px while member error was about 4.8 px. This measures actual canvas circle draw calls, not only handle coordinates. Evidence: `/tmp/dxfer-pattern-center-latency-9a0678f-r3`.

## Implementation

The canvas DTO grants radial-center preview eligibility only for validated patterns whose moved members/center have no attached intent, protected layers or cross-pattern dependents. Source-only intent remains eligible because the source does not move. Local drawing applies the exact center-shift transform `(I - R) * delta` to immutable member geometry, preserving curve parameters. It coalesces movement to animation frames and sends only begin/release/cancel for eligible drags. Constrained patterns keep the validated server path.

The final local frame stays visible until the corresponding server-rendered preview arrives; RPC completion alone is not an acknowledgment. Errors, cancellation and document replacement clear temporary geometry. Camera fitting is suppressed during a center drag. Center data attributes retain roundtrip precision so subsequent fractional-coordinate drags use the same baseline.

## Verification

- 1,860 JavaScript tests passed, including delayed acknowledgment, cancellation/error cleanup, immutable curve transforms, held-motion frame coalescing and guarded fallback.
- 3,237 core/component tests passed; the two radial-card precision tests were rerun after the final markup change.
- Isolated candidate passed all 13 functional browser cases, including constraints, dimension edits, native history/reopen, fixed-center refusal and drag Confirm/Cancel. Evidence: `/tmp/dxfer-pattern-center-latency-functional-r2`.
- With 300 ms deliberately added to server long-poll responses, two candidate sustained drags measured 17.2 ms p95 and 33.5-34.6 ms maxima, with no held-drag camera movement. The old build under the same delayed-response setup measured 2.1-2.25 seconds p95. These are observed test traces, not a guarantee for all devices/drawing sizes. Evidence: `/tmp/dxfer-pattern-center-latency-candidate-r1-300-r2` and `/tmp/dxfer-pattern-center-latency-9a0678f-delay300`.
- Dev deployment pending. No new Flux/Ryan acceptance claimed.

This corrects the preview transport/rendering cause for eligible free-center drags. Constrained preview latency is not claimed fixed. No existing document or business-record repair is needed.

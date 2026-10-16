# Live Polyline Dimension References

## Reproduction and Shared Cause

Flux found an unanchored L polyline (0,0),(10,0),(10,6), with a driving
LinearDistance 10 created by clicking its first two vertices. A drag toward
(10,2) silently changed physical length to approximately 10.198 while the
driving value remained 10. History and native reopen retained the bad result.
This is distinct from the fixed-anchor horizontal-width regression.

Core measurement fell back to the old coordinates embedded in canvas selection
keys because `vertex-N` did not resolve to a live point reference. The shared
reference parser now maps those indexed vertices to segment endpoints. Vertex
and midpoint resolution reads current polyline geometry; missing indices refuse
instead of using saved click coordinates. Preview uses the same identities and
rejects missing vertices. This also makes same-ID dimension edits act on the
actual vertices. No document IDs or dimension values are rewritten as a repair.

## Verification

- Exact stale-coordinate tests failed on the baseline before editing.
- Core tests verify live measurement, valid translation preserving length, and
  subsequent same-ID dimension edits 10 -> 12 -> 9.
- Parser/resolver tests cover first/interior/last vertices, live midpoint
  coordinates, and missing vertices/midpoints.
- Actual browser creates the dimension through the toolbar on a plain DXF,
  verifies canvas preview equals released/exported geometry, tests history,
  native reopen, and edits the same dimension twice.
- `poly-clicks-corrupt.dxf` represents the reported old result. Browser reopen
  now shows an unsatisfied dimension, preserves geometry and value, and does not
  create an Undo entry or infer a repair.
- Existing polyline fillet/chamfer style/reference regressions still pass,
  including midpoint measurements on remapped topology.

## Combination Matrix

Core and preview coverage below is not a claim of independent browser coverage.
Fixtures are in `FixedPeerCombinationMatrixTests.cs` and
`fixedPeerCombinations.test.mjs`.

| Case | Valid | Invalid/unsupported | Recovery/order |
|---|---|---|---|
| Equal moving/fixed R3 circles, moving circle Tangent to horizontal line | Center (0,0) -> (2,.0001), fixed peer unchanged, tangent line follows | Radius 3 -> 5 with fixed equal peer refuses | Remove Fix: both radii 5, line tangent; all 6 relation orders pass |
| Fixed line y=3, tangent circle center(0,0) R3 | Center -> (0,1) gives R2 | With driving diameter6, same displacement refuses | Radius-free positive control; both orders pass |
| Midpoint line(0,0)-(10,0), Fixed marker(5,0) | Endpoint -> (12,4) gives opposite end(-2,-4), marker unchanged | Fix opposite endpoint too: refusal unchanged; collapse targets refuse | Symmetric recovery implemented in the follow-up below; both orders pass |

Flux separately reports rectangle short-edge edits/conflict/recovery/history and
fixed tangent-line/diameter center-position controls passing on 7a47210. Preserve
that evidence; do not substitute the above unit tests for its remaining replay.

## Existing Documents

No business records or manufacturing files were repaired. Previously corrupted
geometry now needs explicit user review, Undo, or a verified original. Do not
automatically reconstruct the intended shape from a dimension label alone.

## Fixed Midpoint Follow-Up

The fixed-marker limitation above was reproduced with failing core and preview
tests before the follow-up. Constraint propagation now mirrors the opposite free
endpoint about the fixed midpoint when exactly one endpoint changed. Two changed
endpoints do not identify a unique driver; no arbitrary solution is invented.
Shared driving-dimension/constraint validation and original Fixed guards remain.

- Core and preview test both relation orders, start/end drags, near-collapse
  refusal/recovery and near-degenerate valid movement.
- A fixed Equal-length peer permits a compatible 90-degree rotation about the
  fixed midpoint in both tested orders. This does not claim arbitrary coupled
  length projection is supported.
- Existing negative tests now also fix the opposite endpoint, making the fixture
  genuinely incompatible instead of expecting rejection of a valid movement.
- `mid-anchor.dxf` and `mid-locked.dxf` provide actual-canvas fixtures for preview,
  release, unchanged/no-Undo refusal, history/native reopen and repeated edits.

Independent Flux acceptance: a96a5de covers committed anchored line/polyline/weld
behavior, history/reopen/reedit, IDs and clean DXF audits. Held-pointer preview
remains owner-verified, not independently claimed. 678a2ca independently closes
the exact stale-vertex L-shape case, repeated same-ID 10/12/9 edits, and red
warning on unchanged old corrupt geometry. No overall solver acceptance claimed.

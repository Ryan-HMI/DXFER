# Solver recovery QA handoff

This change targets false PlaneGCS refusals in full-ellipse symmetry, large slot
width edits, four-bar angle edits, and repeated native drawing opens. Deploy to
DXFER Dev only. Independent Flux acceptance is separate from owner-run tests.
Sync Test revision notes and CNC filtering are a separate handoff.

## Shared corrections

- Retry a failed native solve from its original seed with DogLeg, retaining the
  entire hard system and every temporary drag or stability goal. Residual,
  fixed-geometry and dimension-rank validation still gate publication.
- An explicitly edited line angle may retry its equivalent geometric sectors.
  Other stored dimensions are not changed to make the edit solve. Search is
  limited to one edited angle, with at most three alternatives.
- Treat full ellipses as undirected geometry while preserving explicit endpoint
  identity. Choose a compatible mirrored frame; on refusal, trace the conflicting
  parameter components before retrying alternate exact correspondences. Partial
  ellipses retain their existing directed endpoint behavior.
- Map three-point midpoint identities to linear equations. This avoids native
  rank changing with tiny accepted residuals and incorrectly classifying an
  independently editable minor radius as redundant.
- Use the shared live reference resolver for C# Coincident and point-pair H/V
  validation. Ellipse axis endpoints are evaluated from current geometry, not
  stale cursor-acquisition coordinates. Unknown identities and nonfinite points
  refuse; the general measurement resolver's snapshot fallback is not used.

No tolerance relaxation, constraint deletion, hard-only recovery, or drawing
rewrite is used. Full-ellipse branch search is bounded to 32 attempts and a
500 ms retry budget; exhaustion refuses atomically with an explicit diagnostic.
This is not a claim of complete nonlinear search for arbitrary sketches.

## Disposable browser cases

All fixtures are in `tests/DXFER.Blazor.Tests/fixtures/qa-20261104/`.
Use a new disposable drawing. Download and reopen native DXF when requested.

| Fixture | Replay | Expected result |
| --- | --- | --- |
| `same-direction-ellipses.dxf` | Pick the two ellipses, then their vertical axis; apply Symmetric. Repeat tool-first. | One satisfied relation; the already mirrored geometry stays in place. Undo/Redo and reopen preserve the result. |
| `slot-horizontal-width20.dxf` and `slot-sloped-width20.dxf` | Edit width 20 to 10, then back to 20 after reopening. | Short centerline slot resizes; tangent joins and source relationships remain satisfied. |
| `four-bar-left-leaning.dxf` and `four-bar-right-leaning.dxf` | Add/edit the linked angle through 180, then 160 after reopening. | A reachable angle sector solves while the base and all link lengths remain fixed. See `largeStep.browser.mjs` for exact UI picks. |
| `session-witness.dxf` | Apply Symmetric to the two polygons about the fixed axis; open the original fixture again in the same browser page and repeat three times. | Each solve succeeds without reload; the original drawing is restored on each open. |
| `rectangle.dxf`, then `session-witness.dxf` | In a fresh page edit rectangle width 15 to 16, open the witness and apply Symmetric. | First witness solve succeeds after the unrelated edit. |
| `collapsed-circles.dxf`, `collapsed-lines.dxf`, `collapsed-points.dxf` | Resize or drag the coincident mirrored peers. | They can separate symmetrically; their fixed axis remains unchanged. |
| Polygon and elliptical-arc creation fixtures with recipe JSON files | Create a line from the indicated vertex/endpoint by mouse and by keyed length. | Source is unchanged; Coincident and keyed driving length persist through history/reopen. |

For slot and four-bar controls, fixing the geometry before an incompatible edit
must still refuse without geometry movement. A refused dimension's stored intent
may remain unsatisfied; refusal must not silently drop constraints.

## Reproducible owner verification

The combined source passes 2,134 JavaScript tests and 3,795 C# core tests, with no
skips or TODOs. Native regression coverage includes both full-ellipse frames,
both pick orders, six independent pairs, connected cycles, exact fixed-conflict
refusal, independent versus redundant dimensions, and session/drag recovery.

Actual UI replay scripts use disposable drawings, a localhost-only application
and random transient credentials. They close their own browser/server:

- `collapsedSymmetry.browser.mjs`: 17 symmetry and creation-reference cases.
- `largeStep.browser.mjs`: six slot/four-bar/history/reopen/refusal workflows.
- `sessionReplay.browser.mjs`, with `DXFER_SESSION_WITNESS=reopen` or `rectangle`:
  14 or 11 cases, asserting native sidecar continuity until real browser reload.

The 17-case browser replay passed at widths 1440 and 1180; the six large-step
workflows and both session replays also passed against the combined build, with
zero browser errors. The subsequent strict-reference correction has its own
negative regressions in the full C# result above.

See `browser-session-solver-2026-11-04.md` for the native numerical diagnosis.
The synthetic session witness reproduces both observed triggers; Flux's exact
original drawing is not present, so its independent acceptance remains open.

## Existing drawings

These are shared code corrections, not data repairs. No existing user drawings
were modified. Previously refused operations can be retried; old failed intent
is not automatically removed, and previously omitted creation dimensions or
anchors are not inferred or backfilled. Review those saved drawings explicitly.

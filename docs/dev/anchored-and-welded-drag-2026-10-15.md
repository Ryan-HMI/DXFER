# Anchored and Welded Drag Hardening

Scope: DXFer Dev only. No engine replacement or visual changes.

## Reproduced Causes

1. The dimension-preserving drag fallback only tried translation. A line with
   fixed start and driving length could rotate, but the valid drag was refused.
2. The polyline translation fallback checked whole-entity Fix but ignored fixed
   vertices. It could move a fixed start while keeping the driving width valid.
3. Core translation fallback validated before propagating welded peers. Browser
   preview moved those peers, so a valid mixed weld preview was refused on release.

## Shared Fixes

- Single anchored linear/H/V endpoint dimensions reuse the existing dimension
  solver for bounded analytic recovery. Original driving values are retained.
- Translation candidates propagate constraints before validation.
- Polyline translation respects fixed vertices; every accepted core drag now
  compares fixed references against the original document before committing.
- Browser preview follows the same bounded recovery and fixed-reference checks.
- Unsupported movement reports an inability to satisfy the supported movement,
  not a proof of mathematical inconsistency. Zero-direction recovery refuses.

## Verification

The six disposable fixtures in `tests/DXFER.Blazor.Tests/fixtures/drag-matrix`
are independently replayable. Local actual-canvas tests cover preview/release,
refusal without an Undo entry, valid history, native download/reopen and edit
again. Core/preview regressions also cover near-degenerate targets, fixed peers,
recovery and meaningful constraint-order permutations.

Free line translation, fixed-start rotation, fixed-endpoint polyline width and
line/polyline/point welding pass. Coupled multiple driving dimensions and
closed-polyline alias variants are not claimed by this bounded recovery.
Equal/Midpoint/Tangent with Fixed combinations remain the next matrix work.

No existing documents were modified or repaired. Previously saved geometry
that was moved incorrectly needs Undo or a verified original, not inferred
reconstruction. Deployment and independent QA are recorded separately.

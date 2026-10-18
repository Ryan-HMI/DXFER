# Coincident Point/Edge and Edge/Edge

## Scope and Interaction Contract

Ryan requested Coincident for a point and edge, and two edges to make lines
collinear. The existing button and glyph handle all three selection combinations:
point/point remains a weld, point/straight-edge is point-on-supporting-line, and
straight-edge/straight-edge is collinear. Segment overlap is not required.

No palette, inspector, alignment, wrapping, width, overflow or hit-area redesign.
The prompt names the eligible combinations. Both selection orders work. Point
and collinear-line drags retain along-line freedom; line alignment preserves
length, and Fixed/driving constraints still validate before commit. Unsupported
coupled cases may refuse unchanged; this is not a complete general solver.

## Shared Implementation

The command classifies the selected references into persistent PointOnLine or
Collinear relations. Keeping these distinct from point/point Coincident avoids
turning sliding incidence into an endpoint weld in topology/anchor traversal.
Core creation/propagation/validation and canvas drag preview implement matching
projection/alignment behavior. Native DXF intent retains the new relation kinds.
Degenerate supporting lines refuse, rather than inventing a direction.

Browser verification also exposed an existing shared picker exception: origin
selection inspected `target.point` on an already-selected standalone Point,
whose location is stored as `snapPoint`. The picker now uses the existing target
location resolver. A regression covers point-first picking and origin targeting.
Canvas and nested target-module URLs are versioned for the change.

## Verification

- 1,730 core tests and 893 JavaScript tests pass; Web build passes.
- New tests cover both orders, endpoint/edge, standalone point/edge, origin/edge,
  polyline segments, non-overlapping collinear lines, fixed endpoints, driving
  length preservation, conflicting angles/Fixed peers, degenerate lines and
  diagonal drag projection/propagation to free peers.
- Synthetic native fixtures are generated through the application DXF writer.
  Browser coverage uses actual Coincident picks, both orders, conflicting Fixed
  controls, Undo/Redo, native download/reopen and projected drag after reopen.
- Evidence: `/tmp/incidence-core-full.log`, `/tmp/incidence-js-full.log`,
  `/tmp/incidence-build.log`, `/tmp/incidence-browser.log`,
  `/tmp/line-incidence-local`.

No existing drawings or business records were changed. No data repair is needed
for this feature or the shared picker fix. Previously saved drawings do not gain
new constraints automatically. Independent QA remains separate from owner tests.

## Deployment

Pending verified Dev-only rollout. Production/Test remain unchanged.

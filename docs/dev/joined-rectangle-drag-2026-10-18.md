# Joined Rectangle Edge Drag

## Report and Reproduction

Ryan reported an edge refusing to drag and the tab locking up after sustained
movement. Slack attachment F0CA4RVCMRC shows an upright rectangle anchored at the
origin, an angled rectangle sharing that corner, and a nearby circle. The exact
native drawing has not been supplied. This fix reproduces the corner-welded
rectangle rejection, not every constraint in that screenshot or the exact freeze.

Synthetic fixture: A corners (0,0), (6,0), (6,9), (0,9); B corners (0,0),
(12,-2), (11.5,-5), (-0.5,-3). Each has four corner coincidences, opposite-edge
Parallel relations and one Perpendicular relation. A also has Horizontal and
Vertical, its lower-left corner Coincident to origin, and B's first corner welded
to that corner. Drag A's right edge from (6,4.5) to (8,5.5).

Before the fix, both browser preview and core reject the diagonal drag. Removing
the inter-rectangle weld restores the valid perpendicular edge resize. Four
regressions fail in each implementation (body/midpoint, both constraint orders).

## Shared Cause and Fix

Rectangle detection treated the entire Coincident-connected component as one
rectangle and required exactly four lines. An external corner weld therefore
disabled rectangle-specific perpendicular drag projection. Both implementations
now find a unique four-edge cycle containing the selected line and require the
existing rectangle relations. Multiple candidates or an excessive local search
decline this specialization; the existing constraint validation is retained.

A recognized rectangle with fewer than two driving dimensions also fell through
to a whole-drawing four-line-combination search for modified rectangles. It now
returns immediately. With forty unrelated lines preceding the rectangle, the
regression records 292,460 constraint-kind reads before and 1,166 after. This is
a reproduced avoidable cost, not proof of Ryan's exact tab-freeze cause.

## Interaction Contract

- Existing palette, canvas, inspector alignment/sizing/wrapping/overflow and hit
  areas remain unchanged. No visual redesign or snap tolerance changes.
- A free rectangle edge projects a diagonal pointer movement perpendicular to
  itself; corner welds and the peer rectangle remain intact.
- Fixed geometry and driving dimensions still validate before commit. A refused
  drag adds no Undo entry; no constraint is silently removed.
- Sustained held preview and release agree, with Undo/Redo, native reopen and
  another edit verified. The versioned canvas module forces the new preview code.

## Verification

- 1,717 core tests and 882 JavaScript tests pass; Web build passes.
- New core/preview regressions cover both constraint orders, body/midpoint picks,
  fixed-edge refusal, source/peer preservation and bounded constraint visits.
- Local browser checks exercise 100 pointer moves over more than two seconds per
  drag, held-preview positions, event-loop heartbeats, release, Undo/Redo,
  native download/reopen and another edit. Both orders and fixed controls pass.
- Browser fixtures are generated through the application's native DXF writer;
  no manufacturing drawings or live business records are edited.
- Evidence: `/tmp/joined-rectangle-core-before.log`,
  `/tmp/joined-cost-regression-before.log`, `/tmp/joined-core-all.log`,
  `/tmp/joined-js-all.log`, `/tmp/joined-browser-local.log`,
  `/tmp/joined-rectangle-local-final`.

The systemic joined-rectangle rejection is fixed; no existing records require
repair for this fix. Ryan's exact freeze remains open pending his native drawing
or a replay of this installed change. No blanket solver acceptance is claimed.
Independent QA remains pending.

## Deployment

Installed `f8129a2a9b15b551d41cf4531d16385f547f2353` on DXFer Dev at
2026-10-18T22:46Z. Four assemblies match the published candidate; ten runtime
settings preserved, health200 and anonymous302. Normal sign-in and disabled Sync
callbacks retained; Production/Test runtimes unchanged.

Public browser verifies rendered build f8129a2 and all four fixtures. Six sustained
drags (including re-edits) retain a responsive 20ms heartbeat, with observed
maximum gaps of 20.5-24.8ms. This is a measured run, not a universal timing bound.
Held geometry, release, peer preservation, Undo/Redo/native reopen/re-edit and
fixed refusal pass. Six exported DXFs audit with zero errors/fixes; screenshot
checked. Evidence `/tmp/joined-browser-dev.log`, `/tmp/joined-rectangle-dev`.
Local QA is stopped. Exact original drawing/freeze and independent Flux replay
remain outstanding; no records were repaired or need repair for this code fix.

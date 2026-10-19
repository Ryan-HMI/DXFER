# Junction endpoint picking

Ryan delivery e80d78b74488dca11981aa4c, screenshot F0CA5L56W4W.
Flux minimized free T-junction: carrier (2,4)-(5,-2), branch ends at (3,2),
PointOnLine only. Drag toward (4,3) reports "Drag the line endpoint, midpoint,
or edge."

## Interaction contract

Keep existing canvas/palette dimensions, colors, layout and pointer hit radii.
At the same world location, a real geometry grip must win over a synthetic
intersection when selecting/dragging. Do not enlarge hit areas, target unrelated
nearby vertices, bypass Fixed/locked peers, or change creation snap provenance.
Preview and committed drag must operate on the same real reference. Independent
virtual crossings do not become new vertices or silently split geometry.

## Verification

- Before the fix, four of eight exact/off-center junction pick tests fail when
  the carrier precedes the branch: selected key is
  `carrier|point|intersection-branch-0|3|2`, not `branch|point|end|3|2`.
- Four Core tests pass without a solver change: the real endpoint moves to (4,3),
  incidence stays satisfied, both entity orders work, Fixed endpoints refuse,
  and native reopen/edit-again preserves the relationship.
- Shared within-entity/across-entity picking now prefers real grips over
  co-located synthetic intersection/tangent points in Select mode only. Creation
  provenance and ordinary distance ranking are unchanged. Canvas module URL is
  versioned so a refresh loads the correction.
- 1,761 Core tests, 933 JavaScript tests and Web build pass. Tests cover both
  entity orders, two-pixel off-center grabs, Fixed and locked controls, nearby
  unrelated endpoints, creation intersection provenance and true virtual crossings.
- Local actual-browser positive and Fixed controls in both entity orders pass:
  held preview/release, unchanged stored constraints, Undo/Redo, native reopen and
  editing again. Ten exported DXFs audit with zero errors/fixes. Held screenshot
  inspected; no browser circuit errors.
- DEV `456f536f9e81f406a5b31143b658ffd9fbd0dbbc` installed
  2026-10-19T02:08Z. Public browser repeats all four fixtures successfully; rendered
  build matches. Ten public DXFs audit zero errors/fixes. Public held screenshot
  inspected; no browser errors or server warning/error entries during replay.
- Four installed assembly hashes match; ten runtime settings retained, health200,
  anonymous root302, normal sign-in and disabled Sync callbacks retained.
  Production/Test process IDs unchanged. Rollback:
  `/home/ryan/dxfer-runtime/dev-publish-before-456f536`.
- Evidence `/tmp/junction-{pick-before,pick-after,core,js,build,publish,local,dev}.log`;
  screenshots/exports `/tmp/junction-local` and `/tmp/junction-dev`.
- Independent Flux acceptance, delivery `64b26efe45956ce483a7f362`, on 456f536:
  original PointOnLine fixture drags with satisfied incidence; both entity orders
  pass Undo/Redo, native reopen and another edit. Fixed endpoints refuse without
  changing geometry or adding history. This closes the reproduced picking case.
- Split-at-point variant also drags, but the operation created no Coincident
  relations and its endpoint moves independently. This does not verify a welded
  junction. Explicit welded-junction coverage and Ryan's original native drawing
  remain separate from this acceptance.
- Subsequent independent control, delivery `4e56a901ada6742ffcec90b4`, on 456f536:
  explicitly adding Coincident between the two split endpoints keeps them moving
  together with Satisfied intent through Undo/Redo, native reopen and a second
  drag. Far endpoints stay unchanged. This closes the explicit two-endpoint weld
  checkpoint above. The third branch was deliberately unwelded, so this does not
  accept a three-way weld or Ryan's exact original drawing.

The identified shared picking defect is fixed, not worked around by deleting
constraints or treating a synthetic intersection as a new vertex. Existing saved
drawings do not require repair. Ryan's exact original native file was not supplied;
this is the matching minimized T-junction, not universal drag/solver acceptance.

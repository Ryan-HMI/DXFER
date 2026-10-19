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
- Public Dev rollout/replay pending. No Production/Test changes intended.

The identified shared picking defect is fixed, not worked around by deleting
constraints or treating a synthetic intersection as a new vertex. Existing saved
drawings do not require repair. Ryan's exact original native file was not supplied;
this is the matching minimized T-junction, not universal drag/solver acceptance.

# Tangent Contact Leader

## Layout and Interaction Contract

- Tangent markers attach to the actual contact point, not the average of entity centers/midpoints.
- A short leader is visible at the default marker position; dragging the marker changes only its screen offset and keeps the leader on the contact.
- Contact follows live geometry during preview/release and zoom/pan. Other constraint marker layouts and hit targets stay unchanged.
- Tangent has a separate marker group from other relations on the same pair, preventing averaged anchors from displacing the contact.
- For missing, degenerate, non-contacting or unsupported geometry, retain the relation marker without drawing a falsely precise contact leader. Line extensions outside the drawn segment and contacts outside an arc sweep do not imply a visible contact.
- No changes to constraints, dimensions, solver behavior or exported geometry.

## Verification

- Root cause: generic relation anchors averaged entity centers/midpoints, and grouping different relation kinds could average away a corrected contact. Tangent now has its own contact-aware group and default leader.
- 19 new JavaScript cases cover reversed references, rotated lines, polyline segments, external/internal circle contacts, arc sweeps, Equal grouping, invalid contacts, marker movement, zoom and constrained preview.
- Full JavaScript suite: 719 passed. Core/.NET suite: 1480 passed. Web build: zero warnings/errors.
- Disposable desktop browser test passes: default leader pixels visible at contact, marker drag leaves geometry/constraints/dimensions and Undo history unchanged, circle slides while preserving tangency, Undo/Redo and native download/reopen pass. Screenshots of default/moved marker and held slide inspected at 1440x1000.
- Evidence: `/tmp/dxfer-tangent-contact-evidence`. Both generated DXFs audit with zero errors/fixes.
- This is a shared rendering fix, not a solver or data repair. Existing drawings need no repair; refresh loads the corrected module. Deployment scope is DXFer Dev only.

# Redundant driving distances

UT-SOLVER-006 was only partially covered by an old entity-level heuristic. It
looked for matching horizontal/vertical lengths in a connected line component,
without proving those coordinates were fixed. It missed point projections and
polyline paths and could reject an unconstrained hinge merely because lengths
matched.

The shared check now follows live point-reference identities and satisfied
Coincident, axis, parallel/perpendicular and Fix evidence. Driving projections
and axis-proven lengths establish fixed coordinate differences. If both endpoint
differences are fixed, an additional driving distance is redundant even when its
number matches the measured distance. Edited IDs, reference-only measurements,
suppressed constraints, stale satisfied flags and failed dependency evidence
cannot supply the proof. Existing adapter rollback preserves geometry and keeps
editable failed intent. No replacement solver or external dependency added.

## Verification

- Worker32 focused cases; integrated845 .NET tests pass. Scoped formatting passes.
- Actual deployed-before browser accepted a driving5 between points with driving
  horizontal3/vertical4. Local corrected browser rejects it with visible failure,
  identical geometry and original3/4 dimensions, and only the new dimension red.
- Undo removes failed intent, Redo restores it, and native download/reopen retains
  the exact geometry and dimension records with the failure explanation.
- Screenshot `/tmp/dxfer-redundant-after.png` inspected. Repeatable browser check:
  `/tmp/dxfer-drag-live-qa.cjs --projections`; disposable fixture generated under
  `/tmp/dxfer-drag-fixtures`. No saved user file was used or changed.

Deployed Dev183c2c1 on2026-10-15. The installed browser repeated the same
rejection, stable geometry, Undo/Redo and native reopen checks successfully.
Release assemblies match;10 runtime settings retained, normal sign-in302,
health200, Sync callbacks disabled. Production/Test processes unchanged.
This is app verification, not Ryan acceptance.

## Limits And Separate Work

This is a conservative orthogonal proof, not a general rotated/nonlinear rank
solver. It recognizes live supported point references across lines, polylines,
points, circle/arc centers, arc endpoints and polygon centers. Snapshot-only
ellipse/spline samples do not prove dependencies. A failed proof is not evidence
that a general system is independent. Previously accepted redundant intent may
need deletion or conversion to reference; no stored document repair is attempted.

The browser check also exposed existing diagonal guide rendering for horizontal/
vertical dimensions. Numerical measurement/solver semantics are correct; visual
projection is tracked separately as AXIS-DIMENSION-GUIDES-20261015. It is not
silently marked fixed by this core patch.

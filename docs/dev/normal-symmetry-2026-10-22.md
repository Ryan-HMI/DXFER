# Normal And Symmetry Constraints

Ryan requested persistent symmetry between two matching entities and a selected
line axis, including both position and size. Normal and Symmetric were disabled
UI placeholders, not completed constraints.

## Interaction Contract

Use existing constraint palette buttons, icons, selection cues, hotkey settings
and failure feedback. No new panel, card, spacing or dock behavior. Symmetry takes
the pair first and axis last; preserve that order in preselection and tool-first
workflows. Store correspondence, not a newly chosen pairing on every edit.
Normal uses a line endpoint at contact with a circle/arc and keeps the line radial
to that curve. Unsupported selections must give a specific refusal.

## Solver Contract

Use native PlaneGCS equations. Symmetry reflects point pairs about a nondegenerate
line; mirrored circles additionally have equal radii. Reflected arcs reverse
endpoint correspondence and retain equal sweeps. Normal keeps its contact point
on the finite curve and the curve center on the supporting line. Validate both
solver residuals and reconstructed native geometry. Fixed/locked geometry,
driving intent, history and native persistence must remain protected.

## Implemented Scope

- Symmetric point pairs, whole lines, circles, arcs and matching-topology straight
  polylines. Polyline correspondence is a stored forward/reverse topology walk;
  closed contours may use a cyclic shift. Ellipses, splines and mismatched types
  are explicitly unsupported by this new command.
- Normal between a line/straight polyline-segment endpoint and a circle or finite
  arc, in either pick order. A body pick chooses the nearest endpoint. This is
  radial contact, not the existing line-line Perpendicular relation.
- Preselection and tool-first selection, configurable hotkeys, shared palette and
  canvas glyphs. The selected symmetry axis is always the third pick.
- Original finite curves and canonical references are checked before solving;
  finite-arc endpoint tolerance is spatial rather than a scale-dependent angle.

## Verification

- Full .NET suite: 3,433 passed, including actual PlaneGCS runtime tests.
- Full JavaScript suite: 1,939 passed, including 20 native Normal/Symmetric tests.
- Release candidate `beadcc7`: six real-browser cases passed, producing 68 native
  exports. Circle symmetry and line-circle Normal each cover preselection,
  tool-first selection, driving-radius edits, source movement, Undo/Redo,
  native reopen/edit again, and fixed-conflict refusal. Failed intent stays
  visibly unsatisfied while the previous geometry remains intact.
- Core/native tests additionally cover reflected arcs, oblique axes, paired
  lines, closed polylines, axis rotation, finite-arc Normal and invalid inputs.
  These additional kinds are not claimed as independently browser-tested.
- Candidate evidence: `/tmp/dxfer-normal-symmetry-beadcc7-qa/results.json`.

## Deployment

DXFer Dev only: `beadcc73699a81a13dcc0d727248d83737f9e961`, installed
2026-10-22T12:48:51Z. Public-URL replay independently verified the rendered build
tooltip and passed all six cases with 68 native exports and no browser/circuit
errors. Owner evidence: `/tmp/dxfer-normal-symmetry-beadcc7-public/results.json`;
screenshots were visually inspected. This is owner QA, not Flux acceptance.
Google sign-in remains
configured, Sync callbacks remain disabled, and Production/Test were untouched.

This implements the missing shared constraint paths, not a per-drawing repair.
No existing records need repair for these newly enabled commands; no relations
are automatically added to existing drawings. Flux's independent acceptance is
still pending.

# Three-Point Midpoint

## Behavior

Midpoint accepts three point references belonging to separate entities, in
addition to its existing point-plus-line form. The uniquely closest candidate
to the average of the other two is selected as the middle. Ambiguous, coincident
or nonfinite choices refuse without adding history and show an explanation.
The stored references are `[middle, outerA, outerB]`; later edits never re-elect
the middle based on its new position.

No helper points or construction lines are inserted. A derived line midpoint
can remain a reference to its source geometry; representing it does not require
adding exported geometry. This does not assert how Onshape implements its tool.

## Implementation

Creation, validation and propagation share a three-point relation implementation.
Moving an outer point updates a free middle. Moving the middle translates both
free outer points, or reflects the one free outer around the middle when its peer
is fixed. With a fixed middle, moving one outer can reflect the other free outer.
Browser drag preview follows the same rules.

Candidate writes are atomic. Duplicate physical point aliases, malformed segment
references, nonfinite results and incompatible fixed/driving intent do not permit
partial geometry updates. Existing protected-layer and generated-geometry checks
remain at the solve acceptance boundary.

This extends the existing procedural solver, not a general simultaneous solver.
Derived read-only references are not automatically made writable. Unsupported
coupled edits can still refuse; no constraint is silently removed to make a
candidate succeed. Ordinary point-plus-line Midpoint remains unchanged.

## Verification

- 2,641 core tests pass, including selection permutations, mixed owners, fixed
  alternatives, driving conflicts, alias protection and native persistence.
- 1,162 JavaScript tests pass, including three-point preview propagation,
  fixed/driving refusal and malformed references.
- Web build: zero warnings and errors.
- Eight actual-browser cases cover selection-first/tool-first, standalone
  points, mixed Point/circle-center/line-endpoint picks, ambiguity feedback and
  legacy point-plus-line operation. Positive cases check Undo/Redo and native
  download/reopen; source-follow cases check held-pointer preview and another
  edit after reopen.

Logs: `/tmp/dxfer-three-midpoint-core.log`,
`/tmp/dxfer-three-midpoint-js-final.log`,
`/tmp/dxfer-three-midpoint-build-final.log`.
Local browser evidence: `/tmp/dxfer-three-point-midpoint-local-final/`.
Installed Dev evidence is recorded separately after deployment; precommit local
tooltips identify the preceding commit and are not deployed-version proof.

## Existing Documents

No stored drawings or application records are rewritten. This adds a missing
relation form; existing two-reference Midpoint relations retain their meaning.
No existing-data repair is required for this feature.

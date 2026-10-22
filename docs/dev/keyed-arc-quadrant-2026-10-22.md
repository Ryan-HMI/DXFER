# Keyed Tangent Arc Quadrant Intent

Flux reported that acquiring a quadrant, changing radius and pressing Enter
created the right arc but omitted its promised endpoint/center axis constraint.
Earlier browser checks moved the pointer after key-in, reacquiring the snap and
therefore masking this path.

The keyed radius changes the endpoint position. Commit previously compared the
new endpoint against the old pointer snap and discarded the relation. Retain and
remap only the accepted draft-arc quadrant target as the radius changes, then
use that target for the snap cue and commit. Pointer re-aim clears the retained
target. External geometry snap references are not moved or inferred.

Interaction contract: preserve the existing radius field, keyboard focus,
palette layout, cue clamping and Auto ON/OFF behavior. No new controls or dialogs.

Verification:

- Public Dev `10a1310` reproduced the missing Horizontal relation (`0 != 1`)
  after radius 3/Enter, with identical pointer event counts before and after
  typing. Evidence: `/tmp/dxfer-tangent-arc-keyed-enter-dev10a1310/results.json`.
- All 1,915 JavaScript tests pass, including 19 quadrant-focused tests.
- Isolated browser candidate passes all eight cases with 74 native exports:
  quarter/half turns, typed radius 3 committed with Enter without intervening
  pointer events, Auto ON/OFF, re-aim away from the accepted quadrant, existing
  mouse paths, Undo/Redo and native reopen. Zero browser errors.
- Evidence: `/tmp/dxfer-keyed-quadrant-final-qa/results.json`; screenshots checked.

Dev `cdfb098cab19068b5667445572fa707a6bb31b8d` installed at
`2026-10-22T11:50:35Z`; public browser replay passes all eight cases and 74 native
exports with zero browser errors. Rendered tooltip independently reports
`cdfb098`; installed screenshots checked. Evidence:
`/tmp/dxfer-keyed-quadrant-installed/results.json`.
Google sign-in remains configured; Production/Test and Sync are untouched.
Production/Test process IDs remain 3584417/3584475. Isolated QA5038 stopped.

Flux independently accepted the keyed-radius omission on public Dev `cdfb098`
(Slack delivery `653d66e242f2dcf2aff70bd1`). Auto ON, near-90-degree acquisition,
radius 3 and Enter without pointer movement retain a satisfied center/end
Horizontal alongside Coincident/Tangent. Native reopen/re-export retain radius
and relation. Auto OFF adds no automatic constraints; re-aiming off-quadrant
clears the candidate without retaining a stale relation. Auto constraints were
restored ON. This closes that reported omission only. The separate screenshot
solver conflict remains unverified and open.

Existing arcs are not rewritten: drawings
saved through the old path may still lack the intended quadrant relation, which
must be deliberately reapplied or the arc recreated. Geometry alone cannot prove
that a missing relation was originally intended.

# Trim at Arc Endpoints

## Reproduction and Cause

Ryan reported a rectangle edge touching both ends of a semicircle. Public Dev
`fd7eddc` trims the unconstrained equivalent, but refuses the same edge with
ordinary rectangle corner welds. A separate fixture adds Midpoint between the
original edge and arc center, matching the visible relationship in the screenshot
without claiming to reconstruct Ryan's exact drawing.

Middle trim keeps the original line ID on its start-side fragment and creates a
second line for the far fragment. Previously the original end-point constraint
references stayed on the first fragment's new cut endpoint. Refreshing coordinates
did not preserve endpoint identity. The initial 33-case core suite had ten failures:
eight retained-end weld/Fix cases and two small-scale intersection cases.

## Repair

- Map retained end-point constraint references onto the new far fragment only
  when it is the sole new line and both pieces are verified subsets of the original
  line. Preserve canonical and raw canvas references. The shared finalizer also
  covers Split at point. Copy the original layer/style to that fragment.
- Compare Fixed point positions using the mapped reference, while protecting
  whole-entity Fix and original driving spans. Dimension references are deliberately
  not remapped across the removed gap. No implicit constraint deletion or new
  virtual construction geometry; no duplication of whole-edge H/V onto fragments.
- Report the specific invalidated constraint kind, including Midpoint. A whole-edge
  midpoint relationship is not equivalent to either remaining fragment's midpoint.
- Use distance-based line/circle intersection instead of a fixed discriminant
  tolerance that collapses small secants to tangencies. Normalize angular wrap at
  an arc's start endpoint without widening interior-cut eligibility.

## Verification

Baseline browser evidence: `/tmp/dxfer-trim-arc-constrained-baseline/`.
All 2,411 core and 1,114 JavaScript tests pass, including 101 new trim/numerical
cases. Web build has zero warnings/errors. The existing exact circle-cutter test
also remains unchanged and passes; the half-chord calculation avoids unnecessary
double-square-root rounding at normal scales, with an overflow fallback.

Isolated Chromium passes all three fixtures: unconstrained trim, corner-welded
rectangle trim, and explicit Midpoint refusal. The welded case checks the exact
mapped constraints, unchanged peer edges/arc, Undo/Redo, native download/reopen,
and refusal of a subsequent trim that would delete a welded remnant. The refusal
adds no history. Success and readable refusal screenshots were inspected; no
browser errors. Evidence: `/tmp/dxfer-trim-arc-final/`,
`/tmp/dxfer-trim-arc-browser.log`, `/tmp/dxfer-trim-arc-core.log`,
`/tmp/dxfer-trim-arc-js.log`, `/tmp/dxfer-trim-arc-build.log`.
The isolated precommit build tooltip still names the prior commit; this is local
candidate verification, not a claim that the old public build was fixed.

Scope: DXFer Dev only. No saved drawings or Sync records are rewritten. Previously
refused edits can be retried; a genuine midpoint/full-span conflict still requires
a deliberate intent change. No automatic reconstruction of previously deleted
geometry is attempted.

# Midpoint Alignment To Origin

## Cause

Ryan clarified that the rejected Horizontal relation connects a line midpoint
to the origin, not the circle in the screenshot. Public Dev `580b232` reproduced
this with actual midpoint/origin picks on a joined rectangle. Exported references
were correct, but the relation remained Unsatisfied and geometry stayed unchanged.

Midpoints are derived, read-only references. Axis alignment tried to write one
of its two point references; neither the midpoint nor the origin is writable.
It never proposed moving the midpoint's underlying line.

## Correction

For H/V alignment between a midpoint and the origin, translate the free source
line's two endpoints equally along the constrained axis. Existing propagation
updates joined geometry, and the complete solve validates all driving intent.
Both pick orders work. The origin stays virtual and immutable.

This is not generic inverse-midpoint editing: established read-only behavior for
midpoint-to-midpoint and fixed-circle-to-midpoint relations remains unchanged.
No-op alignment does not rewrite entities. Undimensioned neighboring edges may
change length; declared dimensions must remain satisfied.

Reuse the existing drag fixed-geometry comparison at the solver acceptance
boundary. This prevents a polyline's alternate shared-vertex reference from
bypassing Fix, including an adjacent fixed segment. Failed candidates retain
original geometry; no constraint or dimension is silently discarded.

## Verification

The final core suite passes 2,567 tests, including 54 new focused cases.
All 1,146 JavaScript tests pass. Web build has zero warnings/errors.
Focused cases cover H/V, both orders, raw/canonical references, standalone lines,
joined dimensioned rectangles, source/peer Fix, driving-coordinate conflicts,
polyline closure and shared-vertex aliases, native persistence and protected
layer acceptance.

Public baseline evidence: `/tmp/dxfer-midpoint-origin-baseline/`.
Core and JS logs: `/tmp/dxfer-midpoint-origin-core.log` and
`/tmp/dxfer-midpoint-origin-js.log`.

The browser regression exercises actual tool-first picks on undimensioned and
dimensioned rectangles, plus fixed refusal, Undo/Redo and native reopen.
All ten isolated-browser cases pass, with no browser errors. The local precommit
tooltip still names the old commit and is not installed-build evidence.
Local verification output: `/tmp/dxfer-midpoint-origin-final/`.
Installed verification output: `/tmp/dxfer-midpoint-origin-dev/`.

Ryan subsequently clarified selection-first, then tool click (delivery
`d56d0c1f5507e7f8d40b6f95`). Ten additional actual-browser cases pass on rendered
Dev `c397469`: preselect the midpoint and origin in either order, then click H/V.
The test verifies both selected reference identities before the tool click.
Dimensioned/undimensioned rectangles, Fixed refusal, Undo/Redo and native reopen
pass with no browser errors. Evidence:
`/tmp/dxfer-midpoint-origin-preselect-dev/` and
`/tmp/dxfer-midpoint-origin-preselect-dev.log`.
This follow-up changes tests/documentation only; installed runtime is unchanged.

One separate existing limitation is characterized, not fixed: horizontal
single-edge dragging of the already-aligned rectangle with a driving width can
refuse rather than translate the entire rectangle. This is not claimed as a
general rectangle-drag fix.

## Existing Documents

No files or application records are rewritten. Earlier refused solves preserved
geometry, so no geometry repair is indicated. A saved failed Horizontal/Vertical
intent is not silently re-solved on import; undo or remove only that failed intent
and apply it again after refreshing. Preserve other valid constraints.

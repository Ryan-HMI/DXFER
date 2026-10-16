# Origin references and dimension placement

## Contract

- DXFer Dev only. Desktop canvas and existing independent palettes retain their layout.
- Origin is an immutable reference at (0,0), not an exportable drawing entity.
  Select it for point constraints and dimensions with the existing screen-space hit tolerance.
  A geometry endpoint wins an exact overlap; origin remains available when selecting a second reference.
- A dimension's explicit label anchor follows the average displacement of its referenced
  points/geometry centers. Preserve its offset, driving value and reference identity.
  This applies to indirectly moved welded geometry as well as the dragged entity.
- Preview and release use the same rule, calculated from the original document once,
  not by accumulating pointer deltas or adding to an already-adjusted anchor.
- Invalid/fixed drags preserve geometry, labels and history. Undo/Redo and native
  reopen preserve committed placement. No stored manufacturing files are rewritten.
- Icon changes retain compact targets, tooltips, accessible names and palette persistence.

## Verification

- 1,364 .NET tests and 615 JS tests passed; clean Web build.
- Actual isolated Chromium: partial endpoint movement follows the dimension span
  while held, on release, through Undo/Redo and native reopen. Driving X=4 remains 4.
- Origin Coincident works in both pick orders. Native reopen retains the relation;
  dragging its endpoint refuses unchanged with no Undo entry. There is no extra entity.
- Origin dimension creates through the normal tool, edits to 4 and survives native reopen.
- Screenshots inspected at 1440x1000. Existing palette layout is unchanged.
- Evidence: `/tmp/dxfer-origin-evidence`. No live manufacturing documents used.

## Cause and scope

The origin used to be a canvas decoration, excluded by both hit testing and the
command factory's entity-only selection normalization. The shared reference now
resolves to (0,0) and is intrinsically fixed in solving and preview propagation.

Label movement previously depended on the selected entity/drag branch and missed
partially moved spans and indirectly moved welded peers. Both drag commit and
preview now derive anchor movement once from actual referenced geometry. The old
parallel-line test expected a full selected-line offset; it now checks the average
displacement of both referenced lines, consistent with the same rule for all spans.
Ellipse axis reference locations use the existing live dimension-point resolver,
not stale coordinates embedded in click keys.

These shared fixes need no existing-record repair. Previously hand-positioned labels
retain their current offset; this does not reconstruct an earlier intended placement.
Circle/arc center visibility and implicit tangent creation are follow-on requests,
not included in this bounded origin/label acceptance. Deployment verification pending.

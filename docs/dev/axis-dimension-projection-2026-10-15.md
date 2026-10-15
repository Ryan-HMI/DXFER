# Axis Dimension Projection

HorizontalDistance and VerticalDistance previously fell through to aligned
linear graphics. Points (0,0) and (3,4) displayed correct stored values 3/4,
but the guides measured a diagonal. Rendering and hit-testing now share an
axis-aware adapter in drawingCanvas.js. Aligned LinearDistance is unchanged.

## Rendering Contract

- Horizontal guides are parallel to screen X; vertical guides to screen Y.
- The anchor's perpendicular coordinate places the dimension line; its
  parallel coordinate places the text gap, including outside-span leaders.
- Extensions start at the actual references. Existing dimension primitives
  supply arrows, gaps and six-CSS-pixel extension overshoot independently
  from each reference, including references on opposite sides of the guide.
- Reversed references retain their order. Projection is recomputed from the
  current view for drawing and hit-testing; backing pixel ratio does not
  scale the CSS-pixel presentation twice.
- Existing label styles, input placement/clamping, widths, wrapping and
  interaction targets are unchanged. No layout, fit bounds or Fit Extents
  changes: an intentionally outside anchor can remain outside the viewport.

No values, reference keys, anchors, solver state, driving/reference mode or
document data are rewritten. The fix applies to existing documents on render;
no historical data repair is required. Unresolved references retain the
existing null geometry behavior. A zero projected span produces no guide
geometry rather than falling back to an unrelated aligned measurement, using
the existing zero-length primitive behavior. Existing overlay descriptors
retain their stored value; this does not add a new zero-span annotation style.

## Verification

Run `node --test tests/DXFER.Blazor.Tests/*.test.mjs` (333 pass).

The focused suite initially reproduced 25 failures with the unchanged aligned
adapter (two unchanged-behavior controls passed). After the fix and correction
of test-only anchor-call/style assertions, all 27 focused cases pass. Two
additional canvas draw-path tests verify rendered guide segments, label
coordinates and resize at device pixel ratio 2. Coverage includes the reported
3/4 fixture, reversed references, shifted/interior/outside anchors, anchor
overrides, zoom/pan, reference-only and unsatisfied state preservation,
unresolved references, zero axis span and unchanged aligned dimensions.

Parent integration passes845 .NET and333 JS tests. Local actual browser at
1440x1000 and500x761 with device pixel ratio2 verifies axis-guide hit-testing,
dimension-label movement without geometry changes, and Undo/Redo. Both
screenshots inspected: `/tmp/dxfer-axis-guides-desktop.png` and
`/tmp/dxfer-axis-guides-narrow.png`. Repeat with
`node /tmp/dxfer-drag-live-qa.cjs --axis-guides`.

Deployed Dev f40d167 and repeated the browser check successfully against
https://dxferdev.sync.harrisonmetals.com. Release hashes match;10 runtime
settings retained, health200/normal sign-in302, callbacks disabled and other
runtime PIDs unchanged. No core/solver edits,
new dependencies or application-data changes. Existing outside anchors still
need zoom/pan when Fit Extents only frames document geometry; this patch does
not claim to change that separate framing contract.

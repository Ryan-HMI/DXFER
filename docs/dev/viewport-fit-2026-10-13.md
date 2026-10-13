# Unobscured viewport fit

Root cause: import and Fit Extents used the full canvas rectangle even where
docked/floating palettes covered it. The drawing itself was not incorrectly
scaled or translated in the document; the view transform ignored the UI.

Layout contract: preserve all panel positions, sizes, docking, scroll, collapse
and interaction targets. Use the largest axis-aligned clear rectangle between
visible panel bounds for import/explicit Fit Extents. Retain existing fit margins
and zoom limits. Do not automatically reset manual pan/zoom on palette movement.
Empty sketches center their origin in the clear region at the existing scale.
If no clear region exists, leave the view unchanged rather than claiming a fit.

The helper clips offscreen/overlapping panel rectangles, scans obstruction-edge
x intervals and finds unobstructed y gaps. No CAD geometry is modified.
Applies to existing bounds for line, polyline, polygon, circle, arc, ellipse,
spline and point entities. Dimensions and constraint glyphs retain existing
geometry-relative transforms; fitting standalone annotation extents is not
added here, nor are measurement or solver semantics changed.

## Verification

Seven new JS regressions cover no panels, four dock edges, floating/overlapping/
invalid/offscreen panels, fully covered canvases, fitted geometry and blank
sketch origins.265 JavaScript tests pass. Isolated browser verified initial
import and explicit Fit at1440/820/390/320px, collapsed panels and actual dragged
floating palette. Geometry bounds intersected zero panel rectangles, remained
inside the canvas, and canvas pixel checks were nonblank at every size.
Desktop/floating screenshots inspected. Download/reopen retains36.283185in
and two contours. No browser errors, source edits or business-data repairs.
Evidence: /tmp/dxfer-fit-qa/evidence.json and screenshots. Temporary QA stopped.

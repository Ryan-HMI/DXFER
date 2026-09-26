# Cut Perimeter (TEST Integration)

The normalize response and Sync save callback add nullable decimal
`cutLengthInches` and nullable string `cutLengthReviewReason`. A missing total
is never a zero estimate. Unknown units, discarded geometry, invalid geometry,
and unsupported cut entities invalidate the entire total, not just one path.
Values are recomputed from the current document at export/save, never cached
from the original import. DXF INSUNITS 1 and 4 mean inches and millimeters;
other or absent units require review.
The optional `units` field reports `Inches`, `Millimeters`, or `Unspecified`
for existing boundingWidth/boundingHeight fields, which remain document units.
CutLengthInches is always inches, independently of the bounds units.
Export preserves units and unsafe-import
markers so reimport cannot turn a partial drawing into a quotable total.

## Entity Matrix

| Entity | Policy |
| --- | --- |
| Line | Exact endpoint distance. |
| Polyline | Sum straight segments, including imported closed final segment. Bulges and nonzero widths are unsafe until preserved by the geometry model. |
| Polygon | Closed perimeter using existing vertices. |
| Circle | Exact circumference, including holes. |
| Arc | Exact counterclockwise radius times sweep; zero/ambiguous or multi-turn sweeps require review. |
| Ellipse | Existing point evaluator, successively refined chord lengths; bounded work and convergence guard, not an exact analytic result. |
| Spline | Deliberately nonquotable: existing fitting/sampling can reconstruct imported knots/weights and has no length-error guarantee. |
| Point | Excluded. |
| Dimension | Excluded; associated block geometry must not be counted. |
| Constraint glyph/reference | Excluded: stored outside drawing entities. |
| Construction / GRAIN layer | Excluded, including generated grain annotation. |
| Unsupported / nonplanar / block insert | Entire total requires review. |

Open cut paths count as paths, not closed contours. Coincident duplicate paths
are counted separately; this is geometry length, not a CAM toolpath optimizer
(no kerf, lead-ins, travel, pierces, common-line merging, or nesting).

## Layout Contract

Add a `Cut total` readout to the existing compact command-bar readout group.
Use the existing label/value typography, spacing, and responsive wrapping.
Display inches explicitly, or `Review`; expose the full reason in a title and
accessible label. Do not add controls, panels, or change canvas/toolbar sizes.
Refresh when the immutable document reference changes, including undo/redo.

## Verification

Automated coverage: exact geometry, holes, units, closed paths, ignored entities,
invalid imports, persistence of safety markers, edited documents, and callback.
Full .NET suite: 541 passed. Web build: zero warnings and errors.
Local runtime on port 5206: real normalize returned 36.2831853071796 inches
for `tests/fixtures/cut-length/rectangle-hole-inches.dxf` (10 x 5 inch rectangle,
radius 1 inch hole). The real multipart callback sent the same value. Desktop
1440 x 1000 and mobile 390 x 844 screenshots confirmed the compact readout fits;
unitless import displays Review with the reason, with no browser errors.
Screenshots: `/tmp/dxfer-cut-length-desktop.png` and
`/tmp/dxfer-cut-length-mobile.png`. Existing mobile canvas/tool layout is unchanged.
User acceptance and HMI integration verification belong to the main agent.
Production deployment/restart is out of scope.

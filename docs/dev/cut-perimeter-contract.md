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

Open cut paths and coincident duplicates still contribute to perimeter, but
invalidate the separate contour estimate. This is geometry length, not a CAM
toolpath optimizer (no kerf, lead-ins, travel, common-line merging, or nesting).

## Closed Contours

Optional `closedContourCount` (nullable int) and `contourReviewReason` (nullable
string) accompany both normalize and multipart callback. One verified closed
contour represents one estimated pierce for HMI; it is not an entity count or
a claim about a CAM cutting strategy. Perimeter remains independent.

Line/arc/polyline endpoints are joined only when each endpoint has exactly one
partner within 0.00001 inch (0.000254 mm). No transitive cluster guessing or
nearest-of-many matching is permitted. Polygon closure and circles/full
ellipses each form a path; holes and disconnected loops count individually.
Polyline interior vertices are preserved. Partial ellipses and splines require
review, as do all unsafe import cases already excluded from perimeter.
Points, dimensions, constraints, construction and GRAIN use the same exclusions.

Existing curve evaluators produce chords with a conservative second-derivative
deviation bound of join tolerance / 8. NetTopologySuite checks the assembled
closed linework for simplicity and minimum clearance. Crossings, duplicates,
overlap, touches or clearance at/below four join tolerances require review.
Endpoint averaging can move a join by at most half the join tolerance.
No topology repair, polygon union or simplification hides bad paths.
Work is capped at 512 input paths and 32768 total vertices, with coordinates
limited to +/- 1000000 inches; exceeding a limit returns review. Very small,
near-contact or highly detailed valid shapes can conservatively require review.

Library references: NetTopologySuite `Geometry.IsSimple` and
`Precision.MinimumClearance.GetDistance`; see
https://nettopologysuite.github.io/NetTopologySuite/api/NetTopologySuite.Precision.MinimumClearance.html.

## Layout Contract

Add a `Cut total` readout to the existing compact command-bar readout group.
Use the existing label/value typography, spacing, and responsive wrapping.
Display inches explicitly, or `Review`; expose the full reason in a title and
accessible label. Do not add controls, panels, or change canvas/toolbar sizes.
Refresh when the immutable document reference changes, including undo/redo.
Add `Contours` beside `Cut total` in the same wrapping readout group. Display
an integer or `Review`, with the full reason in title and accessible label.
Keep canvas/toolbars and all existing readout styling unchanged.

## Verification

Automated coverage: exact geometry, holes, units, closed paths, ignored entities,
invalid imports, persistence of safety markers, edited documents, and callback.
Full .NET suite with contour extension: 558 passed. Web build: zero warnings and errors.
Local runtime on port 5206: real normalize returned 36.2831853071796 inches
for `tests/fixtures/cut-length/rectangle-hole-inches.dxf` (10 x 5 inch rectangle,
radius 1 inch hole). The real multipart callback sent the same value. Desktop
1440 x 1000 and mobile 390 x 844 screenshots confirmed the compact readout fits;
unitless import displays Review with the reason, with no browser errors.
Screenshots: `/tmp/dxfer-cut-length-desktop.png` and
`/tmp/dxfer-cut-length-mobile.png`. Existing mobile canvas/tool layout is unchanged.
User acceptance and HMI integration verification belong to the main agent.
Production deployment/restart is out of scope.

Contour extension: `tests/fixtures/cut-length/rectangle-lines-hole-inches.dxf`
contains four shuffled/reversed rectangle lines plus a hole. Real 5206 normalize
and multipart callback returned `closedContourCount: 2`, no contour review,
and the same 36.2831853071796 inch perimeter. The original polyline fixture
also represents two contours. An open 10 inch line retained its perimeter
with null contour count and a review reason; unitless import required review.
Screenshots `/tmp/dxfer-contours-desktop.png` and
`/tmp/dxfer-contours-mobile.png` show both readouts fitting. Browser errors: none.
Old save payloads without the two new fields deserialize to null/unknown.

Verification commands:

```sh
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --verbosity minimal
dotnet build src/DXFER.Web/DXFER.Web.csproj --verbosity minimal
curl -sSf -H "X-DXFER-API-Key: $DXFER_API_KEY" \
  -F dxf=@tests/fixtures/cut-length/rectangle-lines-hole-inches.dxf \
  http://127.0.0.1:5206/api/dxfer/normalize
```

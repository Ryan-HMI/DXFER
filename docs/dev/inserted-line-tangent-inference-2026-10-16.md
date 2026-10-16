# Inserted line tangent inference

## Bounded fix

Ryan requested new-line/existing-circle-or-arc automatic Tangent inference only.
The insertion factory previously inferred new-arc/existing-line tangency, but
never the reverse. Pre-edit reproduction: 44 focused cases, 22 missing-inference
failures and 22 passing rejection/scope controls.

`SketchCreationConstraintFactory.CreateConstraintsForInsertion` now checks newly
created `LineEntity` instances against existing circles/arcs. It adds one
Satisfied Tangent per reference pair through the existing deduplication helper.
The predicate requires finite, nondegenerate geometry, perpendicular contact
within the finite line segment, and radius agreement within the existing absolute
`1e-6` drawing-unit tolerance. Endpoint slack is also in drawing units, never a
line-length fraction. Arc contacts must lie within the counterclockwise sweep;
wrapped angles and full turns are supported, zero sweeps rejected. Arc endpoint
slack is spatial rather than a fixed angle. No screen-proximity or radius-scaled
tangency rule is introduced. The old new-arc inference path is unchanged.

## Fixtures and coverage

- Circle `curve`: center `(10,10)`, radius `5`; arc variant `0..90` degrees.
  Contact `(13,14)`; outgoing line `(13,14)->(9,17)`, incoming reverse, interior
  contact line `(17,11)->(9,17)`. Exactly one Tangent; application leaves exact
  geometry unchanged and Satisfied.
- Horizontal contact at `(10,15)`: radial offsets `0`, `+/-0.5e-6` accepted;
  `+/-2e-6` and `0.001` rejected. Finite endpoint gaps `0`, `0.5e-6` accepted;
  `2e-6`, `5` rejected, both line directions.
- Arc start/end/interior, excluded opposite contact, `300..60`, `-60..60`,
  `360..450`, full `0..360`, zero sweep, and spatial endpoint epsilon cases.
- Secant/radial/zero/epsilon-length lines, invalid radius, nonfinite inputs,
  million-unit radius and hundred-million-unit line proximity controls.
- Duplicate candidates and reordered curves produce one constraint per pair;
  distinct tangent curves retain distinct constraints. Arc endpoint continuation
  retains Coincident alongside Tangent. New-circle and polyline controls remain
  outside this new inference path.

## Verification

```sh
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --no-restore --filter FullyQualifiedName~SketchCreation --verbosity minimal
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --no-build --no-restore --filter FullyQualifiedName~SketchCreationLineTangentTests --verbosity minimal
dotnet build src/DXFER.Web/DXFER.Web.csproj --no-restore --verbosity minimal
```

Results: 85 creation tests passed, including 51 focused new cases; focused
no-build rerun passed 51. Web build passed with zero warnings/errors.

Applicable: new LineEntity (including lines emitted by creation tools), existing
CircleEntity/ArcEntity. New polyline/polygon/ellipse/spline/point geometry,
dimensions and constraint glyphs gain no inference here. New-circle tangency,
selected-center visibility, outgoing tangent snapping, and leader clarity remain
separate work. No JS/canvas/workbench/solver changes, browser/history/native
acceptance, deployment, or Slack activity is claimed by this patch.

The insertion root cause is fixed for future creations. Existing sketches are
not backfilled: a previously missing relation still requires explicit Tangent
application or recreation if the user intends it; proximity alone is not safe
evidence for bulk repair. Parent-owned files and ledger remain untouched.

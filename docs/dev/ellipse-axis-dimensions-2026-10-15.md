# UT-ELLIPSE-001: Driving Axis Edits

## Installed Follow-Up

Dev170e976 on2026-10-15 includes crossed-axis analysis. Parent installed browser
checks5x3 ->6x3 ->6x4 ->6x8: last state reports22.103492in and1contour, retaining
the native axes/dimensions on reopen.970 integrated .NET/352 JS tests pass.
Flux independently accepted the prior67ef64a edit/tangent/export cases; his
separate analysis warning is now internally fixed, awaiting independent retest.
Production/Test remain unchanged; no existing files or business records repaired.

## Evidence And Root Cause

Flux and the parent reproduced keyed creation of a 5 x 3 ellipse with two
`LinearDistance` driving dimensions and no constraints. Editing major diameter
to 6 retained failed intent but left the geometry at semimajor 2.5, ratio 0.6.
Undo/Redo and native reopen correctly preserved that failed transaction; they
were not the defect.

The dimension measurement path resolves named ellipse axis references against
live geometry. The distance application path only handled ordinary editable
point references, so `major-start`/`major-end` and `minor-start`/`minor-end`
dimensions could be measured but never resized their ellipse.

## Core Contract

- Recognize exactly two opposite named axis points on the same `EllipseEntity`
  for a `LinearDistance` dimension. Reference order is irrelevant and labels are
  case-insensitive. Stored coordinate snapshots do not determine axis geometry.
- Values are full diameters. A major edit preserves the absolute minor radius,
  adjusting both the major vector length and ratio. A minor edit changes only
  the ratio. Center, axis direction, entity ID, construction state and arc
  parameters remain unchanged. Reapplication order does not couple the axes.
- Whole-entity Fix blocks resizing; a fixed center does not. Suppressed Fix is
  ignored, following the existing fixed-reference contract.
- Nonfinite, nonpositive and degenerate target sizes are unsatisfied. Resizing
  requires finite existing axes, major radius and ratio above the existing
  geometry tolerance (1e-6), target radius above that tolerance, and a resulting
  finite ratio above that tolerance. This is not a repair path for degenerate
  source ellipses.
- `LegacySketchSolverAdapter` still owns transaction rollback. Conflicting
  dimensions or blocked edits keep original entities/metadata and retain failed
  intent with diagnostics. Direct `ApplyDimension(s)` remains nontransactional.
- Existing positive ratios greater than one remain supported in the editor;
  named axes are not swapped/relabelled when the named minor diameter exceeds
  the named major. DXF export must canonicalize them as described below.

## DXF Axis Crossing Follow-Up

The initial axis-edit patch exposed an existing writer defect: it wrote the
editor ratio directly, even above one. Pinned ezdxf 1.4.4 audited eight actual
writer outputs (major shrink/minor growth, full/arc, plain/native) and repaired
every one with `INVALID_ELLIPSE_RATIO`. Native roundtrip success did not establish
CAD compliance. The model, keyed creation and reader all already accept ratios
above one, so rejecting axis crossing in the solver would impose a new editor
restriction rather than fix that shared export defect.

The writer now canonicalizes only its local export copy when ratio > 1:

- Export major vector becomes the old minor vector `(-major.Y, major.X) * ratio`.
- Export ratio becomes `1 / ratio`; arc parameters shift by -90 degrees and
  normalize to [0, 360). Full ellipses use 0..360, including the editor's
  equal-parameter full-ellipse convention. This preserves the curve and directed
  arc span; full-ellipse sampling may have a different arbitrary start seam.
- The source document, named references, dimensions and native snapshot remain
  unchanged. The native fingerprint covers the canonical external geometry;
  verified reopen restores the original named-axis representation exactly.
- Plain external DXF contains the same geometric curve with canonical axes, not
  editor dimension intent. External edits still invalidate the native snapshot
  under the existing fingerprint contract.
- If canonicalization overflows an axis or would require a ratio below DXF's
  1e-10 minimum, export throws `FormatException` rather than clamping the curve
  or emitting an invalid file. General malformed/degenerate ellipse validation
  and the existing writer numeric precision are not redesigned here.

Previously exported files are not rewritten. Re-exporting representable source
documents through the fixed writer produces canonical files; external consumers
may otherwise need to repair old ratio-above-one exports.

## Applicability And Limits

Full ellipses and elliptical arcs support these full-axis dimensions. Arbitrary
ellipse point-to-point distances, projected horizontal/vertical axis dimensions,
and named-axis-point Fix constraints gain no new support. This is not a general
ellipse constraint/DOF solver. Reference-only dimensions do not resize geometry.

Line, polyline, polygon, circle, arc, spline and point application paths are
unchanged. Dimension IDs, reference keys and anchors are preserved. Constraint
glyph rendering, drag logic, orthogonal redundancy checks, history, native
snapshot serialization, UI and JavaScript are unchanged. Only the external DXF
ellipse representation is canonicalized at export.

The shared axis application defect is fixed. No saved files or existing records
were rewritten. Native files retaining this compatible failed intent can recover
through an explicit solve/edit; opening them still does not silently solve them.
Other conflicts continue to require correction, not data overrides.

## Verification

- Red: all seven initial axis-edit/reapplication tests failed as OverConstrained
  against base `7ad15ab`.
- Green: 22 focused tests cover keyed major/minor edits at 0/37/90 degrees,
  independent reapplication, invalid values, conflicting dimensions, whole and
  center Fix, suppressed Fix, stale coordinate snapshots/reversed uppercase
  labels, elliptical arc span/construction state, reference-only intent, and
  native success/conflict/recovered-intent round trips.
- Full core suite: 867 passed, zero failures/skips.
- Follow-up red: 10 crossing export tests failed on ratios 1.2/1.5; two existing
  canonical-axis controls passed. Follow-up green: 16 writer tests and the full
  883-test suite pass, zero failures/skips. Cases include rotated, wrapped,
  negative and equal parameters, native intent/re-solve, canonical controls and
  explicit rejection of unrepresentable exports.
- Independent follow-up: 20 actual-writer exports audited with pinned ezdxf 1.4.4
  have zero errors/repairs. Full-curve and directed arc sample errors are below
  1.7e-10 drawing units. Local evidence and reproducible probes are under
  `/tmp/dxfer-ellipse-axis-export-20261015` (`before` and `after` audit JSON/DXFs).
- Commands: `dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --filter
  FullyQualifiedName~SketchEllipseAxisDimensionTests` and the same command
  without the filter.
- Parent reported normal 6 x 3 -> 6 x 4 browser edits, Undo/Redo and reopen passing.
  Crossing/browser rollout verification remains with the parent. No deployment
  or production data writes were performed for these patches.

## Cut Analysis Follow-Up

Flux and parent reproduced a crossed 6 x 8 ellipse reporting invalid cut geometry
on Dev `67ef64a`, including native reopen, despite its canonical DXF passing audit.
The cut-length gate incorrectly required the editor ratio to be at most one.
The existing sampler already supports either axis order and preserves the exact
parameter span, so analysis now samples the original entity without swapping or
mutating it. The eccentricity bound uses `min(ratio, 1 / ratio) >= 0.001`.
Contour sampling uses the larger physical radius to preserve its deviation bound.
Removing only the cut-length gate incorrectly counted two thin ellipses that
equivalent canonical representations correctly sent for clearance review; the
larger-radius change fixes those regressions as well.

Limits remain conservative: chord refinement must converge within the existing
1e-6-inch threshold and 32768-segment cap. Nonfinite, overflowed, degenerate,
sub-resolution and too-eccentric geometry remains Review, never a partial total.
Partial ellipse cut lengths are supported, including rotated/wrapped spans;
partial ellipse contour counts still require Review. Existing contour coordinate,
vertex, clearance and endpoint guards remain in place. This does not extend the
numeric range of the model sampler or guarantee results below its tolerances.

Only ellipse cut length and full-ellipse contour sampling change. Line, polyline,
polygon, circle, arc, spline, point, dimension and constraint-reference paths are
unchanged; document geometry, styles, intent and parameters are not mutated.
No dependencies, solver, native serialization, writer or UI changes are included.

Red: ten initial crossed-axis tests failed on base `f38c50f`; after the length-only
fix, two thin-contour regressions still failed. Green: all nineteen focused tests
and all 930 core-suite tests pass. Coverage includes 6 x 8, native/plain reopen,
rotated/partial/wrapped/equal spans, disjoint contours, eccentricity symmetry and
nonfinite/overflow rejection. Focused command: `dotnet test
tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --filter
FullyQualifiedName~EllipseCutAnalysisTests`.

The shared analysis cause is fixed; no stored geometry or records need repair
for this false Review result. Reanalysis uses the existing geometry. Corrected
installed/browser verification and deployment remain with the parent.

# UT-ELLIPSE-001: Driving Axis Edits

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
- Existing positive ratios greater than one remain supported; this patch does
  not swap/relabel axes when the named minor diameter exceeds the named major.

## Applicability And Limits

Full ellipses and elliptical arcs support these full-axis dimensions. Arbitrary
ellipse point-to-point distances, projected horizontal/vertical axis dimensions,
and named-axis-point Fix constraints gain no new support. This is not a general
ellipse constraint/DOF solver. Reference-only dimensions do not resize geometry.

Line, polyline, polygon, circle, arc, spline and point application paths are
unchanged. Dimension IDs, reference keys and anchors are preserved. Constraint
glyph rendering, drag logic, orthogonal redundancy checks, history, native
serialization, UI and JavaScript are unchanged.

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
- Commands: `dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --filter
  FullyQualifiedName~SketchEllipseAxisDimensionTests` and the same command
  without the filter.
- Corrected installed/browser verification remains with the parent. No deployment
  or production data writes were performed for this patch.

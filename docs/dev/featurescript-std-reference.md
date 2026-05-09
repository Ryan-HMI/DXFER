# FeatureScript Std Reference Workflow

DXFER can use the Onshape FeatureScript Standard Library as a reference corpus for
sketch/tool semantics without asking anyone to manually aggregate tab text.

## Source

- Upstream public source document: Onshape `std`.
- Local sync source used by DXFER tooling:
  `https://github.com/javawizard/onshape-std-library-mirror.git`
- Default branch: `without-versions`, which removes version churn from imports.
- License: MIT for the FeatureScript Standard Library, copyright PTC Inc.

The synced source is a reference artifact, not a runtime dependency. Do not paste
large chunks of FeatureScript std into DXFER source unless the MIT copyright and
license text are carried with that copy.

## Sync And Index

Run from the repo root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts\Sync-FeatureScriptStd.ps1
```

That script:

1. Clones or fast-forwards the public mirror into
   `artifacts/featurescript-std/onshape-std-library-mirror`.
2. Runs `tools/DXFER.FeatureScriptStd`.
3. Writes `artifacts/featurescript-std/manifest.json`.

`artifacts/` is ignored by Git, so the full FeatureScript text is available to
local agents without vendoring the corpus into DXFER commits.

## Manifest Contents

The manifest records one entry per `.fs` tab:

- `modulePath`, such as `onshape/std/sketch.fs`
- repo-relative source path
- SHA-256 of the source text
- byte and line counts
- imports and re-exports
- exported functions/types/predicates/enums/consts
- builtin calls such as `@skSolve` and `@skConstraint`

This gives agents a stable inventory for targeted searches. For example,
`sketch.fs` exposes the public sketch API, while builtin calls show where the
visible FeatureScript wrapper delegates into Onshape runtime internals.

The first verified sync indexed 265 `.fs` modules. `onshape/std/sketch.fs`
contained 28 exports, including:

- sketch lifecycle: `newSketch`, `newSketchOnPlane`, `skSetInitialGuess`,
  `skSolve`
- entities: `skPoint`, `skLineSegment`, `skCircle`, `skEllipse`, `skArc`,
  `skSpline`, `skBezier`, `skRectangle`, `skRegularPolygon`, `skPolyline`
- constraints: `skConstraint`
- types/enums: `Sketch`, `DimensionDirection`, `SketchProjectionType`

The same file recorded builtin calls for `@skSolve`, `@skConstraint`, and most
entity constructors. That confirms the visible std code is a wrapper and
semantic guide, not a complete portable numeric solver.

## How To Use It For DXFER

Use FeatureScript std as a semantics guide:

- sketch entity naming and local IDs
- constraint and dimension vocabulary
- generated geometry patterns such as rectangles and polylines
- initial-guess and solve lifecycle shape
- error/status behavior around unsupported geometry

Do not assume it provides a portable solver. Key sketch functions, including
solve and constraints, delegate to Onshape builtins. DXFER should still route
runtime behavior through `ISketchSolver` and keep PlaneGCS or other solver
engines behind replaceable adapters.

## Next Solver Contract Slice

The useful next extraction target is a DXFER-owned sketch contract:

- entities: point, line, circle, arc, ellipse, spline/polyline
- references: entity, endpoint, center, curve, midpoint, tangent handles
- constraints: coincident, horizontal, vertical, parallel, perpendicular,
  tangent, concentric, equal, fix
- dimensions: linear distance, horizontal/vertical distance, radius, diameter,
  angle
- solve request: document entities, constraints, dimensions, fixed references,
  initial guesses
- solve result: solved/failed/underconstrained/overconstrained/unavailable,
  diagnostics, affected references

That contract should be DXFER-owned even when its vocabulary is informed by the
FeatureScript std library.

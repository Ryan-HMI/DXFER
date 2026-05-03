# DXFER V1 Design

Date: 2026-05-03

## Purpose

DXFER is a future HMI-Sync companion for preparing trusted flat-pattern DXF files and inspecting reference CAD artifacts. The first version is not a general CAD replacement. It should solve the practical prep workflow first while keeping the codebase extensible enough for a later web-based parametric entity editor.

The primary V1 workflow is:

1. Open an Onshape-generated flat-pattern DXF attached to a part definition.
2. Inspect and normalize the flat pattern orientation and origin.
3. Mark grain direction as metadata.
4. Compute bounds.
5. Export a normalized DXF and `*.dxfer.json` sidecar.
6. Let HMI-Sync later import the sidecar for the same part-definition artifact.

## Product Scope

### First Implementation Milestone

The first implementation milestone should produce a working canvas prototype by the end of the first development pass:

- Blazor/.NET 8 app shell opens a drawing view.
- Internal document model can hold basic 2D entities.
- Canvas renders basic line, polyline, circle, and arc-like entities from document state.
- Pointer hover highlights the nearest selectable entity.
- Click selects and deselects entities.
- Selected entities remain visually distinct from hover state.
- Coordinate transforms support pan, zoom, and fit-to-extents at prototype quality.

This milestone may use generated fixture geometry before full DXF import is complete, but it should be wired through the same document-state boundary planned for DXF input.

### V1 Goals

- Edit and normalize trusted DXF flat patterns.
- Align selected or all entities to global X or Y.
- Align selected or all entities to a picked reference vector, mapped to +X, -X, +Y, or -Y.
- Move selected or all entities to origin by selecting a point.
- Compute and display bounds before and after normalization.
- Store manufacturing metadata in a sidecar JSON file.
- Store grain direction as annotation metadata only.
- Provide production measurement tools for trusted DXF files.
- Open DWG files externally for reference inspection.
- Target .NET 8 to stay aligned with HMI-Sync.

### V1 Non-Goals

- Native DWG parsing.
- DWG editing or DWG writing.
- Deriving manufacturing bounds or scale from customer-supplied DWG, PDF, or DXF files.
- Full sketch constraint solving.
- 3D STEP viewing.
- Blender scene generation.
- Direct writes to the HMI-Sync database.

## Trust Boundary

DXFER must distinguish manufacturing inputs from reference artifacts.

Trusted manufacturing input:

- Onshape-generated DXF flat patterns uploaded as flat-pattern artifacts on a part definition.
- These can be normalized, measured for bounds, exported, and paired with `*.dxfer.json`.

Reference-only input:

- Customer DWG, PDF, DXF, or other CAD artifacts.
- These can be inspected, but cannot write manufacturing geometry, scale, bounds, or grain data back into HMI-Sync.

Rationale:

- Customer files can have scale problems.
- Customer bend allowances and brake tooling may not match shop tooling.
- Manufacturable flat data should come from the trusted Onshape process.

## Architecture

DXFER should be a .NET 8 solution with a shared core and a client-side canvas interaction layer.

Projects:

- `DXFER.Core`: units, entity model, stable IDs, transforms, bounds, selection math, measurements, sidecar schema, grain metadata.
- `DXFER.CadIO`: adapter-based drawing IO. V1 implements DXF read/write. DWG handling launches an external viewer.
- `DXFER.Blazor`: reusable Razor components for toolbars, properties, layers, file workflows, metadata panels, and measurement displays.
- `DXFER.Canvas`: JavaScript or TypeScript canvas/WebGL layer for high-frequency pan, zoom, hit testing, snapping, rubber-band selection, and point picking.
- `DXFER.Desktop`: Windows desktop shell using Blazor Hybrid/WebView for local file access and production use.
- `DXFER.Tests`: unit and integration tests for geometry, DXF IO, sidecars, and measurement behavior.

Blazor owns app state and workflow. The canvas owns high-frequency pointer interaction and rendering. The canvas must render from serialized document state and report picks or commands back to .NET; it must not become the source of truth.

## Library Strategy

V1 should not use OpenCascade as the 2D flat-pattern kernel. OpenCascade is more relevant for later STEP and 3D workflows, but it adds native packaging, interop, and licensing compliance work before V1 needs it.

Recommended V1 stack:

- DXF IO: `IxMilia.Dxf` behind DXFER-owned reader/writer interfaces.
- Geometry kernel: a small internal 2D C# model for lines, arcs, circles, polylines, bounds, transforms, snaps, and measurements.
- Canvas: custom JavaScript or TypeScript canvas/WebGL renderer and interaction layer.
- Polygon operations later: `Clipper2` when offsets, cleanup, or boolean operations are needed.
- Server-side thumbnails later: `SkiaSharp` if needed.

Kernel boundary:

- Public app code should depend on DXFER-owned types such as `DrawingDocument`, `EntityId`, `LineEntity`, `ArcEntity`, `Bounds2D`, `MeasurementResult`, and `IGeometryKernel`.
- Third-party kernel or IO types must stay inside adapter projects.
- Future adapters can add OpenCascade, a commercial CAD SDK, or another kernel without forcing HMI-Sync or the UI to hold those native types.

OpenCascade does not automatically force all of HMI-Sync open source if used correctly, because OCCT is LGPL 2.1 with an additional exception. The concern is compliance and deployment complexity, not automatic GPL-style source disclosure. If OCCT is used later, keep it optional and isolated in a dedicated adapter package.

## Document Modes

### Trusted Flat-Pattern Mode

Trusted flat-pattern mode is available for Onshape-generated DXF artifacts attached to a part definition.

Capabilities:

- Read DXF into the internal 2D entity model.
- Preserve layers, colors, line types, and unsupported entities where practical.
- Select by click, window, layer, all, or invert.
- Transform selected or all entities.
- Set grain axis for export.
- Compute bounds.
- Export normalized DXF and sidecar metadata.

### Reference Inspection Mode

Reference inspection mode is used for customer artifacts.

Capabilities:

- DWG launches in the installed external viewer, preferably Autodesk DWG TrueView or the system DWG handler.
- Customer DXF/PDF viewing can be added later, but remains reference-only unless regenerated through the trusted flat-pattern process.
- Reference measurements are overlays only and cannot write manufacturing metadata.

## Sidecar Metadata

The Sync-facing sidecar should store the final normalized result, not detailed transform replay. Exact transform history is useful for debugging, but Sync should not depend on replaying rotations or translations.

The sidecar should include:

- schema version
- source filename and source hash
- normalized filename and normalized hash
- trusted-source marker
- units
- final bounds
- lightweight normalization summary
- grain metadata

Example:

```json
{
  "schemaVersion": 1,
  "source": {
    "fileName": "part-flat.dxf",
    "sha256": "source-file-hash",
    "trustedSource": "OnshapeFlatPatternArtifact",
    "units": "in"
  },
  "normalized": {
    "fileName": "part-flat.normalized.dxf",
    "sha256": "normalized-file-hash",
    "alignedGrainAxis": "X",
    "originPolicy": "SelectedPointToOrigin"
  },
  "bounds": {
    "width": 12.5,
    "height": 8.25,
    "minX": 0.0,
    "minY": 0.0,
    "maxX": 12.5,
    "maxY": 8.25
  },
  "grain": {
    "required": true,
    "axis": "X",
    "basis": "NormalizedPartCoordinates",
    "annotationOnly": true
  }
}
```

## Grain Direction

Grain is metadata and viewer annotation only in V1. It should not be emitted as cut geometry.

Behavior:

- Export UI provides a radio choice for grain aligned to global X or global Y.
- Default can be global X unless shop convention says otherwise.
- HMI-Sync later displays a grain arrow and label from sidecar metadata.
- Laser and plasma operators remain responsible for nesting, drops, table placement, and downstream rotation.

Optional future behavior:

- Annotated review DXF export with a non-manufacturing grain layer.
- Explicit scribe/etch layer only if the shop decides that should become machine geometry.

## Editing Tools

V1 editing tools:

- Open trusted DXF.
- Save/export normalized DXF.
- Select by click, window, layer, all, and invert.
- Pan, zoom, fit, and extents.
- Align selection or full drawing to global X or Y.
- Align selection or full drawing to a picked vector, mapped to +X, -X, +Y, or -Y.
- Move selection or full drawing by selecting a point and sending it to origin.
- Set grain axis on export: global X or global Y.
- Undo and redo transform operations.
- Show before and after bounds.
- Export normalized DXF plus `*.dxfer.json`.

## Measurement Tools

V1 measurement tools:

- point-to-point distance
- X delta
- Y delta
- shortest distance between two entities where supported
- line length
- arc length
- polyline length
- selected-entity bounds
- full-drawing bounds
- units display and precision control

Snap points:

- endpoint
- midpoint
- center
- intersection
- nearest

Measurements are overlays by default. They do not modify manufacturing geometry or Sync metadata unless a future annotation feature explicitly allows it.

## Future Parametric Editor Extensibility

V1 must not hard-code assumptions that block a future web-based parametric entity editor.

Design rules:

- Entities have stable IDs.
- Entities expose typed parameters rather than only raw drawing primitives.
- Mutations flow through command objects to support undo, redo, replay, and later constraint solving.
- Measurements and annotations are separate from manufacturing geometry.
- The canvas renders document state and never owns the document state.
- Constraints should be layered over entities through a future service.
- DXF import/export stays adapter-based so the internal model can grow richer than DXF group codes.

Likely future constraints:

- horizontal
- vertical
- coincident
- parallel
- perpendicular
- equal length/radius
- distance dimension
- angle dimension

## Error Handling And Guardrails

Guardrails:

- Opening DWG launches an external viewer and does not create editable geometry.
- Reference files are labeled as reference-only.
- Missing units or suspicious bounds warn the user before export.
- Very large coordinates far from origin should prompt the user to use point-to-origin normalization.
- Unsupported entities should be preserved where possible and reported when not measurable or editable.
- Export should write normalized DXF and sidecar JSON together where possible.
- Sidecar includes source and normalized file hashes so HMI-Sync can detect mismatches.

Unsupported entity behavior:

- Preserve unsupported entities during DXF round-trip if the IO library supports it.
- Exclude unsupported entities from measurement and bounds only when unavoidable.
- Surface warnings with entity counts and layer names where available.

## Testing

Required V1 tests:

- geometry transform tests: rotate, translate, align to X/Y, point to origin
- bounds tests before and after normalization
- grain metadata tests for X/Y export choices
- measurement tests for distance, X delta, Y delta, line length, arc length, and selected bounds
- DXF fixture tests using small Onshape-generated sample files
- DXF round-trip tests: load, normalize, save, reload, verify bounds and entity count where possible
- sidecar schema tests for hashes, trusted-source marker, bounds, and grain metadata
- guardrail tests for reference-only DWG handling and unsupported entity warnings

Canvas interaction tests should be added after the first canvas exists, focused on hit testing, snapping, rubber-band selection, and coordinate conversion.

## HMI-Sync Integration

V1 integration is file-based:

- DXFER writes `*.dxfer.json` beside the normalized DXF.
- HMI-Sync later imports sidecars attached to part-definition flat-pattern artifacts.
- HMI-Sync should reject or warn when the sidecar source hash does not match the attached trusted flat-pattern source.
- HMI-Sync displays bounds and grain annotation from metadata.
- HMI-Sync does not import manufacturing metadata from customer reference artifacts.

Direct HMI-Sync database/API writes are out of scope for V1.

## Future 3D And Rendering Roadmap

STEP viewing and Blender rendering should be a separate future 3D pipeline:

1. STEP file input.
2. 3D CAD reader and tessellator.
3. Derived web asset such as GLB/glTF plus metadata.
4. HMI-Sync browser viewer.
5. Optional Blender bridge/add-on for scene setup, materials, camera, and lighting presets.

OpenCascade is a strong candidate for this future STEP pipeline. It should be added as `DXFER.Cad3D` or `DXFER.Step`, not as the V1 2D flat-pattern editor kernel.

HMI-Sync should consume derived preview assets rather than performing heavy CAD processing inside Blazor Server request flow.

## External References

- HMI-Sync target framework observed locally: `net8.0`.
- Autodesk RealDWG is an SDK, not the free viewer.
- Autodesk DWG TrueView is the expected external DWG viewer path for V1.
- IxMilia.Dxf is the preferred initial DXF IO candidate.
- OpenCascade is reserved for future 3D/STEP adapter evaluation.

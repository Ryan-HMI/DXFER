# Native document constraint persistence

## Application integration

The standalone full editor's existing Download DXF now opts into native intent.
Cleanup-only downloads and every Sync save/callback writer remain geometry-only.
Verified native reopen bypasses cleanup/normalization and accepts empty native
drawings, retaining exact IDs and failed intent; displayed diagnostics are
restored from the document. Invalid serialization leaves the save point intact,
and downloads exceeding the browser's25MiB reopen limit are refused.

Parent verification:704 .NET and265 JavaScript tests pass. Actual browser
Download/New/Open/Download compares entity IDs, geometry, constraints and
dimensions; the ten-inch conflict stays unchanged and explained. External DXF
editing to12in imports that geometry with a stale-intent warning and no old
constraints. Existing ordinary imports and Sync ownership guards pass. No
existing saved files or records were repaired. Older files without native intent
cannot recover constraints that were already lost. Grain controls, viewport and
undo history remain outside document persistence.

Dev-only worker branch based on `23c57d9`. No Workbench, visual, Sync, machine,
business-data, or deployment files changed. Parent owns browser integration.

## Format and boundaries

Existing DXF writer output contains manufacturing geometry, not sketch intent;
the existing analysis sidecar contains counts, not an editable document. Neither
previously supported native document records or XDATA. The new format is a
versioned `dxfer-document` JSON snapshot, optionally embedded in ordinary DXF as a
named `DXFER_DOCUMENT` dictionary entry pointing to an OBJECTS/XRECORD. This
avoids the XDATA size limit and introduces no constraint/dimension cut entities.

XRECORD structure follows the [Autodesk reference](https://help.autodesk.com/cloudhelp/2020/ENU/AutoCAD-DXF/files/GUID-24668FAF-AE03-41AE-AFA4-276C3692827F.htm).
Payload uses group 310 binary chunks of at most 127 bytes, as defined in the
[group-code reference](https://help.autodesk.com/cloudhelp/2024/ENU/AutoCAD-DXF/files/GUID-3F0380A5-1C15-464D-BC66-2C5F094BCFB9.htm).
Dictionary and record handles use the writer's previously unused reserved 8/9;
ordinary table/entity handles still start at hex 100.

The snapshot preserves all supported entity types, exact domain IDs and geometry,
construction entities, polyline segments, parametric polygons, spline fit/control
points and tangent handles, constraint IDs/kinds/references/states (including
unsatisfied and suppressed), dimensions/values/anchors/driving flags, and
DrawingDocument metadata. Source trust is deliberately reset on local read.
No solve, cleanup, normalization, reference rewriting, or geometry repair occurs
during restoration. Unresolved failed-intent references remain intact.

This is document persistence, not all workspace state. Grain UI state, selection,
viewport, undo history, and transient solver diagnostics are not in
DrawingDocument and are not persisted. GrainAnnotation still exports its normal
DXF annotation when supplied. Parent restores displayed diagnostics from the
reopened document rather than treating import as a new solve.

## Stale and corrupt data

The envelope includes SHA-256 fingerprints of the native payload and every DXF
record outside OBJECTS, including geometry, handles, units, blocks, tables, and
annotations. DXF line-ending/group-code padding changes are tolerated. Other
external changes conservatively invalidate the snapshot, even some harmless CAD
rewrites. This is integrity checking, not source authentication.

On mismatch, corruption, unknown envelope version, or invalid native schema, DXF
import returns the current file's ordinary geometry, no restored intent, and a
`native-document-ignored` warning. It never overwrites externally edited geometry
with the stale snapshot. Applications that strip OBJECTS also strip the intent;
no constraint persistence is promised for arbitrary external DXF round trips.

Standalone native JSON rejects unknown properties/versions, duplicate IDs/JSON
properties, missing required geometry, invalid enum/numeric values, and null
records/references/metadata with FormatException. It does not silently partially
load or reinterpret an old analysis sidecar. Input depth is bounded at 32 and
native text/payload at 32 MiB. Embedded hex increases file size; callers must also
enforce their transport limit (current browser Open is 25 MiB).

## Parent integration contract

- Local full-editor Download only: use `CreateDxfWriteOptions() with {
  IncludeNativeDocument = EnableFullEditing && !IsSyncLaunch }` when calling
  `DxfDocumentWriter.Write`. Default is false, so other writer callers retain
  ordinary geometry-only export. No Sync writer changes are required.
- Open: call `DxfDocumentReader.Read(text, out var nativeDocumentRestored)`.
  When true, bypass ExactDuplicateGeometryService.Remove and AutoNormalize;
  accept empty documents and restore diagnostic presentation from document intent.
  Preserve the existing WithOpenFileMetadata wrapper.
- When false, retain ordinary DXF import behavior and display metadata warnings.
- Export errors (including invalid document data) must not mark the document
  saved. Standalone serializer validation failures are FormatException.
- Optional future standalone native entry points are
  `DxferDocumentSerializer.Write/Read` and `DrawingDocumentFile.Read(name, text,
  out bool)`, using `.dxfer.json`. No native-extension UI was added here.

## Verification

- Core suite: 650 passing tests, including 49 persistence cases. Tests reopen
  constrained rectangles and continue propagation, edit a reopened driving
  length, preserve failed axis/length intent without moving geometry, and cover
  all entity types, metadata, ordinary/legacy DXF, empty/construction-only
  documents, stale external geometry/units/deletion, corrupt/unknown records,
  nullable payloads, and repeated deterministic round trips.
- Installed ezdxf 1.4.4 (`/opt/hmisync/cypnest-venv-20261014/bin/python`): mixed
  plain/enriched exports audit with zero errors and zero fixes, identical
  modelspace type/counts, and the native entry recognized as XRECORD. Empty and
  construction-only intent exports also audit clean with zero modelspace entities.
- QCAD 3.33.1: explicit-layer plain/enriched exports both import successfully
  with identical entity counts (one each LINE/CIRCLE/ARC/ELLIPSE/POINT/SPLINE,
  two LWPOLYLINE). Both paths emit existing handle warnings. Unstyled mixed
  exports fail QCAD import on both paths; this preexisting plain-writer behavior
  remains outside this persistence task. Do not claim universal CAD compatibility.
- Local independent fixtures/generator: `/tmp/dxfer-native-persistence-qa/`.
  Browser round-trip verification belongs to the parent's integration work.

The underlying document serialization gap is fixed in these APIs. Existing DXF
files that already lost intent cannot reconstruct it; users need an in-memory
document or a prior native snapshot. No existing saved records were repaired or
modified, and nothing was deployed.

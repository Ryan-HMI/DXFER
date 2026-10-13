# DXF output interoperability

The old writer emitted domain IDs as DXF handles and omitted version/subclass
metadata. The BRCAB back retained both splines in Sync exports, but independent
ezdxf rejected their structure. This is a shared writer defect, not missing
geometry in the layer cleanup.

The writer emits AC1015, unique hexadecimal file-local handles, symbol-table
subclasses, AcDbEntity and the appropriate geometry subclasses. Arc geometry
uses AcDbCircle followed by AcDbArc. Grain text retains its annotation behavior.
No coordinates, curves, units, machining dimensions or tolerances change.

Applies to line, polyline, polygon, circle, arc, ellipse, spline and point output.
Dimensions and constraint glyphs remain editor-only as before. Regression tests
cover all supported types and exclude construction geometry by its coordinates,
not by invalid handles. Existing source files need a new export/save; Sync has
an export-only repair for its identifiable legacy DXFer files. No bulk artifact
rewrite is performed.

Independent ezdxf 1.4.4 reads/audits all 23 BRCAB writer outputs with no errors or
automatic repairs, retaining all 64 splines and every original numeric geometry
field. The corresponding 23 Sync exports also pass. Diagnostics:
`/tmp/codex-dxf-interop/audit.py` and `audit.json`.

Native CypNest validation remains Ryan's manual check. Dev-only rollout;
Production/Test are not updated and may still create legacy files.

Deployed0cbf4fa to `dxferdev.sync.harrisonmetals.com` on2026-10-13.
591 .NET tests pass. Installed browser imported the real back source, displayed
the drawing and downloaded81 entities with both original splines. Independent
audit of that download has zero errors/repairs. Screenshot inspected; no browser
errors. Published CadIO assembly matches the Release build. Public auth/status
remains unauthenticated with Google configured; no auth settings changed.
Evidence: `/tmp/codex-dxf-interop/browser.cjs`, `browser-back.dxf`,
`back-in-editor.png`. Rollback publish retained at
`/home/ryan/dxfer-runtime/dev-publish-before-interop-20261013`.

## QCAD Retest Correction

The earlier output still failed Ryan's Windows QCAD 3.33.1. This was reproduced
with the same version's Linux ODA importer: `Null object Id`. Missing model and
paper-space block records caused the failure; adding that framing alone made the
back import without changing geometry. The writer now always emits the space
table/blocks with unique handles and valid entity ownership, explicit planar Z
fields, and standard linetypes. Sync's export adapter also repairs earlier
unreferenced AC1015 files with absent space records.

All 46 real BRCAB outputs (writer and export paths) pass native QCAD import with
every entity-type count matching, including 64 splines per path. Independent
ezdxf audits require zero errors AND zero repairs and verify original numeric
geometry fields. 591 .NET tests pass. No runtime CAD dependency was added.
Native evidence: `/tmp/codex-dxf-qcad/verify-native.cjs` and `all-qcad.json`.
Source artifacts remain unchanged; regenerate previous downloads. Dev-only;
Windows QCAD/CypNest user retest remains pending.

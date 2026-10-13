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

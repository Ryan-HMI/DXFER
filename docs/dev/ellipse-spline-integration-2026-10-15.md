# Ellipse And Spline Integration

## Scope

Dev-only candidate integrates named ellipse axis edits, canonical crossed-axis
DXF export, native control-spline tangent dragging and four reviewed geometry
icons. Production/Test and Sync callbacks remain unchanged. This is bounded
tool verification, not acceptance of the whole editing workspace.

## Parent Verification

- 911 .NET tests and 352 JavaScript tests pass; Web build has zero warnings/errors.
- Actual keyed ellipse creation 5 x 3, major edit to 6, minor edit to 4:
  center/orientation and the opposite diameter stay stable. Undo/Redo and native
  download/reopen preserve the exact resulting model and both driving values.
- Actual cubic tangent-start grip (0.25,0) to (0.25,0.4): only adjacent control
  (1,0) changes to approximately (1,1.6), within pointer precision. Endpoints,
  other controls, degree 3, knots and weights stay unchanged. Undo restores the
  original; Redo and native download/reopen preserve the corrected curve.
- Screenshot `/tmp/dxfer-spline-tangent-after.png` inspected: curve renders,
  existing palettes/status remain unobscured. Ellipse screenshot and the four
  icon screenshots at 1440/820/390 widths were also inspected.
- Export worker independently audited 20 actual ellipse outputs with pinned
  ezdxf: zero errors/repairs. Full and partial crossed-axis ellipses preserve
  geometry; native snapshots preserve named-axis intent.

## Data And Limits

These fix shared dimension application, export and drag/preview paths, not a
single fixture. No application records or original artifacts are repaired.
Previously failed ellipse edits require explicit resubmission; previously
noncanonical external ellipse files require re-export. The spline no-op did
not save changed geometry and needs no repair. Unsupported tangent mappings
remain refused; a quadratic shares its middle control between both ends.

## Installed Dev Verification

Deployed67ef64a on2026-10-15. PID4119144; four installed assemblies match
the reviewed Release publish, ten runtime settings are unchanged, public health
returns200 and anonymous root redirects302 to normal sign-in. Production/Test
PIDs unchanged. Rollback publish retained. Both exact browser cases above pass
again against the installed Dev URL, including Undo/Redo and native reopen.
Flux has the reference for independent retesting; no blanket user acceptance.

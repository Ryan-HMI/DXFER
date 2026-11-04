# Production Cleanup Parity

Ryan requested existing CypNest manufacturing cleanup on every production DXFER
entry point. In progress; no production deployment or record repair claimed.

## Contract

- Reuse pinned Sync algorithms with source-hash regression tests.
- Remove unreferenced model-space circles strictly below 0.050 inch diameter.
- Remove bend/form-outline layers; convert safe isolated straight louver tears
  to existing 0.040 inch closed slots. Keep already converted slots.
- Consolidate cutting geometry to CUT; retain grain separately.
- Apply before local Open, Sync launch (even autoNormalize=false), and the API.
  Download/callback then use the prepared document, not a divergent cleanup.
- Preserve Sync originals before replacement. Refusal must not replace the
  current document, partially save, or silently rewrite historical records.
- Reject explicit non-inch units, native intent, unsafe import and ambiguous
  louver intersections rather than producing a distorted manufacturing file.
- Batch labels/quantities and final CypNest contour gates remain export-only.

## Entity Applicability

Lines, straight polylines/polygons, circles, arcs, ellipses and valid splines use
shared layer preparation. Small-hole deletion applies only to free circles.
Points remain non-cut. Bulged/width/3D polylines, paper-space, malformed curves,
dimensions and native constraint documents refuse where this preparation-only
workbench cannot preserve their intent. Native editing remains in DXFER Dev.

## Verification

UI contract: reuse the existing footer/status strip, with wrapped feedback and
unchanged action targets; no floating cards or new panels. Local import refusal
must be visible, keep filename/geometry unchanged and clear on the next command.

Pending: entry-point/refusal tests, idempotence, slot dimensions, circle cutoff,
units, curves/references, running API/browser checks and independent Flux replay.

# Production Cleanup Parity

Ryan requested existing CypNest manufacturing cleanup on every production DXFER
entry point. Implemented and deployed to Test and normal Production; independent
Flux acceptance remains pending. No historical record repair was performed.

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

Owner verification on the final code candidates:

- Test `898307e`: 829 unit tests passed. Production `9865808`: 831 passed,
  including two additional auth-neutral production contract checks.
- Sync source-preservation/validation: 66 focused tests passed on each branch;
  Test `6aa81c84`, selective production backport `28b3a675`.
- Both fresh self-contained release builds bundle .NET/ASP.NET 8.0.33;
  runtime verifier and its four regression tests passed.
- Playwright against both published builds passed API cleanup/refusal, local
  Open/download, Sync callback with autoNormalize on and off, refusal preserving
  the prior document, and nonblank desktop/mobile canvas checks.
- Published production loading/failure/retry checks passed at 1440, 390 and 320px.
- The checked-in disposable `tests/Fixtures/production-cleanup-louver.dxf`
  produces 9 entities, 3 contours, 15 x 15 inch bounds, and 66.282743 inch cut
  length: one tear slot, one small circle removed, and the 0.050 inch circle kept.
- Desktop/mobile screenshots inspected. The pre-existing floating tool palette
  overlaps part of the mobile canvas; this is not a mobile layout redesign.

## Deployment

On 2026-11-04, Sync Test `6aa81c84` and Production `28b3a675` were installed
first. Public health endpoints verified both SHAs. Then DXFER Test `898307e` and
normal Production `9865808` were activated from fresh self-contained directories.
Public capabilities verified both build identities and process maps verified
the bundled runtimes. DXFER Dev remains on `1e430d8`.

The installed production service passed the complete synthetic browser suite
and public normalization API fixture. Sync's authentication redirects and
DXFER Test's unauthenticated API refusal remain intact. Environment-file
comparisons showed no unexpected configuration changes. Previous release
directories were retained for rollback; no authentication or schema changes.

Flux received the exact installed builds and fixture in #all-agents at Slack
timestamp `1793825406.085909`. Independent acceptance is pending; these are owner
checks, not user or independent QA acceptance. No historical files were bulk
rewritten or repaired. Existing files require a new Open/reprocess operation to
receive cleanup. Previously changed historical source files were not audited.

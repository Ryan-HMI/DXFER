# Production Cleanup Parity

Ryan requested existing CypNest manufacturing cleanup on every production DXFER
entry point. Implemented and deployed to Test and normal Production; independent
Flux acceptance remains pending. The initial rollout did not repair historical
records; the targeted regression recovery below restored missing measurements.

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

### Independent QA Blocker

Flux reported Google `redirect_uri_mismatch` before entering the installed Test
UI. Owner reproduced on 2026-11-04: Test's generated callback is exactly
`https://dxfer-test.sync.harrisonmetals.com/signin-google`; HTTPS and host are
correct. Test and Dev use the same OAuth client, project number 84006041312.
Google rejects Test at `/signin/oauth/error` while Dev reaches the sign-in page.
The existing client's authorized redirect list must admit that exact Test URL
without replacing other entries. No application/authentication changes were
made for this diagnosis. Cloud Console browser access is unavailable in this
session and the cached Google Cloud CLI login requires reauthentication.

This is an unresolved external configuration blocker, not a failed cleanup
replay and not independent acceptance. Retry login only after the administrator
updates the existing client; no password retries, key rotation or auth bypass.

## Metadata Import Regression And Recovery

Ryan reported that Connor's uploaded flat was blank and bounds were not being
extracted. The new blanket code-102/XDATA rejection was the shared cause, not a
bad part definition. Ordinary Onshape font/provenance records were rejected
before display and measurement. A later baseplate also exposed CIRCLE radius
provenance and legitimate repeated group-5 draw-order keys in SORTENTSTABLE.

The fix validates object ownership, known scalar provenance, matching radius,
and draw-order entity references before detaching metadata in the working copy.
Unknown associations, malformed/shared graphs, genuine entity reactors and
native design intent still refuse. Original input bytes are unchanged. Verified
draw-order entries do not protect duplicate geometry or small holes from cleanup.

Installed hotfix sequence on 2026-11-04:

- Production `77c5664`, then `c6887e7`; Test `b1f1313`, then `0f95047`.
- Final full unit suites: 898 Production / 896 Test passed.
- Both final self-contained releases passed runtime verification with
  .NET/ASP.NET 8.0.33. Public capabilities verified the exact installed commits.
- Installed Production browser replays passed actual panel and baseplate local
  Open, both Sync normalization modes, callbacks to a localhost stub, and
  desktop/mobile nonblank canvases. Source hashes remained unchanged. The
  synthetic cleanup/refusal/download suite also passed. Screenshots inspected.
- Sync remains Production `28b3a675`, Test `6aa81c84`; DXFER Dev untouched.
  No authentication, permission, schema or original-file changes.

After the systemic fix, a scoped, reviewed maintenance helper restored missing
measurements on original artifacts 866, 867, 869, 870, 872, 873, 875, 876 and 877.
It verified exact source hashes, latest revision ownership, current selection,
installed build and raw/API geometry, then rechecked under serializable row
locks. Original files and artifact IDs/current selections remain unchanged.
Fresh database/file verification passed all nine, with zero remaining missing
bounds among current flats uploaded during the outage window.

The reported panel 866 is 50.5 x 38.5 inches, 187.132741-inch cut, one contour.
Baseplate 875 is deliberately different: its original has five duplicate circles,
18 entities, 8 x 8 bounds, 49.132741-inch raw cut and an ambiguous-contour warning.
Those original-file measurements and warning were retained. DXFER's cleaned
preview has 13 entities, 39.707963-inch cut and six contours. Saving that cleaned
version back is still required to replace the original's duplicate-bearing
geometry; recovery did not falsely attach cleaned measurements to original bytes
or declare manufacturing clearance.

Local recovery evidence is under
`/home/ryan/.codex-nudger/repairs/fellowship-bounds-20261104/evidence/`
at `20261104T222157Z` and `20261104T223132Z`. Each contains before-state snapshots,
original-file copies, API previews and after-state evidence. Helper and explicit
seven-artifact manifest are retained beside the investigation; no bulk history
rewrite was attempted. Final persisted verification is
`/tmp/dxfer-bounds-investigation/persisted-verification.log`.

Flux handoff: #all-agents `1793831565.154829`. Independent acceptance is not
claimed. Test Google OAuth still rejects the exact Test callback with
`redirect_uri_mismatch`; that external client configuration issue is separate
and was not changed. Preserve both metadata hotfixes in future promotions.

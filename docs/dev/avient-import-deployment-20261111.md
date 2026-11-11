# AVIENT BC14-025 Import Recovery

Production DXFER `ae6304e2ada03894d1823f31a1de2d6ea688b283` and Test DXFER
`902c385549fb8335740aac031fc78cb86e0dcef8` were deployed on 2026-11-11 after
isolated self-contained publish and exact-file replay. Authentication and
environment configuration were preserved. Sync Production was not restarted.

## Cause and Verification

The valid legacy R14 metadata in the uploaded flat was rejected by strict
production preparation. The shared importer now validates the supported legacy
representations without treating them as design geometry. Writer precision was
also corrected so export/reopen does not collapse near-coincident source circles
into exact duplicates. See `legacy-r14-production-import-20261111.md` for scope.

- Production source suite: 1098 passed. Test source suite: 1076 passed.
- Agent review independently passed 92 proxy tests and reported no blocking
  importer findings.
- Original source SHA-256:
  `762a3482bb8b5b589acf3273f70d60edac69f5a7e9551c22588c31aac29715f6`.
- Exact source has 40 entities; preparation removes 3 bend lines and retains all
  37 cut entities. The retained geometry equals the original cut geometry.
- Public Production and authenticated Test API/browser replays render the flat.
  Repeated normalization preserves bounds, cut length, and the contour warning.
- Normalized bounds: width 15.399394022393665 in, height 14.938222988335108 in.
  Cut length: 64.2559995982958 in.
- Desktop screenshots were inspected. A 500 px resize remained nonblank but was
  not fully framed; no mobile layout acceptance is claimed.

## Data Status and Review Hold

The definition is Part482, `HMI-SS-FRPCP-AVIENT-BC14-025`, current Rev A1393,
flat957. Original flat956 on historical Unreleased revision376 was not changed.

No Production database or flat-file repair was applied. A guarded repair dry run
identified the still-valid, unconsumed edit session39 on flat957. The final
repair guard refuses to supersede an active session. Connor was asked to refresh
his DXFER tab, inspect the recovered drawing, and use the ordinary Save to Sync
workflow to persist measurements. His unsaved work and source remain intact.

The source also contains near-coincident duplicate circles. The contour result
continues to say `Branched or ambiguous contour endpoints require review.` This
is not a clean-cut acceptance or an engineering release. No geometry-tolerance
merge, status override, or warning suppression was performed.

Review of the repair found that the older Production Sync callback does not
require its original artifact to remain current. This is an additional reason
not to replace an artifact behind an outstanding edit session. The repair does
not claim to fix that separate stale-callback behavior.

Deployment evidence is append-only under:

- `/home/ryan/.local/state/hmisync-production-archive/dxfer-avient-deploy-2026-11-11T21-28-05.154Z-2cfdfab4-d206-4fe9-841a-e7a1edb511f9`
- `/home/ryan/.local/state/hmisync-test-archive/dxfer-avient-deploy-2026-11-11T21-32-04.315Z-a712275d-c0b6-4652-b525-d808a6e6d1ec`

Dry-run and refusal evidence remains in uniquely named `avient-flat-reprocess-*`
directories under the Production archive. Failed/partial evidence is retained;
these directories must never be reused, removed, or overwritten.

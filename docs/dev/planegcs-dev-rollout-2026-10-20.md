# PlaneGCS Dev Rollout

## Installed Scope

2026-10-20, approximately 16:13 UTC: code `25fb640` is published at
https://dxferdev.sync.harrisonmetals.com, from the pushed
`feature/full-editing-workspace-20261012` branch.

The rendered tooltip and canvas report PlaneGCS, not Legacy. Preview,
constraint/dimension edits, creation and mapped dependency propagation use the
actual pinned engine. There is no runtime-failure fallback to Legacy.
Unsupported operations refuse without publishing a partially solved drawing.

This is a Dev rollout for continued testing, not acceptance of every solver
combination. Unrelated Sync features were not changed.

## Verification

- Local full suites: 2,874 Core tests and 1,605 JavaScript tests pass; the latter
  includes 443 focused actual-WASM cases. These counts include existing
  regression coverage, not 4,479 independent new solver behaviors.
- Release publish succeeded. All four installed DXFER assembly hashes match the
  Release output. Solver, Offset, Pattern JavaScript and WASM hashes match the
  committed source assets; all 59 vendored checksums pass.
- Actual published-WASM smoke solves fixed-start Horizontal plus driving length.
- All 16 browser workflows pass locally and on the public Dev URL: rectangle
  movement and fixed refusal, midpoint/origin and point/origin Horizontal,
  driving length, circle/rectangle Offset gap edits, rectangular/radial pattern
  following, ellipse/spline editing, and endpoint/midpoint drag snaps with Auto
  ON/OFF. History, native reopen and continued editing are included.
- Public Dev sustained motion: 42 accepted real-engine preview updates during
  1,850 ms of pointer movement; newest release commits; Escape prevents pending
  stale repaint. Undo/Redo and native reopen pass.
- Installed screenshots inspected: nonblank geometry and existing workspace
  layout retained. No visual redesign in this rollout.

Local evidence:

- `/tmp/dxfer-planegcs-current-core.log`
- `/tmp/dxfer-planegcs-current-js-final.log`
- `/tmp/dxfer-planegcs-browser-full-final`
- `/tmp/dxfer-planegcs-installed-25fb640`
- `/tmp/dxfer-planegcs-installed-sustained-25fb640`

The full public suite used the authorized API-key harness. Its initial WebSocket
upgrade received 302 and the connection continued through LongPolling; all
workflows passed with no page exceptions. This does not verify a user's complete
interactive Google login or claim a clean console. Anonymous auth status returns
200 and root redirects to Google with the correct HTTPS Dev callback.

## Runtime and Isolation

- Dev service PID after rollout: 2058397.
- Production PID 3584417 and Test PID 3584475 unchanged.
- Full editing true; Sync integration false.
- Production ASP.NET mode, ordinary Google sign-in, existing API key, application
  discriminator and independent persistent Data Protection keys retained.
- Only new setting:
  `/etc/systemd/system/dxfer-dev.service.d/25-planegcs.conf`,
  `Dxfer__SketchSolverEngine=PlaneGCS`.
- Scoped Node hosts receive no application environment credentials. Existing
  bounded frame/output/process/memory budgets and five-second solve timeout
  remain; timed-out children are terminated.
- No existing drawings or business records rewritten. No data repair performed.

## Root Causes and Limits

The systemic edit/preview integration now uses a general constraint engine
rather than adding more procedural propagation cases. Integration regressions
also corrected reused native-system state, native fixed-parameter handling,
joined-offset topology validation, exact source preservation on gap edits,
drag-snap intent, and dimension-anchor following. Tests cover the shared failure
modes, not a repair of one saved drawing.

Remaining limits are explicit:

- Radial 512-member solves remain CPU-bound and can hit the five-second budget.
  A speculative pointer fast path was not integrated because incompatible
  targets could stall before fallback and shape preferences were not equivalent.
- General topology lineage remapping and unsupported curve/equation combinations
  may refuse. The direct topology path validates candidates; it does not silently
  drop references or call Legacy to make them pass.
- Independent Flux replay and Ryan's original drawings are still pending.
- Previously flattened, corrupted or conflicting saved geometry is not
  reconstructed automatically. Verified originals or explicit repair are needed
  if those documents are encountered.

## Rollback

The complete previous publish is retained at:
`/home/ryan/dxfer-runtime/dev-publish-before-25fb640`.

To revert, stop only `dxfer-dev.service`, retain the current publish separately,
restore that backup to `dev-publish`, remove only `25-planegcs.conf`, reload
systemd and start Dev. Preserve `dev-state`, keys, runtime.env, other overrides
and all Production/Test services. Recheck auth status and build tooltip.

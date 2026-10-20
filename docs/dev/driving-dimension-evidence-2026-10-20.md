# Driving dimension validation and evidence

## Scope and cause

Ryan reported agreeing driving dimensions being accepted until one value changed.
The existing RMB Driving/Driven controls were present, but their promotion path
used legacy measurement validation rather than PlaneGCS rank validation.
Separately, the workbench discarded native failure evidence when retaining a
rejected dimension. Agreeing redundant values cannot be identified from their
geometric residual alone. File-open provenance changes also discarded evidence.

## Correction

- Retain native evidence with failed intent, presentation-only commits and file
  open. The existing semantic signature still invalidates evidence when geometry
  or intent changes; this does not manufacture acceptance from stale results.
- In PlaneGCS mode, mode changes use the native solver with the original document
  as baseline. A redundant promotion stays Driven without a history entry.
- Demoting the rejected duplicate to Driven clears its driving equation and
  refreshes its measurement. Subsequent driving edits update that measurement.
- No new layout, controls, solver fallback, runtime limits or geometry equations.

## Verification

Four new real-native component-handler tests failed before the correction and
pass afterward. They cover line/circle duplicate placement, unchanged geometry,
label moves, Undo/Redo, native DXF roundtrip, the actual file-open handler,
mode conversion, refused promotion and measurement following.
The tests use the existing serialized resource-budget collection so parallel
test processes do not exceed the application runtime's eight-process cap.

- Full core suite: 3,037 passed.
- Full JavaScript suite: 1,772 passed.
- Published local browser: immediate visible redundant circle dimension,
  Undo/Redo, native reopen, RMB Driven conversion, refused redundant promotion,
  original driving edit and updated Driven measurement after reopen passed.
- Browser screenshots inspected; no circuit or browser errors.
- Browser fixture: `tests/DXFER.Blazor.Tests/drivingDimensionEvidence.browser.mjs`.
- Local evidence: `/tmp/dxfer-driving-browser`,
  `/tmp/dxfer-driving-core-final.log`, `/tmp/dxfer-driving-js.log`.

## Existing files and rollout

Dev only; Production/Test and Sync are unchanged. No saved files are rewritten.
Older files already containing redundant driving dimensions still require the
user to remove the extra dimension or convert it to Driven. Lost historical
failure evidence is not invented or silently reconstructed on file open.

Installed Dev `2f2ecf5` was independently identified from the rendered build
tooltip by the browser regression. The full browser sequence above passed on
`https://dxferdev.sync.harrisonmetals.com`, including visible immediate failure,
reopen, mode conversion and subsequent measurement following. Screenshots were
inspected. Evidence: `/tmp/dxfer-driving-installed` and
`/tmp/dxfer-driving-installed.log`. Service is active; anonymous root remains
302 and `/auth/status` still reports Google authentication configured. No auth
settings changed. Rollback: `/home/ryan/dxfer-runtime/dev-publish-before-2f2ecf5`.

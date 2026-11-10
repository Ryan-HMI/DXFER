# External-Only Sync Launches

## Scope And Baseline

Source-only DXFER change based on installed Production
`5ddaef1e8debe0c7028faad9cdc1f3680f7349ca`, confirmed through the installed
release symlink and read-only capabilities endpoint. Worktree:
`/home/ryan/.codex/worktrees/DXFER-external-only-20261110`.

Production, Estimating, and other non-Engineering flat readers use the same
`saveToSync=false` launch capability. DXFER does not decide roles. Sync owns
purpose-scoped token verification and rejection at both callback endpoints;
the query flag is a client behavior contract, not a security boundary.

This Production baseline has an explicitly auth-neutral Web host, not the
local signed-launch session authentication present in other DXFER branches.
Keep that boundary unchanged: fetch the supplied signed download URL exactly,
honor HTTP authorization failures before import, and never equate disabled
callbacks with missing launch credentials. No auth code is ported from the
older main checkout. No account settings or Sync source are changed.

## Layout Contract (Before Implementation)

- Alignment: preserve the compact top-right desktop Sync strip and existing
  File menu alignment; omit the Sync save action for external-only launches.
- Wrapping: retain the strip's flex wrapping and wrap-safe error text. Use the
  short status `External save only` without adding instructional UI copy.
- Sizing: preserve existing status bounds, icon targets, canvas grid, palette,
  and footer dimensions. No CSS or new controls are needed.
- Overflow: retain viewport containment and the current error overflow-wrap;
  hidden save actions must not leave a misleading active target.
- Interaction: keep retry and local download, full existing geometry editing,
  cleanup, history, current filename, and keyboard behavior. Guard direct
  callback, command, hotkey, and job-folder fallback write paths as well as UI.
- Responsive: retain the separate Sync status row above the canvas/palette at
  720px and below. Verify 1440px, 390px, and 320px with screenshots.

Reviewed the inherited AGENTS rules, STARTUP.md, hmi-ui-layout, ui-design,
designing-layouts, HMI design system/component registry (read-only), current
Workbench/MainLayout markup, workbench CSS, and shared CadIconButton pattern.
No AGENTS.md is present in this DXFER source or installed release.

## Compatibility And Entity Applicability

Append SaveToSync with a true default. Preserve the existing boolean parser
conventions. HasLaunchCredentials describes tuple presence only, never token
validity; IsCallbackConfigured additionally requires SaveToSync. Engineering
launches with an absent/true flag keep their existing one-shot callback path.

Permission handling is document-wide: line, polyline, polygon, circle, arc,
ellipse, spline, point, dimension, and constraint references retain their
existing editing/import/export behavior. No entity-specific restrictions or
new editing surfaces are introduced. Existing supported export is DXF; DWG
retains its external-viewer handoff. Local SaveDxf already downloads externally.

## Data And Deployment

No data repair is required by the DXFER change. No live artifact writes,
service restarts, deployment, or changes outside this worktree are authorized.
Server cryptographic enforcement is a separate, parallel Sync change.

## Verification

- `dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --verbosity minimal`:
  928 passed, zero failed/skipped. New cases cover parser defaults/casing,
  credentials versus permission, no-HTTP client rejection, rendered controls,
  both normalization modes, edit/undo/redo, unchanged current file/history,
  external DXF payload, blocked commands/injected hotkeys, and no fallback file.
- External-only authorization regressions honor HTTP 401/403 before import,
  show no token/URL secrets, and retry the exact signed URL. Component tests
  assert the sole network request is the input GET, including after all save
  paths are invoked. ProductionFoundationContractTests remain unchanged.
- `node --test tests/DXFER.Blazor.Tests/*.test.mjs scripts/verify-linux-publish.test.cjs`:
  260 passed, zero failed/skipped.
- `dotnet build src/DXFER.Web/DXFER.Web.csproj --configuration Release --verbosity minimal`:
  zero warnings/errors.
- Local Playwright suites passed: `syncExternalOnly.browser.mjs`,
  `syncSaveFeedback.browser.mjs`, `syncLaunchFailure.browser.mjs`,
  `productionCleanup.browser.mjs`, and `productionFoundation.browser.mjs`.
  External-only coverage includes downloaded edited geometry, working history,
  signed-download denial/retry, zero callbacks, menu absence, nonblank canvas
  pixels, and viewport-contained status at 1440/390/320px. Existing suites
  retain Engineering's default-true save/consumed-job/close behavior, cleanup,
  and anonymous API access (`/auth/status` remains 404).
- Screenshots: `/tmp/dxfer-external-only-20261110`, with Engineering, load-failure,
  cleanup, and auth-neutral subdirectories. Desktop and phone screenshots were
  visually inspected. Existing narrow-screen menu/palette clipping is unchanged;
  this patch verifies the changed Sync status/actions, not a palette redesign.

Browser QA used `http://127.0.0.1:5286`, an empty process environment apart from
runtime settings, a temporary HOME, and synthetic local download/callback
servers. No real launch tokens or artifact callbacks were used. Playwright was
loaded from the existing Codex node dependency cache and used installed
`/usr/bin/google-chrome`. The foundation script's default bundled Chromium was
unavailable; it passed with a process-local launch override to installed Chrome,
without changing that test or installing a browser. The QA app was stopped after
verification. No installed services were changed.

The DXFER systemic issue (treating launch credential presence as write
permission) is fixed. No existing records need DXFER-side repair. Sync's
purpose-scoped enforcement must accompany rollout; the client query flag alone
does not provide cryptographic authorization. Main confirmed the same editToken
parameter carries purpose `HMI-Sync.DXFER.DownloadJob.v1`, accepted only by the
download route and rejected by both callbacks regardless of UI flags.

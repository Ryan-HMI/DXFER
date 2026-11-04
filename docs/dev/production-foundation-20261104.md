# Auth-Neutral Production Foundation

The foundation below was verified at `553cdbb`. The subsequent candidate stage
at the end of this document supersedes the original remaining-integration list.

## Scope And Provenance

Ryan requested this source-only foundation on 2026-11-04. It starts at the
installed normal release's commit, `0a051ee0664a2313efa8cf8c67fa28d0d9d372f9`,
and ports these feature commits in order:

- `d3398d8`: guarded cut perimeter, import safety, classic-polyline closure.
- `d85cd99`: guarded closed-contour counts and NetTopologySuite 2.6.0.
- `9d02a11`: inch-coordinate manufacturing measurement/export behavior.
- `fa7390d`: exact duplicate cleanup and its existing production controls.

Historical approval and verification for those features was Test-only. This
document does not recast it as prior production approval. The current request
authorizes this foundation, not deployment or unrelated authentication changes.

The four feature patches do not require their historical auth ancestor
`4e64ce5`. Normal's Web project, authentication boundary, antiforgery, and HTTPS
behavior remain unchanged. Program.cs gains only the manufacturing-unit wrapper
and guarded measurement fields. MainLayout gains only the duplicate command.

The writer HEADER conflict retains deployed AC1015, unit declaration, unique
handles, model/paper-space records, owners, and subclass metadata, adding review
markers inside that existing HEADER. The deployed load/retry and save-blocking
changes are retained. Shared CadIO, Core, and Blazor source trees must match
Test `36cf6f28a18e0a619b21d7fde40a6f4c6bf3cccd` exactly.

## Layout Contract

Cut and contour values use the existing inline label/value readouts and footer
alignment. The readout group wraps and switches to left alignment below 900px;
canvas sizing, footer overflow, and fixed tool targets remain unchanged. The
duplicate action uses the existing icon/menu controls and disabled/reference-only
states. Mobile Sync controls retain the deployed separate-row layout. No CSS,
auth markup, or Dev editor surface is introduced.

## Remaining Integration

This is not the complete production cleanup/parity implementation. The parent
will add the pinned Sync vendor, cleanup integration, and reader safeguards.
Validate original units before AssumeInches; preserve raw spline/bulge geometry
before lossy import or duplicate removal. Local download, API normalization,
Sync save, and fallback export all need the final preparation policy.

No services, deployment paths, databases, existing artifacts, or credentials are
changed by this foundation. Existing-record repair is not assessed here.

## Verification

- `dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --verbosity minimal`:
  588 passed, zero failed or skipped, including auth-neutral foundation contracts.
- `dotnet build src/DXFER.Web/DXFER.Web.csproj --configuration Release --verbosity minimal`:
  zero warnings and errors.
- `node --test tests/DXFER.Blazor.Tests/*.test.mjs scripts/verify-linux-publish.test.cjs`:
  260 passed, zero failed or skipped.
- `git diff --exit-code 36cf6f2 -- src/DXFER.CadIO src/DXFER.Core src/DXFER.Blazor`:
  no differences. Web project/package references remain identical to `0a051ee`.
- `productionFoundation.browser.mjs`: isolated loopback runtime, no credentials;
  capabilities 200, auth/status 404, normalize GET 405, anonymous normalize POST
  200. The rectangle/hole fixture returns 36.2831853071796 inches, two contours,
  and no review reason. Local open, duplicate control, and footer bounds/wrapping
  pass at 1440, 390, and 320 pixels with no browser errors.
- Existing `syncLaunchFailure.browser.mjs`: visible failures, disabled save,
  loading state, exact-URL retry, recovered drawing, and 1440/390/320-pixel Sync
  status layout all pass.
- Screenshots retained outside the repository in
  `/tmp/dxfer-production-foundation-20261104`; desktop and phone foundation
  readouts visually inspected. Existing canvas/palette behavior is not restyled.

Browser checks used a temporary home directory for the app, an empty app
environment apart from runtime settings, synthetic inputs, and an ephemeral
loopback port. The process was stopped afterward; installed services and
production data were never used. No publish or deployment was performed.

## Candidate Assembly And Hold

Per Ryan's follow-up request, this same production worktree now includes:

- `326191c`: pinned Sync preparation vendor and parity tests.
- `f16ad69`: guarded DXF reader without spline reconstruction.
- `2089714`: shared production preparation for local, Sync, and API imports.

Program.cs retains normal's auth-neutral pipeline. The new import handler uses
ProductionDxfPreparation, exposes its cleanup report, and translates preparation
refusals into HTTP 400. The foundation regression now checks that entry point.

REVIEW HOLD: the parent reported three malformed-input blockers. This candidate
is assembled for verification only, not cleared for deployment. Follow-up
preflight/reader fixes and independent review remain required even if the
currently available test suite passes. No records have been repaired.

Candidate verification before those follow-up fixes:

- Full .NET suite: 746 passed, zero failed or skipped (the parent's 744 plus
  two foundation contracts).
- JavaScript/runtime-validator suite: 260 passed, zero failed or skipped.
- Release Web build: zero warnings and errors.
- Shared CadIO, Core, Blazor, and vendor trees exactly match `2089714`.
  Compared with that Test source, the Web differences are the omitted 363-line
  authentication block and Google package reference; no other application
  source differs.
- All three loopback browser suites pass: `productionFoundation.browser.mjs`,
  `syncLaunchFailure.browser.mjs`, and `productionCleanup.browser.mjs`.
  Coverage includes anonymous API behavior, local open/download, API cleanup,
  both Sync normalization modes, synthetic callback, refusal/retained document,
  and nonblank desktop/mobile rendering. Desktop/phone cleanup screenshots were
  inspected in `/tmp/dxfer-production-candidate-20261104`.
- The isolated QA process was stopped. These results do not clear the review hold.

# Ellipse quadrant dimension handoff

Scope: DXFER only, based on Dev `82b3779`. No deployment, Slack posting, Sync
changes, or commercial/user drawing repair was performed for this fix.

Source: https://agentswarmworkspace.slack.com/archives/C0CB4EV4B6F/p1793814931638529
The readable Slack report contained no attachment IDs. The regression recreates
the reported single unconstrained ellipse with native `quadrant-90` and
`quadrant-270` references, rather than claiming to use Flux's original file.

## Cause and correction

The canvas and PlaneGCS JavaScript already resolve the four ellipse quadrant
identities. C# input validation used an ellipse axis resolver that recognized
only `major-start/end` and `minor-start/end`. It rejected quadrant dimensions
before invoking PlaneGCS. A separate C# dimension measurement implementation
also fell back to stale acquisition coordinates for those identities, so simply
allowing validation would still reject edits or validate the wrong geometry.

The shared live ellipse resolver now recognizes the four exact quadrant aliases.
Dimension measurement uses that same resolver instead of its duplicate axis
implementation and snapshot fallback. Finite-coordinate, entity-type, geometry
validity, fixed-point, residual and rank guards remain in place. No reference
rewriting, solver equation changes, tolerance relaxation or constraint deletion.

Layout contract: preserve existing alignment, wrapping, sizes, overflow, canvas
hit targets, label placement/edit interaction and responsive layout. No markup,
CSS, or production JavaScript changes. The repository has no AGENTS.md or shared
HMI design docs; the supplied rules and main HMI checkout's design/registry docs
were read along with the local DXFER patterns.

## Verification

- Red first: new tests reproduced `Invalid dimension 'picked-axis'` before the
  runtime call and exposed stale-coordinate measurement/invalid-key acceptance.
- 470 focused C# tests passed, including real PlaneGCS creation/edit/reopen,
  reversed/rotated major and minor quadrant picks, stale-coordinate measurements,
  malformed/nonfinite refs, fixed-point result guards and fixed conflicts.
  Existing ellipse, symmetry, slot/four-bar, polygon/endpoint and center-creation
  regressions were included.
- 284 focused JavaScript tests passed, including both polygon session triggers
  and the prior center rectangle, symmetry, ellipse, slot and four-bar fixes.
- Web build passed with zero warnings/errors. No simultaneous .NET builds;
  `DOTNET_PROCESSOR_COUNT=2 -m:1 -nodeReuse:false` throughout.
- Local browser: top-first and bottom-first at 1440 and 1180 pixels passed actual
  Dimension picks, non-red placement, 6 -> 8 edit, Undo/Redo, native reopen and
  8 -> 10 edit. Native exports retain exact quadrant identities and driving
  intent; live geometry matches the values. Placement leaves geometry unchanged.
  Screenshots inspected; zero page errors. Local servers stopped afterward.
- Evidence: `/tmp/dxfer-ellipse-quadrant-1440/` and
  `/tmp/dxfer-ellipse-quadrant-1180/` contain screenshots, native exports and JSON
  results. These initial source-build runs show the base commit in the tooltip.

## Repeat locally

Run from `/home/ryan/.codex/worktrees/DXFER-creation-intent-20261104`:

```bash
export DOTNET_PROCESSOR_COUNT=2
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~Ellipse|FullyQualifiedName~PlaneGcsCollapsedSymmetryRuntimeTests|FullyQualifiedName~PlaneGcsSymmetricNormalRuntimeTests|FullyQualifiedName~PlaneGcsPolygonCreationRuntimeTests|FullyQualifiedName~PlaneGcsLargeStepRuntimeTests|FullyQualifiedName~SketchCreationCenterAnchorTests|FullyQualifiedName~SketchCreationDimensionFactoryTests|FullyQualifiedName~CreationSnapReferenceTests|FullyQualifiedName~SketchDimensionSolverServiceTests'
node --test --test-concurrency=1 tests/DXFER.Blazor.Tests/{centerRectangleCreation,creationSnapIntent,planeGcsCreatedRectangle,planeGcsCollapsedSymmetry,planeGcsEllipseFrames,planeGcsEllipseRank,planeGcsEllipseEditing,planeGcsLargeStep,planeGcsSessionReplay,planeGcsSessionUiWitness,planeGcsPolygonVertex,planeGcsPolygonEditing,lineSlot}.test.mjs
dotnet build src/DXFER.Web/DXFER.Web.csproj --no-restore -m:1 -nodeReuse:false
export PLAYWRIGHT_MODULE=/home/ryan/HMI-Sync/node_modules/playwright/test.mjs
DXFER_START_LOCAL=1 DXFER_QA_URL=http://127.0.0.1:5068 DXFER_QA_WIDTH=1440 DXFER_SCREENSHOTS=/tmp/dxfer-ellipse-quadrant-repeat-1440 node tests/DXFER.Blazor.Tests/ellipseQuadrantDimension.browser.mjs
DXFER_START_LOCAL=1 DXFER_QA_URL=http://127.0.0.1:5068 DXFER_QA_WIDTH=1180 DXFER_SCREENSHOTS=/tmp/dxfer-ellipse-quadrant-repeat-1180 node tests/DXFER.Blazor.Tests/ellipseQuadrantDimension.browser.mjs
git diff --check
```

Port 5068 must be free; choose another loopback port if occupied. Browser tests
generate random local credentials, disable Sync integration and close their own
server/browser. They use disposable drawings only.

## Next Dev deployment, not executed

Only after Ryan authorizes deployment, from this clean, committed worktree:

```bash
set -euo pipefail
test -z "$(git status --porcelain)"
sha=$(git rev-parse --short=7 HEAD)
release="/home/ryan/dxfer-runtime/releases/dev-$sha-ellipse-quadrant"
test ! -e "$release"
DOTNET_PROCESSOR_COUNT=2 dotnet publish src/DXFER.Web/DXFER.Web.csproj -c Release -r ubuntu.24.04-x64 --self-contained true -p:PublishProfile=LinuxSelfContained -p:UseRidGraph=true -m:1 -nodeReuse:false -o "$release"
node scripts/verify-linux-publish.cjs "$release"
```

Before changing the service, smoke-test the exact package on a free loopback port
in the same shell (set PLAYWRIGHT_MODULE as above):

```bash
qa_key=$(openssl rand -hex 32)
ASPNETCORE_URLS=http://127.0.0.1:5068 ASPNETCORE_CONTENTROOT="$release" ASPNETCORE_ENVIRONMENT=Production DOTNET_PROCESSOR_COUNT=2 DXFER_API_KEY="$qa_key" Dxfer__SketchSolverEngine=PlaneGCS Dxfer__EnableFullEditing=true Dxfer__EnableSyncIntegration=false Dxfer__DataProtectionDirectory="/tmp/dxfer-quadrant-package-$sha/keys" "$release/DXFER.Web" >"/tmp/dxfer-quadrant-package-$sha.log" 2>&1 &
qa_pid=$!
trap 'kill "$qa_pid"; wait "$qa_pid" || true' EXIT
DXFER_START_LOCAL=0 DXFER_QA_URL=http://127.0.0.1:5068 DXFER_QA_API_KEY="$qa_key" DXFER_EXPECTED_REVISION="$sha" DXFER_SCREENSHOTS="/tmp/dxfer-quadrant-package-$sha" node tests/DXFER.Blazor.Tests/ellipseQuadrantDimension.browser.mjs
kill "$qa_pid"
wait "$qa_pid" || true
trap - EXIT
unset qa_key
```

Switch only Dev, retaining its old release and environment/state. The baseline
check deliberately stops if another deployment has occurred:

```bash
target=/home/ryan/dxfer-runtime/dev-publish
previous=$(readlink -f "$target")
test "$previous" = /home/ryan/dxfer-runtime/releases/dev-82b3779-20261104
test ! -e "$target.next-$sha"
ln -s "$release" "$target.next-$sha"
sudo systemctl stop dxfer-dev.service
mv -Tf "$target.next-$sha" "$target"
sudo systemctl start dxfer-dev.service
systemctl is-active dxfer-dev.service
curl -fsS https://dxferdev.sync.harrisonmetals.com/auth/status
```

Verify the installed build tooltip equals `$sha`, PlaneGCS is active, normal
Google sign-in remains enabled and Sync integration remains disabled. Then have
Flux independently replay the exact case with edit/history/reopen and Fixed
conflict controls on disposable drawings. No independent acceptance of this new
fix is claimed. Do not promote to Production or touch Sync services.

If installed verification fails, rollback only Dev in the same shell:

```bash
ln -s "$previous" "$target.rollback-$sha"
sudo systemctl stop dxfer-dev.service
mv -Tf "$target.rollback-$sha" "$target"
sudo systemctl start dxfer-dev.service
```

## Existing drawings and limits

The underlying shared defect is fixed; no existing records were modified.
Saved failed dimensions keep their exact identities and can be retried/edited
after deployment. Previously retained failed intent is not automatically removed
or repaired; affected saved drawings still need an explicit retry and verification.
Missing historical dimensions/anchors are not inferred. Free ellipse dimensions
do not pin its otherwise unconstrained center or orientation. This is not a
general solver-rank change, full-suite certification, or independent Flux signoff.

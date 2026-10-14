# DXFer Dev playground

## Current Deployment Follow-up

2026-10-14 approximately19:18Z: ba2a661 deployed Dev only. Independent palette
layout persists across reload, including dock, order, compaction and preferred
floating coordinates. Integrated tests:783 .NET and273 JS pass. Installed real
browser verifies drag/dock/collapse/reload,820/390/320 viewport recovery,
malformed/unknown storage fallback, and no JS errors. Desktop/320 screenshots
inspected. Ten runtime settings preserved, all four assemblies match Release,
health200 and anonymous root302. Production/Test runtime PIDs unchanged.
Rollback:dev-publish-before-ba2a661. Browser-local preferences only; no drawing
or business records repaired. Flux independent QA requested. Includes42df453
trim-result visibility; spline geometry correction is not in this deployment.

2026-10-14 approximately18:56Z:0eb7dc5 deployed Dev only. Includes101d415
invalid-corner-input gating and responsive result text;775 tests and installed
browser checks pass. Stable CSS/JS responses now require revalidation. Flux
confirmed the previous footer discrepancy was stale cached CSS and verified
the new layout after hard reload. No Production/Test changes.

2026-10-14 approximately18:31Z: dcb6304 deployed Dev only, including7de1167
polyline corner topology.769 .NET tests pass. Actual installed browser vertex
gestures apply R0.7 fillet and D0.5 chamfer and survive Undo/Redo. The browser
check first exposed unsupported canvas vertex references; the follow-up resolves
them against live polyline vertices, rejects stale/invalid references, and shows
corner operation feedback instead of hiding it behind the modal prompt. Existing
native roundtrip/failed-intent/stale-envelope smoke passed on7de1167; final corner
gesture checks passed ondcb6304. Core Release/installed hashes match. Normal
Google sign-in and disabled Sync callbacks retained. Production/Test unchanged.
Rollback directories:dev-publish-before-7de1167 anddev-publish-before-dcb6304.
Layout contract: existing command bar, targets, docking and responsive structure
unchanged; only corner result text uses the existing status slot. Desktop
screenshot inspected; independent Flux QA requested. Palette persistence and
curved-edge fillet support remain open. No existing document data repaired.

2026-10-14T11:53Z verification:885aca7 deployed Dev only. Native constraint
download/reopen integration passes704 .NET/265 JS tests and installed browser
roundtrip/stale-envelope checks. All four installed assemblies match Release;
auth/status200, normal Google configured, callbacks false, PID3819916. Actual
browser DXF audits with zero errors/fixes. Backup:dev-publish-before-885aca7.
No Production/Test restart, Sync callback or existing drawing repair. Flux has
independent QA; polyline corner implementation continues separately.

2026-10-14 approx11:42Z: 7005ac1 deployed to DXFer Dev only, including
4e85712 history conflict feedback and the first reviewed icon integration.
648 .NET and265 JS tests pass; isolated and installed browser checks show
unchanged geometry, restored Redo explanation and unclipped glyphs at1440,
1280,820,390px. Published Blazor/Web DLL hashes match Release. Normal Google
auth retained, Sync integration false, service PID3813052. Rollback publish:
dev-publish-before-7005ac1. No Production/Test restarts or record repairs.
Flux independent QA requested; native persistence and full workspace composition
are not claimed complete by this deployment.

2026-10-14T02:58Z: 442c395 deployed to DXFer Dev only. Shared constraint
transaction/propagation patch: 601 .NET and 265 JS tests plus isolated canvas
conflict/undo/redo checks pass. Installed Core DLL matches Release; public
auth/status200 with normal Google authentication retained. Sync callbacks remain
disabled. Rollback directory: dev-publish-before-solver-442c395. Flux retest
requested; native save/reopen of constraints remains open. See
constraint-transactions-2026-10-13.md. Production/Test were not restarted.

2026-10-13T11:40:29Z: code4d7e7c9 deployed, including document-lifecycle fix
2571107.591 .NET and265 JS tests pass. Installed-service browser validates
initial/explicit Fit at1440/820/390/320, collapse and floating palettes,
download/reopen and unchanged cut totals, with no browser errors.
Details: document-lifecycle-2026-10-13.md and viewport-fit-2026-10-13.md.
Evidence /tmp/dxfer-fit-qa/live-evidence.json. Public auth/status200 and normal
Google302; published assembly hash matches Release output. Standalone Sync
integration remains disabled. Production PID1503 and Test PID637712 unchanged;
both public auth/status endpoints return200. No live drawings or records changed.
Rollback publishes retained at dev-publish-before-lifecycle-20261013 and
dev-publish-before-fit-20261013 under /home/ryan/dxfer-runtime.

Ryan approved `dxferdev.sync.harrisonmetals.com` in Slack delivery
`84250bfd09f300892bdf59f7`, replacing the originally requested hostname that
lacked public DNS. Scope is the full editor playground, not a claim that
historical geometry defects are resolved or that Dev Sync save-back is ready.

## Runtime

- `dxfer-dev.service` is enabled, running on loopback port 5175.
- Publish: `/home/ryan/dxfer-runtime/dev-publish`.
- Private state: `/home/ryan/dxfer-runtime/dev-state`, mode 0700, separate
  Data Protection keys and application discriminator, private temporary files.
- Unit template: `scripts/deploy/dxfer-dev.service`.
- Environment file is mode 0600. It contains the existing Google OAuth client
  configuration and a newly generated Dev-only API key, not committed.
- Production ASP.NET hosting mode avoids public developer error details.
  Full editing is explicitly enabled; Sync integration is explicitly disabled.
- No database, Sync storage or machine integration was configured. Systemd
  grants persistent write access only to the Dev state directory.
- Normal Google Workspace sign-in is retained. No automatic login enabled.
- Caddy has the new HTTPS host pointed at 5175. Existing upstreams unchanged.
  Prior config: `/etc/caddy/Caddyfile.before-dxfer-dev-20261013`.
- Existing LAN DNS for `sync.harrisonmetals.com` maps the new hostname to
  192.168.1.247. The old standalone DNS override was removed; backup is
  `/etc/dxfer-dev-dns.before-host-switch-20261013`.

## Public activation confirmed

Public resolver 1.1.1.1 confirms the approved hostname resolves to
`97.71.230.14` through existing wildcard coverage under `sync.harrisonmetals.com`.
No public DNS edits are required. Caddy obtained a trusted certificate at
2026-10-13T02:52:43Z. HTTPS `/auth/status` returns 200 via both LAN DNS and an
explicit connection to the public IP, without skipping certificate validation.
Host-switch backup: `/etc/caddy/Caddyfile.before-dxferdev-host-20261013`.

The Google OAuth callback URI is:
`https://dxferdev.sync.harrisonmetals.com/signin-google`.
The app's root and login endpoints both redirect to Google with this exact URI.
Ryan confirmed successful interactive sign-in in Slack delivery
`dfd418ab82ec7f9a005f19f0`. Public playground access is verified. This confirms
access only, not acceptance of all editing tools or Sync save-back.

## Isolation and layout contract

`Dxfer:EnableSyncIntegration` defaults true to preserve existing deployments.
When false, the server rejects root query launches before authentication,
the workbench does not parse input paths/download URLs/Sync tokens, the menu
omits Connect/Save to Sync, and the callback client refuses all save requests
before making a network call. Direct component navigation cannot restore a
callback context. Local import/download remains available.

No geometry or layout sizing changed. The existing menu alignment, wrapping,
target sizes, dock scrolling and responsive canvas contract remain intact;
only unavailable Sync commands are omitted in standalone mode.

## Verification

- 579 .NET tests and 256 JavaScript tests pass; Release publish succeeds.
- New regression proves a disabled callback makes zero HTTP requests; existing
  enabled callback contract tests still pass.
- Browser tests on the installed service verify unauthenticated status,
  Google challenge with the exact new HTTPS callback URI, and rejected Sync
  launch query without performing any live callback or artifact download.
- Header-key browser QA verifies the full palette, absence of Sync commands,
  fixture import (36.283185 inches / 2 contours), circle creation (38.112351
  inches / 3 contours), undo/redo and DXF download/reopen, unchanged 10 x 5 bounds.
- Desktop 1440, tablet 820, phone 390 and 320: nonblank canvas, dock controls
  reachable, no horizontal page overflow, no uncaught JavaScript errors.
  Screenshots inspected at desktop and 320px. Existing unobscured-fit limitations
  are still open, not fixed by deployment.
- Local QA script/evidence/screenshots: `/tmp/dxfer-workspace-20261012/qa-dev.cjs`
  and `evidence.json`, `desktop.png`, `tablet.png`, `phone.png`, `small-phone.png`.
  The script reads credentials locally without printing them.
- Caddy validation and dnsmasq syntax checks pass. Production/Test DXFer auth
  status endpoints return 200; service PIDs remain 1503 and 637712.
- No existing drawings or business records modified; no data repair required.

## Rollback

Stop and disable only `dxfer-dev.service`. Remove only the Dev host block from
the current Caddyfile, validate and reload Caddy. Do not remove the shared LAN
DNS entry for `sync.harrisonmetals.com`. Preserve any unrelated configuration
changes made since deployment. Production/Test require no rollback.

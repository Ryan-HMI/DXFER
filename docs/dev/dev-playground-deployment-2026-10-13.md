# DXFer Dev playground

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

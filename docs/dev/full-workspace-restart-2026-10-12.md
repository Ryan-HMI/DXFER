# Full editing workspace restart

Update: the full editor is hosted as a standalone Dev service on port 5175
at `https://dxferdev.sync.harrisonmetals.com`. Public DNS and trusted HTTPS are
verified; Google redirect registration and interactive login remain pending.
See [Dev deployment](dev-playground-deployment-2026-10-13.md).
Production/Test and live Sync launch destinations remain unchanged.

Ryan requested resuming the full DXFer editor on 2026-10-12. Work is isolated on
`feature/full-editing-workspace-20261012`, based on `f6d96fd`, retaining current
duplicate cleanup, inch-based length/contour analysis and the Sync callback.
Do not replace these with the older `dev` branch wholesale.

## First slice and layout contract

Restore the retained full palette behind server configuration
`Dxfer:EnableFullEditing`, false by default. Explicit development instances
expose sketch, modify, transform, pattern, constraints, view, cleanup and grain.
Sync-connected editing keeps its callback boundary and has no direct database
or storage access. Existing cleanup deployments remain unchanged.

Retain the canvas-first layout, existing dockable groups, icon dimensions,
tooltips, command readout and compact Sync bar. Full groups belong in the same
palette, not another toolbar over the canvas. Groups retain their existing
manual collapse controls. Groups must stay within the workspace and scroll
inside their panel without covering the readout. Preserve a usable canvas on
desktop, tablet and narrow screens. Future tools remain disabled.

Narrow-layout contract: side groups must fit the actual dock content width,
including collapsed headers. The existing 50vw dock cap remains, but group
minimum widths must not defeat it. Icon-grid columns wrap inside that width;
the collapse control remains reachable, with vertical panel scrolling only.

## Next stages

1. Verify local import, sketch editing, undo/redo, download/reopen and analysis
   consistency in the browser with the restored palette.
2. Reproduce and fix the logged trim, fillet/chamfer and constrained-drag bugs.
   Follow STARTUP.md's full entity matrix, not rectangle-only special cases.
3. Finish document lifecycle: new/discard, unsaved-change protection, standalone
   versus Sync ownership, and Dev callback round trips. A new blank document
   must never implicitly retain an old Sync token.
4. Complete selection/inspector/dimension editing and keyboard ergonomics using
   the existing commands, solver and CAD reader/writer, not a replacement engine.

Restoring the palette does not resolve all historical editor defects. Existing
bug-feature and user-test logs remain authoritative. Production/Test DXFer and
live Sync launch destinations are outside this initial development slice.

## Verified first checkpoint

- Web build succeeds; 578 .NET and 256 JavaScript tests pass.
- Default cleanup-only palette remains covered; full editing requires explicit
  server configuration, not a browser query parameter.
- Actual browser imported the inch rectangle/hole fixture: 36.283185 in and
  two contours. Adding a circle yielded 38.112351 in and three contours.
  Undo restored the original values; redo restored the edit. Download/reopen
  retained edited values and 10 x 5 bounds.
- Desktop 1440, tablet 820, phone 390 and 320 screenshots inspected. Canvas is
  nonblank; no page-level horizontal overflow or uncaught JavaScript exceptions.
  Group collapse/expand works at each size. Corrected the shared minimum-width
  mismatch that previously clipped narrow side-dock controls.
- Local evidence: `/tmp/dxfer-workspace-20261012/` contains screenshots,
  `qa.cjs`, `evidence.json`, and the exported test DXF. Header-key browser QA
  used Blazor's long-polling fallback; normal Google sign-in was not exercised.

Development preview is running at http://127.0.0.1:5036 with normal authentication
retained. The Google callback registration for that new local port is not
confirmed, so this is not yet advertised as a complete Ryan acceptance flow.
No Production/Test deployment, existing drawing mutation, or Sync callback
write was performed.

The palette switch and narrow-width bug are shared-code fixes, not data repairs.
No existing DXF records require repair for these changes. Historical geometry
bugs remain in the log; the viewport also still fits the full canvas behind
floating/docked palettes, which needs a separate unobscured-fit pass.

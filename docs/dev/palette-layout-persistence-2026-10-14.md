# Palette layout persistence

Base: 0eb7dc5. Follows the independent-palette contract in
`docs/design/visual-overhaul-20261014/IMPLEMENTATION.md`.

## Bounded layout contract

- Preserve current identities (View, Sketch, Modify, Transform, Pattern,
  Constraints, Cleanup, Grain; Edit in the restricted workspace), command
  membership, handlers, dock zones, order, and compaction semantics.
- Keep existing header alignment, button targets, widths, wrapping and dock
  scrolling. No consolidation, redesign, visibility menu or new commands.
- Persist only palette layout in origin-local `dxfer.paletteLayout.v1`, with
  schema version 1. Group names are the existing stable identities. Numeric
  dock/compaction values match the component enums; changing them needs migration.
- Save on completed drag and compaction toggle, not every pointer move or load.
  Restore only recognized identities and validated finite layout values. Missing,
  malformed or unsupported data uses defaults; unavailable storage never disables
  tools. Retain valid inactive entries on save so a restricted group set does not
  erase the full workspace's arrangement. Unknown groups never create controls.
- New groups receive existing defaults. Identities introduced by later parameter
  updates are checked against saved state once, even after an empty first render
  or while another load is pending. Startup edits win over delayed restoration.
- Clamp the displayed floating position to the visible palette host, accounting
  for the actual palette width and header height. Recheck after render/resize.
  Keep preferred coordinates intact in memory/storage so enlarging the viewport
  restores them. Other palettes are not moved, hidden or redocked.
- No drawing/entity data, tool inputs, selection, undo state, solver, native file
  metadata, Sync callbacks or deployment changes. All entity types are unaffected.

## Verification

Node tests exercise schema recovery, independent state round trips, inactive and
new groups, storage denial/quota failures, bounds and observer disposal. Existing
palette contract tests and the .NET suite remain applicable. Browser verification
uses only an isolated localhost process with Sync integration disabled.

Verified locally on 2026-10-14: 273 Node tests and 782 .NET tests passed; Web build
passed without warnings. The explicit Playwright regression
`tests/DXFER.Blazor.Tests/paletteLayout.browser.mjs` exercises real header drags,
compaction, reordering, reload, corrupt/unknown/missing entries and storage denial.
It checks two independent floating headers after both resize and reload at
1440x900, 1280x720, 800x600 and 390x844, then restoration of preferred coordinates
when enlarged. This verifies header recovery, not general mobile CAD usability.
Screenshots are in `/tmp/dxfer-palette-evidence/` (default, mixed, reopened,
clamped-1280, clamped-800, clamped-390, all-docks-reopened-1440).

Seven renderer-backed `WorkbenchPaletteLifecycleTests` also exercise empty and
late Groups, pending-load edits, groups arriving during interop refresh, actual
System.Text.Json serialization of private state DTOs, one-array interop arguments,
save timing, parameter refresh, failed reads, and disposal/disconnected circuits.
Current DrawingWorkbench supplies groups synchronously based on EnableFullEditing;
the component nevertheless supports delayed/changed parameters without losing
previously saved identities. Observer disposal and module disposal are best-effort
when the Blazor circuit has already disconnected.

Reproduce with an isolated full-editing localhost app (Sync disabled), then:

```sh
PLAYWRIGHT_MODULE=/path/to/playwright/index.mjs CHROMIUM_PATH=/path/to/chrome \
DXFER_LOCAL_URL=http://127.0.0.1:5187 DXFER_LOCAL_API_KEY=palette-local-check \
node tests/DXFER.Blazor.Tests/paletteLayout.browser.mjs
```

Limits: browser/origin-local preferences only, last completed write wins between
tabs, no cross-device synchronization. Existing docking hit thresholds and
overlap behavior are unchanged; this is not an automatic palette packing system.
No visibility state is invented because the component has no hide capability.

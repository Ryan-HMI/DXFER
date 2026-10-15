# Compact Palette Density

## Layout Contract

Ryan rejected the installed oversized labeled tiles. This supersedes the earlier
default-on label styling, not independent palettes or the functional QA evidence.
Desktop browser scope only. Default side/floating palettes are152px wide with
four columns of32px square buttons,20px glyphs,3px gaps and4px body padding.
Headers remain independently draggable with separate label/collapse controls,
compact20px header buttons and a readable12px title. No command is removed.
Collapsed palettes retain the same narrow width. Horizontal docks use32px buttons
and3px gaps. Active, disabled and keyboard-focus styles remain distinguishable.

Every tool retains its existing accessible name and native hover tooltip/hotkey.
Optional labels use a single column of compact rows in a232px palette, not the
old two-column56px tiles. Long labels wrap; labels never become the default.
Each side dock reserves only its actual compact/labeled width; top/bottom docks
must not reserve the obsolete282px. Canvas Fit uses measured palette obstacles.

Keep storage key and all valid dock/order/collapse/position fields. A density
revision marker resets pre-correction ShowLabels to false once. A deliberate
post-correction label toggle is saved with that marker and survives reload. Missing,
denied or corrupt storage remains nonfatal; new groups default to icons only.
No geometry, document history, machine integrations or business records change.

Verify fresh and old-label browser profiles, explicit label opt-in/reload, mixed
docks/float/collapse, actual control/palette bounds, hover metadata, accessible
names, active/disabled/focus states and command activation at1440/1188/1100px.
Inspect screenshots before Dev-only deployment; Production/Test stay unchanged.

## Cause and Verification

The shared default set ShowLabels=true and fixed side/floating widths at15.8rem,
while labeled styling used two columns of56px-high tiles and16px glyphs. Hiding labels alone would
have retained wasted width. Both the default and common sizing rules are corrected.
Density marker2 in the existing v1 envelope resets only old label state on restore;
no storage deletion or position reconstruction. Fresh defaults and new groups are
icon-only. Explicit later choices retain labels via the new marker.

- 1,070 .NET and366 JavaScript tests pass; Web build clean.
- Compact real-browser contract verifies32x32 buttons,20px glyphs,152px palettes,
  all accessible names/native tooltip titles, hover/focus/active/disabled styling,
  old-label migration, post-migration opt-in/reload and unchanged command inventory.
  Native OS tooltip popup rendering is left to Flux's interactive browser check;
  the automated check verifies title metadata and hover, not a captured OS popup.
- Sketch measured152x211.6px. Screenshots cover fresh1440x900, migrated floating/
  right-docked layouts, optional232px single-column labels,1188x761 and1100x705.
- Existing independent docking/order/collapse/reload/corrupt-storage contract
  passes. Header/inspector contract also passes, including invalid input, constrained
  refusal, reference edit/Exit/history/download/reopen and right docking.
- No drawings, history or business records changed; no record repair required.
  The intentional one-time browser preference migration is the only cleanup.

Browser driver: `tests/DXFER.Blazor.Tests/compactPalette.browser.mjs`, with
`DXFER_QA_URL`, `DXFER_QA_API_KEY`, optional `PLAYWRIGHT_MODULE`. Only isolated local
or approved DXFer Dev hosts accepted. Uses disposable browser contexts and blank
drawings. Independent visual acceptance remains pending until reported.

## Installed Dev

`0b1e0da39a7173840b9e61b78019efaebe53b2b2` deployed2026-10-15T14:35Z at
https://dxferdev.sync.harrisonmetals.com. Installed compact browser contract passes
with rendered tooltip `DXFER build 0b1e0da`. Four DLL hashes, both served scoped
stylesheets and palette JS match published bytes. Ten runtime settings retained;
health200/anonymous302. Production/Test process IDs unchanged. Normal sign-in and
disabled Sync callbacks retained. Rollback copy:
`/home/ryan/dxfer-runtime/dev-publish-before-0b1e0da`.

Flux QA request: compare fresh and old saved labeled layouts;32px buttons/20px
icons/152px palettes, native tooltips and keyboard names/focus, active/disabled
states; opt into labels for one palette and reload; mixed floating/side/top/bottom
docks and collapse/preferences remain independent. Check1188x761 and1100x705 with
the inspector open. No phone QA. Earlier functional acceptance is not Ryan's
visual acceptance, and this correction is not claimed independently accepted yet.

Installed header/inspector regression also passes on0b1e0da: invalid input remains
blocked, driving conflict adds no Undo, reference edit survives Exit/history/native
reopen, state/unit/read-only header is accurate and reduced-canvas docking works.

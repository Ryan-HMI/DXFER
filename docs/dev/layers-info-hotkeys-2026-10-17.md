# Layers, information and Hotkeys corrections

## Layout and interaction contract

- Retain Foundry colors, 48px header, existing palette docking and right-side
  information/layers width. No geometry or saved layout changes from styling.
- Layer action targets remain28px; glyphs are explicitly18px and cannot expand
  to the available container size. Names retain ellipsis and full-name tooltips.
- Layer-name editing uses Blazor input binding so delayed renders acknowledge
  the browser's current value. Rename remains explicit; typing adds no history.
- Information visibility is independent of active tool state. Exit cancels the
  tool only; a circled-i hide/show control remains usable while idle.
- Hotkeys is a centered600x560px dialog, clamped to the actual canvas bounds
  with24px margins. Header/search/footer stay put;
  the command list alone scrolls using the existing thin Foundry scrollbar.
  Search and existing binding persistence remain unchanged; Escape closes.
- Verify at1440x1000 and1100x705, including constrained viewport bounds, rapid
  overlapping rapid layer input events, idle panel reopening and keyboard controls.

No existing drawings or layer names will be automatically rewritten.

## Geometry corrections

Six core regressions reproduced two gaps before changes:

- Tangent with circle quadrant picks retained point-qualified references that
  circle-radius solving cannot use. The factory now maps only circle-like
  Tangent operands to whole curves, preserving line and point-constraint meaning.
- Concentric lacked origin support in selection, factory, validation, solving
  and preview. It now accepts circle/arc + immutable origin in either order.
  Core/preview share center-only behavior: no synthetic origin entity/radius;
  Fixed centers refuse, radius remains free, and other geometry stays unchanged.

The quadrant-pick failure is reproduced and corrected; this does not establish
that every intermittent Tangent report had that cause. Previously saved failed
point-qualified Tangent intent needs deliberate remove/reapply. No backfill.

## Verification

1622 .NET and797 JavaScript tests pass. Actual browser verifies rapid typing,
rename/history/native reopen,18px layer glyphs, Exit preserving info and idle
hide/show, Hotkeys centered on the field at1440x1000 and1100x705, search/scroll
and Escape, both circle quadrant-Tangent pick orders, and both circle-center /
origin Concentric orders with unchanged peers/radius, Undo/Redo/native reopen.
Core tests also cover arcs, Fixed refusal and free radius; preview regressions
cover immutable center and free radius. Browser radius reedit is also checked.
Screenshots inspected; no phone QA is requested for this desktop application.

## Installed Dev verification

Installed `3404e37a2badc75f216d154f97c7f19d7bf34d3f` on DXFer Dev at
2026-10-17T23:34Z. Public browser independently read `DXFER build3404e37`
and replayed the full browser scope above. Seven downloaded DXFs audit with
zero errors and zero fixes. Public screenshots at both desktop sizes inspected.

Installed assemblies match the published candidate; all ten tracked runtime
settings retained. Health200, anonymous root302. Normal Google sign-in and
disabled Sync callbacks retained; Production/Test process IDs unchanged.
Rollback publish retained. No application records or existing drawings changed.

Independent Flux replay remains pending. This fixes the demonstrated quadrant
pick failure, not every possible intermittent Tangent solve. Existing failed
point-qualified Tangent constraints still require deliberate removal/reapplication.

## Independent replay update

Flux Slack1792280766.677999 (retrieved through channel history) accepts visible
3404e37 rapid layer typing, icons, Exit-to-idle information and hide/reopen,
compact Hotkeys/search/scrolling at1180x757. Circle quadrant Tangent both orders
and circle/arc Concentric-to-origin pass, including free radius and retained arc
sweep, history/native reopen and18 clean exports. Exact1100px layout remains
owner-tested only. A separate empty-drawing download error after layer rename
is not yet minimized; nonempty exports passed. Rectangle coupled Tangent is
tracked separately and is not included in this independent acceptance.

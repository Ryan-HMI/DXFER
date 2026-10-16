# Palette Scrollbars and Defaults

## Layout Contract

- Cleanup and Grain start right-docked, in that order; all other defaults stay unchanged.
- Compact icon targets remain 32px; side docks reserve an additional 12px for the thin native scrollbar so it cannot clip controls or shift them when overflow appears.
- Existing moved/reordered/floating palettes, label choices and collapsed states persist.
- Legacy Cleanup/Grain positions matching the original left-side default coordinates/order migrate once. Old preferences did not record explicit placement intent, so an explicit choice identical to the original default cannot be distinguished.
- Scrollable workspace surfaces use the existing Foundry panel and border colors with thin native scrollbars. Overflow remains scrollable, including floating palettes and the inspector; headers and controls remain reachable at desktop heights.
- Drawing geometry, constraints, export and document history are unchanged.

## Verification

- 17 JS persistence tests and 10 component lifecycle tests pass; Web build has zero warnings/errors.
- Real-browser local checks pass at 1188x761 and 1100x705: right defaults, exact Foundry scrollbar colors, scroll overflow with full control width, legacy migration, floating/collapse/label retention and actual Grain drag back left surviving reload. Command inventory and document history remain unchanged.
- Screenshots: `/tmp/dxfer-scrollbar-evidence/`; `default-1100.png` visually inspected.
- Dev-only rollout; no manufacturing data repair is needed. A repeat manual-testing reminder is not required for this small layout correction.
- Installed Dev `3b9337b4b57cbbee9995f8015e5eb03ac69a5c1a`, independently identified from the rendered build tooltip by the same public-browser suite; all checks pass. Installed assemblies match, all ten runtime settings retained, health 200 and anonymous sign-in redirect 302; Production/Test PIDs unchanged. Existing compact-palette browser suite also passes locally.

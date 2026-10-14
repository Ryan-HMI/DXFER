# Asset validation — 2026-10-14

Completed:
- `python validate_assets.py`: PASS, 81 valid XML icon masters with consistent 24×24 viewBox, 1.6-unit stroke and inherited currentColor; 81 unique sprite IDs; both SVG compositions parse; no embedded image, script, external asset reference or active content in icon masters.
- Contact sheet rasterized using Sharp/librsvg and visually inspected: dark/light/selected samples plus 18 px downscale. Complex polygon variants and circle/arc construction variants require visible labels; 20 px is the recommended minimum for modeling tools.
- 1440×900 SVG workspace target rasterized and visually inspected: independent palette grouping, viewport dominance, visible selection slots, units, preview state, persistent Cancel/Apply, no overlap or clipping in this composition.
- All deliverable builders use relative output paths and create only original design assets. No private reference screenshots, private document text, credentials, customer files, session logs or internal notes belong in this package.

Not established:
- HTML previews were generated, but headless Chromium could not start in the current render sandbox (socket creation is unavailable). The inspected layout evidence is the SVG render, not a browser screenshot.
- No runtime code was edited or launched for this design handoff. No command wiring, pointer/keyboard behavior, solver behavior, narrow HTML layout or application screenshot is certified by these assets.
- The static HTML is an illustrative design sketch; the implementation must apply the responsive and behavioral acceptance matrix in IMPLEMENTATION.md.
- Existing repository command IDs and icon mappings must be inspected by the implementing owner before adoption. Asset names are not proof that corresponding capabilities are implemented.

Independent review corrections applied:
- Essential boundary token now #687B90 in dark theme (4.24:1 against panel, 4.47:1 against input) and #6B7D8E in light theme (3.53:1 panel, 4.02:1 input), measured with WCAG relative luminance.
- Light color-scheme, viewport background/depth/foreground/grid, geometry and accent drawing treatments are explicit. Keyboard focus inherits the light accent.
- Split master endpoint is inset to (20,2), avoiding clipping.
- State board Selected and Disabled samples now use real aria-pressed and disabled attributes.

## Independent-palette correction

The original single-shelf composition and conditional docking wording are superseded. The corrected contract requires existing palette identities, membership, independent movement, docking, collapse, visibility and saved layouts. The target SVG and HTML now show seven separate docked palette headers and one floating View palette, each with grip and dock/collapse controls. Collapsed examples are user-chosen illustrative states, never automatic behavior.

The corrected 1440×900 SVG was rasterized and visually inspected. All eight palette headers and controls are visible; the floating View example is separate from the dock zone and does not obscure the model. HTML structure has eight distinct palette IDs and matching separate control labels. This is a static mockup, not evidence of implemented drag behavior; HTML browser rendering and application interaction remain subject to the documented runtime acceptance gates.

Icons and tokens are unchanged by this correction. Independent-palette runtime QA must cover moving at least two floating palettes separately and preserving mixed state after reload and resize.

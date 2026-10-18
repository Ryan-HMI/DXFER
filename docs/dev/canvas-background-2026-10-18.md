# Canvas Background Preference

## Contract

- Replace the tinted field gradient with a solid neutral charcoal default #262626.
- Compact Background row after Layers in the existing right information panel:
  label, native color swatch, icon-only reset with tooltip and accessible name.
  Fixed 28px controls; flexible label, no panel width or canvas layout change.
- Preserve docking, wrapping, overflow and desktop interaction targets. No
  explanatory in-app prose or new floating panel.
- Persist a validated RGB hex preference per browser, independently of drawings,
  layer colors, geometry, Undo history and exports. Reset restores the default.
- Invalid or inaccessible storage falls back safely; blocked persistence must
  not prevent changing the current view. Annotations adapt to light backgrounds.
- Verify default/custom/reset/reload, light/dark annotation contrast, unchanged
  exported drawing/layer data and desktop screenshots.

## Verification

Implemented in `CanvasAppearance.razor` with `canvasAppearance.js` validation,
local storage (`dxfer.canvasBackground.v1`) and a scoped canvas redraw event.
The canvas remains transparent above its CSS background. Dimension lines/text,
selection/hover and uncolored auxiliary marks use dark ink for light fields;
explicit layer colors are retained. Shared nested CSS and dimension modules
have versioned URLs to avoid stale loaded styles.

- 1,712 core / 876 JavaScript tests and Web build pass.
- Actual local browser verifies neutral/custom background pixels, color/reset
  persistence, invalid stored preference fallback, unchanged complete native
  exports/layer colors, unchanged dirty/history state and light dimension ink.
- 1440x1000 and 1100x705 screenshots inspected. Surface regression passes at
  1440, 1188, 1100 and 1920 widths: original chrome, compact controls, inspector,
  transparent redraw and geometry retained. Updated its obsolete Exit-tool
  selector to the already-installed corner Cancel command.
- Three exports audit with zero errors/fixes. Evidence `/tmp/background-local`,
  `/tmp/background-surfaces` and corresponding logs. Public replay pending.

This is a view preference; no drawing repair or migration.

## Deployment

Installed `88ebab335fb3f280697a6d665a8391d11d4c3490` on DXFer Dev at
2026-10-18T22:34Z. Installed assembly hashes match, ten runtime settings
preserved, health200/anonymous302; normal sign-in and disabled Sync callbacks
retained. Production/Test runtimes unchanged.

Public browser replay passes on rendered88ebab3: background pixels, light
dimension ink, full native export/layer invariance, dirty/history preservation,
reload/reset/invalid preference fallback and desktop layout. Three public
exports audit0/0; screenshot inspected. Evidence `/tmp/background-dev` and
matching log. Local QA stopped. Independent visual acceptance pending; no
recurring trivial-UI testing reminder.

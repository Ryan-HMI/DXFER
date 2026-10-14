# DXFer precision workspace design pack

Status: implementation-ready design assets and target layout, **not a shipped UI or an application test result**. Original artwork; no external font, icon library, script, or network dependency is required. Additive handoff; preserve existing repository design documentation and runtime behavior.

## Start here

1. Open `workspace-target.svg` for the layout target and `icon-contact-sheet.svg` for all 81 monochrome icons in dark, small-size, light and selected treatments.
2. Read `IMPLEMENTATION.md` before wiring controls. `component-states.html` illustrates semantic control states. `workspace-target.html` is a static composition, not a working CAD editor.
3. Adopt `tokens.css` at the workspace root, then wire icons through the existing action registry. Names in this package are asset IDs, **not verified runtime command IDs**.
4. Use either `icons/*.svg` as inline SVG masters or `dxfer-icons.svg` as an inline symbol sprite. Do not install every representation into the application bundle.

## Integration

SVG masters use a 24 × 24 canvas, round 1.6-unit strokes and `currentColor`. Preferred display is 20–22 px in a 34 px desktop target; 44 px comfortable/touch targets. The 18 px contact-sheet sample is the lower bound, not the default. Keep CAD-specific labels visible. Use 20 px or larger for three-point circle, polygon variants and normal/symmetric constraints; their fine differences are not reliably recognizable without labels.

In Blazor/Razor, make one `DxIcon` component that emits a trusted symbol or an inline master. Mark decorative glyphs `aria-hidden="true"`, put the accessible name on the button, and keep a visible text span for modeling commands. Do not inject user-provided SVG or construct command handlers from asset names. Inline or symbol references allow `currentColor` to inherit; a plain `<img src="...svg">` does not inherit the parent text color.

Example structure (adapt to the actual component API):

```html
<button class="dx-action" aria-pressed="true" data-action="existing-action-id">
  <svg class="dx-icon" aria-hidden="true"><use href="#dxfer-fillet"/></svg>
  <span>Fillet</span>
</button>
```

Bind enabled/selected/pressed to actual application state. Preserve existing handlers, shortcuts, tooltip text, test identifiers, transaction boundaries and undo semantics. Do not replace geometry logic while applying this visual layer.

## Files

- `icons/`: 81 original monochrome masters
- `icons.json`: source body dictionary for component or atlas generation
- `dxfer-icons.svg`: sprite with `dxfer-` symbol prefixes
- `icon-contact-sheet.svg`: visual comparison at 24 and 18 px
- `tokens.css`: scoped dark/light/comfortable semantic tokens and component recipes
- `workspace-target.svg`: authoritative inspected 1440 × 900 design composition
- `workspace-target.html`: equivalent static HTML layout sketch
- `component-states.html`: state board and readable conflict example
- `ICON-DECISIONS.csv`: asset semantics and label requirements
- `IMPLEMENTATION.md`: behavior-preserving rollout, layout contract, QA
- `build_assets.py`, `build_preview.py`, `build_workspace_svg.py`: reproducible source generators
- `validate_assets.py`: structural asset validation
- `VALIDATION.md`: exact checks and remaining verification

No runtime dependencies are introduced by the generators. Optional PNG rendering in `build_assets.py` uses CairoSVG if already installed; the SVG deliverables do not require it. Run the three Python builders to regenerate text previews; run `python validate_assets.py` to validate the package.

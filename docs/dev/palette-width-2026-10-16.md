# Labeled palette width contract

- Expanded labeled palettes have independent preferred widths, 176-480 CSS pixels (232 default).
- Drag the canvas-facing edge: right edge for left/floating palettes, left edge for right palettes. Pointer capture keeps resizing active outside the handle. Arrow keys resize by 8px, Shift by 32px; Home restores the default.
- Each side dock reserves its widest visible palette plus padding/scrollbar space. Other palettes retain their own widths. Labels wrap; no horizontal text clipping or control overlap.
- Width is capped to 40% of the workspace on smaller desktop windows without overwriting the preferred width. Floating headers remain reachable. Icon-only and collapsed palettes keep existing compact dimensions.
- Persist width with existing per-group browser preferences, preserving docking, ordering, positions, collapse and label choices. Older preferences retain their existing layout and use the default width.
- Verify left/right/floating pointer resize, keyboard adjustment, reload, label/collapse toggles, viewport changes and unchanged geometry/history in an isolated browser and Dev. No Production/Test changes.

## Implementation and local verification

The fixed labeled-palette CSS width was the shared limitation. A per-group preferred width now drives both the palette and side-dock clearance. A captured pointer edge and keyboard separator update the existing preference store. Invalid saved widths fall back without discarding other preferences; smaller windows cap displayed widths without changing the saved preference.

- 1,533 .NET and 740 JavaScript tests pass; build has zero warnings/errors.
- Actual browser resize/reload tests pass for left, right and floating palettes; labels/collapse restore width; keyboard adjustment and Tab exit work; desktop viewport limits preserve the preference. Drawing and Undo history remain untouched.
- Existing default docking, custom preference migration, scrollbar and drag/reload browser regressions pass.
- Screenshots inspected under `/tmp/dxfer-palette-width-evidence`. No drawing/data repair or preference reset is required.

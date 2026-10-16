# Constraint hover lifetime

The shared canvas visibility filter previously removed glyphs from both drawing and hit-testing immediately when the pointer left their geometry. Retain departed groups for 2.5 seconds at full opacity, then fade for 0.5 seconds. Hovering a glyph restores full opacity; selected glyphs stay visible for deletion. The existing glyph positions, sizes, leaders and docking layout are unchanged.

Retained groups remain hit-testable until expiration. Timed redraws must finish the fade even with a stationary pointer. Show-all remains unchanged; visibility toggle, document replacement, interaction reset and disposal clear transient timers. Resolve retained group keys against the current document so deleted constraints cannot leave stale clickable glyphs. No geometry, dimension, constraint data, history or saved-document mutation is involved.

## Verification

- 746 JavaScript tests and 1,533 .NET tests pass; build has zero warnings/errors.
- Six focused regressions cover exact hold/fade deadlines, icon hover recovery, selected pinning, deletion pruning, show-all toggles and disposed-state refusal.
- Actual local browser pixels verify full opacity after leaving, intermediate fade and expiration without pointer movement. Returning to the icon after a pause keeps it visible, selecting pins it, and Delete removes only that constraint. Undo/Redo and native exports preserve the expected constraints, geometry and dimensions.
- Screenshots inspected at `/tmp/dxfer-constraint-hover-evidence`.
- Shared presentation/hit-testing cause fixed. No existing drawing or business record requires repair.

# Constraint icon parity

Toolbar symbols and canvas glyphs must use one set of 24-unit vector paths, including a plain equilateral Midpoint triangle and crosshairs with a filled heavier Coincident center dot. Preserve the compact toolbar dimensions, accessible names/tooltips, 16px canvas badges, selection/failure colors, leaders, hover timeout and hit targets. Scale paths inside badges without changing layout, wrapping, docking or solver behavior. Desktop visual verification required; DXFer Dev only.

The underlying issue is duplicate independent drawings in Razor and canvas. The shared C# catalog renders SVG paths in CadIcon and supplies those same paths to canvas initialization; canvas caches Path2D objects per instance. No geometry, constraints, stored files or preferences require repair.

## Verification

- 1,542 .NET and 752 JavaScript tests pass; build has zero warnings/errors and diff checks pass.
- Actual browser compares all ten toolbar SVG paths and filled details with Path2D strokes on the app canvas. Midpoint side lengths are equal; Coincident has the heavier filled center. Native entities, dimensions, constraints and Undo remain unchanged.
- Existing browser hover retention/fade, delayed glyph selection, deletion and Undo/Redo passes. The icon artwork check passes at 1x/2x with four color treatments and unchanged 32px targets/20px toolbar icons.
- Desktop screenshot inspected: `/tmp/dxfer-constraint-icon-evidence/installed-icons.png`.
- Archived 2026-10-14 handoff artwork is historical; the shared constraint catalog supersedes its constraint icons. Tests now cover the requested shapes and single rendering source instead of requiring the old designs.

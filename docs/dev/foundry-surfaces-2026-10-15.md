# Foundry surface integration

Reference: docs/design/visual-overhaul-20261014/workspace-target.svg and
tokens.css, confirmed by Flux in Slack at 1792076822. The inspected SVG is
authoritative where its center color (#172832) differs from the HTML sketch.

## Layout and behavior contract

- Preserve 32px icon-only targets, 20px glyphs, 3px gaps and 152px palettes.
  Optional labels, per-palette docking, collapse, order and persistence remain.
- Preserve the 48px document header, existing inspector width and footer tracks.
  No added controls, changed wrapping, new clipping, or geometry hit-test changes.
- Use reference chrome #0A0D12, panels #10141B, raised #151B24, hover #1A222D,
  cyan #66D9FF, text #F4F7FB and secondary #BBC5D1. Keep distinct warning/error
  states, keyboard focus, disabled explanations and native hover tooltips.
- Render the field's elliptical fade from #172832 to #0B1016 beneath the
  transparent drawing canvas. Remove the per-redraw solid fill which previously
  hid the field background. Geometry/export/persistence are not recolored data.
- Do not add a decorative grid that could be confused with a snapping/unit grid.
- Ryan's separate Split at Point request replaces the former scissors with two
  separated collinear segments, end caps and a central point/caret. Its command,
  tooltip, hit target and keyboard behavior are unchanged.
- Verify screenshots against the inspected reference on PC widths, with actual
  geometry and active operation feedback. This is not overall visual acceptance.

## Verification

Local implementation verified: 1,070 .NET tests and 367 JavaScript tests pass.
Browser checks cover exact reference surface colors, actual screenshot pixels
(center RGB 23/40/50, edge 11/16/22), transparent canvas after redraw, nonblank
geometry, unchanged native exported entities and no Undo entry from visual work.
Screenshots at 1440x900, 1188x761, 1100x705 and 1920x1080 inspected against the
authoritative SVG. Minimum nominal geometry/selection contrast at the brightest
field point is regression-tested at 4.5:1. No artificial grid or proposed fake
controls were imported from the illustration.

Existing compact-palette browser migration/persistence/state contract and the
header/inspector invalid input, constraint refusal, Exit, Undo/Redo and native
download/reopen contract also pass. Actual 20px Split glyph capture inspected.
Evidence: /tmp/dxfer-foundry-evidence, /tmp/dxfer-compact-evidence and the existing
inspector evidence directory. Native OS scaling has not been independently
verified by this pass. Dev deployment verification is pending.

No drawing or business-record repair is needed; this fixes the shared rendering
and surface integration, not just one stored layout. Overall visual acceptance
remains Ryan's; independent Dev checks will be handed to Flux after deployment.

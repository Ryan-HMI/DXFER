# Workspace Tool UI Acceptance

Manual checks for the workspace/tool UI cleanup slice:

- File > New File opens a blank canvas named `Untitled.dxf`; File > Open sample remains available as a separate sample command.
- The left tool dock remains icon-first, with tooltips and compact labeled collapsible groups.
- Pattern and Constraints start collapsed; View, Sketch, Modify, Transform, and Cleanup are available as compact dock sections.
- Cleanup contains rotate 90, bounds/point/vector orientation prep, grain `None/X/Y`, and a disabled `Remove duplicates` future command with a clear tooltip.
- Transform contains modal transform tools only: translate, rotate, scale, and mirror.

import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const workbenchMarkup = readFileSync(
  new URL("../../src/DXFER.Blazor/Components/DrawingWorkbench.razor", import.meta.url),
  "utf8");
const workbenchCode = readFileSync(
  new URL("../../src/DXFER.Blazor/Components/DrawingWorkbench.razor.cs", import.meta.url),
  "utf8");
const workbenchCss = readFileSync(
  new URL("../../src/DXFER.Blazor/Components/DrawingWorkbench.razor.css", import.meta.url),
  "utf8");
const paletteMarkup = readFileSync(
  new URL("../../src/DXFER.Blazor/Components/WorkbenchToolPalette.razor", import.meta.url),
  "utf8");
const paletteCss = readFileSync(
  new URL("../../src/DXFER.Blazor/Components/WorkbenchToolPalette.razor.css", import.meta.url),
  "utf8");

test("workbench hosts dockable tool groups over the canvas instead of a fixed tool column", () => {
  assert.doesNotMatch(workbenchMarkup, /Dock tools left/);
  assert.doesNotMatch(workbenchMarkup, /Dock tools right/);
  assert.doesNotMatch(workbenchMarkup, /Dock tools top/);
  assert.doesNotMatch(workbenchMarkup, /Dock tools bottom/);
  assert.doesNotMatch(workbenchMarkup, /Float tools/);
  assert.doesNotMatch(workbenchMarkup, /<aside class="dxfer-tool-panel"/);
  assert.match(workbenchMarkup, /dxfer-tool-palette-host/);
  assert.match(workbenchMarkup, /<WorkbenchToolPalette Groups="@ToolGroups"/);
});

test("tool groups own independent dock state and drag handles", () => {
  assert.match(paletteMarkup, /Dictionary<string, ToolGroupDockState>/);
  assert.match(paletteMarkup, /ToolGroupDockSide\.Left/);
  assert.match(paletteMarkup, /ToolGroupDockSide\.Right/);
  assert.match(paletteMarkup, /ToolGroupDockSide\.Top/);
  assert.match(paletteMarkup, /ToolGroupDockSide\.Bottom/);
  assert.match(paletteMarkup, /ToolGroupDockSide\.Floating/);
  assert.match(paletteMarkup, /BeginGroupDrag/);
  assert.match(paletteMarkup, /OnPalettePointerMove/);
  assert.match(paletteMarkup, /OnPalettePointerUp/);
  assert.match(paletteMarkup, /_previewDropSide/);
  assert.match(paletteMarkup, /BuildDockZoneClass/);
  assert.match(paletteMarkup, /BuildFloatingLayerClass/);
  assert.match(paletteMarkup, /dxfer-tool-group-grip/);
  assert.match(paletteMarkup, /dxfer-tool-group-compact-grip/);
  assert.match(paletteMarkup, /CycleGroupCompaction/);
  assert.match(paletteMarkup, /DockOrder/);
  assert.match(paletteMarkup, /OrderBy\(group => GetDockState\(group\)\.DockOrder\)/);
});

test("drag preview zones share the same dock side state used for final drop", () => {
  assert.match(paletteMarkup, /_previewDropSide = GetDropSide\(args\)/);
  assert.match(paletteMarkup, /state\.Side = _previewDropSide \?\? GetDropSide\(args\)/);
  assert.match(paletteMarkup, /state\.DockOrder = GetDockOrder\(state\.Side, args\)/);
  assert.match(paletteMarkup, /dxfer-tool-dock-zone-preview/);
  assert.match(paletteMarkup, /dxfer-tool-floating-layer-preview/);
  assert.match(paletteCss, /\.dxfer-workbench-tool-palette-dragging \.dxfer-tool-dock-zone-preview::before/);
});

test("drop model supports each requested side plus free floating", () => {
  assert.match(paletteMarkup, /RightDockThreshold = 1120/);
  assert.match(paletteMarkup, /BottomDockThreshold = 620/);
  assert.match(paletteMarkup, /return ToolGroupDockSide\.Left/);
  assert.match(paletteMarkup, /return ToolGroupDockSide\.Right/);
  assert.match(paletteMarkup, /return ToolGroupDockSide\.Top/);
  assert.match(paletteMarkup, /return ToolGroupDockSide\.Bottom/);
  assert.match(paletteMarkup, /return ToolGroupDockSide\.Floating/);
});

test("dragging keeps the grip under the pointer and uses a closed hand cursor", () => {
  assert.match(paletteMarkup, /DragGripOffset = 16/);
  assert.match(paletteMarkup, /PaletteViewportTop = 48/);
  assert.match(paletteMarkup, /FloatingX = Math\.Max\(0, args\.ClientX - DragGripOffset\)/);
  assert.match(paletteMarkup, /FloatingY = Math\.Max\(0, args\.ClientY - PaletteViewportTop - DragGripOffset\)/);
  assert.match(paletteMarkup, /FloatingY = Math\.Max\(0, args\.ClientY - PaletteViewportTop - _draggedGroup\.OffsetY\)/);
  assert.match(paletteMarkup, /new DraggedToolGroup\(group\.Name, DragGripOffset, DragGripOffset\)/);
  assert.match(paletteCss, /\.dxfer-workbench-tool-palette-dragging\s*\{[^}]*cursor:\s*grabbing;/s);
  assert.match(paletteCss, /\.dxfer-tool-group-dragging\s*\{[^}]*cursor:\s*grabbing;/s);
});

test("compact docks hide headings but keep draggable handles", () => {
  assert.match(paletteMarkup, /ShouldShowGroupHeader/);
  assert.match(paletteMarkup, /IsCompactDock/);
  assert.doesNotMatch(paletteMarkup, /<details/);
  assert.doesNotMatch(paletteMarkup, /<summary/);
  assert.match(paletteCss, /\.dxfer-tool-group-compact/);
  assert.match(paletteCss, /\.dxfer-tool-group-compact-grip/);
  assert.match(paletteCss, /grid-template-columns:\s*repeat\(var\(--dxfer-group-columns,\s*2\),\s*2\.15rem\)/);
});

test("top and bottom docked groups use horizontal compact strips", () => {
  assert.match(paletteMarkup, /dxfer-tool-group-horizontal/);
  assert.match(paletteCss, /\.dxfer-tool-dock-zone-top,[\s\S]*flex-wrap:\s*wrap;/);
  assert.match(paletteCss, /\.dxfer-tool-group-horizontal \.dxfer-command-strip\s*\{[^}]*display:\s*flex;[^}]*flex-wrap:\s*nowrap;/s);
});

test("redocking to a side restores expanded heading state", () => {
  assert.match(paletteMarkup, /state\.Side is ToolGroupDockSide\.Left or ToolGroupDockSide\.Right/);
  assert.match(paletteMarkup, /state\.Compaction = ToolGroupCompaction\.Expanded/);
});

test("floating groups are positioned and movable", () => {
  assert.match(paletteMarkup, /FloatingX/);
  assert.match(paletteMarkup, /FloatingY/);
  assert.match(paletteMarkup, /left:\{state\.FloatingX:0\}px;top:\{state\.FloatingY:0\}px/);
  assert.match(paletteCss, /\.dxfer-tool-group-floating\s*\{[^}]*position:\s*absolute;/s);
});

test("cleanup tools remain available as a dockable group", () => {
  assert.match(workbenchCode, /new WorkbenchToolGroup\("Cleanup", CleanupCommands, "Prep", InitiallyOpen: false\)/);
  assert.match(workbenchCode, /RemoveDuplicates/);
});

test("confirmed-good green styling is not reintroduced", () => {
  assert.doesNotMatch(paletteMarkup, /IsConfirmedWorking/);
  assert.doesNotMatch(paletteMarkup, /dxfer-icon-button-confirmed/);
  assert.doesNotMatch(paletteCss, /dxfer-icon-button-confirmed/);
});

test("tool palette host overlays the canvas without owning a layout column", () => {
  assert.match(workbenchCss, /\.dxfer-tool-palette-host/);
  assert.match(workbenchCss, /grid-template-columns:\s*minmax\(0,\s*1fr\)\s+var\(--dxfer-inspector-width,\s*280px\)\s*!important/);
  assert.match(workbenchCss, /\.dxfer-canvas-panel\s*\{[^}]*grid-column:\s*1\s*!important;/s);
  assert.match(workbenchCss, /\.dxfer-inspector\s*\{[^}]*grid-column:\s*2\s*!important;/s);
  assert.match(workbenchCss, /\.dxfer-command-bar\s*\{[^}]*grid-row:\s*2\s*!important;/s);
});

test("top and bottom dock zones reserve side dock width instead of overlapping side stacks", () => {
  assert.match(paletteCss, /--dxfer-side-dock-width:\s*14\.65rem/);
  assert.match(paletteCss, /--dxfer-horizontal-dock-preview-height:\s*4\.35rem/);
  assert.match(paletteCss, /\.dxfer-tool-dock-zone-top,[\s\S]*left:\s*var\(--dxfer-side-dock-width\);[\s\S]*right:\s*var\(--dxfer-side-dock-width\);/);
  assert.match(paletteCss, /\.dxfer-tool-dock-zone-top,[\s\S]*min-height:\s*var\(--dxfer-horizontal-dock-preview-height\);/);
  assert.match(paletteCss, /\.dxfer-workbench-tool-palette-dragging \.dxfer-tool-dock-zone-top::before\s*\{[^}]*inset:\s*1px 1px auto;/s);
  assert.match(paletteCss, /\.dxfer-workbench-tool-palette-dragging \.dxfer-tool-dock-zone-bottom::before\s*\{[^}]*inset:\s*auto 1px 1px;/s);
});

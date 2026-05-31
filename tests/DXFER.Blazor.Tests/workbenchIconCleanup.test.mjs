import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import test from "node:test";

const iconMarkup = readFileSync(
  new URL("../../src/DXFER.Blazor/Components/CadIcon.razor", import.meta.url),
  "utf8");
const iconButtonMarkup = readFileSync(
  new URL("../../src/DXFER.Blazor/Components/CadIconButton.razor", import.meta.url),
  "utf8");
const iconCss = readFileSync(
  new URL("../../src/DXFER.Blazor/Components/CadIcon.razor.css", import.meta.url),
  "utf8");
const iconButtonCss = readFileSync(
  new URL("../../src/DXFER.Blazor/Components/CadIconButton.razor.css", import.meta.url),
  "utf8");
const paletteMarkup = readFileSync(
  new URL("../../src/DXFER.Blazor/Components/WorkbenchToolPalette.razor", import.meta.url),
  "utf8");
const paletteCss = readFileSync(
  new URL("../../src/DXFER.Blazor/Components/WorkbenchToolPalette.razor.css", import.meta.url),
  "utf8");

function iconCase(name) {
  const start = iconMarkup.indexOf(`case CadIconName.${name}:`);
  assert.notEqual(start, -1, `${name} case exists`);
  const end = iconMarkup.indexOf("break;", start);
  assert.ok(end > start, `${name} case has a break`);
  return iconMarkup.slice(start, end);
}

test("icon buttons keep hotkey metadata but do not render visible kbd labels", () => {
  assert.match(iconButtonMarkup, /public string\? Hotkey/);
  assert.match(iconButtonMarkup, /title="@TitleText"/);
  assert.match(iconButtonMarkup, /aria-label="@Label"/);
  assert.doesNotMatch(iconButtonMarkup, /<kbd\b/);
  assert.doesNotMatch(iconButtonCss, /dxfer-icon-button-hotkey/);
});

test("toolbar icons are enlarged inside the fixed-size button without changing button dimensions", () => {
  assert.match(iconButtonCss, /width:\s*2\.15rem;/);
  assert.match(iconButtonCss, /height:\s*2\.15rem;/);
  assert.match(iconCss, /width:\s*1\.9rem;/);
  assert.match(iconCss, /height:\s*1\.9rem;/);
});

test("requested cleanup icons use the specified SVG motifs", () => {
  assert.match(iconCase("AutoCleanup"), /dxfer-auto-cleanup-icon/);
  assert.match(iconCase("AutoCleanup"), /M4 17 9 6l5 11/);

  assert.match(iconCase("Construction"), /<path d="M5 19 19 5" stroke-dasharray="2 3" \/>/);
  assert.doesNotMatch(iconCase("Construction"), /M5 12h14|M12 5v14/);

  assert.match(iconCase("Split"), /dxfer-scissors-icon/);
  assert.match(iconCase("Split"), /<circle cx="8" cy="8" r="2"/);
  assert.match(iconCase("Split"), /<circle cx="8" cy="16" r="2"/);

  assert.match(iconCase("Scale"), /<rect x="4" y="15" width="5" height="5" fill="currentColor" \/>/);
  assert.match(iconCase("Scale"), /<rect x="14" y="4" width="6" height="6" \/>/);
  assert.match(iconCase("Scale"), /m14 10 6-6/);

  assert.match(iconCase("LinearPattern"), /<rect x="5" y="5" width="5" height="5" fill="currentColor" \/>/);
  assert.match(iconCase("LinearPattern"), /<rect x="14" y="14" width="5" height="5" \/>/);

  assert.match(iconCase("CircularPattern"), /dxfer-circular-pattern-icon/);
  assert.match(iconCase("CircularPattern"), /fill="currentColor"/);

  assert.match(iconCase("Text"), /dxfer-text-a-icon/);
  assert.match(iconCase("Text"), /M6 20 12 4l6 16/);
  assert.match(iconCase("Text"), /M8\.6 14h6\.8/);
});

test("rotate icons use standard circular arrow motifs", () => {
  assert.match(iconCase("Rotate"), /dxfer-rotate-free-icon/);
  assert.match(iconCase("Rotate"), /M5 12a7 7 0 0 1 14 0/);
  assert.match(iconCase("Rotate"), /fill="currentColor" stroke="none" d="M5 12 3\.55 9\.15 6\.45 9\.15Z"/);
  assert.match(iconCase("Rotate"), /fill="currentColor" stroke="none" d="M19 12 17\.55 9\.15 20\.45 9\.15Z"/);
  assert.equal(iconCase("Rotate").match(/fill="currentColor" stroke="none"/g)?.length, 2);
  assert.doesNotMatch(iconCase("Rotate"), /M5 12l2\.4|M19 12l-2\.4|m18 5 1 5-5-1/);
  assert.match(iconCase("Rotate90Clockwise"), /dxfer-rotate-90-cw-icon/);
  assert.match(iconCase("Rotate90Clockwise"), /<path fill="currentColor" stroke="none"/);
  assert.match(iconCase("Rotate90Clockwise"), /M17\.65 6\.35/);
  assert.match(iconCase("Rotate90Clockwise"), /L13 11h7V4l-2\.35 2\.35z/);
  assert.equal(iconCase("Rotate90Clockwise").match(/<path /g)?.length, 1);
  assert.doesNotMatch(iconCase("Rotate90Clockwise"), /<circle|<polygon|16\.5|M15 13|v4h|m18 6/);
  assert.match(iconCase("Rotate90CounterClockwise"), /dxfer-rotate-90-ccw-icon/);
  assert.match(iconCase("Rotate90CounterClockwise"), /<path fill="currentColor" stroke="none"/);
  assert.match(iconCase("Rotate90CounterClockwise"), /M6\.35 6\.35/);
  assert.match(iconCase("Rotate90CounterClockwise"), /L11 11H4V4l2\.35 2\.35z/);
  assert.equal(iconCase("Rotate90CounterClockwise").match(/<path /g)?.length, 1);
  assert.doesNotMatch(iconCase("Rotate90CounterClockwise"), /<circle|<polygon|16\.5|M15 13|v4h|m6 6/);
});

test("grain marking icons use G with axis subscripts", () => {
  for (const name of ["GrainX", "GrainY", "GrainVector"]) {
    assert.match(iconCase(name), /dxfer-grain-icon/);
    assert.match(iconCase(name), />G</);
  }

  assert.match(iconCase("GrainX"), />X</);
  assert.match(iconCase("GrainY"), />Y</);
  assert.match(iconCase("GrainVector"), />V</);
});

test("grain none icon uses a clear symbol instead of a grain flag", () => {
  assert.match(iconCase("GrainNone"), /dxfer-grain-none-icon/);
  assert.match(iconCase("GrainNone"), /M6 6l12 12/);
  assert.doesNotMatch(iconCase("GrainNone"), />G</);
  assert.doesNotMatch(iconCase("GrainNone"), />0</);
});

test("confirmed-good green icon styling remains absent from toolbar files", () => {
  assert.doesNotMatch(iconButtonMarkup, /IsConfirmedWorking|dxfer-icon-button-confirmed/);
  assert.doesNotMatch(iconButtonCss, /green|#22c55e|#16a34a|dxfer-icon-button-confirmed/i);
  assert.doesNotMatch(paletteMarkup, /IsConfirmedWorking|dxfer-icon-button-confirmed/);
  assert.doesNotMatch(paletteCss, /green|#22c55e|#16a34a|dxfer-icon-button-confirmed/i);
});

test("side dock compaction uses explicit controls that do not start group drag", () => {
  assert.doesNotMatch(paletteMarkup, /dxfer-tool-section-count|Commands\.Count/);
  assert.doesNotMatch(paletteCss, /dxfer-tool-section-count/);
  assert.match(paletteMarkup, /dxfer-tool-group-collapse-toggle/);
  assert.match(paletteMarkup, /CanToggleSideCompaction\(GetDockState\(group\)\)/);
  assert.match(paletteMarkup, /state\.Side is ToolGroupDockSide\.Left or ToolGroupDockSide\.Right/);
  assert.match(paletteMarkup, /IsSideCollapsed/);
  assert.match(paletteMarkup, /dxfer-tool-group-side-collapsed/);
  assert.match(paletteMarkup, /CadIconName\.ChevronUp/);
  assert.match(paletteMarkup, /CadIconName\.ChevronDown/);
  assert.match(paletteMarkup, /@onpointerdown:stopPropagation="true"/);
  assert.match(paletteMarkup, /@onclick:stopPropagation="true"/);
  assert.match(paletteMarkup, /ToggleSideCompaction\(group\)/);
  assert.match(paletteMarkup, /state\.Compaction is ToolGroupCompaction\.Expanded[\s\S]*ToolGroupCompaction\.DoubleColumn[\s\S]*ToolGroupCompaction\.Expanded/);
  assert.doesNotMatch(paletteMarkup, /@ondblclick="@\(\(\) => CycleGroupCompaction\(group\)\)"/);
  assert.doesNotMatch(paletteMarkup, /BuildCompactionIcon[\s\S]*CollapseLeft|BuildCompactionIcon[\s\S]*CollapseRight/);
  assert.match(paletteCss, /\.dxfer-tool-group-collapse-toggle/);
  assert.match(paletteCss, /\.dxfer-tool-group-side-collapsed \.dxfer-command-strip\s*\{[^}]*display:\s*none;/s);
  assert.doesNotMatch(paletteCss, /dxfer-tool-group-compact-rail|dxfer-tool-group-compact-toggle/);
});

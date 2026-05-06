import assert from "node:assert/strict";
import test from "node:test";
import {
  getToolHotkeyEventKey,
  normalizeRecordedBaseKey
} from "../../src/DXFER.Blazor/wwwroot/workbenchHotkeys.js";

test("normalizes browser event keys beyond letters", () => {
  assert.equal(getToolHotkeyEventKey({ key: "a" }), "A");
  assert.equal(getToolHotkeyEventKey({ key: "F2" }), "F2");
  assert.equal(getToolHotkeyEventKey({ key: "ArrowLeft" }), "ArrowLeft");
  assert.equal(getToolHotkeyEventKey({ key: "Delete" }), "Delete");
  assert.equal(getToolHotkeyEventKey({ key: "/" }), "Slash");
  assert.equal(getToolHotkeyEventKey({ key: " " }), "Space");
  assert.equal(getToolHotkeyEventKey({ key: "+" }), "Plus");
});

test("ignores modifier-only and composition keys", () => {
  assert.equal(getToolHotkeyEventKey({ key: "Shift" }), null);
  assert.equal(getToolHotkeyEventKey({ key: "Control" }), null);
  assert.equal(getToolHotkeyEventKey({ key: "Alt" }), null);
  assert.equal(getToolHotkeyEventKey({ key: "Meta" }), null);
  assert.equal(getToolHotkeyEventKey({ key: "Dead" }), null);
  assert.equal(getToolHotkeyEventKey({ key: "Unidentified" }), null);
});

test("recorder uses the same broad key normalization", () => {
  assert.equal(normalizeRecordedBaseKey("z"), "Z");
  assert.equal(normalizeRecordedBaseKey("F12"), "F12");
  assert.equal(normalizeRecordedBaseKey("ArrowDown"), "ArrowDown");
  assert.equal(normalizeRecordedBaseKey("."), "Period");
});

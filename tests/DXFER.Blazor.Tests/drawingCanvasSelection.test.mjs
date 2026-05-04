import assert from "node:assert/strict";
import test from "node:test";
import {
  applyDirectSelectionClick,
  syncActiveSelectionWithSelectedKeys
} from "../../src/DXFER.Blazor/wwwroot/drawingCanvas.js";

test("direct click selects an unselected target and makes it active", () => {
  const state = {
    selectedKeys: new Set(),
    activeSelectionKey: null
  };

  const changed = applyDirectSelectionClick(state, "line-a");

  assert.equal(changed, true);
  assert.deepEqual(Array.from(state.selectedKeys), ["line-a"]);
  assert.equal(state.activeSelectionKey, "line-a");
});

test("direct click on selected inactive target makes it active", () => {
  const state = {
    selectedKeys: new Set(["line-a", "line-b"]),
    activeSelectionKey: "line-a"
  };

  const changed = applyDirectSelectionClick(state, "line-b");

  assert.equal(changed, true);
  assert.deepEqual(Array.from(state.selectedKeys), ["line-a", "line-b"]);
  assert.equal(state.activeSelectionKey, "line-b");
});

test("direct click on active target deselects it and clears active", () => {
  const state = {
    selectedKeys: new Set(["line-a", "line-b"]),
    activeSelectionKey: "line-b"
  };

  const changed = applyDirectSelectionClick(state, "line-b");

  assert.equal(changed, true);
  assert.deepEqual(Array.from(state.selectedKeys), ["line-a"]);
  assert.equal(state.activeSelectionKey, null);
});

test("active selection clears when box deselect removes it", () => {
  const state = {
    selectedKeys: new Set(["line-a"]),
    activeSelectionKey: "line-b"
  };

  const changed = syncActiveSelectionWithSelectedKeys(state);

  assert.equal(changed, true);
  assert.equal(state.activeSelectionKey, null);
});

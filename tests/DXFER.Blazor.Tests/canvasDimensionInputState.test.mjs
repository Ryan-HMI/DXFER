import assert from "node:assert/strict";
import test from "node:test";

import {
  getDefaultActiveDimensionKey,
  getDimensionKeys,
  getNextDimensionKey,
  getPendingPersistentDimensionEditId,
  getVisibleDimensionDescriptors,
  resolveActiveDimensionKey
} from "../../src/DXFER.Blazor/wwwroot/canvas/dimensionInputState.js";

test("dimension input active key defaults and cycles through visible keys", () => {
  const dimensions = [{ key: "width" }, { key: "height" }, { key: "" }, null];

  assert.deepEqual(getDimensionKeys(dimensions), ["width", "height"]);
  assert.equal(getDefaultActiveDimensionKey(dimensions, null), "width");
  assert.equal(getDefaultActiveDimensionKey(dimensions, "height"), "height");
  assert.equal(getDefaultActiveDimensionKey(dimensions, "radius"), "width");
  assert.equal(getNextDimensionKey(dimensions, "width", false), "height");
  assert.equal(getNextDimensionKey(dimensions, "height", false), "width");
  assert.equal(getNextDimensionKey(dimensions, "width", true), "height");
  assert.equal(getNextDimensionKey([], "width", false), null);
});

test("dimension input active key resolution keeps pending focus until visible", () => {
  assert.deepEqual(resolveActiveDimensionKey([], null, "depth"), {
    activeKey: null,
    pendingKey: "depth"
  });

  assert.deepEqual(resolveActiveDimensionKey([{ key: "length" }, { key: "depth" }], null, "depth"), {
    activeKey: "depth",
    pendingKey: null
  });

  assert.deepEqual(resolveActiveDimensionKey([{ key: "length" }, { key: "depth" }], "length", null, "depth"), {
    activeKey: "depth",
    pendingKey: null
  });
});

test("visible dimension descriptors preserve drawn order before map order", () => {
  const state = {
    visibleDimensionKeys: ["length", "depth", "missing"],
    dimensionInputs: new Map([
      ["depth", {}],
      ["length", {}],
      ["width", {}]
    ])
  };

  assert.deepEqual(getVisibleDimensionDescriptors(state), [{ key: "length" }, { key: "depth" }]);
  assert.deepEqual(getVisibleDimensionDescriptors({
    visibleDimensionKeys: [],
    dimensionInputs: state.dimensionInputs
  }), [{ key: "depth" }, { key: "length" }, { key: "width" }]);
});

test("pending persistent dimension edit id returns the first newly added id", () => {
  assert.equal(getPendingPersistentDimensionEditId(
    [
      { id: "existing-dim" },
      { id: "new-dim" },
      { id: "newer-dim" }
    ],
    new Set(["existing-dim"])), "new-dim");

  assert.equal(getPendingPersistentDimensionEditId([{ id: "existing-dim" }], new Set(["existing-dim"])), null);
  assert.equal(getPendingPersistentDimensionEditId(null, new Set(["existing-dim"])), null);
});

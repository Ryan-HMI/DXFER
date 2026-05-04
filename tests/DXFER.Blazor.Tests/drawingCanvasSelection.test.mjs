import assert from "node:assert/strict";
import test from "node:test";
import {
  applyDraftDimensionValue,
  applyLockedDraftDimensions,
  applyDirectSelectionClick,
  applyPolarSnapIfRequested,
  getDefaultActiveDimensionKey,
  getNextDimensionKey,
  isDynamicTargetCurrentToPointer,
  isPanPointerDownForTool,
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

test("draft line dimension preserves direction when length is typed", () => {
  const state = {
    activeTool: "line",
    toolDraft: {
      points: [{ x: 0, y: 0 }],
      previewPoint: { x: 3, y: 4 }
    }
  };

  const changed = applyDraftDimensionValue(state, "length", 10);

  assert.equal(changed, true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 6, y: 8 });
});

test("draft midpoint line dimension uses full mirrored line length", () => {
  const state = {
    activeTool: "midpointline",
    toolDraft: {
      points: [{ x: 10, y: 10 }],
      previewPoint: { x: 13, y: 14 }
    }
  };

  const changed = applyDraftDimensionValue(state, "length", 10);

  assert.equal(changed, true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 13, y: 14 });
});

test("draft circle dimension updates radius along current direction", () => {
  const state = {
    activeTool: "centercircle",
    toolDraft: {
      points: [{ x: 5, y: 5 }],
      previewPoint: { x: 8, y: 9 }
    }
  };

  const changed = applyDraftDimensionValue(state, "radius", 10);

  assert.equal(changed, true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 11, y: 13 });
});

test("draft rectangle dimensions preserve dragged quadrant", () => {
  const state = {
    activeTool: "twopointrectangle",
    toolDraft: {
      points: [{ x: 10, y: 10 }],
      previewPoint: { x: 8, y: 13 }
    }
  };

  assert.equal(applyDraftDimensionValue(state, "width", 6), true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 4, y: 13 });

  assert.equal(applyDraftDimensionValue(state, "height", 4), true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 4, y: 14 });
});

test("locked draft dimensions reapply after cursor movement", () => {
  const state = {
    activeTool: "line",
    toolDraft: {
      points: [{ x: 0, y: 0 }],
      previewPoint: { x: 0, y: 2 },
      dimensionValues: { length: 10 }
    }
  };

  const changed = applyLockedDraftDimensions(state);

  assert.equal(changed, true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 0, y: 10 });
});

test("shift is polar snap instead of pan while sketch tools are active", () => {
  assert.equal(isPanPointerDownForTool({ button: 0, shiftKey: true }, "line"), false);
  assert.equal(isPanPointerDownForTool({ button: 0, shiftKey: true }, "select"), false);
  assert.equal(isPanPointerDownForTool({ button: 1, shiftKey: false }, "line"), true);
  assert.equal(isPanPointerDownForTool({ button: 2, shiftKey: false }, "line"), false);
});

test("dynamic inferred target is hidden when cursor moves away from it", () => {
  const state = {
    pointerScreenPoint: { x: 100, y: 100 },
    view: {
      scale: 1,
      offsetX: 0,
      offsetY: 0
    }
  };

  const target = {
    dynamic: true,
    point: { x: 0, y: 0 }
  };

  assert.equal(isDynamicTargetCurrentToPointer(state, target), false);
  state.pointerScreenPoint = { x: 3, y: -4 };
  assert.equal(isDynamicTargetCurrentToPointer(state, target), true);
  assert.equal(isDynamicTargetCurrentToPointer(state, { dynamic: false, point: { x: 0, y: 0 } }), true);
});

test("line polar snap is ortho only near an ortho axis by default", () => {
  const state = {
    polarSnapIncrementDegrees: 15,
    view: {
      scale: 1
    },
    toolDraft: {
      points: [{ x: 0, y: 0 }]
    }
  };
  const nearHorizontal = { x: 10, y: 3 };
  const nearRadius = Math.hypot(nearHorizontal.x, nearHorizontal.y);

  const ortho = applyPolarSnapIfRequested(state, nearHorizontal, "line", { shiftKey: false });
  assertApproxEqual(ortho.x, nearRadius);
  assertApproxEqual(ortho.y, 0);

  const free = applyPolarSnapIfRequested(state, { x: 10, y: 8 }, "line", { shiftKey: false });
  assert.deepEqual(free, { x: 10, y: 8 });

  const fine = applyPolarSnapIfRequested(state, nearHorizontal, "line", { shiftKey: true });
  assertApproxEqual(fine.x, Math.cos(Math.PI / 12) * nearRadius);
  assertApproxEqual(fine.y, Math.sin(Math.PI / 12) * nearRadius);
});

test("dimension active key defaults and cycles through all visible dimensions", () => {
  const dimensions = [{ key: "width" }, { key: "height" }];

  assert.equal(getDefaultActiveDimensionKey(dimensions, null), "width");
  assert.equal(getDefaultActiveDimensionKey(dimensions, "height"), "height");
  assert.equal(getDefaultActiveDimensionKey(dimensions, "radius"), "width");
  assert.equal(getNextDimensionKey(dimensions, "width", false), "height");
  assert.equal(getNextDimensionKey(dimensions, "height", false), "width");
  assert.equal(getNextDimensionKey(dimensions, "width", true), "height");
});

function assertApproxEqual(actual, expected, tolerance = 0.000001) {
  assert.ok(Math.abs(actual - expected) <= tolerance, `expected ${actual} to equal ${expected}`);
}

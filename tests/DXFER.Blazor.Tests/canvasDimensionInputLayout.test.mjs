import assert from "node:assert/strict";
import test from "node:test";

import {
  getClampedDimensionInputScreenPoint
} from "../../src/DXFER.Blazor/wwwroot/canvas/dimensionInputLayout.js";

test("dimension input layout clamps screen points inside canvas bounds", () => {
  const size = { width: 300, height: 200 };

  assert.deepEqual(getClampedDimensionInputScreenPoint({ x: 150, y: 100 }, size, 50, 20), { x: 150, y: 100 });
  assert.deepEqual(getClampedDimensionInputScreenPoint({ x: -40, y: 250 }, size, 50, 20), { x: 50, y: 180 });
  assert.deepEqual(getClampedDimensionInputScreenPoint({ x: 360, y: -25 }, size, 50, 20), { x: 250, y: 20 });
});

test("dimension input layout keeps cramped canvases inside the minimum margins", () => {
  const size = { width: 40, height: 10 };

  assert.deepEqual(getClampedDimensionInputScreenPoint({ x: 500, y: 500 }, size, 52, 18), { x: 52, y: 18 });
});

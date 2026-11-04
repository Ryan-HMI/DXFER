import test from 'node:test';
import assert from 'node:assert/strict';
import {
  applyDraftDimensionValue,
  applyLockedDraftDimensions,
  getSketchToolDimensionLocks
} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

function draft(center, signX = 1, signY = 1) {
  return {
    activeTool: 'centerrectangle',
    toolDraft: {
      points: [center],
      previewPoint: { x: center.x + signX * 3, y: center.y + signY * 2 }
    }
  };
}

for (const center of [{ x: 0, y: 0 }, { x: 7, y: -9 }]) {
  for (const signX of [-1, 1]) {
    for (const signY of [-1, 1]) {
      for (const order of [['width', 'height'], ['height', 'width']]) {
        test(`center rectangle retains full dimensions at ${JSON.stringify(center)}, quadrant ${signX}/${signY}, ${order}`, () => {
          const state = draft(center, signX, signY);
          const sizes = { width: 15.25, height: 10.5 };
          for (const key of order) {
            assert.equal(applyDraftDimensionValue(state, key, sizes[key]), true);
          }
          assert.deepEqual(state.toolDraft.points, [center]);
          assert.deepEqual(state.toolDraft.previewPoint, {
            x: center.x + signX * sizes.width / 2,
            y: center.y + signY * sizes.height / 2
          });
          assert.deepEqual(getSketchToolDimensionLocks(state.toolDraft), {
            keys: order, values: order.map(key => sizes[key])
          });
          state.toolDraft.previewPoint = { x: center.x - signX * 99, y: center.y - signY * 99 };
          assert.equal(applyLockedDraftDimensions(state), true);
          assert.deepEqual(state.toolDraft.previewPoint, {
            x: center.x - signX * sizes.width / 2,
            y: center.y - signY * sizes.height / 2
          });
        });
      }
    }
  }
}

test('one center-rectangle dimension leaves the other pointer extent unchanged', () => {
  const state = draft({ x: 4, y: 5 }, -1, 1);
  assert.equal(applyDraftDimensionValue(state, 'width', 12), true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: -2, y: 7 });
  assert.deepEqual(getSketchToolDimensionLocks(state.toolDraft), { keys: ['width'], values: [12] });
});

test('invalid center-rectangle dimensions do not mutate the draft', () => {
  const state = draft({ x: 4, y: 5 });
  const before = structuredClone(state);
  for (const value of [0, -1, NaN, Infinity, 'invalid']) {
    assert.equal(applyDraftDimensionValue(state, 'width', value), false);
    assert.deepEqual(state, before);
  }
});

import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
const p = (x, y) => ({ x, y });
const fixture = (constraints = []) => ({ entities: [{ id: 'poly', kind: 'polyline', points: [p(0, 0), p(6, 0), p(4, 3), p(1, 4), p(0, 0)] }], dimensions: [], constraints });
for (const vertex of [0, 4]) test(`partially dimensioned closing vertex ${vertex} retains free vertical movement`, () => {
  const original = fixture([{ id: 'fix', kind: 'fix', referenceKeys: ['poly|segment|0:end'], state: 'satisfied' }]);
  original.dimensions = [{ id: 'width', kind: 'horizontalDistance', referenceKeys: ['poly|segment|0:start', 'poly|segment|0:end'], value: 6, isDriving: true, anchor: p(3, -1) }];
  const next = applyGeometryDragPreview(original, `poly|point|vertex-${vertex}|0|0`, p(0, 0), p(-1, 1));
  assert.ok(next);
  assert.deepEqual(next.entities[0].points, [p(0, 1), p(6, 0), p(4, 3), p(1, 4), p(0, 1)]);
  assert.equal(next.dimensions[0].value, 6);
});
for (const index of [0, 4]) test(`closed vertex ${index} aliases move together and respect opposite Fix`, () => {
  const key = `poly|point|vertex-${index}|0|0`;
  const next = applyGeometryDragPreview(fixture(), key, p(0, 0), p(-1, 1));
  assert.ok(next);
  assert.deepEqual(next.entities[0].points, [p(-1, 1), p(6, 0), p(4, 3), p(1, 4), p(-1, 1)]);
  const fixed = fixture([{ id: 'fix', kind: 'fix', referenceKeys: [index === 0 ? 'poly|segment|3:end' : 'poly|segment|0:start'], state: 'satisfied' }]);
  assert.equal(applyGeometryDragPreview(fixed, key, p(0, 0), p(-1, 1)), null);
});
for (const segment of [0, 3]) test(`closed segment ${segment} translation keeps closure`, () => {
  const next = applyGeometryDragPreview(fixture(), `poly|segment|${segment}`, p(0, 0), p(-1, 1));
  assert.ok(next);
  assert.deepEqual(next.entities[0].points[0], p(-1, 1));
  assert.deepEqual(next.entities[0].points.at(-1), p(-1, 1));
});

test('nearby open endpoints stay independent', () => {
  const original = fixture(); original.entities[0].points[4] = p(0, .00000001);
  const next = applyGeometryDragPreview(original, 'poly|point|vertex-0|0|0', p(0, 0), p(-1, 1));
  assert.ok(next);
  assert.deepEqual(next.entities[0].points[4], p(0, .00000001));
});
for (const reverse of [false, true]) test(`peer drag updates closing alias reverse=${reverse}`, () => {
  const keys = ['poly|segment|3:end', 'peer'];
  const original = fixture([{ id: 'weld', kind: 'coincident', referenceKeys: reverse ? keys.reverse() : keys, state: 'satisfied' }]);
  original.entities.push({ id: 'peer', kind: 'point', points: [p(0, 0)] });
  const next = applyGeometryDragPreview(original, 'peer', p(0, 0), p(-1, 1));
  assert.ok(next);
  assert.deepEqual(next.entities[0].points[0], p(-1, 1));
  assert.deepEqual(next.entities[0].points[4], p(-1, 1));
});

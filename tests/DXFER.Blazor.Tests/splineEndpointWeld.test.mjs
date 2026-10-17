import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
const p = (x, y) => ({ x, y });
function fixture(reverse, fixed) {
  const points = [p(0, 0), p(1, 0), p(3, 1), p(4, 1)];
  const constraints = [
    { id: 'join', kind: 'coincident', referenceKeys: ['spline:end', 'line:start'], state: 'satisfied' },
    { id: 'join-point', kind: 'coincident', referenceKeys: ['line:start', 'point'], state: 'satisfied' }
  ];
  if (fixed) constraints.push({ id: 'fix', kind: 'fix', referenceKeys: ['point'], state: 'satisfied' });
  return { entities: [
    { id: 'spline', kind: 'spline', points, controlPoints: points, fitPoints: [], splineDegree: 3,
      splineKnots: [0, 0, 0, 0, 1, 1, 1, 1], splineWeights: [1, 1, 1, 1] },
    { id: 'line', kind: 'line', points: [p(4, 1), p(8, 1)] },
    { id: 'point', kind: 'point', points: [p(4, 1)] }
  ], dimensions: [], constraints: reverse ? constraints.reverse() : constraints };
}
for (const reverse of [false, true]) for (const dragSpline of [false, true]) {
  test(`spline endpoint weld preview reverse=${reverse} spline=${dragSpline}`, () => {
    const original = fixture(reverse, false);
    const key = dragSpline ? 'spline|point|end|4|1' : 'line|point|start|4|1';
    const next = applyGeometryDragPreview(original, key, p(4, 1), p(5, 2));
    assert.ok(next);
    assert.deepEqual(next.entities[0].controlPoints, [p(0, 0), p(1, 0), p(3, 1), p(5, 2)]);
    assert.deepEqual(next.entities[1].points, [p(5, 2), p(8, 1)]);
    assert.deepEqual(next.entities[2].points, [p(5, 2)]);
    assert.equal(applyGeometryDragPreview(fixture(reverse, true), key, p(4, 1), p(5, 2)), null);
  });
}

for (const kind of ['fit', 'weighted', 'linear']) for (const start of [false, true])
  for (const reverse of [false, true]) for (const delta of [1, .00001]) {
    test(`endpoint preview ${kind} start=${start} reverse=${reverse} delta=${delta}`, () => {
      const original = fixture(false, false), spline = original.entities[0];
      if (kind === 'fit') spline.fitPoints = spline.controlPoints.map(a => ({ ...a }));
      if (kind === 'weighted') spline.splineWeights = [1, 2, 3, 1];
      if (kind === 'linear') { spline.splineDegree = 1; spline.splineKnots = [0, 0, 1, 2, 3, 3]; }
      const index = start ? 0 : 3, from = spline.controlPoints[index], to = p(from.x + delta, from.y + delta);
      const keys = [`spline:${start ? 'start' : 'end'}`, 'point'];
      original.entities = [spline, { id: 'point', kind: 'point', points: [from] }];
      original.constraints = [{ id: 'weld', kind: 'coincident', referenceKeys: reverse ? keys.reverse() : keys, state: 'satisfied' }];
      const next = applyGeometryDragPreview(original, 'point', from, to);
      assert.ok(next);
      const edited = next.entities[0];
      assert.deepEqual(kind === 'fit' ? edited.fitPoints : edited.controlPoints,
        spline.controlPoints.map((p, i) => i === index ? to : p));
      assert.deepEqual(edited.splineKnots, spline.splineKnots);
      assert.deepEqual(edited.splineWeights, spline.splineWeights);
    });
  }

test('unclamped endpoint preview refuses both direct and peer edit', () => {
  const original = fixture(false, false);
  original.entities[0].splineKnots = [0, 1, 2, 3, 4, 5, 6, 7];
  const from = p(17 / 6, 5 / 6), to = p(from.x + 1, from.y + 1);
  original.entities[1].points[0] = from;
  original.entities[2].points[0] = from;
  for (const key of ['spline|point|end|4|1', 'point'])
    assert.equal(applyGeometryDragPreview(original, key, from, to), null);
});

test('driving endpoint span translates the welded spline without changing intent', () => {
  const original = fixture(false, false);
  original.dimensions = [{ id: 'span', kind: 'linearDistance', referenceKeys: ['spline:start', 'spline:end'],
    value: Math.sqrt(17), anchor: p(2, -1), isDriving: true }];
  const next = applyGeometryDragPreview(original, 'spline|point|end|4|1', p(4, 1), p(5, 2));
  assert.ok(next);
  assert.deepEqual(next.entities[0].controlPoints, original.entities[0].controlPoints.map(a => p(a.x + 1, a.y + 1)));
  assert.deepEqual(next.entities[1].points[0], p(5, 2));
  assert.equal(next.dimensions[0].value, Math.sqrt(17));
});

test('endpoint pick offset preserves the requested delta', () => {
  const next = applyGeometryDragPreview(fixture(false, false), 'spline|point|end|4|1', p(4.1, 1.1), p(5.1, 2.1));
  assert.ok(next);
  assert.deepEqual(next.entities[0].controlPoints.at(-1), p(5, 2));
  assert.deepEqual(next.entities[1].points[0], p(5, 2));
});

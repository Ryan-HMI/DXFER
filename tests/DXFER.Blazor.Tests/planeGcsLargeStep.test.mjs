import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createPlaneGcsSolver } from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';
import { GcsWrapper, Algorithm } from '../../src/DXFER.Blazor/wwwroot/vendor/planegcs/dist/index.js';
import { shortSlot, fourBar } from './fixtures/largeStepFixtures.mjs';

let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(() => solver.dispose());
const near = (actual, expected, epsilon = 1e-6) => assert.ok(Math.abs(actual - expected) < epsilon,
  `${actual} differs from ${expected}`);
const length = entity => Math.hypot(entity.points[1].x - entity.points[0].x, entity.points[1].y - entity.points[0].y);
const solved = result => assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));

function checkSlot(result, width, separation = 1) {
  solved(result);
  const get = id => result.entities.find(entity => entity.id === id);
  for (const cap of ['startArc', 'endArc']) near(get(cap).radius, width / 2);
  near(length(get('axis')), separation);
  near(get('axis').points[0].x, 0);
  near(get('axis').points[0].y, 0);
  for (const [edge, index, cap, angleKey] of [
    ['left', 0, 'startArc', 'startAngleDegrees'], ['left', 1, 'endArc', 'endAngleDegrees'],
    ['right', 0, 'endArc', 'startAngleDegrees'], ['right', 1, 'startArc', 'endAngleDegrees']
  ]) {
    const line = get(edge), arc = get(cap), endpoint = line.points[index];
    const angle = arc[angleKey] * Math.PI / 180;
    near(endpoint.x, arc.center.x + arc.radius * Math.cos(angle));
    near(endpoint.y, arc.center.y + arc.radius * Math.sin(angle));
    const dx = line.points[1].x - line.points[0].x, dy = line.points[1].y - line.points[0].y;
    near((dx * (endpoint.x - arc.center.x) + dy * (endpoint.y - arc.center.y)) / (length(line) * arc.radius), 0);
  }
  assert.deepEqual(result.redundantDrivingDimensionIds, []);
}

for (const angle of [0, 20, 37, 45, 60, 90, -37]) {
  test(`direct slot width 20 to 10 retains intrinsic constraints, angle=${angle}`, () => {
    const document = shortSlot({ angle, horizontal: angle === 0 }), before = structuredClone(document);
    const result = solver.solve({ document, dimensions: [{ ...document.dimensions[1], value: 10 }],
      attemptedDrivingDimensionIds: ['width'] });
    checkSlot(result, 10);
    assert.deepEqual(document, before);
  });
}

test('LM failure and incremental success are independent native witnesses for the short-slot regression', () => {
  const original = GcsWrapper.prototype.solve;
  GcsWrapper.prototype.solve = function () { return original.call(this, Algorithm.LevenbergMarquardt); };
  try {
    let document = shortSlot();
    const direct = solver.solve({ document, dimensions: [{ ...document.dimensions[1], value: 10 }] });
    assert.equal(direct.status, 'failed');
    assert.deepEqual(direct.entities, document.entities);
    for (const value of [18, 14, 10]) {
      const width = { ...document.dimensions[1], value };
      const next = solver.solve({ document, dimensions: [width], attemptedDrivingDimensionIds: ['width'] });
      checkSlot(next, value);
      document = { ...document, entities: next.entities, dimensions: [document.dimensions[0], width] };
    }
  } finally { GcsWrapper.prototype.solve = original; }
});

test('long-slot direct edit remains supported', () => {
  const document = shortSlot({ length: 30 });
  checkSlot(solver.solve({ document, dimensions: [{ ...document.dimensions[1], value: 10 }],
    attemptedDrivingDimensionIds: ['width'] }), 10, 30);
});

test('fixed short slot rejects incompatible width atomically and does not poison the next solve', () => {
  const document = shortSlot(), before = structuredClone(document);
  const failed = solver.solve({ document, dimensions: [{ ...document.dimensions[1], value: 10 }],
    fixedReferenceKeys: document.entities.map(entity => entity.id), attemptedDrivingDimensionIds: ['width'] });
  assert.equal(failed.status, 'failed');
  assert.deepEqual(failed.entities, document.entities);
  checkSlot(solver.solve({ document, attemptedDrivingDimensionIds: ['width'] }), 20);
  assert.deepEqual(document, before);
});

test('a conflicting radius cannot be silently discarded by the alternative algorithm', () => {
  const document = shortSlot();
  const failed = solver.solve({ document, dimensions: [
    { ...document.dimensions[1], value: 10 },
    { id: 'incompatible-radius', kind: 'Radius', referenceKeys: ['startArc'], value: 8, isDriving: true }
  ], attemptedDrivingDimensionIds: ['width'] });
  assert.equal(failed.status, 'failed');
  assert.deepEqual(failed.entities, document.entities);
});

for (const rightAngle of [80, -80, 100, 120, -120, 150])
for (const reverse of [false, true])
test(`four-bar 180-degree request tries the reachable sector, start=${rightAngle} reversed=${reverse}`, () => {
  const document = fourBar({ rightAngle }), before = structuredClone(document);
  const referenceKeys = reverse ? ['right', 'ground'] : ['ground', 'right'];
  const result = solver.solve({ document, dimensions: [
    { id: 'angle', kind: 'Angle', referenceKeys, value: 180, isDriving: true }
  ], attemptedDrivingDimensionIds: ['angle'] });
  solved(result);
  assert.deepEqual(result.redundantDrivingDimensionIds, []);
  assert.deepEqual(document, before);
  assert.deepEqual(result.entities.find(entity => entity.id === 'ground'), document.entities[0]);
  for (const [id, value] of [['ground', 14.583333333333334], ['left', 8], ['top', 12], ['right', 10]])
    near(length(result.entities.find(entity => entity.id === id)), value);
  const right = result.entities.find(entity => entity.id === 'right');
  near(right.points[1].x - right.points[0].x, -10);
  near(right.points[1].y - right.points[0].y, 0);
});

test('four-bar alternate-sector retries cannot move a fixed bar or silently drop lengths', () => {
  const document = fourBar();
  const result = solver.solve({ document, dimensions: [
    { id: 'angle', kind: 'Angle', referenceKeys: ['ground', 'right'], value: 180, isDriving: true }
  ], fixedReferenceKeys: ['right'], attemptedDrivingDimensionIds: ['angle'] });
  assert.equal(result.status, 'failed');
  assert.deepEqual(result.entities, document.entities);
});

test('four-bar keeps an already feasible nearest sector instead of always flipping', () => {
  const document = fourBar({ left: 18, top: 12 });
  const result = solver.solve({ document, dimensions: [
    { id: 'angle', kind: 'Angle', referenceKeys: ['ground', 'right'], value: 180, isDriving: true }
  ], attemptedDrivingDimensionIds: ['angle'] });
  solved(result);
  const right = result.entities.find(entity => entity.id === 'right');
  near(right.points[1].x - right.points[0].x, 10);
  near(right.points[1].y - right.points[0].y, 0);
});

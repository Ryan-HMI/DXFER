import assert from 'node:assert/strict';
import {before, after, test} from 'node:test';
import {createPlaneGcsSolver} from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';

let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(async () => { await solver.dispose(); });
const point = (x, y) => ({x, y});
const line = (id, a, b) => ({id, kind: 'line', points: [a, b]});
const arc = (id, center, startAngleDegrees, endAngleDegrees) =>
  ({id, kind: 'arc', center, radius: 1, startAngleDegrees, endAngleDegrees});
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-6, `${a} != ${b}`);

function slot(reverse) {
  const entities = [line('axis', point(0, 0), point(10, 0)),
    line('top', point(0, 1), point(10, 1)), line('bottom', point(10, -1), point(0, -1)),
    arc('right', point(10, 0), -90, 90), arc('left', point(0, 0), 90, 270)];
  const constraints = [];
  const add = (kind, ...referenceKeys) => constraints.push({id: `c${constraints.length}`, kind,
    referenceKeys: reverse ? referenceKeys.reverse() : referenceKeys, state: 'satisfied'});
  add('Coincident', 'axis:start', 'left:center');
  add('Coincident', 'axis:end', 'right:center');
  add('Equal', 'left', 'right');
  for (const [edge, cap] of [['top:start', 'left:start'], ['top:end', 'right:end'],
    ['bottom:start', 'right:start'], ['bottom:end', 'left:end']]) add('Coincident', edge, cap);
  for (const edge of ['top', 'bottom']) for (const cap of ['left', 'right']) add('Tangent', edge, cap);
  if (reverse) constraints.reverse();
  return {entities, constraints, dimensions: [{id: 'width', kind: 'PointToLineDistance',
    referenceKeys: ['top', 'bottom'], value: 2, isDriving: true}]};
}

for (const reverse of [false, true]) test(`joined arc tangency supports edited driving width and native rank, reverse=${reverse}`, async () => {
  const document = slot(reverse), original = structuredClone(document);
  const result = await solver.solve({document, constraints: [], dimensions: [{...document.dimensions[0], value: 4}],
    fixedReferenceKeys: ['axis'], attemptedDrivingDimensionIds: ['width'], initialGuesses: []});
  assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
  assert.deepEqual(result.redundantDrivingDimensionIds, []);
  assert.deepEqual(document, original);
  assert.deepEqual(result.entities.find(e => e.id === 'axis'), document.entities[0]);
  for (const entity of result.entities.filter(e => e.kind === 'arc')) near(entity.radius, 2);
  const top = result.entities.find(e => e.id === 'top'), bottom = result.entities.find(e => e.id === 'bottom');
  top.points.forEach(p => near(p.y, 2)); bottom.points.forEach(p => near(p.y, -2));
});

test('fixed joined slot rejects incompatible width without publishing partial geometry', async () => {
  const document = slot(false);
  const result = await solver.solve({document, constraints: [], dimensions: [{...document.dimensions[0], value: 4}],
    fixedReferenceKeys: document.entities.map(e => e.id), attemptedDrivingDimensionIds: ['width'], initialGuesses: []});
  assert.notEqual(result.status, 'solved');
  assert.deepEqual(result.entities, document.entities);
});

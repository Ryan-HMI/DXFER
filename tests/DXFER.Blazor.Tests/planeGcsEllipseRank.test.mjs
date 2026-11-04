import assert from 'node:assert/strict';
import {after, before, test} from 'node:test';
import {createPlaneGcsSolver} from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';

let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(() => solver.dispose());

const p = (x, y) => ({x, y});
const near = (actual, expected) => assert.ok(Math.abs(actual - expected) < 1e-6,
  `${actual} != ${expected}`);
const dimension = (id, part, value) => ({id, kind: 'LinearDistance', value, isDriving: true,
  referenceKeys: ['b:center', `b:${part}-end`]});

function request(sign, reverse) {
  return {
    document: {
      entities: [
        {id: 'axis', kind: 'line', points: [p(0, -10), p(0, 10)]},
        {id: 'a', kind: 'ellipse', center: p(-5, 0), majorAxisEndPoint: p(3, 0), minorRadiusRatio: .5},
        {id: 'b', kind: 'ellipse', center: p(5, 0), majorAxisEndPoint: p(sign * 3, 0), minorRadiusRatio: .5},
        {id: 'target', kind: 'point', points: [p(5 + sign * 3, 0)]}
      ],
      constraints: [
        {id: 'mirror', kind: 'Symmetric', referenceKeys: [...(reverse ? ['b', 'a'] : ['a', 'b']), 'axis']},
        {id: 'endpoint', kind: 'Coincident', referenceKeys: ['b:major-end', 'target']}
      ],
      dimensions: [dimension('major', 'major', 3), dimension('minor', 'minor', 1.5)]
    },
    fixedReferenceKeys: ['axis', 'a:center', 'a:major-end', 'target'],
    dimensions: [dimension('minor', 'minor', 2)],
    attemptedDrivingDimensionIds: ['minor']
  };
}

function solve(request) {
  const before = structuredClone(request);
  const result = solver.solve(request);
  assert.deepEqual(request, before, 'solving must not mutate the request');
  if (result.status !== 'solved') assert.deepEqual(result.entities, request.document.entities);
  return result;
}

for (const sign of [1, -1]) for (const reverse of [false, true]) {
  test(`ellipse minor edit stays independent beside redundant major, sign=${sign}, reverse=${reverse}`, () => {
    const input = request(sign, reverse);
    const result = solve(input);
    assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
    assert.ok(!result.redundantDrivingDimensionIds.includes('minor'), JSON.stringify(result.diagnostics));
    assert.ok(result.diagnostics.includes('Native rank check minor: DOF 0, without intent 1.'));
    assert.deepEqual(result.entities[0], input.document.entities[0]);
    assert.deepEqual(result.entities[3], input.document.entities[3]);
    for (const [index, expectedX, expectedMajor] of [[1, -5, 3], [2, 5, sign * 3]]) {
      const entity = result.entities[index];
      near(entity.center.x, expectedX); near(entity.center.y, 0);
      near(entity.majorAxisEndPoint.x, expectedMajor); near(entity.majorAxisEndPoint.y, 0);
      near(Math.hypot(entity.majorAxisEndPoint.x, entity.majorAxisEndPoint.y) * entity.minorRadiusRatio, 2);
    }
    const reopened = solve({...input, document: JSON.parse(JSON.stringify({...input.document,
      entities: result.entities, dimensions: [dimension('major', 'major', 3), dimension('minor', 'minor', 2)]}))});
    assert.equal(reopened.status, 'solved', JSON.stringify(reopened.diagnostics));
    assert.ok(!reopened.redundantDrivingDimensionIds.includes('minor'), JSON.stringify(reopened.diagnostics));
  });

  test(`ellipse rank still identifies attempted redundant major, sign=${sign}, reverse=${reverse}`, () => {
    const input = request(sign, reverse);
    input.attemptedDrivingDimensionIds = ['major', 'minor'];
    const result = solve(input);
    assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
    assert.deepEqual(new Set(result.redundantDrivingDimensionIds), new Set(['major']));
    assert.ok(result.diagnostics.includes('Native rank check major: DOF 0, without intent 0.'));
    assert.ok(result.diagnostics.includes('Native rank check minor: DOF 0, without intent 1.'));
  });

  test(`ellipse duplicate minor stays redundant, sign=${sign}, reverse=${reverse}`, () => {
    const input = request(sign, reverse);
    input.document.dimensions[1].value = 2;
    input.document.entities[1].minorRadiusRatio = 2 / 3;
    input.document.entities[2].minorRadiusRatio = 2 / 3;
    input.dimensions = [dimension('duplicate-minor', 'minor', 2)];
    input.attemptedDrivingDimensionIds = ['duplicate-minor'];
    const result = solve(input);
    assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
    assert.ok(result.redundantDrivingDimensionIds.includes('duplicate-minor'), JSON.stringify(result.diagnostics));
    assert.ok(result.diagnostics.includes('Native rank check duplicate-minor: DOF 0, without intent 0.'));
  });

  test(`fixed ellipse rejects incompatible minor edit unchanged, sign=${sign}, reverse=${reverse}`, () => {
    const input = request(sign, reverse);
    input.fixedReferenceKeys.push('b');
    const result = solve(input);
    assert.equal(result.status, 'failed', JSON.stringify(result.diagnostics));
    assert.deepEqual(result.entities, input.document.entities);
  });
}

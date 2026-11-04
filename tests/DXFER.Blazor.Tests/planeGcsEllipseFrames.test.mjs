import assert from 'node:assert/strict';
import {before, after, test} from 'node:test';
import {createPlaneGcsSolver} from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';
import {GcsWrapper} from '../../src/DXFER.Blazor/wwwroot/vendor/planegcs/dist/index.js';

let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(() => solver.dispose());
const p = (x, y) => ({x, y});
const axis = {id: 'axis', kind: 'line', points: [p(0, -10), p(0, 10)]};
const ellipse = (id, x, y = 1) => ({id, kind: 'ellipse', center: p(x, y), majorAxisEndPoint: p(3, 0), minorRadiusRatio: .5});
const constraint = (id, kind, referenceKeys) => ({id, kind, referenceKeys});
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-6, `${a} != ${b}`);
const solved = result => assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
const mirror = reverse => constraint('mirror', 'Symmetric', [...(reverse ? ['b', 'a'] : ['a', 'b']), 'axis']);
const run = (document, extra = {}) => solver.solve({document, fixedReferenceKeys: ['axis'], ...extra});

for (const reverse of [false, true]) {
  test(`named ellipse endpoint chooses nonpreferred equivalent frame, reverse=${reverse}`, () => {
    const document = {entities: [axis, ellipse('a', -5), ellipse('b', 5),
      {id: 'target', kind: 'point', points: [p(2, 1)]}], dimensions: [],
    constraints: [mirror(reverse), constraint('endpoint', 'Coincident', ['b:major-end', 'target'])]};
    const result = run(document, {fixedReferenceKeys: ['axis', 'a', 'target']}); solved(result);
    assert.deepEqual(result.entities[1], document.entities[1]);
    near(result.entities[2].center.x, 5); near(result.entities[2].center.y, 1);
    near(result.entities[2].majorAxisEndPoint.x, -3); near(result.entities[2].minorRadiusRatio, .5);
    const reopened = run(JSON.parse(JSON.stringify({...document, entities: result.entities})),
      {fixedReferenceKeys: ['axis', 'a', 'target']}); solved(reopened);
    near(reopened.entities[2].majorAxisEndPoint.x, -3);
  });

  test(`ellipse symmetry moves an initially coincident auxiliary endpoint, reverse=${reverse}`, () => {
    const document = {entities: [axis, ellipse('a', -5), ellipse('b', 2)], constraints: [mirror(reverse)], dimensions: []};
    const result = run(document, {fixedReferenceKeys: ['axis', 'a']}); solved(result);
    assert.deepEqual(result.entities[1], document.entities[1]);
    near(result.entities[2].center.x, 5); near(result.entities[2].center.y, 1);
    near(result.entities[2].majorAxisEndPoint.x, 3); near(result.entities[2].minorRadiusRatio, .5);
  });

  test(`equivalent ellipse symmetry stays live through center drag, reverse=${reverse}`, () => {
    const document = {entities: [axis, ellipse('a', -5), ellipse('b', 5)], constraints: [mirror(reverse)], dimensions: []};
    const accepted = run(document); solved(accepted);
    const moved = run({...document, entities: accepted.entities},
      {drag: {key: 'a:center', start: p(-5, 1), end: p(-7, 4)}}); solved(moved);
    near(moved.entities[1].center.x, -7); near(moved.entities[1].center.y, 4);
    near(moved.entities[2].center.x, 7); near(moved.entities[2].center.y, 4);
    for (const e of moved.entities.slice(1)) {
      near(e.majorAxisEndPoint.x, 3); near(e.majorAxisEndPoint.y, 0); near(e.minorRadiusRatio, .5);
    }
  });

  test(`fixed ellipse conflicts refuse unchanged across both frame choices, reverse=${reverse}`, () => {
    const document = {entities: [axis, ellipse('a', -5), ellipse('b', 4)], constraints: [mirror(reverse)], dimensions: []};
    const result = run(document, {fixedReferenceKeys: ['axis', 'a', 'b']});
    assert.equal(result.status, 'failed'); assert.deepEqual(result.entities, document.entities);
  });

  for (const ratio of [.5, 1.5]) test(`full focal ellipses preserve fixed equivalent frames, reverse=${reverse}, ratio=${ratio}`, () => {
    const document = {entities: [axis, {...ellipse('a', -5), minorRadiusRatio: ratio},
      {...ellipse('b', 5), minorRadiusRatio: ratio}], constraints: [mirror(reverse)], dimensions: []};
    const result = run(document, {fixedReferenceKeys: ['axis', 'a', 'b', 'a:focus1']}); solved(result);
    assert.deepEqual(result.entities, document.entities);
  });
}

for (const order of [0, 1, 2]) test(`six independent pairs choose frames without order-dependent exhaustion ${order}`, () => {
  const entities = [axis], constraints = [], fixedReferenceKeys = ['axis'];
  const flip = i => order === 0 || (order === 1 ? i < 3 : i >= 3);
  for (let i = 0; i < 6; i++) {
    const a = ellipse(`a${i}`, -5, i * 5), b = ellipse(`b${i}`, 5, i * 5);
    entities.push(a, b, {id: `target${i}`, kind: 'point', points: [p(flip(i) ? 2 : 8, i * 5)]});
    constraints.push(constraint(`mirror${i}`, 'Symmetric', [a.id, b.id, 'axis']),
      constraint(`endpoint${i}`, 'Coincident', [`${b.id}:major-end`, `target${i}`]));
    fixedReferenceKeys.push(a.id, `target${i}`);
  }
  const result = run({entities, constraints, dimensions: []}, {fixedReferenceKeys}); solved(result);
  for (let i = 0; i < 6; i++) near(result.entities.find(e => e.id === `b${i}`).majorAxisEndPoint.x, flip(i) ? -3 : 3);
});

test('connected six-ellipse cycle changes consistent endpoint correspondence together', () => {
  const entities = [], constraints = [], fixedReferenceKeys = ['v0'];
  for (let i = 0; i < 6; i++) {
    entities.push(ellipse(`v${i}`, 10 * i, 0), {id: `target${i}`, kind: 'point', points: [p(10 * i + (i % 2 ? -3 : 3), 0)]});
    fixedReferenceKeys.push(`target${i}`);
    constraints.push(constraint(`endpoint${i}`, 'Coincident', [`v${i}:major-end`, `target${i}`]));
    const j = (i + 1) % 6, x = 5 * (i + j);
    entities.push({id: `axis${i}`, kind: 'line', points: [p(x, -10), p(x, 10)]});
    fixedReferenceKeys.push(`axis${i}`);
    constraints.push(constraint(`mirror${i}`, 'Symmetric', [`v${i}`, `v${j}`, `axis${i}`]));
  }
  const result = run({entities, constraints, dimensions: []}, {fixedReferenceKeys}); solved(result);
  for (let i = 0; i < 6; i++) {
    const e = result.entities.find(e => e.id === `v${i}`);
    near(e.center.x, 10 * i); near(e.center.y, 0);
    near(e.majorAxisEndPoint.x, i % 2 ? -3 : 3); near(e.minorRadiusRatio, .5);
  }
});

test('unrelated fixed-line conflict does not retry satisfied ellipse correspondences', () => {
  const entities = [axis, {id: 'line', kind: 'line', points: [p(0, 0), p(5, 0)]}], constraints = [];
  for (let i = 0; i < 6; i++) {
    entities.push(ellipse(`a${i}`, -5, i * 5), ellipse(`b${i}`, 5, i * 5));
    constraints.push(constraint(`mirror${i}`, 'Symmetric', [`a${i}`, `b${i}`, 'axis']));
  }
  const document = {entities, constraints, dimensions: [
    {id: 'impossible', kind: 'LinearDistance', referenceKeys: ['line'], value: 7, isDriving: true}]};
  const original = GcsWrapper.prototype.solve;
  let calls = 0;
  GcsWrapper.prototype.solve = function (...args) { calls++; return original.apply(this, args); };
  try {
    const result = run(document, {fixedReferenceKeys: ['axis', 'line']});
    assert.equal(result.status, 'failed'); assert.deepEqual(result.entities, entities);
    assert.ok(calls <= 4, `unrelated conflict caused ${calls} native solve calls`);
  } finally { GcsWrapper.prototype.solve = original; }
});

test('ellipse symmetry cycle preserves a consistent equivalent frame', () => {
  const document = {entities: [axis, ellipse('a', -5), ellipse('b', 5)], dimensions: [],
    constraints: [mirror(false), {...mirror(true), id: 'reverse'}]};
  const result = run(document); solved(result);
  assert.deepEqual(result.entities, document.entities);
});

test('ellipse axis dimension remains driving with equivalent frames and exact rank checks', () => {
  const dimension = {id: 'major', kind: 'LinearDistance', referenceKeys: ['a:major-start', 'a:major-end'],
    value: 6, isDriving: true};
  const document = {entities: [axis, ellipse('a', -5), ellipse('b', 5)],
    dimensions: [dimension], constraints: [mirror(false)]};
  const edited = {...dimension, value: 8};
  const result = run(document, {dimensions: [edited], attemptedDrivingDimensionIds: ['major']}); solved(result);
  assert.deepEqual(result.redundantDrivingDimensionIds, []);
  const [a, b] = result.entities.slice(1);
  for (const e of [a, b]) near(Math.hypot(e.majorAxisEndPoint.x, e.majorAxisEndPoint.y), 4);
  near(a.center.x, -b.center.x); near(a.center.y, b.center.y);
  near(a.majorAxisEndPoint.x, b.majorAxisEndPoint.x); near(a.majorAxisEndPoint.y, -b.majorAxisEndPoint.y);
  near(a.minorRadiusRatio, b.minorRadiusRatio);
  solved(run({...document, entities: result.entities, dimensions: [edited]}));
});

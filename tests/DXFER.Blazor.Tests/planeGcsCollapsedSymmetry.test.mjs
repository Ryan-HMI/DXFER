import assert from 'node:assert/strict';
import {before, after, test} from 'node:test';
import {createPlaneGcsSolver} from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';

let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(() => solver.dispose());
const p = (x, y) => ({x, y});
const relation = (id, kind, referenceKeys) => ({id, kind, referenceKeys});
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-6, `${a} != ${b}`);
const solved = result => assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
const axis = angle => ({id: 'axis', kind: 'line', points: [p(-10 * Math.cos(angle), -10 * Math.sin(angle)),
  p(10 * Math.cos(angle), 10 * Math.sin(angle))]});
const run = (document, extra = {}) => solver.solve({document, fixedReferenceKeys: ['axis'], ...extra});

for (const angle of [0, Math.PI / 2, .7]) {
  for (const separation of [0, 1e-8, .00888]) {
    test(`symmetric circle diameter edit at separation ${separation}, axis ${angle}`, () => {
      const normal = p(-Math.sin(angle) * separation, Math.cos(angle) * separation);
      const document = {entities: [axis(angle),
        {id: 'a', kind: 'circle', center: normal, radius: 3},
        {id: 'b', kind: 'circle', center: p(-normal.x, -normal.y), radius: 3}],
      constraints: [relation('mirror', 'Symmetric', ['a', 'b', 'axis'])],
      dimensions: [{id: 'diameter', kind: 'Diameter', referenceKeys: ['a'], value: 6, isDriving: true}]};
      if (!separation) document.constraints.push(relation('centers', 'Coincident', ['a:center', 'b:center']));
      const result = run(document, {dimensions: [{...document.dimensions[0], value: 8}],
        attemptedDrivingDimensionIds: ['diameter']});
      solved(result);
      near(result.entities[1].radius, 4); near(result.entities[2].radius, 4);
      assert.deepEqual(result.entities[0], document.entities[0]);
      assert.deepEqual(result.redundantDrivingDimensionIds, []);
      const reopened = run({...document, entities: result.entities,
        dimensions: [{...document.dimensions[0], value: 8}]});
      solved(reopened);
    });
  }
}

test('coincident symmetric point pair can separate again', () => {
  const document = {entities: [axis(Math.PI / 2), ...['a', 'b'].map(id => ({id, kind: 'point', points: [p(0, 2)]}))],
    constraints: [relation('mirror', 'Symmetric', ['a', 'b', 'axis'])], dimensions: []};
  const result = run(document, {drag: {key: 'a:center', start: p(0, 2), end: p(-3, 4)}});
  solved(result);
  near(result.entities[1].points[0].x, -3); near(result.entities[1].points[0].y, 4);
  near(result.entities[2].points[0].x, 3); near(result.entities[2].points[0].y, 4);
});

test('symmetric line pair with coincident starts accepts a length edit', () => {
  const document = {entities: [axis(Math.PI / 2),
    {id: 'a', kind: 'line', points: [p(0, 0), p(-6, 0)]},
    {id: 'b', kind: 'line', points: [p(0, 0), p(6, 0)]}],
  constraints: [relation('mirror', 'Symmetric', ['a:start', 'b:start', 'a:end', 'b:end', 'axis'])],
  dimensions: [{id: 'length', kind: 'LinearDistance', referenceKeys: ['a:start', 'a:end'], value: 6, isDriving: true}]};
  const result = run(document, {dimensions: [{...document.dimensions[0], value: 8}], attemptedDrivingDimensionIds: ['length']});
  solved(result);
  for (const entity of result.entities.slice(1)) near(Math.hypot(entity.points[1].x - entity.points[0].x,
    entity.points[1].y - entity.points[0].y), 8);
  result.entities[1].points.forEach((point, i) => {
    near(point.x, -result.entities[2].points[i].x); near(point.y, result.entities[2].points[i].y);
  });
});

test('fixed collapsed circles still refuse incompatible radius without geometry changes', () => {
  const document = {entities: [axis(Math.PI / 2), ...['a', 'b'].map(id => ({id, kind: 'circle', center: p(0, 2), radius: 3}))],
    constraints: [relation('mirror', 'Symmetric', ['a', 'b', 'axis'])], dimensions: []};
  const result = run(document, {fixedReferenceKeys: ['axis', 'a', 'b'],
    dimensions: [{id: 'diameter', kind: 'Diameter', referenceKeys: ['a'], value: 8, isDriving: true}]});
  assert.notEqual(result.status, 'solved'); assert.deepEqual(result.entities, document.entities);
});

test('distant noncollapsed symmetric points retain native numerical conditioning', () => {
  const document = {entities: [{id: 'axis', kind: 'line', points: [p(0, -10), p(0, 10)]},
    {id: 'a', kind: 'point', points: [p(-10000.13, 10000.17)]},
    {id: 'b', kind: 'point', points: [p(10000.13, 10000.17)]}],
    constraints: [relation('mirror', 'Symmetric', ['a', 'b', 'axis'])], dimensions: []};
  const result = run(document, {fixedReferenceKeys: ['axis', 'a']});
  solved(result); assert.deepEqual(result.entities, document.entities);
});

test('full ellipse symmetry allows explicit endpoint intent to choose the opposite equivalent frame', () => {
  const document = {entities: [{id: 'axis', kind: 'line', points: [p(0, -10), p(0, 10)]},
    ...['a', 'b'].map((id, i) => ({id, kind: 'ellipse', center: p(i ? 5 : -5, 1),
      majorAxisEndPoint: p(3, 0), minorRadiusRatio: .5, startAngleDegrees: 0, endAngleDegrees: 360})),
    {id: 'target', kind: 'point', points: [p(2, 1)]}], dimensions: [],
    constraints: [relation('mirror', 'Symmetric', ['a', 'b', 'axis']),
      relation('endpoint', 'Coincident', ['b:major-end', 'target'])]};
  const result = run(document, {fixedReferenceKeys: ['axis', 'a', 'target']});
  solved(result);
  const b = result.entities.find(e => e.id === 'b');
  near(b.center.x, 5); near(b.center.y, 1);
  near(b.majorAxisEndPoint.x, -3); near(b.majorAxisEndPoint.y, 0); near(b.minorRadiusRatio, .5);
});

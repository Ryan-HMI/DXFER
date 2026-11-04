import assert from 'node:assert/strict';
import {before, after, test} from 'node:test';
import {readFileSync} from 'node:fs';
import {createPlaneGcsSolver} from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';
let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(() => solver.dispose());
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-6, `${a} != ${b}`);
const solved = r => assert.equal(r.status, 'solved', JSON.stringify(r.diagnostics));
const vertex = (e, i) => {
  const angle = e.rotationAngleDegrees * Math.PI / 180 + (e.circumscribed ? Math.PI / e.sideCount : 0) + i * 2 * Math.PI / e.sideCount;
  const radius = e.circumscribed ? e.radius / Math.cos(Math.PI / e.sideCount) : e.radius;
  return {x: e.center.x + radius * Math.cos(angle), y: e.center.y + radius * Math.sin(angle)};
};
for (const circumscribed of [false, true]) for (const index of [0, 1, 2, 3, 4]) {
  for (const alias of [false, true]) test(`polygon ${circumscribed} vertex ${index}, canvas alias ${alias}, follows radius and center`, () => {
    const polygon = {id: 'poly', kind: 'polygon', center: {x: 17, y: -9}, radius: 10, rotationAngleDegrees: 53.13010235415598,
      sideCount: 5, circumscribed};
    polygon.points = Array.from({length: 5}, (_, i) => vertex(polygon, i));
    const start = vertex(polygon, index);
    const line = {id: 'line', kind: 'line', points: [start, {x: start.x + 3, y: start.y + 4}]};
    const key = alias ? `poly|point|vertex-${index}|999|-999` : `poly|segment|${index}:start`;
    const document = {entities: [polygon, line], dimensions: [],
      constraints: [{id: 'join', kind: 'Coincident', referenceKeys: ['line:start', key]}]};
    const original = structuredClone(document);
    const created = solver.solve({document, fixedReferenceKeys: ['poly']}); solved(created);
    const {points: actualVertices, ...actualPolygon} = created.entities[0];
    const {points: expectedVertices, ...expectedPolygon} = polygon;
    assert.deepEqual(actualPolygon, expectedPolygon); assert.deepEqual(document, original);
    actualVertices.forEach((point, i) => { near(point.x, expectedVertices[i].x); near(point.y, expectedVertices[i].y); });
    near(created.entities[1].points[0].x, start.x); near(created.entities[1].points[0].y, start.y);
    const resized = solver.solve({document: {...document, entities: created.entities}, fixedReferenceKeys: ['poly:center'],
      dimensions: [{id: 'radius', kind: 'Radius', referenceKeys: ['poly'], value: 12, isDriving: true}],
      attemptedDrivingDimensionIds: ['radius']}); solved(resized);
    const expected = vertex(resized.entities[0], index);
    near(resized.entities[0].radius, 12); near(resized.entities[1].points[0].x, expected.x); near(resized.entities[1].points[0].y, expected.y);
    const moved = solver.solve({document: {...document, entities: resized.entities},
      drag: {key: 'poly:center', start: polygon.center, end: {x: 20, y: -5}}}); solved(moved);
    const next = vertex(moved.entities[0], index);
    near(moved.entities[0].center.x, 20); near(moved.entities[0].center.y, -5);
    near(moved.entities[1].points[0].x, next.x); near(moved.entities[1].points[0].y, next.y);
  });
}

test('polygon vertex identity rejects out-of-range index and preserves geometry', () => {
  const polygon = {id: 'poly', kind: 'polygon', center: {x: 0, y: 0}, radius: 3, rotationAngleDegrees: 0, sideCount: 5, circumscribed: false};
  for (const key of ['poly|segment|5:start', 'poly|point|vertex-5|3|0']) {
    const document = {entities: [polygon, {id: 'p', kind: 'point', points: [{x: 3, y: 0}]}], dimensions: [],
      constraints: [{id: 'join', kind: 'Coincident', referenceKeys: ['p', key]}]};
    const result = solver.solve({document});
    assert.equal(result.status, 'unsupported'); assert.deepEqual(result.entities, document.entities);
  }
});

test('radius rank check ignores stale soft stability targets after a keyed connected vertex moves', () => {
  const request = JSON.parse(readFileSync(new URL('./fixtures/polygon-radius-rank.json', import.meta.url), 'utf8'));
  const before = structuredClone(request);
  const result = solver.solve(request); solved(result);
  assert.deepEqual(request, before);
  assert.deepEqual(result.redundantDrivingDimensionIds, []);
  const polygon = result.entities.find(e => e.id === 'source'), line = result.entities.find(e => e.id === 'created');
  near(polygon.radius, 15);
  const point = vertex(polygon, 3);
  near(line.points[0].x, point.x); near(line.points[0].y, point.y);
  near(Math.hypot(line.points[1].x - line.points[0].x, line.points[1].y - line.points[0].y), 12);
});

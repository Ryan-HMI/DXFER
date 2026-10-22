import assert from 'node:assert/strict';
import {before, after, test} from 'node:test';
import {createPlaneGcsSolver} from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';

let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(async () => { await solver.dispose(); });
const p = (x, y) => ({x, y});
const line = (id, a, b) => ({id, kind: 'line', points: [a, b]});
const circle = (id, center, radius) => ({id, kind: 'circle', center, radius});
const relation = (kind, referenceKeys) => ({id: kind, kind, referenceKeys, state: 'satisfied'});
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-6, `${a} != ${b}`);
const run = (document, fixedReferenceKeys = [], extra = {}) => solver.solve({document,
  constraints: [], dimensions: [], initialGuesses: [], fixedReferenceKeys, ...extra});
const solved = r => assert.equal(r.status, 'solved', JSON.stringify(r.diagnostics));
const byId = (r, id) => r.entities.find(e => e.id === id);
const axis = () => line('axis', p(0, -10), p(0, 10));

for (const reverse of [false, true]) test(`circle symmetry mirrors position and size, reverse ${reverse}`, async () => {
  const document = {entities: [axis(), circle('a', p(-4, 2), 2), circle('b', p(6, 4), 3)],
    constraints: [relation('Symmetric', [...(reverse ? ['b', 'a'] : ['a', 'b']), 'axis'])], dimensions: []};
  const result = await run(document, ['axis', 'a']); solved(result);
  assert.deepEqual(byId(result, 'a'), document.entities[1]);
  near(byId(result, 'b').center.x, 4); near(byId(result, 'b').center.y, 2); near(byId(result, 'b').radius, 2);
  const edit = await run({...document, entities: result.entities}, ['axis'], {dimensions: [{id: 'radius',
    kind: 'Radius', referenceKeys: ['a'], value: 4, isDriving: true}], attemptedDrivingDimensionIds: ['radius']});
  solved(edit); near(byId(edit, 'a').radius, 4); near(byId(edit, 'b').radius, 4);
});

test('paired line endpoints preserve full mirrored shape on an oblique axis', async () => {
  const document = {entities: [line('axis', p(-10, -10), p(10, 10)),
    line('a', p(1, 3), p(2, 6)), line('b', p(7, 0), p(5, 2))],
  constraints: [relation('Symmetric', ['a:start', 'b:end', 'a:end', 'b:start', 'axis'])], dimensions: []};
  const result = await run(document, ['axis', 'a']); solved(result);
  near(byId(result, 'b').points[0].x, 6); near(byId(result, 'b').points[0].y, 2);
  near(byId(result, 'b').points[1].x, 3); near(byId(result, 'b').points[1].y, 1);
});

test('arc symmetry reverses endpoint correspondence and preserves sweep', async () => {
  const a = {...circle('a', p(-4, 0), 2), kind: 'arc', startAngleDegrees: 0, endAngleDegrees: 90};
  const b = {...circle('b', p(5, 1), 3), kind: 'arc', startAngleDegrees: 80, endAngleDegrees: 170};
  const document = {entities: [axis(), a, b], constraints: [relation('Symmetric', ['a', 'b', 'axis'])], dimensions: []};
  const result = await run(document, ['axis', 'a']); solved(result);
  const mirrored = byId(result, 'b'); near(mirrored.center.x, 4); near(mirrored.center.y, 0);
  near(mirrored.radius, 2); near(mirrored.startAngleDegrees, 90); near(mirrored.endAngleDegrees, 180);
});

test('fixed conflicting symmetry refuses without partial geometry', async () => {
  const document = {entities: [axis(), circle('a', p(-4, 2), 2), circle('b', p(6, 4), 3)],
    constraints: [relation('Symmetric', ['a', 'b', 'axis'])], dimensions: []};
  const result = await run(document, ['axis', 'a', 'b']);
  assert.notEqual(result.status, 'solved'); assert.deepEqual(result.entities, document.entities);
});

for (const curveKind of ['circle', 'arc']) test(`Normal attaches endpoint and keeps radial direction on ${curveKind}`, async () => {
  const curve = {...circle('c', p(0, 0), 3), kind: curveKind,
    ...(curveKind === 'arc' ? {startAngleDegrees: 0, endAngleDegrees: 180} : {})};
  const document = {entities: [curve, line('l', p(4, 2), p(7, 3))],
    constraints: [relation('Normal', ['l:start', 'c'])], dimensions: []};
  const result = await run(document, ['c']); solved(result);
  const [a, b] = byId(result, 'l').points;
  near(Math.hypot(a.x, a.y), 3);
  near(a.x * (b.y - a.y) - a.y * (b.x - a.x), 0);
  assert.deepEqual(byId(result, 'c'), curve);
});

test('Normal fixed conflict refuses and removal of Fix recovers', async () => {
  const document = {entities: [circle('c', p(0, 0), 3), line('l', p(4, 2), p(7, 3))],
    constraints: [relation('Normal', ['l:start', 'c'])], dimensions: []};
  const result = await run(document, ['c', 'l']);
  assert.notEqual(result.status, 'solved'); assert.deepEqual(result.entities, document.entities);
  solved(await run(document, ['c']));
});

test('degenerate symmetry axis explicitly refuses', async () => {
  const document = {entities: [line('axis', p(0, 0), p(0, 0)), circle('a', p(-4, 0), 2), circle('b', p(4, 0), 2)],
    constraints: [relation('Symmetric', ['a', 'b', 'axis'])], dimensions: []};
  const result = await run(document);
  assert.notEqual(result.status, 'solved'); assert.deepEqual(result.entities, document.entities);
});

test('symmetric circles follow center and radius drags without moving fixed axis', async () => {
  const document = {entities: [axis(), circle('a', p(-4, 2), 2), circle('b', p(4, 2), 2)],
    constraints: [relation('Symmetric', ['a', 'b', 'axis'])], dimensions: []};
  const result = await run(document, ['axis'], {drag: {key: 'a:center', start: p(-4, 2), end: p(-6, 3)}});
  solved(result); near(byId(result, 'a').center.x, -6); near(byId(result, 'b').center.x, 6);
  near(byId(result, 'b').center.y, 3); assert.deepEqual(byId(result, 'axis'), document.entities[0]);
  const resized = await run({...document, entities: result.entities}, ['axis'],
    {drag: {key: 'b:radius', start: p(8, 3), end: p(9, 3)}});
  solved(resized); near(byId(resized, 'a').radius, 3); near(byId(resized, 'b').radius, 3);
});

test('symmetric point pair remains coupled after axis rotation', async () => {
  const document = {entities: [axis(), {id: 'a', kind: 'point', points: [p(-4, 2)]},
    {id: 'b', kind: 'point', points: [p(4, 2)]}],
    constraints: [relation('Symmetric', ['a', 'b', 'axis'])], dimensions: []};
  const result = await run(document, ['a', 'axis:start'],
    {drag: {key: 'axis:end', start: p(0, 10), end: p(10, 10)}});
  solved(result);
  const [start, end] = byId(result, 'axis').points, a = byId(result, 'a').points[0], b = byId(result, 'b').points[0];
  const dx = end.x - start.x, dy = end.y - start.y;
  const t = ((a.x - start.x) * dx + (a.y - start.y) * dy) / (dx * dx + dy * dy);
  near(b.x, 2 * (start.x + t * dx) - a.x); near(b.y, 2 * (start.y + t * dy) - a.y);
});

test('Normal follows changed curve radius while preserving fixed far endpoint', async () => {
  const document = {entities: [circle('c', p(0, 0), 3), line('l', p(3, 0), p(7, 0))],
    constraints: [relation('Normal', ['l:start', 'c'])], dimensions: []};
  const result = await run(document, ['c:center', 'l:end'], {dimensions: [{id: 'radius', kind: 'Radius',
    referenceKeys: ['c'], value: 4, isDriving: true}], attemptedDrivingDimensionIds: ['radius']});
  solved(result); near(byId(result, 'l').points[0].x, 4); near(byId(result, 'l').points[0].y, 0);
  assert.deepEqual(byId(result, 'l').points[1], p(7, 0));
});

test('Normal does not accept contact outside a fixed finite arc', async () => {
  const document = {entities: [{...circle('c', p(0, 0), 3), kind: 'arc', startAngleDegrees: 0, endAngleDegrees: 90},
    line('l', p(-3, 0), p(-7, 0))], constraints: [relation('Normal', ['l:start', 'c'])], dimensions: []};
  const result = await run(document, ['c', 'l']);
  assert.notEqual(result.status, 'solved'); assert.deepEqual(result.entities, document.entities);
});

test('closed polyline symmetry retains all paired vertices through a source edit', async () => {
  const vertices = [p(-5, 1), p(-2, 1), p(-2, 4), p(-5, 4), p(-5, 1)];
  const document = {entities: [axis(), {id: 'a', kind: 'polyline', points: vertices},
    {id: 'b', kind: 'polyline', points: vertices.map(v => p(-v.x, v.y))}], dimensions: [],
    constraints: [relation('Symmetric', [...vertices.slice(0, -1).flatMap((_, i) =>
      [`a|segment|${i}:start`, `b|segment|${i}:start`]), 'axis'])]};
  const result = await run(document, ['axis'], {drag: {key: 'a|segment|1:start', start: p(-2, 1), end: p(-3, 0)}});
  solved(result);
  const a = byId(result, 'a').points, b = byId(result, 'b').points;
  a.forEach((v, i) => { near(-v.x, b[i].x); near(v.y, b[i].y); });
  assert.deepEqual(a[0], a.at(-1)); assert.deepEqual(b[0], b.at(-1));
});

test('Normal finite-arc tolerance is spatial, not angular at large radius', async () => {
  const angle = Math.PI / 2 + 5e-7, radius = 1000;
  const document = {entities: [{...circle('c', p(0, 0), radius), kind: 'arc', startAngleDegrees: 0, endAngleDegrees: 90},
    line('l', p(radius * Math.cos(angle), radius * Math.sin(angle)),
      p(2 * radius * Math.cos(angle), 2 * radius * Math.sin(angle)))],
    constraints: [relation('Normal', ['l:start', 'c'])], dimensions: []};
  const result = await run(document, ['c', 'l']);
  assert.notEqual(result.status, 'solved'); assert.deepEqual(result.entities, document.entities);
});

for (const invalid of [circle('c', p(0, 0), 5e-7),
  {...circle('c', p(0, 0), 3), kind: 'arc', startAngleDegrees: 0, endAngleDegrees: 0},
  {...circle('c', p(0, 0), 3), kind: 'arc', startAngleDegrees: 0, endAngleDegrees: 720}]) {
  test(`Normal refuses invalid original curve ${JSON.stringify(invalid)}`, async () => {
    const document = {entities: [invalid, line('l', p(3, 0), p(7, 0))],
      constraints: [relation('Normal', ['l:start', 'c'])], dimensions: []};
    const result = await run(document);
    assert.notEqual(result.status, 'solved'); assert.deepEqual(result.entities, document.entities);
  });
}

for (const keys of [['a:start', 'a:end', 'axis'], ['a:center', 'b:center', 'a:center', 'b:center', 'axis']]) {
  test(`Symmetric refuses malformed paired references ${keys.join(',')}`, async () => {
    const a = keys[0].endsWith(':center') ? circle('a', p(-4, 2), 2) : line('a', p(-4, 2), p(4, 2));
    const document = {entities: [axis(), a, circle('b', p(4, 2), 2)],
      constraints: [relation('Symmetric', keys)], dimensions: []};
    const result = await run(document);
    assert.notEqual(result.status, 'solved'); assert.deepEqual(result.entities, document.entities);
  });
}

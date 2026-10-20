import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const p = (x, y) => ({ x, y });
const entity = (doc, id) => doc.entities.find(item => item.id === id);
const center = (doc, id) => entity(doc, id).center;
const relation = (kind, keys) => ({ id: kind, kind, referenceKeys: keys, state: 'satisfied' });
const near = (actual, expected) => assert.ok(actual && Math.hypot(actual.x - expected.x, actual.y - expected.y) < 1e-8,
  `${JSON.stringify(actual)} != ${JSON.stringify(expected)}`);
function fixture(reverse = false) {
  return {
    entities: ['a', 'm', 'b'].map((id, index) => ({ id, kind: 'circle', center: p(index * 5, 0), radius: 1 })),
    constraints: [relation('midpoint', ['m:center', ...(reverse ? ['b:center', 'a:center'] : ['a:center', 'b:center'])])],
    dimensions: []
  };
}
function drag(doc, id, target) {
  const start = center(doc, id);
  return applyGeometryDragPreview(doc, `${id}|point|center|${start.x}|${start.y}`, start, target);
}
function unchangedSizes(before, after) {
  assert.ok(after);
  assert.deepEqual(after.constraints, before.constraints);
  for (const item of before.entities) assert.equal(entity(after, item.id).radius, item.radius);
}

for (const reverse of [false, true]) {
  test(`three-point midpoint preview follows an outer center, reverse=${reverse}`, () => {
    const doc = fixture(reverse), snapshot = structuredClone(doc);
    const next = drag(doc, 'a', p(0, 2));
    unchangedSizes(doc, next);
    near(center(next, 'a'), p(0, 2)); near(center(next, 'm'), p(5, 1)); near(center(next, 'b'), p(10, 0));
    assert.deepEqual(doc, snapshot);
  });
  test(`middle drag translates both outer centers, reverse=${reverse}`, () => {
    const doc = fixture(reverse), next = drag(doc, 'm', p(6, 2));
    unchangedSizes(doc, next);
    near(center(next, 'a'), p(1, 2)); near(center(next, 'm'), p(6, 2)); near(center(next, 'b'), p(11, 2));
  });
  test(`fixed middle reflects the other free outer center, reverse=${reverse}`, () => {
    const doc = fixture(reverse);
    doc.constraints.push(relation('fix', ['m:center']));
    const next = drag(doc, 'a', p(1, 2));
    unchangedSizes(doc, next);
    near(center(next, 'a'), p(1, 2)); near(center(next, 'm'), p(5, 0)); near(center(next, 'b'), p(9, -2));
    assert.equal(drag(doc, 'm', p(5, 2)), null);
  });
  test(`fixed outer permits middle movement by reflecting free peer, reverse=${reverse}`, () => {
    const doc = fixture(reverse);
    doc.constraints.push(relation('fix', ['a:center']));
    const next = drag(doc, 'm', p(5, 2));
    unchangedSizes(doc, next);
    near(center(next, 'a'), p(0, 0)); near(center(next, 'm'), p(5, 2)); near(center(next, 'b'), p(10, 4));
  });
  test(`both outer centers fixed refuse middle movement, reverse=${reverse}`, () => {
    const doc = fixture(reverse), snapshot = structuredClone(doc);
    doc.constraints.push(relation('fix', ['a:center', 'b:center']));
    assert.equal(drag(doc, 'm', p(5, 2)), null);
    assert.deepEqual(doc.entities, snapshot.entities);
  });
  test(`driving midpoint position rejects incompatible outer edit, reverse=${reverse}`, () => {
    const doc = fixture(reverse);
    doc.dimensions.push({ id: 'distance', kind: 'linearDistance', referenceKeys: ['a:center', 'm:center'],
      value: 5, isDriving: true });
    const snapshot = structuredClone(doc);
    assert.equal(drag(doc, 'b', p(10, 2)), null);
    assert.deepEqual(doc, snapshot);
  });
}

test('suppressed three-point midpoint does not propagate', () => {
  const doc = fixture();
  doc.constraints[0].state = 'suppressed';
  const next = drag(doc, 'a', p(0, 2));
  assert.ok(next);
  near(center(next, 'm'), p(5, 0)); near(center(next, 'b'), p(10, 0));
});

test('missing point reference refuses without mutating the document', () => {
  const doc = fixture();
  doc.constraints[0].referenceKeys[2] = 'missing:center';
  const snapshot = structuredClone(doc);
  assert.equal(drag(doc, 'a', p(0, 2)), null);
  assert.deepEqual(doc, snapshot);
});

for (const segment of [0, 99]) {
  test(`segment-qualified standalone line reference ${segment} refuses`, () => {
    const doc = fixture();
    doc.entities.push({ id: 'line', kind: 'line', points: [p(10, 0), p(12, 0)] });
    doc.constraints[0].referenceKeys[2] = `line|segment|${segment}:start`;
    const snapshot = structuredClone(doc);
    assert.equal(drag(doc, 'a', p(0, 2)), null);
    assert.deepEqual(doc, snapshot);
  });
}

import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

for (const inside of [false, true]) test(`driving small radius remains while peer resizes inside=${inside}`, () => {
  const x = inside ? 7 : 13;
  const document = { entities: [
    { id: 'large', kind: 'circle', center: {x: 0, y: 0}, radius: 10 },
    { id: 'small', kind: 'circle', center: {x, y: 0}, radius: 3 }
  ], dimensions: [{id: 'small-radius', kind: 'radius', referenceKeys: ['small'], value: 3, isDriving: true, anchor: {x, y: -4}}],
  constraints: [
    { id: 'anchor', kind: 'Concentric', state: 'Satisfied', referenceKeys: ['large', '$origin:center'] },
    { id: 'tangent', kind: 'Tangent', state: 'Satisfied', referenceKeys: ['small', 'large'] }
  ] };
  const changed = applyGeometryDragPreview(document, `small|point|quadrant-90|${x}|3`, {x, y: 3}, {x, y: 4});
  assert.ok(changed);
  assert.deepEqual(changed.entities[0].center, {x: 0, y: 0});
  assert.ok(Math.abs(changed.entities[0].radius - (Math.sqrt(x*x+1) + (inside ? 3 : -3))) < 1e-8);
  assert.deepEqual(changed.entities[1].center, {x, y: 1});
  assert.equal(changed.entities[1].radius, 3);
  assert.equal(changed.dimensions[0].value, 3);
});

for (const scale of [0.00001, 1, 10000]) test(`internal tangent preview preserves branch scale=${scale}`, () => {
  const document = { entities: [
    { id: 'large', kind: 'circle', center: {x: 0, y: 0}, radius: 10 * scale },
    { id: 'small', kind: 'circle', center: {x: 7 * scale, y: 0}, radius: 3 * scale }
  ], dimensions: [], constraints: [
    { id: 'anchor', kind: 'Fix', state: 'Satisfied', referenceKeys: ['large:center'] },
    { id: 'tangent', kind: 'Tangent', state: 'Satisfied', referenceKeys: ['small', 'large'] }
  ] };
  const changed = applyGeometryDragPreview(document, `small|point|center|${7 * scale}|0`,
    {x: 7 * scale, y: 0}, {x: 14 * scale, y: 0});
  assert.ok(changed);
  assert.ok(Math.abs(changed.entities[0].radius - 17 * scale) < 1e-8);
});

for (const anchor of ['Fix', 'Coincident', 'Concentric']) for (const inside of [false, true])
for (const center of [false, true]) for (const reverse of [false, true]) {
  test(`anchored tangent preview ${anchor} inside=${inside} center=${center} reverse=${reverse}`, () => {
    const x = inside ? 7 : 13;
    const constraints = [
      { id: 'anchor', kind: anchor, state: 'Satisfied', referenceKeys: anchor === 'Fix' ? ['large:center']
        : [anchor === 'Concentric' ? 'large' : 'large:center', '$origin:center'] },
      { id: 'tangent', kind: 'Tangent', state: 'Satisfied', referenceKeys: ['small', 'large'] }
    ];
    const document = { entities: [
      { id: 'large', kind: 'circle', center: {x: 0, y: 0}, radius: 10 },
      { id: 'small', kind: 'circle', center: {x, y: 0}, radius: 3 }
    ], dimensions: [], constraints: reverse ? constraints.reverse() : constraints };
    const start = {x: center ? x : x + 3, y: 0};
    const changed = applyGeometryDragPreview(document,
      `small|point|${center ? 'center' : 'quadrant-0'}|${start.x}|0`, start, {x: start.x + 1, y: 0});
    assert.ok(changed);
    assert.deepEqual(changed.entities[0].center, {x: 0, y: 0});
    assert.ok(Math.abs(changed.entities[0].radius - (inside || center ? 11 : 9)) < 1e-8);
    assert.deepEqual(changed.entities[1].center, {x: center ? x + 1 : x, y: 0});
    assert.equal(changed.entities[1].radius, center ? 3 : 4);
    assert.equal(document.entities[0].radius, 10);
  });
}

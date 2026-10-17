import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

for (const scale of [0.00001, 1, 10000]) test(`coupled axis preview cannot accept collapsed diagonal scale=${scale}`, () => {
  const document = { entities: [
    {id: 'a', kind: 'line', start: {x: 0, y: 0}, end: {x: 0, y: 5 * scale}},
    {id: 'b', kind: 'line', start: {x: 4 * scale, y: 5 * scale}, end: {x: 4 * scale, y: 0}}
  ], dimensions: [], constraints: [
    {id: 'axis', kind: 'Vertical', referenceKeys: ['a'], state: 'Satisfied'},
    {id: 'parallel', kind: 'Parallel', referenceKeys: ['a', 'b'], state: 'Satisfied'}
  ]};
  const before = structuredClone(document);
  const preview = applyGeometryDragPreview(document, `b|point|end|${4 * scale}|0`,
    {x: 4 * scale, y: 0}, {x: 7 * scale, y: 0});
  if (preview) for (const line of preview.entities) {
    const dx = line.end.x - line.start.x, dy = line.end.y - line.start.y;
    const length = Math.hypot(dx, dy);
    assert.ok(length > 1e-6 && Math.abs(dx / length) <= 1e-6, JSON.stringify(line));
  }
  assert.deepEqual(document, before);
});

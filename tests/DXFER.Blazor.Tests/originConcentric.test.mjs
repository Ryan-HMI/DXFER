import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
for (const reversed of [false, true]) {
  test(`origin Concentric preview preserves center and free radius, reversed=${reversed}`, () => {
    const keys = ['$origin:center', 'circle'];
    const document = { entities: [{ id: 'circle', kind: 'circle', center: { x: 0, y: 0 }, radius: 3 }],
      dimensions: [], constraints: [{ id: 'origin', kind: 'Concentric', state: 'Satisfied', referenceKeys: reversed ? keys.reverse() : keys }] };
    const held = applyGeometryDragPreview(document, 'circle|point|center|0|0', { x: 0, y: 0 }, { x: 2, y: 2 });
    if (held) {
      assert.deepEqual(held.entities[0].center, { x: 0, y: 0 });
      assert.equal(held.entities[0].radius, 3);
    }
    const changed = applyGeometryDragPreview(document, 'circle|point|quadrant-0|3|0', { x: 3, y: 0 }, { x: 4, y: 0 });
    assert.ok(changed);
    assert.deepEqual(changed.entities[0].center, { x: 0, y: 0 });
    assert.equal(changed.entities[0].radius, 4);
    assert.deepEqual(document.entities[0].center, { x: 0, y: 0 });
  });
}

import assert from 'node:assert/strict';
import test from 'node:test';
import {applyGeometryDragPreview} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

for (const perpendicular of [false, true]) for (const scale of [0.00001, 1, 10000])
test(`anchored rotation fixed peer/recovery perpendicular=${perpendicular} scale=${scale}`, () => {
  const point = (x, y) => ({x: x * scale, y: y * scale});
  const document = {entities: [
    {id: 'a', kind: 'line', points: [point(-5, 10), point(0, 0)]},
    {id: 'b', kind: 'line', points: [point(-14, 9), perpendicular ? point(-4, 14) : point(-9, -1)]}
  ], dimensions: [], constraints: [
    {id: 'anchor', kind: 'Coincident', state: 'Satisfied', referenceKeys: ['a:end', '$origin:center']},
    {id: 'relation', kind: perpendicular ? 'Perpendicular' : 'Parallel', state: 'Satisfied', referenceKeys: ['a', 'b']}
  ]};
  const start = document.entities[0].points[0], end = point(-3, 11), key = `a|point|start|${start.x}|${start.y}`;
  const locked = {...document, constraints: [...document.constraints, {id: 'fixed', kind: 'Fix', state: 'Satisfied', referenceKeys: ['b']}]};
  assert.equal(applyGeometryDragPreview(locked, key, start, end), null);
  const changed = applyGeometryDragPreview(document, key, start, end);
  assert.ok(changed);
  assert.deepEqual(changed.entities[0].points[1], point(0, 0));
  const length = entity => Math.hypot(entity.points[1].x - entity.points[0].x, entity.points[1].y - entity.points[0].y);
  assert.ok(Math.abs(length(changed.entities[1]) - length(document.entities[1])) < scale * 1e-8);
});

for (const reverse of [false, true]) for (const id of ['a', 'b']) for (const endpoint of ['start', 'end'])
test(`origin Parallel endpoint ${id}:${endpoint} reverse=${reverse}`, () => {
  const document = {entities: [
    {id: 'a', kind: 'line', points: [{x: -5, y: 10}, {x: 0, y: 0}]},
    {id: 'b', kind: 'line', points: [{x: -14, y: 9}, {x: -9, y: -1}]}
  ], dimensions: [], constraints: [
    {id: 'anchor', kind: 'Coincident', state: 'Satisfied', referenceKeys: ['a:end', '$origin:center']},
    {id: 'parallel', kind: 'Parallel', state: 'Satisfied', referenceKeys: reverse ? ['b', 'a'] : ['a', 'b']}
  ]};
  const index = endpoint === 'start' ? 0 : 1;
  const start = document.entities.find(e => e.id === id).points[index], end = {x: start.x + 2, y: start.y + 1};
  const changed = applyGeometryDragPreview(document, `${id}|point|${endpoint}|${start.x}|${start.y}`, start, end);
  if (id === 'a' && endpoint === 'end') { assert.equal(changed, null); return; }
  assert.ok(changed);
  assert.deepEqual(changed.entities[0].points[1], {x: 0, y: 0});
  assert.deepEqual(changed.entities.find(e => e.id === id).points[index], end);
});

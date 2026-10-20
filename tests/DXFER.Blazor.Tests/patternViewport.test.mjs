import assert from 'node:assert/strict';
import test from 'node:test';
import { patternViewportGuides } from '../../src/DXFER.Blazor/wwwroot/canvas/patternViewport.js';
import { positionSnapCue } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
const baseline = {sourceX: 10, sourceY: 10, pitchX: 3, pitchY: 4, angle: 0, centerX: 0, centerY: 0, step: 90};
test('pattern pitch guides follow source and rotated axes', () => {
  const guides = patternViewportGuides({...baseline, patternKind: 'Rectangular', angle: 90});
  assert.ok(Math.abs(guides[0].end.x - 10) < 1e-8);
  assert.ok(Math.abs(guides[0].end.y - 13) < 1e-8);
  assert.ok(Math.abs(guides[1].end.x - 6) < 1e-8);
  assert.ok(Math.abs(guides[1].end.y - 10) < 1e-8);
});
test('radial guides retain center and show the angular step', () => {
  const guides = patternViewportGuides({...baseline, patternKind: 'Radial'});
  assert.deepEqual(guides[0], {start: {x: 0, y: 0}, end: {x: 10, y: 10}});
  assert.ok(Math.abs(guides[1].end.x + 10) < 1e-8);
  assert.ok(Math.abs(guides[1].end.y - 10) < 1e-8);
  assert.deepEqual(patternViewportGuides({...baseline, step: 'bad'}), []);
});
test('viewport widget remains on canvas and clear of palettes', () => {
  const obstacles = [{x: 0, y: 0, width: 160, height: 650}, {x: 650, y: 0, width: 150, height: 360}];
  const box = positionSnapCue({x: 750, y: 20}, {width: 800, height: 650}, 224, 180, obstacles);
  assert.ok(box && box.x >= 160 && box.x + box.width <= 800 && box.y >= 0 && box.y + box.height <= 650);
  for (const r of obstacles) assert.ok(box.x >= r.x + r.width || box.x + box.width <= r.x || box.y >= r.y + r.height || box.y + box.height <= r.y);
});

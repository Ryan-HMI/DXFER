import assert from 'node:assert/strict';
import test from 'node:test';
import { patternViewportGuides, patternViewportAnchors, createPatternCenterQueue } from '../../src/DXFER.Blazor/wwwroot/canvas/patternViewport.js';
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

test('controls use distinct geometric anchors and radial center is exact', () => {
  const anchors = patternViewportAnchors({...baseline, patternKind:'Radial'});
  assert.deepEqual(anchors['center-handle'], {x:0,y:0});
  assert.deepEqual(anchors.source, {x:10,y:10});
  assert.ok(Math.abs(anchors.angle.x) < 1e-10);
  assert.ok(Math.abs(anchors.angle.y - Math.sqrt(200) * .65) < 1e-10);
  const rect = patternViewportAnchors({...baseline,patternKind:'Rectangular'});
  assert.deepEqual(rect.x,{x:11.5,y:10}); assert.deepEqual(rect.y,{x:10,y:12});
  assert.equal(patternViewportAnchors({...baseline,step:NaN}),null);
});

test('center drag coalesces movement but serializes begin, final location and release', async () => {
  const sent=[], waiting=[];
  const queue=createPatternCenterQueue(value=>{sent.push(value);return new Promise(resolve=>waiting.push(resolve));},()=>{},assert.fail);
  const add=(phase,x)=>queue.push({id:'radial',x,y:0,phase});
  add('begin',0); for(let x=1;x<=20;x++)add('update',x); add('end',21);
  assert.equal(sent.length,1); waiting.shift()(); await new Promise(setImmediate);
  assert.equal(sent[1].x,20); waiting.shift()(); await new Promise(setImmediate);
  assert.equal(sent[2].phase,'end'); assert.equal(sent[2].x,21);
  waiting.shift()(); await new Promise(setImmediate); assert.equal(queue.busy,false);
});

test('center drag cancellation drops queued movement and disposal suppresses pending work', async () => {
  const sent=[],waiting=[];
  const queue=createPatternCenterQueue(value=>{sent.push(value);return new Promise(resolve=>waiting.push(resolve));},()=>{},assert.fail);
  for(const phase of ['begin','update','cancel'])queue.push({phase});
  waiting.shift()(); await new Promise(setImmediate);
  assert.deepEqual(sent.map(x=>x.phase),['begin','cancel']);
  queue.push({phase:'update'}); queue.dispose(); waiting.shift()(); await new Promise(setImmediate);
  assert.equal(sent.length,2);
});

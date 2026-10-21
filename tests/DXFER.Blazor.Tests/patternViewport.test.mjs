import assert from 'node:assert/strict';
import test from 'node:test';
import { patternViewportGuides, patternViewportAnchors, patternSourceScreenBounds, createPatternCenterQueue } from '../../src/DXFER.Blazor/wwwroot/canvas/patternViewport.js';
import { positionSnapCue, pickPatternDirection, openDrawingContextMenu } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
const baseline = {sourceX: 10, sourceY: 10, pitchX: 3, pitchY: 4, angle: 0, centerX: 0, centerY: 0, step: 90};
test('radial source obstacle follows actual bounds through pan and zoom', () => {
  const data = {sourceMinX: '9', sourceMinY: '9', sourceMaxX: '11', sourceMaxY: '11'};
  assert.deepEqual(patternSourceScreenBounds(data, {scale: 20, offsetX: 100, offsetY: 400}),
    {x: 272, y: 172, width: 56, height: 56});
  assert.deepEqual(patternSourceScreenBounds(data, {scale: 40, offsetX: 50, offsetY: 600}),
    {x: 402, y: 152, width: 96, height: 96});
  assert.equal(patternSourceScreenBounds({}, {scale: 20, offsetX: 0, offsetY: 0}), null);
  assert.equal(patternSourceScreenBounds({...data, sourceMinX: 'bad'}, {scale: 20, offsetX: 0, offsetY: 0}), null);
});

test('radial card clears source extents as well as palettes at multiple zoom levels', () => {
  const size = {width: 884, height: 650};
  const palettes = [{x: 0, y: 0, width: 160, height: 650}, {x: 720, y: 0, width: 164, height: 270}];
  const overlaps = (a,b) => a.x < b.x+b.width && a.x+a.width > b.x && a.y < b.y+b.height && a.y+a.height > b.y;
  for (const scale of [24, 48, 80]) {
    const view = {scale, offsetX: 500-10*scale, offsetY: 340+10*scale};
    const source = patternSourceScreenBounds({sourceMinX: 9, sourceMinY: 9, sourceMaxX: 11, sourceMaxY: 11}, view);
    const box = positionSnapCue({x:500, y:340},size,216,137,[...palettes,source]);
    assert.ok(box);
    for (const obstacle of [...palettes,source]) assert.equal(overlaps(box,obstacle),false);
  }
});

test('active pattern context requests a view-only menu without changing draft selection', () => {
  const calls = [], selectedKeys = new Set(['seed']);
  const state = {patternEditing: true, activeTool: 'pattern', selectedKeys, activeSelectionKey: 'seed',
    clickCandidate: {}, canvas: {dataset: {}}, dotnetRef: {invokeMethodAsync: (...args) => { calls.push(args); }}};
  const event = {clientX: 300, clientY: 250, preventDefault() {}, stopPropagation() {}};
  openDrawingContextMenu(state, event, 'dimension');
  assert.deepEqual(calls, [['OnContextMenuRequested', null, [], 300, 250]]);
  assert.equal(state.selectedKeys, selectedKeys); assert.equal(state.activeSelectionKey, 'seed');
  assert.equal(state.patternEditing, true); assert.equal(state.clickCandidate, null);
  for (const flag of ['disposed', 'geometryDrag', 'dimensionDrag']) {
    calls.length = 0; state[flag] = true; openDrawingContextMenu(state, event);
    assert.deepEqual(calls, []); delete state[flag];
  }
});
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
test('linear guides use resolved U/V references, signed pitches and independent skew', () => {
  const data={...baseline,patternKind:'Staggered',uX:0,uY:1,vX:.6,vY:.8,pitchX:-3,pitchY:4};
  const guides=patternViewportGuides(data);
  assert.deepEqual(guides[0],{start:{x:10,y:10},end:{x:10,y:7}});
  assert.deepEqual(guides[1],{start:{x:10,y:10},end:{x:12.4,y:13.2}});
  const anchors=patternViewportAnchors(data);
  assert.deepEqual(anchors.x,{x:10,y:8.5});
  assert.ok(Math.abs(anchors.stagger.x-12.4)<1e-10);
  assert.ok(Math.abs(anchors.stagger.y-10.2)<1e-10);
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

test('armed direction picker accepts actual edges without changing selected source or beginning a drag', () => {
  for(const [entity,point,key] of [
    [{id:'edge',kind:'line',points:[{x:0,y:0},{x:10,y:0}]},{x:5,y:0},'edge'],
    [{id:'edge',kind:'line',points:[{x:0,y:0},{x:10,y:0}],isLocked:true},{x:0,y:0},'edge'],
    [{id:'poly',kind:'polyline',points:[{x:0,y:-2},{x:0,y:0},{x:10,y:0}]},{x:5,y:0},'poly|segment|1']
  ]) {
    const calls=[],widget={dataset:{patternId:'pattern',patternDirectionPicking:'U'}};
    const state={patternEditing:true,activeTool:'select',document:{entities:[entity],constraints:[],dimensions:[]},
      view:{scale:20,offsetX:100,offsetY:500},selectedKeys:new Set(['seed']),activeSelectionKey:'seed',
      canvas:{dataset:{},parentElement:{querySelector:()=>widget}},dotnetRef:{invokeMethodAsync:(...args)=>calls.push(args)}};
    assert.equal(pickPatternDirection(state,{x:point.x*20+100,y:500-point.y*20}),true);
    assert.deepEqual(calls,[['OnPatternDirectionPicked','pattern','U',key]]);
    assert.deepEqual([...state.selectedKeys],['seed']); assert.equal(state.activeSelectionKey,'seed');
    assert.equal(state.geometryDrag,undefined);
    widget.dataset.patternDirectionPicking=''; calls.length=0;
    assert.equal(pickPatternDirection(state,{x:200,y:500}),false); assert.deepEqual(calls,[]);
  }
});

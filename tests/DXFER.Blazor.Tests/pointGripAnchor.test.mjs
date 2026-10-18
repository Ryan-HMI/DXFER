import assert from 'node:assert/strict';
import test from 'node:test';
import {createDrawingCanvas} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

function harness(t) {
  const previous = globalThis.window, handlers = new Map(), calls = [];
  globalThis.window = {devicePixelRatio: 1, addEventListener() {}, removeEventListener() {}};
  const context = new Proxy({createRadialGradient: () => ({addColorStop(){}}), measureText: text => ({width: String(text).length * 7})}, {get: (o,k) => k in o ? o[k] : () => {}});
  const canvas = {clientWidth: 1000, clientHeight: 800, dataset: {}, style: {}, getContext: () => context,
    getBoundingClientRect: () => ({left:0,top:0,width:1000,height:800}),
    addEventListener: (key,handler) => handlers.set(key,handler), removeEventListener() {}, focus() {}, setPointerCapture() {}, releasePointerCapture() {}};
  const instance = createDrawingCanvas(canvas,{invokeMethodAsync: (...args) => {calls.push(args); return Promise.resolve(true);}});
  instance.setDocument({entities:[
    {id:'line',kind:'line',points:[{x:-2,y:1},{x:0,y:1}]},
    {id:'source',kind:'line',points:[{x:2,y:3},{x:6,y:3}]}
  ],dimensions:[],constraints:[],bounds:{minX:-2,minY:1,maxX:6,maxY:3}},true);
  t.after(() => {instance.dispose(); if(previous===undefined) delete globalThis.window; else globalThis.window=previous;});
  const screen = (x,y) => ({x:x * +canvas.dataset.scale + +canvas.dataset.offsetX,y:-y * +canvas.dataset.scale + +canvas.dataset.offsetY});
  const pointer = (type,p) => handlers.get(type)({clientX:p.x,clientY:p.y,pointerId:1,pointerType:'mouse',button:0,
    buttons:type==='pointerup'?0:1,preventDefault(){}});
  return {canvas,calls,screen,pointer};
}

for (const offset of [-3,0,3]) test(`off-center point grab ${offset}px releases on the exact midpoint`,t => {
  const h=harness(t),start=h.screen(-2,1),end=h.screen(4,3);
  start.x+=offset; start.y-=offset;
  end.x+=1; end.y-=1;
  h.pointer('pointerdown',start); h.pointer('pointermove',end);
  assert.equal(h.canvas.dataset.hoveredId,'source|point|mid|4|3');
  h.pointer('pointerup',end);
  const request=h.calls.find(c=>c[0]==='OnGeometryDragRequested');
  assert.ok(request);
  assert.deepEqual(request.slice(2,6),[-2,1,4,3]);
});

test('off-center click without a drag does not emit a geometry edit',t => {
  const h=harness(t),p=h.screen(-2,1);p.x+=2;p.y-=2;
  h.pointer('pointerdown',p);h.pointer('pointerup',p);
  assert.ok(!h.calls.some(c=>c[0]==='OnGeometryDragRequested'));
});

test('body drags preserve the actual grabbed location',t => {
  const h=harness(t),start=h.screen(-1.5,1.02),end=h.screen(-1.5,2);
  h.pointer('pointerdown',start);h.pointer('pointermove',end);h.pointer('pointerup',end);
  const request=h.calls.find(c=>c[0]==='OnGeometryDragRequested');
  assert.ok(request);
  assert.equal(request[1],'line');
  assert.ok(Math.abs(request[2]+1.5)<1e-10);
  assert.ok(Math.abs(request[3]-1.02)<1e-10);
});

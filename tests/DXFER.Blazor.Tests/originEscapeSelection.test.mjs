import assert from 'node:assert/strict';
import test from 'node:test';
import {createDrawingCanvas} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

function harness(t) {
  const previous = globalThis.window, keys = new Map(), handlers = new Map(), calls = [];
  globalThis.window = {devicePixelRatio: 1, addEventListener: (key, handler) => keys.set(key, handler), removeEventListener() {}};
  const context = new Proxy({measureText: text => ({width: String(text).length * 7})}, {get: (o, k) => k in o ? o[k] : () => {}});
  const canvas = {clientWidth: 1000, clientHeight: 800, dataset: {}, style: {}, getContext: () => context,
    getBoundingClientRect: () => ({left: 0, top: 0, width: 1000, height: 800}),
    addEventListener: (key, handler) => handlers.set(key, handler), removeEventListener() {}, focus() {}, setPointerCapture() {}, releasePointerCapture() {}};
  const instance = createDrawingCanvas(canvas, {invokeMethodAsync: (...args) => {calls.push(args); return Promise.resolve(true);}});
  instance.setDocument({entities: [{id: 'line', kind: 'line', points: [{x: 2, y: 2}, {x: 6, y: 5}]}], dimensions: [], constraints: [],
    bounds: {minX: -5, minY: -5, maxX: 10, maxY: 10}}, true);
  t.after(() => {instance.dispose(); if (previous === undefined) delete globalThis.window; else globalThis.window = previous;});
  const pointer = (type, x, y) => handlers.get(type)({clientX: x * +canvas.dataset.scale + +canvas.dataset.offsetX,
    clientY: -y * +canvas.dataset.scale + +canvas.dataset.offsetY, pointerId: 1, pointerType: 'mouse', button: 0,
    buttons: type === 'pointerup' ? 0 : 1, preventDefault() {}});
  const click = (x, y) => {pointer('pointermove', x, y); pointer('pointerdown', x, y); pointer('pointerup', x, y);};
  const escape = () => keys.get('keydown')({key: 'Escape', target: canvas, preventDefault() {}});
  return {instance, canvas, calls, pointer, click, escape};
}

for (const tool of ['line', 'centercircle', 'point']) test(`origin snap is committed explicitly by ${tool}`, t => {
  const h = harness(t); h.instance.setActiveTool(tool);
  h.click(0.01, 0.01);
  if (tool !== 'point') h.click(-3, 4);
  const commit = h.calls.find(c => c[0] === 'OnSketchToolCommitted');
  assert.ok(commit);
  assert.deepEqual(commit[2].slice(0, 2), [0, 0]);
  assert.equal(commit[6], true);
});

for (const tool of ['select', 'line', 'parallel']) test(`Escape clears canvas and parent state in ${tool}`, t => {
  const h = harness(t); h.instance.setActiveTool(tool); h.instance.selectEntities(['line']);
  h.escape();
  assert.equal(h.canvas.dataset.activeTool, 'select');
  assert.deepEqual(h.instance.getSelectedKeys(), []);
  assert.equal(h.instance.getActiveSelectionKey(), null);
  assert.ok(h.calls.some(c => c[0] === 'OnSketchToolCanceled'));
});

for (const selected of [false, true]) test(`drag preserves deliberate selection selected=${selected}`, t => {
  const h = harness(t);
  if (selected) h.instance.selectEntities(['line']);
  h.pointer('pointerdown', 6, 5); h.pointer('pointermove', 7, 6); h.pointer('pointerup', 7, 6);
  assert.deepEqual(h.instance.getSelectedKeys(), selected ? ['line'] : []);
  assert.ok(h.calls.some(c => c[0] === 'OnGeometryDragRequested'));
});

test('Escape during a held drag cancels without committing on release', t => {
  const h = harness(t);
  h.pointer('pointerdown', 6, 5); h.pointer('pointermove', 7, 6); h.escape(); h.pointer('pointerup', 7, 6);
  assert.ok(!h.calls.some(c => c[0] === 'OnGeometryDragRequested'));
  assert.deepEqual(h.instance.getSelectedKeys(), []);
});

import assert from 'node:assert/strict';
import test from 'node:test';
import {createDrawingCanvas} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

function harness(t) {
  const previous = globalThis.window, handlers = new Map();
  globalThis.window = {devicePixelRatio: 1, addEventListener() {}, removeEventListener() {}};
  const context = new Proxy({createRadialGradient: () => ({addColorStop(){}}), measureText: text => ({width: String(text).length * 7})}, {get: (o, k) => k in o ? o[k] : () => {}});
  const canvas = {clientWidth: 1000, clientHeight: 800, dataset: {}, style: {}, getContext: () => context,
    getBoundingClientRect: () => ({left: 0, top: 0, width: 1000, height: 800}),
    addEventListener: (key, handler) => handlers.set(key, handler), removeEventListener() {}, focus() {}, setPointerCapture() {}, releasePointerCapture() {}};
  const instance = createDrawingCanvas(canvas, {invokeMethodAsync: () => Promise.resolve(true)});
  instance.setDocument({entities: [], dimensions: [], constraints: [], bounds: {minX: -5, minY: -5, maxX: 10, maxY: 10}}, true);
  t.after(() => {instance.dispose(); if (previous === undefined) delete globalThis.window; else globalThis.window = previous;});
  const wheel = (deltaY, deltaMode = 0) => handlers.get('wheel')({clientX: 370, clientY: 290, deltaY, deltaMode, preventDefault() {}});
  const view = () => ({scale: +canvas.dataset.scale, x: (370 - +canvas.dataset.offsetX) / +canvas.dataset.scale,
    y: (+canvas.dataset.offsetY - 290) / +canvas.dataset.scale});
  return {instance, canvas, handlers, wheel, view};
}

for (const mode of [0, 1, 2]) test(`wheel inversion preserves anchoring and sensitivity, deltaMode ${mode}`, t => {
  const h = harness(t), initial = h.view();
  h.wheel(-1, mode);
  const normal = h.view();
  assert.ok(normal.scale > initial.scale);
  h.instance.setInvertWheelZoom(true);
  h.wheel(-1, mode);
  const inverted = h.view();
  assert.ok(inverted.scale < normal.scale);
  assert.ok(Math.abs(inverted.scale - initial.scale) < 1e-8);
  for (const v of [normal, inverted]) {
    assert.ok(Math.abs(v.x - initial.x) < 1e-8);
    assert.ok(Math.abs(v.y - initial.y) < 1e-8);
  }
  h.instance.setInvertWheelZoom(false);
  h.wheel(-1, mode);
  assert.ok(Math.abs(h.view().scale - normal.scale) < 1e-8);
});

test('polygon wheel side-count gesture does not become zoom when inverted', t => {
  const h = harness(t);
  h.instance.setActiveTool('inscribedpolygon');
  const event = {clientX: 450, clientY: 400, pointerId: 1, pointerType: 'mouse', button: 0, buttons: 1, preventDefault() {}};
  h.handlers.get('pointerdown')(event);
  h.handlers.get('pointerup')({...event, buttons: 0});
  const initial = h.view();
  h.instance.setInvertWheelZoom(true);
  h.wheel(-120);
  assert.equal(h.view().scale, initial.scale);
});

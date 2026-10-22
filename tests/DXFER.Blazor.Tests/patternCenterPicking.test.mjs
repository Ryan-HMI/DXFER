import test from 'node:test';
import assert from 'node:assert/strict';
import {bindPatternCenterHandle} from '../../src/DXFER.Blazor/wwwroot/canvas/patternViewport.js';
import {createDrawingCanvas} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

function handle(t) {
  const events = new Map(), calls = [], picks = [], previews = [];
  let captured = false;
  const target = {disabled: false, closest: () => target};
  const widget = {dataset: {patternId: 'radial', centerX: '2', centerY: '3'},
    addEventListener: (name, fn) => events.set(name, fn), removeEventListener: name => events.delete(name),
    setPointerCapture() { captured = true; }, hasPointerCapture: () => captured,
    releasePointerCapture() { captured = false; }};
  const dispose = bindPatternCenterHandle(widget, {toWorld: e => ({x: e.clientX / 10, y: e.clientY / 10}),
    send: async value => calls.push(value), preview: value => previews.push(value), select: value => picks.push(value), onError: assert.fail});
  t.after(dispose);
  const send = (name, x = 20, y = 30, extra = {}) => events.get(name)({target, clientX: x, clientY: y,
    pointerId: 1, button: 0, preventDefault() {}, stopPropagation() {}, ...extra});
  return {send, calls, picks, previews};
}

test('center click selects without starting a pattern edit; Shift is preserved', async t => {
  const h = handle(t);
  h.send('pointerdown'); h.send('pointermove', 21, 31); h.send('pointerup', 21, 31, {shiftKey: true});
  await new Promise(setImmediate);
  assert.deepEqual(h.picks, [true]); assert.deepEqual(h.calls, []); assert.deepEqual(h.previews, []);
});

test('center drag retains staged lifecycle and never also selects', async t => {
  const h = handle(t);
  h.send('pointerdown', 21, 31); h.send('pointermove', 41, 51); h.send('pointerup', 51, 61);
  await new Promise(setImmediate);
  assert.deepEqual(h.calls.map(v => v.phase), ['begin', 'update', 'end']);
  assert.deepEqual(h.calls.at(-1), {id: 'radial', x: 5, y: 6, phase: 'end'});
  assert.deepEqual(h.picks, []);
});

test('center cancellation does not select or commit; keyboard selection works', async t => {
  const h = handle(t);
  h.send('pointerdown'); h.send('pointercancel');
  h.send('keydown', 20, 30, {key: 'Enter'});
  h.send('keydown', 20, 30, {key: ' ', shiftKey: true});
  await new Promise(setImmediate);
  assert.deepEqual(h.calls, []); assert.deepEqual(h.picks, [false, true]);
});

function canvas(t) {
  const previous = globalThis.window, calls = [];
  globalThis.window = {devicePixelRatio: 1, addEventListener() {}, removeEventListener() {}};
  const context = new Proxy({createRadialGradient: () => ({addColorStop(){}}), measureText: text => ({width: String(text).length * 7})}, {get: (o, k) => k in o ? o[k] : () => {}});
  const element = {clientWidth: 1000, clientHeight: 800, dataset: {}, style: {}, getContext: () => context,
    getBoundingClientRect: () => ({left: 0, top: 0, width: 1000, height: 800}),
    addEventListener() {}, removeEventListener() {}, focus() {}, setPointerCapture() {}, releasePointerCapture() {}};
  const instance = createDrawingCanvas(element, {invokeMethodAsync: (...args) => {calls.push(args); return Promise.resolve(true);}});
  instance.setDocument({entities: [{id: 'center', kind: 'point', points: [{x: 2, y: 3}], isConstruction: true},
    {id: 'other', kind: 'point', points: [{x: 8, y: 9}]}], dimensions: [], constraints: [],
    bounds: {minX: -5, minY: -5, maxX: 10, maxY: 10}}, true);
  t.after(() => {instance.dispose(); if (previous === undefined) delete globalThis.window; else globalThis.window = previous;});
  return {instance, element, calls};
}

test('committed center selection replaces seed selection unless additive or constraint tool', t => {
  const h = canvas(t);
  h.instance.selectEntities(['other']); h.instance.pickPatternCenter('center');
  assert.deepEqual(h.instance.getSelectedKeys(), ['center']);
  h.instance.selectEntities(['other']); h.instance.pickPatternCenter('center', true);
  assert.deepEqual(h.instance.getSelectedKeys(), ['other', 'center']);
  h.instance.selectEntities(['other']); h.instance.setActiveTool('coincident'); h.instance.pickPatternCenter('center');
  assert.deepEqual(h.instance.getSelectedKeys(), ['other', 'center']);
  assert.ok(h.calls.some(v => v[0] === 'OnSelectionChangedFromCanvas'));
});

test('dimension tool consumes pattern center as a point, not a pattern-edit action', t => {
  const h = canvas(t);
  h.instance.setActiveTool('dimension'); h.instance.pickPatternCenter('center');
  assert.deepEqual(h.instance.getSelectedKeys(), []);
  assert.equal(h.element.dataset.dimensionDraftKeys, 'center');
  h.instance.pickPatternCenter('other');
  assert.equal(h.element.dataset.dimensionDraftKeys, 'center,other');
  assert.equal(h.element.dataset.dimensionDraftComplete, 'true');
  assert.ok(!h.calls.some(v => v[0] === 'OnPatternCenterChanged'));
});

import test from 'node:test';
import assert from 'node:assert/strict';
import {resolvePatternCenterSnap, getCurrentSnapCue} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
import {bindPatternCenterHandle} from '../../src/DXFER.Blazor/wwwroot/canvas/patternViewport.js';

const p = (x, y) => ({x, y});
const state = entities => ({activeTool: 'circularpattern', document: {entities, dimensions: [], constraints: []},
  acquiredSnapPoints: [], selectedKeys: new Set(), view: {scale: 20, offsetX: 100, offsetY: 500}});
const line = {id: 'edge', kind: 'line', points: [p(2, 3), p(12, 3)]};
const circle = {id: 'circle', kind: 'circle', center: p(10, 10), radius: 2};
for (const [label, entities, near, expected, kind, key] of [
  ['origin', [], p(.1, .1), p(0, 0), 'Coincident', '$origin:center'],
  ['endpoint', [line], p(2.1, 3.1), p(2, 3), 'Coincident', 'edge:start'],
  ['midpoint', [line], p(7.1, 3.1), p(7, 3), 'Midpoint', 'edge'],
  ['circle center', [circle], p(10.1, 10.1), p(10, 10), 'Coincident', 'circle:center'],
  ['edge body', [line], p(5, 3.1), p(5, 3), 'PointOnLine', 'edge']
]) test(`pattern center acquires ${label} with durable intent`, () => {
  const s = state(entities), before = structuredClone(s.document);
  const snap = resolvePatternCenterSnap(s, near);
  assert.deepEqual(snap.point, expected);
  assert.deepEqual(snap.intents, [{point: expected, kind, referenceKeys: [key]}]);
  assert.deepEqual(s.document, before); assert.equal(s.activeTool, 'circularpattern');
  assert.ok(getCurrentSnapCue({...s, patternCenterSnap: snap}), 'Pattern preview has a visible snap cue');
});

test('pattern center never snaps to its own center or generated geometry', () => {
  const s = state([{id: 'center', kind: 'point', points: [p(8, 8)]}, {...circle, id: 'member'}]);
  for (const point of [p(8.1, 8.1), p(10.1, 10.1)]) {
    const snap = resolvePatternCenterSnap(s, point, ['center', 'member']);
    assert.deepEqual(snap, {point, target: null, intents: []});
  }
});

test('moving away from an acquired snap clears its intent', () => {
  const s = state([line]);
  assert.equal(resolvePatternCenterSnap(s, p(7, 3)).intents.length, 1);
  assert.deepEqual(resolvePatternCenterSnap(s, p(20, 20)), {point: p(20, 20), target: null, intents: []});
});

for (const automatic of [true, false]) test(`off-center handle grab snaps its center; auto ${automatic}`, async () => {
  const events = new Map(), calls = []; let captured = false, cleared = 0;
  const handle = {disabled: false, closest: () => handle};
  const widget = {dataset: {patternId: 'radial', centerX: '2', centerY: '3'},
    addEventListener: (name, fn) => events.set(name, fn), removeEventListener: name => events.delete(name),
    setPointerCapture() {captured = true;}, hasPointerCapture: () => captured, releasePointerCapture() {captured = false;}};
  const dispose = bindPatternCenterHandle(widget, {toWorld: e => p(e.clientX / 10, e.clientY / 10),
    send: async value => calls.push(value), preview() {}, onError: assert.fail,
    resolveSnap: point => {
      assert.ok(Math.hypot(point.x - .1, point.y - .1) < 1e-12);
      return {point: p(0, 0), intents: automatic ? [{point: p(0, 0), kind: 'Coincident', referenceKeys: ['$origin:center']}] : []};
    }, clearSnap() {cleared++;}});
  const send = (name, x, y) => events.get(name)({target: handle, clientX: x, clientY: y, pointerId: 1,
    button: 0, preventDefault() {}, stopPropagation() {}});
  send('pointerdown', 21, 31);
  // Preserve the off-center pixel offset before acquiring the snap.
  send('pointermove', 2, 2); send('pointerup', 2, 2);
  await new Promise(setImmediate);
  assert.equal(calls.at(-1).phase, 'end'); assert.equal(calls.at(-1).x, 0); assert.equal(calls.at(-1).y, 0);
  assert.equal(calls.at(-1).creationSnaps.length, automatic ? 1 : 0);
  assert.ok(cleared > 0); dispose();
});

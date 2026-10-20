import assert from 'node:assert/strict';
import test from 'node:test';
import {createDrawingCanvas, getPlaneGcsDragSnapIntent} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const p = (x, y) => ({x, y});
const documentOf = (x = 0, y = 0) => ({entities: [{id: 'circle', kind: 'circle', center: p(x, y), radius: 2}],
  constraints: [], dimensions: []});
const flush = () => new Promise(resolve => setImmediate(resolve));
const result = (x, y) => ({engine: 'PlaneGCS', document: documentOf(x, y), reason: ''});
const close = (actual, expected) => assert.ok(Math.abs(actual - expected) < 1e-9, `${actual} != ${expected}`);

function harness(t, config = {solverEngine: 'PlaneGCS'}, original = documentOf()) {
  const oldWindow = globalThis.window, handlers = new Map(), keys = new Map(), requests = [], commits = [], calls = [];
  globalThis.window = {devicePixelRatio: 1, addEventListener: (name, handler) => keys.set(name, handler), removeEventListener() {}};
  const context = new Proxy({measureText: text => ({width: String(text).length * 7}),
    createRadialGradient: () => ({addColorStop() {}})}, {get: (target, key) => target[key] ?? (() => {})});
  const canvas = {width: 0, height: 0, clientWidth: 1000, clientHeight: 800, dataset: {}, style: {},
    getContext: () => context, getBoundingClientRect: () => ({left: 0, top: 0, width: 1000, height: 800}),
    addEventListener: (name, handler) => handlers.set(name, handler), removeEventListener() {},
    focus() {}, setPointerCapture() {}, releasePointerCapture() {}, removeAttribute() {}};
  const instance = createDrawingCanvas(canvas, {invokeMethodAsync: (method, ...args) => {
    calls.push({method, args});
    if (method === 'GetPlaneGcsDragPreview') return new Promise((resolve, reject) => requests.push({args, resolve, reject}));
    if (method === 'OnGeometryDragRequested') return new Promise((resolve, reject) => commits.push({args, resolve, reject}));
    return Promise.resolve();
  }}, null, {}, config);
  instance.setDocument(original, true);
  t.after(() => {
    instance.dispose();
    if (oldWindow === undefined) delete globalThis.window;
    else globalThis.window = oldWindow;
  });
  const pointer = (type, point, shiftKey = false) => handlers.get(type)({pointerId: 1, pointerType: 'mouse', button: 0,
    buttons: type === 'pointerup' ? 0 : 1, shiftKey, preventDefault() {},
    clientX: point.x * +canvas.dataset.scale + +canvas.dataset.offsetX,
    clientY: -point.y * +canvas.dataset.scale + +canvas.dataset.offsetY});
  const center = (x, y) => {
    const actual = canvas.dataset.firstCenter.split(',').map(Number);
    close(actual[0], x); close(actual[1], y);
    assert.equal(canvas.dataset.firstRadius, '2');
  };
  const escape = () => keys.get('keydown')({key: 'Escape', target: canvas, preventDefault() {}});
  const begin = (end = p(1, 1), start = p(0, 0), shift = false) => {
    pointer('pointerdown', start); pointer('pointermove', end, shift);
  };
  return {instance, canvas, requests, commits, calls, pointer, center, escape, begin};
}

test('opt-in routes ordinary drags to PlaneGCS and coalesces to the latest pointer and modifier', async t => {
  const h = harness(t);
  h.begin(); await flush();
  assert.equal(h.requests.length, 1); h.center(0, 0);
  h.pointer('pointermove', p(2, 2)); h.pointer('pointermove', p(3, 4), true);
  assert.equal(h.requests.length, 1);
  assert.deepEqual(h.requests[0].args.slice(1), [p(0, 0), p(1, 1), false, null]);
  h.requests[0].resolve(result(1, 1)); await flush();
  h.center(0, 0);
  assert.equal(h.requests.length, 2);
  assert.deepEqual(h.requests[1].args.slice(1), [p(0, 0), p(3, 4), true, null]);
  h.requests[1].resolve(result(3, 0)); await flush(); h.center(3, 0);
  h.pointer('pointermove', p(3, 4), true); await flush();
  assert.equal(h.requests.length, 2, 'Identical pointer/modifier does not repeat solved request');
  assert.ok(h.calls.every(c => c.method !== 'GetOffsetDragPreview'));
});

test('same position with a superseded modifier cannot paint', async t => {
  const h = harness(t); h.begin(); await flush();
  h.pointer('pointermove', p(1, 1), true);
  h.requests[0].resolve(result(1, 1)); await flush(); h.center(0, 0);
  assert.equal(h.requests[1].args[3], true);
  h.requests[1].resolve(result(1, 0)); await flush(); h.center(1, 0);
});

for (const response of [null, {engine: 'Legacy', document: documentOf(9, 9)},
  {engine: 'PlaneGCS', document: null, reason: 'Unavailable'}, {engine: 'PlaneGCS', document: {}}]) {
  test(`no legacy fallback on ${JSON.stringify(response)}`, async t => {
    const h = harness(t); h.begin(); await flush();
    h.requests[0].resolve(response); await flush(); h.center(0, 0);
    assert.equal(h.requests.length, 1);
    assert.ok(h.calls.every(c => c.method !== 'GetOffsetDragPreview'));
  });
}

test('null current result restores the baseline rather than leaving an older successful preview', async t => {
  const h = harness(t); h.begin(); await flush();
  h.requests[0].resolve(result(1, 1)); await flush(); h.center(1, 1);
  h.pointer('pointermove', p(3, 3)); await flush();
  h.requests[1].resolve({engine: 'PlaneGCS', document: null, reason: 'Fixed geometry'}); await flush();
  h.center(0, 0); assert.equal(h.canvas.dataset.planeGcsPreviewReason, 'Fixed geometry');
});

test('preview rejection has no fallback and still drains the latest queued pointer', async t => {
  t.mock.method(console, 'error', () => {});
  const h = harness(t); h.begin(); await flush(); h.pointer('pointermove', p(3, 3));
  h.requests[0].reject(new Error('isolated runtime unavailable')); await flush(); h.center(0, 0);
  assert.equal(h.requests.length, 2);
  h.requests[1].resolve(result(3, 3)); await flush(); h.center(3, 3);
  assert.ok(h.calls.every(c => c.method !== 'GetOffsetDragPreview'));
});

test('Escape invalidates both in-flight and queued work; a new session waits for the old invocation', async t => {
  const h = harness(t); h.begin(); await flush();
  h.pointer('pointermove', p(2, 2)); h.escape(); h.center(0, 0);
  h.begin(p(3, 3)); await flush(); assert.equal(h.requests.length, 1);
  h.requests[0].resolve(result(1, 1)); await flush(); h.center(0, 0);
  assert.equal(h.requests.length, 2); assert.deepEqual(h.requests[1].args[2], p(3, 3));
  h.requests[1].resolve(result(3, 3)); await flush(); h.center(3, 3);
  assert.equal(h.commits.length, 0);
});

test('document replacement invalidates old preview and pointer release', async t => {
  const h = harness(t); h.begin(); await flush();
  h.instance.setDocument(documentOf(10, 10));
  h.requests[0].resolve(result(1, 1)); await flush(); h.center(10, 10);
  h.pointer('pointerup', p(3, 3)); assert.equal(h.commits.length, 0);
});

test('pointercancel invalidates pending preview', async t => {
  const h = harness(t); h.begin(); await flush();
  h.pointer('pointercancel', p(1, 1));
  h.requests[0].resolve(result(1, 1)); await flush(); h.center(0, 0);
  assert.equal(h.commits.length, 0);
});

test('release submits the exact final pointer/modifier once without waiting for preview', async t => {
  const h = harness(t); h.begin(); await flush();
  h.pointer('pointermove', p(2, 2)); h.pointer('pointerup', p(4, 5), true);
  assert.equal(h.commits.length, 1);
  assert.deepEqual(h.commits[0].args.slice(1), [0, 0, 4, 5, true, null]);
  h.requests[0].resolve(result(1, 1)); await flush(); h.center(0, 0);
  assert.equal(h.requests.length, 1, 'Queued previews never dispatch after release');
  h.instance.setDocument(documentOf(4, 0)); h.commits[0].resolve(true); await flush(); h.center(4, 0);
});

test('release before preview dispatch still commits exactly once', async t => {
  const h = harness(t); h.begin(); h.pointer('pointerup', p(4, 5)); await flush();
  assert.equal(h.requests.length, 0);
  assert.equal(h.commits.length, 1);
  assert.deepEqual(h.commits[0].args.slice(1), [0, 0, 4, 5, false, null]);
  h.commits[0].resolve(false); await flush(); h.center(0, 0);
});

test('release back at the original pointer cancels motion without history commit', async t => {
  const h = harness(t); h.begin(); await flush(); h.requests[0].resolve(result(1, 1)); await flush();
  h.pointer('pointerup', p(0, 0)); await flush(); h.center(0, 0);
  assert.equal(h.commits.length, 0); assert.equal(h.requests.length, 1);
});

test('release preserves existing snap-to-peer coordinates, not the last preview or raw pointer', async t => {
  const original = documentOf(); original.entities.push({id: 'peer', kind: 'point', points: [p(5, 5)]});
  const h = harness(t, {solverEngine: 'PlaneGCS'}, original);
  h.begin(); await flush();
  h.pointer('pointerup', p(5.001, 5.001));
  assert.deepEqual(h.commits[0].args.slice(1, 5), [0, 0, 5, 5]);
  h.requests[0].resolve(null); h.commits[0].resolve(false); await flush();
});

test('rejected commit restores optimistic preview only while its document and session still own it', async t => {
  const h = harness(t); h.begin(); await flush();
  h.requests[0].resolve(result(1, 1)); await flush();
  h.pointer('pointerup', p(2, 2)); h.center(1, 1);
  h.commits[0].resolve(false); await flush(); h.center(0, 0);
  h.begin(); await flush(); h.requests[1].resolve(result(1, 1)); await flush();
  h.pointer('pointerup', p(2, 2));
  h.instance.setDocument(documentOf(9, 9));
  h.commits[1].resolve(false); await flush(); h.center(9, 9);
});

test('older commit rejection cannot rewind a newer drag before its preview resolves', async t => {
  const h = harness(t); h.begin(); await flush();
  h.requests[0].resolve(result(1, 1)); await flush(); h.pointer('pointerup', p(1, 1));
  h.begin(p(3, 3), p(1, 1)); await flush();
  h.commits[0].resolve(false); await flush(); h.center(1, 1);
  h.requests[1].resolve(result(3, 3)); await flush(); h.center(3, 3);
});

test('disposed canvas cannot paint or dispatch queued results', async t => {
  const h = harness(t); h.begin(); await flush(); h.pointer('pointermove', p(2, 2));
  h.instance.dispose();
  const before = h.canvas.dataset.firstCenter;
  h.requests[0].resolve(result(1, 1)); await flush();
  assert.equal(h.canvas.dataset.firstCenter, before); assert.equal(h.requests.length, 1);
});

test('engine switch invalidates PlaneGCS work and preserves legacy opt-out behavior', async t => {
  const h = harness(t); h.begin(); await flush(); h.instance.setSolverEngine('Legacy');
  h.requests[0].resolve(result(1, 1)); await flush(); h.center(0, 0);
  h.begin(p(2, 2)); h.center(2, 2);
  assert.equal(h.requests.length, 1);
});

for (const config of [{}, {solverEngine: 'Legacy'}]) test(`legacy default is unchanged: ${JSON.stringify(config)}`, t => {
  const h = harness(t, config); h.begin(); h.center(1, 1);
  assert.equal(h.requests.length, 0);
});

test('PlaneGCS also bypasses the legacy shared offset/pattern preview route', async t => {
  const original = documentOf(); original.hasPatterns = true;
  const h = harness(t, {solverEngine: 'PlaneGCS'}, original); h.begin(); await flush();
  assert.equal(h.requests.length, 1); assert.ok(h.calls.every(c => c.method !== 'GetOffsetDragPreview'));
  h.requests[0].resolve(result(1, 1)); await flush(); h.center(1, 1);
});

const snap = (kind, ...referenceKeys) => ({id: 'drag-snap', kind, referenceKeys, state: 'Satisfied'});
for (const automatic of [false, true]) {
  for (const [name, peer, expected] of [
    ['point', {id: 'peer', kind: 'point', points: [p(5, 5)]}, snap('Coincident', 'circle:center', 'peer')],
    ['endpoint', {id: 'peer', kind: 'line', points: [p(5, 5), p(9, 5)]}, snap('Coincident', 'circle:center', 'peer:start')],
    ['midpoint', {id: 'peer', kind: 'line', points: [p(3, 5), p(7, 5)]}, snap('Midpoint', 'circle:center', 'peer')],
    ['polyline-midpoint', {id: 'peer', kind: 'polyline', points: [p(3, 5), p(7, 5), p(9, 7)]}, snap('Midpoint', 'circle:center', 'peer|segment|0')],
    ['center', {id: 'peer', kind: 'circle', center: p(5, 5), radius: 1}, snap('Coincident', 'circle:center', 'peer:center')]
  ]) test(`preview/release share normalized ${name} snap with AutoSnap ${automatic}`, async t => {
    const original = documentOf(); original.entities.push(peer);
    const h = harness(t, {solverEngine: 'PlaneGCS'}, original);
    h.instance.setAutomaticSnapConstraints(automatic);
    h.begin(p(5.001, 5.001)); await flush();
    assert.deepEqual(h.requests[0].args[4], expected);
    h.pointer('pointerup', p(5.001, 5.001));
    assert.deepEqual(h.commits[0].args[6], expected);
    assert.deepEqual(h.commits[0].args.slice(3, 5), [5, 5]);
    h.requests[0].resolve(null); h.commits[0].resolve(false); await flush();
  });
}

test('release recomputes snap intent instead of retaining an earlier in-flight target', async t => {
  const original = documentOf();
  original.entities.push({id: 'a', kind: 'point', points: [p(5, 5)]}, {id: 'b', kind: 'point', points: [p(9, 9)]});
  const h = harness(t, {solverEngine: 'PlaneGCS'}, original);
  h.begin(p(5, 5)); await flush();
  assert.deepEqual(h.requests[0].args[4], snap('Coincident', 'circle:center', 'a'));
  h.pointer('pointerup', p(9, 9));
  assert.deepEqual(h.commits[0].args[6], snap('Coincident', 'circle:center', 'b'));
  h.requests[0].resolve(null); h.commits[0].resolve(false); await flush();
});

test('leaving a snap clears the release intent even before preview completes', async t => {
  const original = documentOf(); original.entities.push({id: 'peer', kind: 'point', points: [p(5, 5)]});
  const h = harness(t, {solverEngine: 'PlaneGCS'}, original);
  h.begin(p(5, 5)); await flush(); h.pointer('pointerup', p(9, 7));
  assert.equal(h.commits[0].args[6], null);
  h.requests[0].resolve(null); h.commits[0].resolve(false); await flush();
});

test('coalescing carries only the latest snap and accepted cues follow a moved target', async t => {
  const original = documentOf();
  original.entities.push({id: 'a', kind: 'point', points: [p(5, 5)]}, {id: 'b', kind: 'point', points: [p(9, 9)]});
  const h = harness(t, {solverEngine: 'PlaneGCS'}, original);
  h.begin(p(5, 5)); await flush(); h.pointer('pointermove', p(9, 9));
  h.requests[0].resolve({engine: 'PlaneGCS', document: original}); await flush();
  assert.deepEqual(h.requests[1].args[4], snap('Coincident', 'circle:center', 'b'));
  const moved = structuredClone(original); moved.entities[0].center = p(10, 10); moved.entities[2].points = [p(10, 10)];
  h.requests[1].resolve({engine: 'PlaneGCS', document: moved}); await flush(); h.center(10, 10);
  assert.ok(h.canvas.dataset.snapCue, 'Accepted coincidence still has a cue after its target moved');
});

test('snap normalization rejects same-entity, unsupported incidence and unrepresentable handles', () => {
  const original = documentOf(), peer = {id: 'peer', kind: 'line', points: [p(3, 5), p(7, 5)]};
  original.entities.push(peer);
  const state = {document: original}, drag = {originalDocument: original, targetKey: 'circle|point|center|0|0'};
  const target = {key: 'peer', entityId: 'peer', entity: peer, kind: 'entity', snapPoint: p(4, 5)};
  assert.equal(getPlaneGcsDragSnapIntent(state, drag, target, p(4, 5)), null, 'PointOnLine is outside this transport contract');
  const self = {key: 'circle|point|center|0|0', entityId: 'circle', entity: original.entities[0], kind: 'point', point: p(0, 0)};
  assert.equal(getPlaneGcsDragSnapIntent(state, drag, self, p(0, 0)), null);
  assert.equal(getPlaneGcsDragSnapIntent(state, {...drag, targetKey: 'circle|point|quadrant-0|2|0'},
    {...target, kind: 'point', key: 'peer|point|start|3|5', label: 'start', point: p(3, 5)}, p(3, 5)), null);
  assert.equal(state.document, original);
});

test('legacy commit supplies an explicit null intent without changing its solve path', async t => {
  const h = harness(t, {solverEngine: 'Legacy'}); h.begin(); h.pointer('pointerup', p(3, 3));
  assert.equal(h.commits[0].args.length, 7); assert.equal(h.commits[0].args[6], null);
  h.commits[0].resolve(true); await flush();
});

test('origin snap retains the virtual point identity for preview and release', async t => {
  const h = harness(t, {solverEngine: 'PlaneGCS'}, documentOf(4, 4));
  h.instance.setOriginAxesVisible(true);
  h.begin(p(0, 0), p(4, 4)); await flush();
  const expected = snap('Coincident', 'circle:center', '$origin:center');
  assert.deepEqual(h.requests[0].args[4], expected);
  h.pointer('pointerup', p(0, 0)); assert.deepEqual(h.commits[0].args[6], expected);
  h.requests[0].resolve(null); h.commits[0].resolve(false); await flush();
});

for (const [key, expected] of [
  ['poly|point|vertex-0|1|1', 'poly|segment|0:start'],
  ['poly|point|vertex-2|3|3', 'poly|segment|1:end']
]) test(`selected polyline point has persistent identity: ${key}`, () => {
  const poly = {id: 'poly', kind: 'polyline', points: [p(1, 1), p(2, 2), p(3, 3)]};
  const peer = {id: 'peer', kind: 'circle', center: p(8, 8), radius: 1};
  const doc = {entities: [poly, peer], constraints: [], dimensions: []};
  const target = {key: 'peer|point|center|8|8', entityId: 'peer', entity: peer, kind: 'point', point: p(8, 8), label: 'center'};
  assert.deepEqual(getPlaneGcsDragSnapIntent({document: doc}, {originalDocument: doc, targetKey: key}, target, p(8, 8)),
    snap('Coincident', expected, 'peer:center'));
});

import test from 'node:test';
import assert from 'node:assert/strict';
import {performance} from 'node:perf_hooks';
import {radialCenterTransform, createRadialCenterPreview, bindPatternCenterHandle} from '../../src/DXFER.Blazor/wwwroot/canvas/patternViewport.js';
import {createDrawingCanvas} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const center = {x: 2, y: 3};
function fixture() {
  return {entities: [
    {id: 'source', kind: 'circle', center: {x: 12, y: 3}, points: [], radius: 2},
    {id: 'one', kind: 'circle', center: {x: 2, y: 13}, points: [], radius: 2},
    {id: 'two', kind: 'circle', center: {x: -8, y: 3}, points: [], radius: 2},
    {id: 'center', kind: 'point', points: [{...center}], isConstruction: true}
  ], constraints: [{kind: 'Fixed', referenceKeys: ['source']}], dimensions: [], radialCenterPreviews: [
    {id: 'radial', center: {...center}, centerPointId: 'center', stepAngleDegrees: 90,
      members: [{entityId: 'one', column: 1}, {entityId: 'two', column: 2}]}
  ]};
}
const near = (actual, expected) => {
  assert.ok(Math.abs(actual.x - expected.x) < 1e-12, `${actual.x} != ${expected.x}`);
  assert.ok(Math.abs(actual.y - expected.y) < 1e-12, `${actual.y} != ${expected.y}`);
};
const freeze = value => {
  if (value && typeof value === 'object') { Object.values(value).forEach(freeze); Object.freeze(value); }
  return value;
};

test('local center transform preserves source, radii and immutable baseline across repeated movement', () => {
  const document = freeze(fixture()), transform = radialCenterTransform(document, 'radial', center);
  const moved = transform({x: 5, y: 7});
  near(moved.entities[1].center, {x: 9, y: 14});
  near(moved.entities[2].center, {x: -2, y: 11});
  assert.deepEqual(moved.entities[3].points, [{x: 5, y: 7}]);
  assert.equal(moved.entities[0], document.entities[0]);
  assert.equal(moved.constraints, document.constraints);
  assert.equal(moved.entities[1].radius, 2);
  assert.deepEqual(transform(center), document);
  assert.deepEqual(transform({x: 5, y: 7}), moved);
  assert.deepEqual(document, fixture());
});

for (const kind of ['line', 'polyline', 'arc', 'ellipse', 'polygon', 'spline', 'point']) {
  test(`${kind} translates positional fields, not vectors or shape parameters`, () => {
    const document = fixture();
    document.entities[1] = {id: 'one', kind, points: [{x: 1, y: 2}], center: {x: 3, y: 4},
      controlPoints: [{x: 5, y: 6}], fitPoints: [{x: 7, y: 8}],
      startTangentHandle: {x: 9, y: 10}, endTangentHandle: {x: 11, y: 12},
      majorAxisEndPoint: {x: 13, y: 14}, radius: 2, startAngleDegrees: 20, endAngleDegrees: 220,
      splineWeights: [1, 2], splineKnots: [0, 1], minorRadiusRatio: .5};
    const before = freeze(document.entities[1]);
    const entity = radialCenterTransform(document, 'radial', center)({x: 5, y: 7}).entities[1];
    for (const key of ['points', 'controlPoints', 'fitPoints']) near(entity[key][0], {x: before[key][0].x + 7, y: before[key][0].y + 1});
    for (const key of ['center', 'startTangentHandle', 'endTangentHandle']) near(entity[key], {x: before[key].x + 7, y: before[key].y + 1});
    for (const key of ['majorAxisEndPoint', 'radius', 'startAngleDegrees', 'endAngleDegrees', 'splineWeights', 'splineKnots', 'minorRadiusRatio'])
      assert.deepEqual(entity[key], before[key]);
  });
}

test('absent eligibility, stale center, malformed members and protected geometry fail closed', () => {
  for (const change of [
    d => delete d.radialCenterPreviews,
    d => d.radialCenterPreviews[0].center.x++,
    d => d.radialCenterPreviews[0].stepAngleDegrees = Infinity,
    d => d.radialCenterPreviews[0].members[0].entityId = 'missing',
    d => d.radialCenterPreviews[0].members.push(d.radialCenterPreviews[0].members[0]),
    d => d.radialCenterPreviews[0].members[0].column = 0,
    d => d.entities[1].isLocked = true,
    d => d.entities[1].center.x = NaN,
    d => d.entities[1].kind = 'unsupported',
    d => d.radialCenterPreviews[0].centerPointId = 'source'
  ]) {
    const document = fixture(); change(document);
    assert.equal(radialCenterTransform(document, 'radial', center), null);
  }
  assert.equal(radialCenterTransform(fixture(), 'radial', center)({x: Infinity, y: 0}), null);
});

function scheduler(document = fixture()) {
  let next = 0;
  const frames = new Map(), paints = [];
  const preview = createRadialCenterPreview({getDocument: () => document,
    requestFrame: fn => { frames.set(++next, fn); return next; }, cancelFrame: key => frames.delete(key),
    render: (point, document) => paints.push({point, document})});
  const flush = () => { const pending = [...frames.values()]; frames.clear(); pending.forEach(fn => fn()); };
  const ack = point => {
    const d = fixture(); d.radialCenterPreviews[0].center = {...point}; preview.accept(d);
  };
  return {preview, frames, paints, flush, ack};
}

test('sustained movement renders once per frame before stopping, never accumulates translation', () => {
  const h = scheduler(); assert.equal(h.preview.begin('radial', center), true);
  for (let frame = 1; frame <= 120; frame++) {
    for (let event = 0; event < 8; event++) h.preview.move({x: frame, y: frame / 3});
    assert.equal(h.frames.size, 1); h.flush();
    assert.deepEqual(h.paints.at(-1).point, {x: frame, y: frame / 3});
  }
  assert.equal(h.paints.length, 120);
  assert.deepEqual(h.paints.at(-1).document,
    radialCenterTransform(fixture(), 'radial', center)({x: 120, y: 40}));
});

test('release paints newest pointer and retains geometry through RPC completion and stale begin DTO', () => {
  const h = scheduler(); h.preview.begin('radial', center);
  h.preview.move({x: 4, y: 5});
  const end = {x: 10 / 3, y: 7 / 11};
  h.preview.finish(end);
  assert.equal(h.frames.size, 0); assert.deepEqual(h.paints.at(-1).point, end);
  h.ack(center); assert.equal(h.preview.busy, true);
  assert.equal(h.preview.begin('radial', center), false);
  h.ack(JSON.parse(JSON.stringify(end))); // G17 server text preserves the double.
  assert.equal(h.preview.busy, false); assert.equal(h.paints.at(-1).document, null);
});

test('fractional G17 center supports repeated gestures and exact acknowledgment without tolerance', () => {
  let document = fixture();
  for (const point of [{x: 10 / 3, y: 7 / 11}, {x: 3.2499996701234567, y: -Math.PI}]) {
    const descriptor = document.radialCenterPreviews[0], baseline = descriptor.center;
    const h = scheduler(document);
    assert.equal(h.preview.begin('radial', {x: Number(String(baseline.x)), y: Number(String(baseline.y))}), true);
    h.preview.move(point); h.preview.finish(point);
    document = h.paints.at(-1).document;
    document = {...document, radialCenterPreviews: [{...descriptor, center: {...point}}]};
    h.preview.accept(JSON.parse(JSON.stringify(document)));
    assert.equal(h.preview.busy, false);
  }
});

test('canvas keeps camera fixed through initial server preview, local release and render acknowledgment', t => {
  const previous = globalThis.window, events = new Map(), frames = new Map();
  let token = 0, captured = false;
  globalThis.window = {devicePixelRatio: 1, addEventListener() {}, removeEventListener() {},
    requestAnimationFrame: fn => {frames.set(++token, fn); return token;}, cancelAnimationFrame: id => frames.delete(id)};
  const widget = {dataset: {patternId: 'radial', centerX: '2', centerY: '3', patternKind: 'Radial',
    sourceX: '12', sourceY: '3', pitchX: '1', pitchY: '1', angle: '0', step: '90'}, style: {},
    addEventListener: (key, fn) => events.set(key, fn), removeEventListener: key => events.delete(key),
    setPointerCapture: () => captured = true, hasPointerCapture: () => captured, releasePointerCapture: () => captured = false,
    querySelector: () => null, querySelectorAll: () => []};
  const context = new Proxy({createRadialGradient: () => ({addColorStop() {}}), measureText: text => ({width: String(text).length * 7})}, {get: (o, k) => k in o ? o[k] : () => {}});
  const element = {clientWidth: 1000, clientHeight: 800, dataset: {}, style: {}, getContext: () => context,
    parentElement: {querySelector: () => widget},
    getBoundingClientRect: () => ({left: 0, top: 0, width: 1000, height: 800}),
    addEventListener() {}, removeEventListener() {}, removeAttribute() {}, focus() {}};
  const instance = createDrawingCanvas(element, {invokeMethodAsync: () => new Promise(() => {})});
  t.after(() => {instance.dispose(); if (previous === undefined) delete globalThis.window; else globalThis.window = previous;});
  const document = {...fixture(), bounds: {minX: -10, minY: 0, maxX: 14, maxY: 15}};
  instance.setDocument(document, true);
  const view = () => ['scale', 'offsetX', 'offsetY'].map(key => element.dataset[key]);
  const original = view(), scale = Number(original[0]), target = {closest: () => target};
  const emit = (name, x, y) => events.get(name)({target, pointerId: 1, button: 0,
    clientX: x * scale + Number(original[1]), clientY: -y * scale + Number(original[2]), preventDefault() {}, stopPropagation() {}});
  emit('pointerdown', 2, 3); emit('pointermove', 5, 7);
  const pending = [...frames.values()]; frames.clear(); pending.forEach(fn => fn());
  assert.equal(element.dataset.patternCenterLocalPreview, 'true');
  instance.setCornerPreview({...document, bounds: {minX: -1000, minY: -1000, maxX: 1000, maxY: 1000}}, null, null, true);
  assert.deepEqual(view(), original);
  emit('pointerup', 5, 7);
  instance.setCornerPreview(document, null, null, true); // late begin delivery
  assert.equal(element.dataset.patternCenterLocalPreview, 'true'); assert.deepEqual(view(), original);
  const point = {x: (5 * scale + Number(original[1]) - Number(original[1])) / scale,
    y: -(-7 * scale + Number(original[2]) - Number(original[2])) / scale};
  const accepted = {...document, radialCenterPreviews: [{...document.radialCenterPreviews[0], center: point}]};
  instance.setCornerPreview(accepted, null, null, true);
  assert.equal(element.dataset.patternCenterLocalPreview, 'false'); assert.deepEqual(view(), original);
});

test('held server responses cannot replace local geometry; Escape restores baseline and removes queued frame', () => {
  const h = scheduler(); h.preview.begin('radial', center);
  h.preview.move({x: 4, y: 6}); h.flush(); h.ack(center);
  assert.deepEqual(h.paints.at(-1).point, {x: 4, y: 6});
  h.preview.move({x: 50, y: 60}); h.preview.finish(center); h.flush();
  assert.equal(h.preview.busy, false);
  assert.deepEqual(h.paints.at(-2).document, fixture());
  assert.equal(h.paints.at(-1).document, null);
});

test('invalidated document, missing descriptor and disposal clear pending local work', () => {
  for (const document of [null, {entities: [], radialCenterPreviews: []}]) {
    const h = scheduler(); h.preview.begin('radial', center); h.preview.move({x: 4, y: 6});
    h.preview.accept(document); h.flush();
    assert.equal(h.preview.busy, false); assert.deepEqual(h.paints, [{point: null, document: null}]);
  }
  const h = scheduler(); h.preview.begin('radial', center); h.preview.move({x: 4, y: 6}); h.preview.reset(); h.flush();
  assert.deepEqual(h.paints, [{point: null, document: null}]);
});

function gesture(local = true, fail = false) {
  const h = scheduler(local ? fixture() : {entities: []}), events = new Map(), calls = [], waits = [], errors = [];
  let captured = false;
  const target = {closest: () => target};
  const widget = {dataset: {patternId: 'radial', centerX: '2', centerY: '3'},
    addEventListener: (key, fn) => events.set(key, fn), removeEventListener: key => events.delete(key),
    setPointerCapture: () => captured = true, hasPointerCapture: () => captured,
    releasePointerCapture: () => captured = false};
  const dispose = bindPatternCenterHandle(widget, {toWorld: e => ({x: e.clientX, y: e.clientY}),
    send: value => { calls.push(value); return fail ? Promise.reject(Error('transport')) : new Promise(resolve => waits.push(resolve)); },
    preview: point => { if (point) h.preview.move(point); },
    beginLocal: (id, center) => h.preview.begin(id, center), finishLocal: point => h.preview.finish(point),
    resetLocal: () => h.preview.reset(), localBusy: () => h.preview.busy, onError: error => errors.push(error)});
  const emit = (name, x = 2, y = 3, extra = {}) => events.get(name)?.({target, pointerId: 1, button: 0,
    clientX: x, clientY: y, preventDefault() {}, stopPropagation() {}, ...extra});
  return {...h, widget, calls, waits, errors, emit, dispose};
}
const tick = () => new Promise(setImmediate);

test('eligible drag sends only begin/end while rendering many frames; final coordinates are not last RAF', async () => {
  const h = gesture(); h.emit('pointerdown');
  for (let i = 10; i < 130; i++) { h.emit('pointermove', i, i / 3); h.flush(); }
  h.emit('pointerup', 130.12345678901234, 7 / 3);
  assert.equal(h.paints.length, 121); assert.deepEqual(h.calls.map(c => c.phase), ['begin']);
  h.waits.shift()(); await tick();
  assert.deepEqual(h.calls[1], {id: 'radial', phase: 'end', x: 130.12345678901234, y: 7 / 3});
  h.waits.shift()(); await tick();
  assert.equal(h.preview.busy, true); // RPC completion is not a render acknowledgment.
  h.ack({x: 130.12345678901234, y: 7 / 3}); assert.equal(h.preview.busy, false);
  h.dispose();
});

test('constrained fallback coalesces server updates and does not invent local geometry', async () => {
  const h = gesture(false); h.emit('pointerdown');
  for (let i = 10; i < 100; i++) h.emit('pointermove', i, i);
  h.emit('pointerup', 101, 102); h.flush(); assert.deepEqual(h.paints, []);
  h.waits.shift()(); await tick(); assert.equal(h.calls[1].phase, 'update'); assert.equal(h.calls[1].x, 99);
  h.waits.shift()(); await tick(); assert.equal(h.calls[2].phase, 'end'); assert.equal(h.calls[2].x, 101);
  h.waits.shift()(); await tick(); h.dispose();
});

for (const event of ['pointercancel', 'lostpointercapture', 'keydown']) {
  test(`${event} cancels exact original center with no later local frame`, async () => {
    const h = gesture(); h.emit('pointerdown'); h.emit('pointermove', 10, 20);
    h.emit(event, 10, 20, {key: 'Escape'}); h.flush();
    assert.deepEqual(h.paints.at(-1).document, fixture());
    h.waits.shift()(); await tick();
    assert.deepEqual(h.calls[1], {id: 'radial', phase: 'cancel', ...center});
    h.waits.shift()(); await tick(); h.ack(center); h.dispose();
  });
}

test('transport error clears held flag and local geometry, and attempts server cancellation once', async () => {
  const h = gesture(true, true); h.emit('pointerdown'); h.emit('pointermove', 10, 20);
  await tick(); h.flush();
  assert.equal(h.preview.busy, false); assert.equal(h.widget.dataset.centerDragging, undefined);
  assert.deepEqual(h.calls.map(c => c.phase), ['begin', 'cancel']);
  assert.equal(h.paints.at(-1).document, null); h.dispose();
});

test('gesture captures pattern identity and disposal cancels pending RAF', () => {
  const h = gesture(); h.emit('pointerdown'); h.emit('pointermove', 10, 20);
  h.widget.dataset.patternId = 'different'; h.dispose(); h.flush();
  assert.equal(h.preview.busy, false); assert.equal(h.paints.at(-1).document, null);
  assert.equal(h.widget.dataset.centerDragging, undefined);
});

test('local 64-member sustained preview benchmark', t => {
  const document = fixture(), descriptor = document.radialCenterPreviews[0];
  document.entities.splice(1, 2); descriptor.members = []; descriptor.stepAngleDegrees = 360 / 65;
  for (let column = 1; column <= 64; column++) {
    const angle = column * descriptor.stepAngleDegrees * Math.PI / 180, id = `member-${column}`;
    document.entities.push({id, kind: 'circle', points: [], center: {x: 2 + 10 * Math.cos(angle), y: 3 + 10 * Math.sin(angle)}, radius: 2});
    descriptor.members.push({entityId: id, column});
  }
  const transform = radialCenterTransform(document, 'radial', center), start = performance.now();
  for (let frame = 0; frame < 120; frame++) assert.equal(transform({x: frame / 60, y: frame / 120}).entities.length, 66);
  t.diagnostic(`120 frames, 64 members: ${(performance.now() - start).toFixed(2)} ms total (transform only; not browser paint)`);
});

import test from 'node:test';
import assert from 'node:assert/strict';
import {lineSlotDraft, lineSlotPreview, lineSlotSourceIsCurrent} from '../../src/DXFER.Blazor/wwwroot/canvas/lineSlot.js';
import {createDrawingCanvas, applyDraftDimensionValue} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const source = {id: 'centerline', kind: 'line', points: [{x: 2, y: 3}, {x: 12, y: 3}]};
const documentFixture = () => ({entities: [structuredClone(source)], constraints: [], dimensions: [],
  bounds: {minX: 0, minY: 0, maxX: 20, maxY: 15}});

test('existing-line draft copies exact endpoints without changing source or adding geometry', () => {
  const document = documentFixture(), before = structuredClone(document);
  const {draft} = lineSlotDraft(document, ['centerline']);
  assert.equal(draft.centerlineReferenceKey, 'centerline'); assert.deepEqual(draft.points, source.points);
  assert.notEqual(draft.points[0], document.entities[0].points[0]);
  assert.equal(lineSlotSourceIsCurrent(document, draft), true);
  draft.points[0].x++; assert.equal(lineSlotSourceIsCurrent(document, draft), false);
  assert.deepEqual(document, before);
});

for (const [label, x] of [['start', 2], ['end', 12], ['mid', 7]]) {
  test(`selected LINE ${label} target normalizes to whole owner and commits once`, t => {
    const key = `centerline|point|${label}|${x}|3`, doc = documentFixture();
    const result = lineSlotDraft(doc, [key]);
    assert.equal(result.draft.centerlineReferenceKey, 'centerline');
    assert.deepEqual(result.draft.points, source.points);
    const h = harness(t); h.instance.selectEntities([key]); h.instance.setActiveTool('slot');
    assert.equal(h.canvas.dataset.toolDraftPointCount, '2');
    h.input().value = '1.25'; h.input().emit('input'); h.input().emit('keydown', {key: 'Enter'});
    assert.deepEqual(h.commits(), [['CommitLineSlot', 'centerline', 1.25, true]]);
    assert.deepEqual(h.doc, h.before);
  });
}

test('point-target normalization refuses wrong entity kinds, invalid labels and malformed references', () => {
  for (const kind of ['point', 'arc', 'polyline']) {
    assert.equal(lineSlotDraft({entities: [{...source, kind}]}, ['centerline|point|mid|7|3']).draft, undefined);
  }
  for (const key of ['centerline|point|center|7|3', 'centerline|point|vertex-0|2|3',
    'centerline|point|mid|bad|3', 'centerline|point|mid|7', 'centerline|point|mid|extra|7|3',
    'centerline|segment|0', 'missing|point|mid|7|3']) {
    assert.equal(lineSlotDraft(documentFixture(), [key]).draft, undefined, key);
  }
  assert.equal(lineSlotDraft(documentFixture(), ['centerline|point|start|2|3', 'centerline|point|end|12|3']).draft, undefined);
});

for (const [name, entities, keys] of [
  ['empty', [source], []], ['multiple', [source], ['centerline', 'other']],
  ['missing', [source], ['other']], ['arc', [{...source, kind: 'arc'}], ['centerline']],
  ['polyline', [{...source, kind: 'polyline'}], ['centerline']],
  ['point', [{...source, kind: 'point'}], ['centerline']],
  ['locked', [{...source, isLocked: true}], ['centerline']],
  ['zero length', [{...source, points: [{x: 2, y: 3}, {x: 2, y: 3}]}], ['centerline']],
  ['nonfinite', [{...source, points: [{x: NaN, y: 3}, {x: 12, y: 3}]}], ['centerline']]
]) test(`${name} centerline refuses with a useful reason`, () => {
  const result = lineSlotDraft({entities}, keys);
  assert.equal(result.draft, undefined); assert.match(result.reason, /line|LINE/);
});

for (const [end, pointer, expected] of [
  [{x: 10, y: 0}, {x: 100, y: 2}, 4],
  [{x: 10, y: 0}, {x: -100, y: -2}, 4],
  [{x: 0, y: 10}, {x: 3, y: 5}, 6],
  [{x: 3, y: 4}, {x: 1.4, y: 5.2}, 4]
]) test(`full width is twice perpendicular distance for end ${JSON.stringify(end)} and pointer ${JSON.stringify(pointer)}`, () => {
  const preview = lineSlotPreview({x: 0, y: 0}, end, pointer);
  assert.ok(Math.abs(preview.width - expected) < 1e-12);
  assert.equal(preview.points.length, 50);
  assert.ok(preview.points.every(p => Number.isFinite(p.x) && Number.isFinite(p.y)));
  assert.ok(Math.abs(Math.hypot(preview.widthEnd.x - preview.widthStart.x, preview.widthEnd.y - preview.widthStart.y) - expected) < 1e-12);
});

test('rounded preview extends by half-width past both endpoints', () => {
  const preview = lineSlotPreview({x: 0, y: 0}, {x: 10, y: 0}, {x: 5, y: 2});
  assert.equal(Math.min(...preview.points.map(p => p.x)), -2);
  assert.equal(Math.max(...preview.points.map(p => p.x)), 12);
  assert.equal(lineSlotPreview({x: 0, y: 0}, {x: 10, y: 0}, {x: 5, y: 0}), null);
});

test('keyed Width locks half-width preview, cannot edit source length or radius', () => {
  const {draft} = lineSlotDraft(documentFixture(), ['centerline']);
  const state = {activeTool: 'slot', toolDraft: draft};
  assert.equal(applyDraftDimensionValue(state, 'width', 1.25), true);
  assert.equal(state.toolDraft.dimensionValues.width, 1.25);
  assert.deepEqual(state.toolDraft.points, source.points);
  assert.equal(lineSlotPreview(...draft.points, draft.previewPoint).width, 1.25);
  assert.equal(applyDraftDimensionValue(state, 'length', 20), false);
  assert.equal(applyDraftDimensionValue(state, 'radius', 4), false);
  assert.deepEqual(state.toolDraft.points, source.points);
});

function harness(t, entities = [source]) {
  const previousWindow = globalThis.window, previousDocument = globalThis.document;
  const calls = [], inputs = [], windowEvents = new Map();
  const element = () => {
    const handlers = new Map();
    return {dataset: {}, style: {}, classList: {add() {}, remove() {}, toggle() {}}, tagName: 'INPUT',
      getBoundingClientRect: () => ({left: 0, top: 0, width: 80, height: 24}),
      addEventListener: (name, fn) => handlers.set(name, fn), removeEventListener() {},
      setAttribute(key, value) {this[key] = value;}, remove() {this.removed = true;}, select() {},
      emit(name, extra = {}) {handlers.get(name)?.({target: this, preventDefault() {}, stopPropagation() {}, ...extra});},
      focus() {
        if (document.activeElement === this) return;
        const old = document.activeElement; document.activeElement = this; old?.emit('blur'); this.emit('focus');
      }};
  };
  globalThis.window = {devicePixelRatio: 1, addEventListener: (key, fn) => windowEvents.set(key, fn), removeEventListener() {}};
  globalThis.document = {activeElement: null, createElement: element};
  const context = new Proxy({createRadialGradient: () => ({addColorStop() {}}), measureText: text => ({width: String(text).length * 7})},
    {get: (o, k) => k in o ? o[k] : () => {}});
  const canvas = {...element(), tagName: 'CANVAS', clientWidth: 1000, clientHeight: 800, getContext: () => context,
    getBoundingClientRect: () => ({left: 0, top: 0, width: 1000, height: 800}), setPointerCapture() {}, releasePointerCapture() {}};
  const instance = createDrawingCanvas(canvas, {invokeMethodAsync: (...args) => {calls.push(args); return Promise.resolve(true);}},
    {appendChild: input => inputs.push(input)});
  const doc = {...documentFixture(), entities: structuredClone(entities)}, before = structuredClone(doc);
  instance.setDocument(doc, true);
  const pointer = (type, x, y, extra = {}) => canvas.emit(type, {clientX: x * +canvas.dataset.scale + +canvas.dataset.offsetX,
    clientY: -y * +canvas.dataset.scale + +canvas.dataset.offsetY, pointerId: 1, pointerType: 'mouse',
    button: 0, buttons: type === 'pointerdown' ? 1 : 0, ...extra});
  const click = (x, y) => {pointer('pointermove', x, y); pointer('pointerdown', x, y); pointer('pointerup', x, y);};
  const key = key => windowEvents.get('keydown')?.({key, target: canvas, preventDefault() {}});
  t.after(() => {instance.dispose();
    if (previousWindow === undefined) delete globalThis.window; else globalThis.window = previousWindow;
    if (previousDocument === undefined) delete globalThis.document; else globalThis.document = previousDocument;});
  return {instance, canvas, doc, before, calls, pointer, click, key,
    input: () => inputs.findLast(input => !input.removed && input.dataset.dimensionKey === 'width'),
    commits: () => calls.filter(c => c[0] === 'CommitLineSlot')};
}

for (const preselected of [false, true]) test(`${preselected ? 'selection' : 'tool'}-first Slot uses existing line and sends full width only`, t => {
  const h = harness(t);
  if (preselected) h.instance.selectEntities(['centerline']);
  h.instance.setActiveTool('slot'); if (!preselected) h.click(7, 3);
  assert.equal(h.canvas.dataset.toolDraftPointCount, '2');
  assert.equal(h.input()['aria-label'], 'Width');
  h.pointer('pointermove', 8, 5);
  assert.equal(h.input().value, '4');
  h.click(8, 5);
  assert.equal(h.commits().length, 1); assert.equal(h.commits()[0][1], 'centerline');
  assert.ok(Math.abs(h.commits()[0][2] - 4) < 1e-12); assert.equal(h.commits()[0][3], false);
  assert.deepEqual(h.calls.filter(c => c[0] === 'OnSketchToolCommitted'), []);
  assert.deepEqual(h.doc, h.before);
});

test('live decimal Width survives redraw and Enter commits keyed exact value once', t => {
  const h = harness(t); h.instance.selectEntities(['centerline']); h.instance.setActiveTool('slot');
  const input = h.input(); input.focus();
  for (const text of ['1', '1.', '1.2', '1.25']) {
    input.value = text; input.emit('input'); h.pointer('pointermove', 6, 7);
    assert.equal(input.value, text); assert.equal(h.commits().length, 0);
  }
  input.emit('keydown', {key: 'Enter'});
  assert.deepEqual(h.commits(), [['CommitLineSlot', 'centerline', 1.25, true]]);
  assert.deepEqual(h.doc, h.before);
});

for (const invalid of ['', '.', '-1', '0', 'bad']) test(`invalid pending Width '${invalid}' refuses click/Enter instead of committing previous lock`, t => {
  const h = harness(t); h.instance.setActiveTool('slot'); h.click(7, 3);
  const input = h.input(); input.value = '2.5'; input.emit('input');
  input.value = invalid; input.emit('input'); input.emit('keydown', {key: 'Enter'});
  h.click(7, 5); assert.deepEqual(h.commits(), []);
  assert.match(h.canvas.dataset.slotFeedback, /valid positive/);
  input.value = '3.25'; input.emit('input'); input.emit('keydown', {key: 'Enter'});
  assert.deepEqual(h.commits(), [['CommitLineSlot', 'centerline', 3.25, true]]);
});

test('empty/arc/multiple picks refuse, valid line recovers without creating arbitrary endpoints', t => {
  const arc = {id: 'arc', kind: 'arc', center: {x: 15, y: 10}, radius: 2, points: [], startAngleDegrees: 0, endAngleDegrees: 180};
  const h = harness(t, [source, arc]);
  h.instance.selectEntities(['centerline', 'arc']); h.instance.setActiveTool('slot');
  assert.equal(h.canvas.dataset.toolDraftPointCount, '0'); assert.match(h.canvas.dataset.slotFeedback, /exactly one/);
  h.click(18, 13); h.click(15, 12); assert.equal(h.canvas.dataset.toolDraftPointCount, '0');
  assert.deepEqual(h.commits(), []); assert.match(h.canvas.dataset.slotFeedback, /straight LINE/);
  h.click(7, 3); h.click(7, 5); assert.equal(h.commits().length, 1);
});

test('source change invalidates draft and Escape removes pending Width without commit', t => {
  const h = harness(t); h.instance.setActiveTool('slot'); h.click(7, 3); h.pointer('pointermove', 7, 5);
  const changed = structuredClone(h.doc); changed.entities[0].points[1].x = 14;
  h.instance.setDocument(changed); assert.equal(h.canvas.dataset.toolDraftPointCount, '0');
  assert.match(h.canvas.dataset.slotFeedback, /centerline changed/); h.key('Enter'); assert.deepEqual(h.commits(), []);
  h.click(7, 3); h.input().value = '2'; h.input().emit('input'); h.input().emit('keydown', {key: 'Escape'});
  assert.equal(h.canvas.dataset.activeTool, 'select'); assert.equal(h.input(), undefined); assert.deepEqual(h.commits(), []);
  assert.deepEqual(h.doc, h.before);
});

test('construction-only source edit invalidates Width draft without changing endpoints', t => {
  const h = harness(t); h.instance.setActiveTool('slot'); h.click(7, 3);
  const input = h.input(); input.value = '1.25'; input.emit('input');
  const changed = structuredClone(h.doc); changed.entities[0].isConstruction = true;
  h.instance.setDocument(changed); h.key('Enter');
  assert.equal(h.canvas.dataset.toolDraftPointCount, '0'); assert.equal(h.input(), undefined);
  assert.deepEqual(h.commits(), []); assert.deepEqual(changed.entities[0].points, source.points);
  assert.deepEqual(h.doc, h.before);
});

test('canvas Enter commits latest pointer width without requiring a keyed dimension', t => {
  const h = harness(t); h.instance.setActiveTool('slot'); h.click(7, 3); h.pointer('pointermove', 7, 5);
  h.key('Enter'); assert.equal(h.commits().length, 1); assert.equal(h.commits()[0][3], false);
  h.key('Enter'); assert.equal(h.commits().length, 1);
});

test('untouched auto-focused Width Enter preserves precise pointer width, not rounded display text', t => {
  const h = harness(t); h.instance.setActiveTool('slot'); h.click(7, 3);
  h.pointer('pointermove', 7, 5.000000123456789);
  const input = h.input(); assert.equal(input.value, '4');
  input.emit('keydown', {key: 'Enter'});
  assert.equal(h.commits().length, 1); assert.equal(h.commits()[0][3], false);
  assert.ok(h.commits()[0][2] > 4);
  assert.ok(Math.abs(h.commits()[0][2] - 4.000000246913578) < 1e-12);
});

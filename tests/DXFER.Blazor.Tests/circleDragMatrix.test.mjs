import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview, createDrawingCanvas } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const p = (x, y) => ({ x, y });
const add = (a, b) => p(a.x + b.x, a.y + b.y);
const fixtures = ['free', 'fixed', 'weld-free', 'weld-fixed', 'radius-free', 'radius-center-fixed'];
const grips = ['center', 'quadrant-0'];
const locked = (fixture, grip) => ['fixed', 'weld-fixed'].includes(fixture) || fixture === 'radius-center-fixed' && grip === 'center';
const removeFix = original => ({ ...original, constraints: original.constraints.filter(c => c.kind !== 'fix') });

function documentOf(fixture, reverse = false) {
    const entities = [{ id: 'circle', kind: 'circle', center: p(0, 0), radius: 3 }];
    const constraints = [];
    if (fixture.startsWith('weld')) {
        entities.push({ id: 'peer', kind: 'line', points: [p(0, 0), p(-2, 0)] });
        constraints.push({ id: 'weld', kind: 'coincident', state: 'satisfied',
            referenceKeys: reverse ? ['peer:start', 'circle:center'] : ['circle:center', 'peer:start'] });
    }
    const fixedKey = { fixed: 'circle', 'weld-fixed': 'peer:start', 'radius-center-fixed': 'circle:center' }[fixture];
    if (fixedKey) constraints.push({ id: 'fix', kind: 'fix', state: 'satisfied', referenceKeys: [fixedKey] });
    if (reverse) constraints.reverse();
    return { entities, constraints, dimensions: [{ id: 'radius', kind: 'radius', referenceKeys: ['circle'],
        value: 3, anchor: p(4, -1), isDriving: !fixture.startsWith('radius') }] };
}

function gripOf(document, grip) {
    const circle = document.entities[0];
    const start = grip === 'center' ? circle.center : add(circle.center, p(circle.radius, 0));
    return { start, key: `circle|point|${grip}|${start.x}|${start.y}` };
}

function assertPoint(actual, expected) {
    assert.ok(actual && Math.abs(actual.x - expected.x) < 1e-8 && Math.abs(actual.y - expected.y) < 1e-8,
        `${JSON.stringify(actual)} != ${JSON.stringify(expected)}`);
}

function assertResult(original, next, center, radius) {
    assert.ok(next);
    assertPoint(next.entities[0].center, center);
    assert.equal(next.entities[0].radius, radius);
    assert.equal(next.dimensions.length, 1);
    const dimension = next.dimensions[0];
    assert.equal(dimension.value, radius);
    assert.equal(dimension.isDriving, original.dimensions[0].isDriving);
    assert.deepEqual(dimension.referenceKeys, ['circle']);
    assertPoint(dimension.anchor, add(p(4, -1), center));
    if (next.entities.length === 2) {
        assertPoint(next.entities[1].points[0], center);
        assert.deepEqual(next.entities[1].points[1], p(-2, 0));
        assert.deepEqual(next.constraints.find(c => c.id === 'weld').referenceKeys, original.constraints.find(c => c.id === 'weld').referenceKeys);
    }
}

for (const fixture of fixtures) for (const grip of grips) for (const reverse of [false, true]) {
    test(`circle ${fixture} ${grip} reverse=${reverse}`, () => {
        const original = documentOf(fixture, reverse);
        const before = structuredClone(original);
        const { key, start } = gripOf(original, grip);
        const translates = original.dimensions[0].isDriving || grip === 'center';
        const delta = translates ? p(1, 1) : p(1, 0);
        let next = applyGeometryDragPreview(original, key, start, add(start, delta));
        if (locked(fixture, grip)) {
            assert.equal(next, null);
            next = applyGeometryDragPreview(removeFix(original), key, start, add(start, delta));
        }
        assertResult(original, next, translates ? delta : p(0, 0), translates ? 3 : 4);
        const again = gripOf(next, grip);
        assertResult(original, applyGeometryDragPreview(next, again.key, again.start, add(again.start, delta)),
            translates ? p(2, 2) : p(0, 0), translates ? 3 : 5);
        assert.deepEqual(original, before);
    });
}

for (const fixture of fixtures) for (const offset of [0, 1e-9]) for (const reverse of [false, true]) {
    test(`circle collapse ${fixture} offset=${offset} reverse=${reverse}`, () => {
        const original = documentOf(fixture, reverse);
        const before = structuredClone(original);
        const { key, start } = gripOf(original, 'quadrant-0');
        assert.equal(applyGeometryDragPreview(original, key, start, p(offset, 0)), null);
        const next = applyGeometryDragPreview(removeFix(original), key, start, p(-4, 0));
        const driving = original.dimensions[0].isDriving;
        assertResult(original, next, driving ? p(-7, 0) : p(0, 0), driving ? 3 : 4);
        assert.deepEqual(original, before);
    });
}

for (const pascalCase of [false, true]) {
    test(`reference radius/diameter refresh preserves intent and DTO casing: Pascal=${pascalCase}`, () => {
        const original = documentOf('radius-center-fixed');
        original.dimensions.push(
            { id: 'diameter', kind: 'diameter', referenceKeys: ['circle'], value: 6, isDriving: false },
            { id: 'missing', kind: 'radius', referenceKeys: ['missing'], value: 7, isDriving: false },
            { id: 'suppressed', kind: 'radius', referenceKeys: ['circle'], value: 8, isDriving: false, state: 'suppressed' });
        if (pascalCase) original.dimensions = original.dimensions.map(d => Object.fromEntries(
            Object.entries(d).map(([key, value]) => [key[0].toUpperCase() + key.slice(1), value])));
        const before = structuredClone(original);
        const next = applyGeometryDragPreview(original, 'circle|point|quadrant-0|3|0', p(3, 0), p(4, 0));
        assert.ok(next);
        const value = d => d.value ?? d.Value;
        assert.deepEqual(next.dimensions.map(value), [4, 8, 7, 8]);
        assert.equal(next.dimensions[0].Value, 4);
        assert.equal(next.dimensions[1].Value, 8);
        assert.deepEqual(original, before);
    });
}

for (const reverse of [false, true]) {
    test(`reference refresh follows center weld propagation reverse=${reverse}`, () => {
        const original = documentOf('weld-free', reverse);
        original.dimensions.push({ id: 'peer-length', kind: 'linearDistance', referenceKeys: ['peer:start', 'peer:end'],
            value: 2, isDriving: false, anchor: p(-1, -1) });
        const before = structuredClone(original);
        const next = applyGeometryDragPreview(original, 'circle|point|center|0|0', p(0, 0), p(1, 1));
        assert.ok(next);
        assert.equal(next.dimensions[0].value, 3, 'driving radius is not rewritten');
        assert.ok(Math.abs(next.dimensions[1].value - Math.sqrt(10)) < 1e-8);
        assertPoint(next.dimensions[1].anchor, p(-0.5 + 1 / Math.sqrt(10), 0.5 - 3 / Math.sqrt(10)));
        assert.deepEqual(original, before);
    });
}

function canvasHarness(t, original, accepted = true) {
    const previousWindow = globalThis.window;
    globalThis.window = { devicePixelRatio: 2, addEventListener() {}, removeEventListener() {} };
    let instance;
    t.after(() => {
        instance?.dispose();
        if (previousWindow === undefined) delete globalThis.window;
        else globalThis.window = previousWindow;
    });
    const paint = [], calls = [], handlers = new Map();
    const context = new Proxy({ measureText: text => ({ width: String(text).length * 7 }) }, {
        get: (target, key) => key in target ? target[key] : (...args) => paint.push([key, ...args])
    });
    const canvas = { width: 0, height: 0, clientWidth: 1000, clientHeight: 800, dataset: {}, style: {},
        getContext: () => context, getBoundingClientRect: () => ({ left: 0, top: 0, width: 1000, height: 800 }),
        addEventListener: (name, handler) => handlers.set(name, handler), removeEventListener() {},
        focus() {}, setPointerCapture() {}, releasePointerCapture() {} };
    instance = createDrawingCanvas(canvas, { invokeMethodAsync: (...args) => {
        calls.push(args);
        return Promise.resolve(args[0] === 'OnGeometryDragRequested' ? accepted : undefined);
    } });
    instance.setDocument(original, true);
    const scale = () => Number(canvas.dataset.scale);
    const screen = point => p(point.x * scale() + Number(canvas.dataset.offsetX), -point.y * scale() + Number(canvas.dataset.offsetY));
    function pointer(type, point) {
        const position = screen(point);
        paint.length = 0;
        handlers.get(type)({ pointerId: 1, pointerType: 'mouse', button: 0, buttons: type === 'pointerup' ? 0 : 1,
            clientX: position.x, clientY: position.y, preventDefault() {} });
    }
    function assertPainted(center, radius) {
        const position = screen(center);
        assert.ok(paint.some(call => call[0] === 'arc' && Math.abs(call[1] - position.x) < 1e-7
            && Math.abs(call[2] - position.y) < 1e-7 && Math.abs(call[3] - radius * scale()) < 1e-7), 'circle painted');
        const anchor = screen(add(p(4, -1), center));
        assert.ok(paint.some(call => call[0] === 'fillText' && call[1] === String(radius)
            && Math.abs(call[2] - anchor.x) < 1e-7 && Math.abs(call[3] - anchor.y - 0.5) < 1e-7), `radius label ${radius} painted`);
        if (original.entities.length > 1) {
            const end = screen(p(-2, 0));
            assert.ok(paint.some((call, index) => call[0] === 'moveTo' && Math.abs(call[1] - position.x) < 1e-7
                && Math.abs(call[2] - position.y) < 1e-7 && paint[index + 1]?.[0] === 'lineTo'
                && Math.abs(paint[index + 1][1] - end.x) < 1e-7 && Math.abs(paint[index + 1][2] - end.y) < 1e-7), 'peer painted');
        }
        assert.deepEqual([canvas.width, canvas.height], [2000, 1600]);
    }
    return { instance, pointer, assertPainted, paint, calls, setAccepted: value => { accepted = value; } };
}

for (const fixture of fixtures) for (const grip of grips) {
    test(`registered canvas circle ${fixture} ${grip} preview/release`, async t => {
        const original = documentOf(fixture);
        const before = structuredClone(original);
        const refused = locked(fixture, grip);
        const h = canvasHarness(t, original, !refused);
        const { start } = gripOf(original, grip);
        const translates = original.dimensions[0].isDriving || grip === 'center';
        const end = add(start, translates ? p(1, 1) : p(1, 0));
        const center = translates ? p(1, 1) : p(0, 0), radius = translates ? 3 : 4;
        h.pointer('pointerdown', start);
        h.pointer('pointermove', end);
        h.assertPainted(refused ? p(0, 0) : center, refused ? 3 : radius);
        assert.equal(h.instance.getActiveSelectionKey(), null, 'drag must not create persistent selection');
        assert.equal(h.calls.filter(call => call[0] === 'OnGeometryDragRequested').length, 0);
        h.pointer('pointerup', end);
        await new Promise(resolve => setImmediate(resolve));
        h.assertPainted(refused ? p(0, 0) : center, refused ? 3 : radius);
        const requests = h.calls.filter(call => call[0] === 'OnGeometryDragRequested');
        assert.equal(requests.length, 1);
        assert.ok(requests[0][1].startsWith(`circle|point|${grip}|`), requests[0][1]);
        assert.equal(h.instance.getActiveSelectionKey(), null);
        assertPoint(p(requests[0][2], requests[0][3]), start);
        assertPoint(p(requests[0][4], requests[0][5]), end);
        assert.equal(requests[0][6], false);
        if (refused) {
            h.instance.clearSelection();
            h.instance.setDocument(removeFix(original));
            h.setAccepted(true);
            h.pointer('pointerdown', start);
            h.pointer('pointermove', end);
            h.assertPainted(center, radius);
            h.pointer('pointerup', end);
            await new Promise(resolve => setImmediate(resolve));
            h.assertPainted(center, radius);
        }
        assert.deepEqual(original, before);
    });
}

for (const fixture of ['free', 'radius-free', 'radius-center-fixed']) {
    test(`registered canvas circle ${fixture} collapse/recovery and server rollback`, async t => {
        const original = documentOf(fixture);
        const h = canvasHarness(t, original, false);
        const start = p(3, 0), end = original.dimensions[0].isDriving ? p(4, 1) : p(4, 0);
        const center = original.dimensions[0].isDriving ? p(1, 1) : p(0, 0);
        const radius = original.dimensions[0].isDriving ? 3 : 4;
        h.pointer('pointerdown', start);
        h.pointer('pointermove', end);
        h.assertPainted(center, radius);
        h.pointer('pointermove', p(0, 0));
        h.assertPainted(p(0, 0), 3);
        h.pointer('pointermove', end);
        h.assertPainted(center, radius);
        h.pointer('pointerup', end);
        h.paint.length = 0;
        await new Promise(resolve => setImmediate(resolve));
        h.assertPainted(p(0, 0), 3);
        assert.equal(h.calls.filter(call => call[0] === 'OnGeometryDragRequested').length, 1);
    });
}

for (const fixture of ['free', 'radius-center-fixed']) for (const offset of [0, 1e-9]) {
    test(`registered canvas circle ${fixture} rejected collapse and negligible release offset=${offset}`, async t => {
        const original = documentOf(fixture);
        const before = structuredClone(original);
        const h = canvasHarness(t, original, false);
        const start = p(3, 0);
        h.pointer('pointerdown', start);
        h.pointer('pointermove', p(offset, 0));
        h.assertPainted(p(0, 0), 3);
        h.pointer('pointerup', p(offset, 0));
        await new Promise(resolve => setImmediate(resolve));
        h.assertPainted(p(0, 0), 3);
        assert.equal(h.calls.filter(call => call[0] === 'OnGeometryDragRequested').length, 1);
        h.instance.clearSelection();
        h.setAccepted(true);
        const driving = original.dimensions[0].isDriving;
        const end = driving ? p(4, 1) : p(4, 0);
        const center = driving ? p(1, 1) : p(0, 0), radius = driving ? 3 : 4;
        h.pointer('pointerdown', start);
        h.pointer('pointermove', end);
        h.assertPainted(center, radius);
        h.pointer('pointermove', add(start, p(offset, 0)));
        h.pointer('pointerup', add(start, p(offset, 0)));
        h.assertPainted(p(0, 0), 3);
        assert.equal(h.calls.filter(call => call[0] === 'OnGeometryDragRequested').length, 1, 'negligible release sends no commit');
        h.instance.clearSelection();
        h.pointer('pointerdown', start);
        h.pointer('pointermove', end);
        h.pointer('pointerup', end);
        await new Promise(resolve => setImmediate(resolve));
        h.assertPainted(center, radius);
        assert.equal(h.calls.filter(call => call[0] === 'OnGeometryDragRequested').length, 2);
        assert.deepEqual(original, before);
    });
}

import assert from 'node:assert/strict';
import test from 'node:test';
import {
    applyGeometryDragPreview, createDrawingCanvas, getPolygonWorldPoints
} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const p = (x, y) => ({ x, y });
const add = (a, b) => p(a.x + b.x, a.y + b.y);
const grips = ['center', 'mid-0', 'whole'];
const fixtures = ['free', 'fixed', 'weld-free', 'weld-fixed', 'weld-fixed-entity'];
const lockedFixture = fixture => fixture === 'fixed' || fixture.startsWith('weld-fixed');

function documentOf(fixture, circumscribed = false, reverse = false) {
    const polygon = { id: 'polygon', kind: 'polygon', center: p(0, 0), radius: 5,
        rotationAngleDegrees: 30, sideCount: 6, circumscribed };
    polygon.points = getPolygonWorldPoints(polygon.center, p(5 * Math.cos(Math.PI / 6), 2.5), circumscribed, 6);
    const entities = [polygon];
    const constraints = [];
    if (fixture.startsWith('weld')) {
        entities.push({ id: 'peer', kind: 'line', points: [p(0, 0), p(-2, 0)] });
        constraints.push({ id: 'weld', kind: 'coincident', state: 'satisfied',
            referenceKeys: reverse ? ['peer:start', 'polygon:center'] : ['polygon:center', 'peer:start'] });
    }
    if (lockedFixture(fixture)) constraints.push({ id: 'fix', kind: 'fix', state: 'satisfied',
        referenceKeys: [fixture === 'fixed' ? 'polygon' : fixture === 'weld-fixed-entity' ? 'peer' : 'peer:start'] });
    const dimensions = [
        { id: 'radius', kind: 'radius', referenceKeys: ['polygon'], value: 5, anchor: p(6, -2), isDriving: true },
        { id: 'count', kind: 'count', referenceKeys: ['polygon'], value: 6, anchor: p(0, -6), isDriving: true }
    ];
    if (reverse) { constraints.reverse(); dimensions.reverse(); }
    return { entities, dimensions, constraints };
}

function gripOf(document, grip) {
    const polygon = document.entities[0];
    const [a, b] = polygon.points;
    const start = grip === 'center' ? polygon.center : grip === 'mid-0'
        ? p((a.x + b.x) / 2, (a.y + b.y) / 2) : p(0.7 * a.x + 0.3 * b.x, 0.7 * a.y + 0.3 * b.y);
    return { start, key: grip === 'whole' ? 'polygon' : `polygon|point|${grip}|${start.x}|${start.y}` };
}

function assertPoint(actual, expected) {
    assert.ok(actual && Math.abs(actual.x - expected.x) < 1e-8 && Math.abs(actual.y - expected.y) < 1e-8,
        `${JSON.stringify(actual)} != ${JSON.stringify(expected)}`);
}

function assertTranslated(original, next, delta) {
    assert.ok(next);
    const before = original.entities[0];
    const polygon = next.entities[0];
    assertPoint(polygon.center, add(before.center, delta));
    for (const key of ['radius', 'sideCount', 'rotationAngleDegrees', 'circumscribed'])
        assert.equal(polygon[key], before[key]);
    assert.equal(polygon.points.length, before.points.length);
    polygon.points.forEach((point, index) => assertPoint(point, add(before.points[index], delta)));
    assert.equal(next.dimensions.length, original.dimensions.length);
    for (const dimension of next.dimensions) {
        const previous = original.dimensions.find(d => d.id === dimension.id);
        assertPoint(dimension.anchor, add(previous.anchor, delta));
        for (const key of ['id', 'kind', 'value', 'isDriving', 'referenceKeys'])
            assert.deepEqual(dimension[key], previous[key]);
    }
    const expectedConstraints = original.constraints.filter(c => c.kind !== 'fix');
    assert.equal(next.constraints.length, expectedConstraints.length);
    next.constraints.forEach((constraint, index) => {
        for (const key of ['id', 'kind', 'referenceKeys', 'state'])
            assert.deepEqual(constraint[key], expectedConstraints[index][key]);
    });
    if (original.entities.length > 1) {
        assertPoint(next.entities[1].points[0], polygon.center);
        assert.deepEqual(next.entities[1].points[1], p(-2, 0));
    }
}

for (const grip of grips) for (const fixture of fixtures)
for (const circumscribed of [false, true]) for (const reverse of [false, true]) {
    test(`polygon ${grip} ${fixture} circumscribed=${circumscribed} reverse=${reverse}`, () => {
        const original = documentOf(fixture, circumscribed, reverse);
        const before = structuredClone(original);
        const { key, start } = gripOf(original, grip);
        let next = applyGeometryDragPreview(original, key, start, add(start, p(1, 1)));
        if (lockedFixture(fixture)) {
            assert.equal(next, null);
            const unlocked = { ...original, constraints: original.constraints.filter(c => c.kind !== 'fix') };
            next = applyGeometryDragPreview(unlocked, key, start, add(start, p(1, 1)));
        }
        assertTranslated(original, next, p(1, 1));
        const again = gripOf(next, grip);
        assertTranslated(original, applyGeometryDragPreview(next, again.key, again.start, add(again.start, p(-1, 2))), p(0, 3));
        assert.deepEqual(original, before);
    });
}

for (const grip of grips) for (const kind of ['radius', 'count']) {
    test(`polygon ${grip} refuses invalid driving ${kind} without rewriting intent`, () => {
        const original = documentOf('weld-free');
        original.dimensions.find(d => d.kind === kind).value += 1;
        const before = structuredClone(original);
        const { key, start } = gripOf(original, grip);
        assert.equal(applyGeometryDragPreview(original, key, start, add(start, p(1, 1))), null);
        assert.deepEqual(original, before);
    });
}

for (const grip of grips) for (const offset of [0, 1e-9])
for (const locked of [false, true]) for (const reverse of [false, true]) {
    test(`polygon negligible ${grip} offset=${offset} locked=${locked} reverse=${reverse}`, () => {
        const original = documentOf(locked ? 'weld-fixed' : 'weld-free', false, reverse);
        const before = structuredClone(original);
        const { key, start } = gripOf(original, grip);
        const next = applyGeometryDragPreview(original, key, start, add(start, p(offset, offset)));
        // Sub-tolerance previews may be identity candidates; release's click threshold prevents commits.
        if (next) {
            assertPoint(next.entities[0].center, original.entities[0].center);
            next.entities[0].points.forEach((point, index) => assertPoint(point, original.entities[0].points[index]));
            assert.deepEqual(next.entities[1].points, original.entities[1].points);
            next.dimensions.forEach((dimension, index) => {
                assertPoint(dimension.anchor, original.dimensions[index].anchor);
                assert.equal(dimension.value, original.dimensions[index].value);
            });
        }
        const unlocked = { ...original, constraints: original.constraints.filter(c => c.kind !== 'fix') };
        assertTranslated(original, applyGeometryDragPreview(unlocked, key, start, add(start, p(1e-4, 1e-4))), p(1e-4, 1e-4));
        assert.deepEqual(original, before);
    });
}

// Exercise registered pointer handlers and actual canvas rendering, not a replacement preview engine.
function canvasHarness(t, original, accepted) {
    const previousWindow = globalThis.window;
    globalThis.window = { devicePixelRatio: 2, addEventListener() {}, removeEventListener() {} };
    let instance;
    t.after(() => {
        instance?.dispose();
        if (previousWindow === undefined) delete globalThis.window;
        else globalThis.window = previousWindow;
    });
    const paint = [];
    const handlers = new Map();
    const calls = [];
    const context = new Proxy({ createRadialGradient: () => ({ addColorStop() {} }), measureText: text => ({ width: String(text).length * 7 }) }, {
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
    const screen = point => p(point.x * Number(canvas.dataset.scale) + Number(canvas.dataset.offsetX),
        -point.y * Number(canvas.dataset.scale) + Number(canvas.dataset.offsetY));
    const pointer = (type, point) => {
        const position = screen(point);
        paint.length = 0;
        handlers.get(type)({ pointerId: 1, pointerType: 'mouse', button: 0, buttons: type === 'pointerup' ? 0 : 1,
            clientX: position.x, clientY: position.y, preventDefault() {} });
    };
    function assertPainted(delta, welded = original.entities.length > 1) {
        const vertices = original.entities[0].points.map(point => screen(add(point, delta)));
        for (const [index, point] of vertices.entries()) {
            assert.ok(paint.some(call => call[0] === (index ? 'lineTo' : 'moveTo')
                && Math.abs(call[1] - point.x) < 1e-7 && Math.abs(call[2] - point.y) < 1e-7), `polygon vertex ${index} painted`);
        }
        for (const dimension of original.dimensions) {
            const point = screen(add(dimension.anchor, delta));
            assert.ok(paint.some(call => call[0] === 'fillText' && call[1] === String(dimension.value)
                && Math.abs(call[2] - point.x) < 1e-7 && Math.abs(call[3] - point.y - 0.5) < 1e-7), `${dimension.id} label travels`);
        }
        if (welded) {
            const start = screen(delta);
            const end = screen(p(-2, 0));
            assert.ok(paint.some((call, index) => call[0] === 'moveTo'
                && Math.abs(call[1] - start.x) < 1e-7 && Math.abs(call[2] - start.y) < 1e-7
                && paint[index + 1]?.[0] === 'lineTo'
                && Math.abs(paint[index + 1][1] - end.x) < 1e-7 && Math.abs(paint[index + 1][2] - end.y) < 1e-7), 'welded peer painted');
        }
        assert.deepEqual([canvas.width, canvas.height], [2000, 1600]);
    }
    return { instance, canvas, calls, pointer, assertPainted, paint, setAccepted: value => { accepted = value; } };
}

for (const fixture of ['free', 'fixed', 'weld-free', 'weld-fixed']) for (const grip of grips) {
    test(`registered canvas pointer preview/release: polygon ${fixture} ${grip}`, async t => {
        const original = documentOf(fixture);
        const before = structuredClone(original);
        const locked = lockedFixture(fixture);
        const harness = canvasHarness(t, original, !locked);
        const { start } = gripOf(original, grip);
        const end = add(start, p(1, 1));
        harness.pointer('pointerdown', start);
        harness.pointer('pointermove', end);
        harness.assertPainted(locked ? p(0, 0) : p(1, 1));
        if (!locked) {
            harness.pointer('pointermove', start);
            harness.assertPainted(p(0, 0));
            harness.pointer('pointermove', end);
            harness.assertPainted(p(1, 1));
        }
        assert.equal(harness.calls.filter(call => call[0] === 'OnGeometryDragRequested').length, 0);
        assert.equal(harness.instance.getActiveSelectionKey(), null, 'drag must not create persistent selection');
        harness.pointer('pointerup', end);
        await new Promise(resolve => setImmediate(resolve));
        harness.assertPainted(locked ? p(0, 0) : p(1, 1));
        const requests = harness.calls.filter(call => call[0] === 'OnGeometryDragRequested');
        assert.equal(requests.length, 1);
        if (grip === 'whole') assert.equal(requests[0][1], 'polygon');
        else assert.ok(requests[0][1].startsWith(`polygon|point|${grip}|`), requests[0][1]);
        assert.equal(harness.instance.getActiveSelectionKey(), null);
        assertPoint(p(requests[0][2], requests[0][3]), start);
        assertPoint(p(requests[0][4], requests[0][5]), end);
        assert.equal(requests[0][6], false);
        assert.deepEqual(original, before);
        if (locked) {
            harness.instance.clearSelection();
            harness.instance.setDocument({ ...original, constraints: original.constraints.filter(c => c.kind !== 'fix') });
            harness.setAccepted(true);
            harness.pointer('pointerdown', start);
            harness.pointer('pointermove', end);
            harness.assertPainted(p(1, 1));
            harness.pointer('pointerup', end);
            await new Promise(resolve => setImmediate(resolve));
            harness.assertPainted(p(1, 1));
            assert.equal(harness.calls.filter(call => call[0] === 'OnGeometryDragRequested').length, 2);
            assert.deepEqual(original, before);
        }
    });
}

for (const grip of grips) {
    test(`registered canvas rolls back server refusal then recovers: polygon ${grip}`, async t => {
        const original = documentOf('weld-free');
        const harness = canvasHarness(t, original, false);
        const { start } = gripOf(original, grip);
        const end = add(start, p(1, 1));
        harness.pointer('pointerdown', start);
        harness.pointer('pointermove', end);
        harness.assertPainted(p(1, 1));
        harness.pointer('pointerup', end);
        harness.paint.length = 0;
        await new Promise(resolve => setImmediate(resolve));
        harness.assertPainted(p(0, 0));
        harness.pointer('pointerdown', start);
        harness.pointer('pointermove', end);
        harness.assertPainted(p(1, 1));
        harness.pointer('pointercancel', end);
        harness.assertPainted(p(0, 0));
        assert.equal(harness.calls.filter(call => call[0] === 'OnGeometryDragRequested').length, 1);
    });
}

for (const grip of grips) for (const offset of [0, 1e-9]) {
    test(`registered canvas negligible release sends no commit: polygon ${grip} offset=${offset}`, async t => {
        const original = documentOf('weld-free');
        const harness = canvasHarness(t, original, true);
        const { start } = gripOf(original, grip);
        const end = add(start, p(1, 1));
        harness.pointer('pointerdown', start);
        harness.pointer('pointermove', end);
        harness.assertPainted(p(1, 1));
        harness.pointer('pointermove', add(start, p(offset, offset)));
        harness.pointer('pointerup', add(start, p(offset, offset)));
        await new Promise(resolve => setImmediate(resolve));
        harness.assertPainted(p(0, 0));
        assert.equal(harness.calls.filter(call => call[0] === 'OnGeometryDragRequested').length, 0);
        harness.instance.clearSelection();
        harness.pointer('pointerdown', start);
        harness.pointer('pointermove', end);
        harness.assertPainted(p(1, 1));
        harness.pointer('pointerup', end);
        await new Promise(resolve => setImmediate(resolve));
        harness.assertPainted(p(1, 1));
        assert.equal(harness.calls.filter(call => call[0] === 'OnGeometryDragRequested').length, 1);
    });
}

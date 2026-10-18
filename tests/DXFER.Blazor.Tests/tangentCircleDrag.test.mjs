import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview, createDrawingCanvas } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const p = (x, y) => ({ x, y });
const add = (a, b) => p(a.x + b.x, a.y + b.y);
const near = (a, b) => assert.ok(a && Math.hypot(a.x - b.x, a.y - b.y) < 1e-7,
    `${JSON.stringify(a)} != ${JSON.stringify(b)}`);
const circleOf = document => document.entities.find(e => e.id === 'circle');
const constraint = (id, kind, referenceKeys) => ({ id, kind, referenceKeys, state: 'satisfied' });

function fixture({ angle = 0, dimension = 'diameter', polyline = false, reverse = false, fixed = true } = {}) {
    const radians = angle * Math.PI / 180;
    const xy = (x, y) => p(x * Math.cos(radians) - y * Math.sin(radians), x * Math.sin(radians) + y * Math.cos(radians));
    const points = reverse ? [xy(20, 0), xy(0, 0)] : [xy(0, 0), xy(20, 0)];
    if (polyline) points.push(xy(20, -10));
    const edge = { id: 'edge', kind: polyline ? 'polyline' : 'line', points };
    const lineKey = polyline ? 'edge|segment|0' : 'edge';
    const constraints = [constraint('tangent', 'tangent', reverse ? ['circle', lineKey] : [lineKey, 'circle'])];
    if (fixed) constraints.push(constraint('fixed-edge', 'fix', [lineKey]));
    if (reverse) constraints.reverse();
    const document = { entities: [edge, { id: 'circle', kind: 'circle', center: xy(10, 7.5), radius: 7.5 }],
        constraints, dimensions: [{ id: 'size', kind: dimension, referenceKeys: ['circle'],
            value: dimension === 'diameter' ? 15 : 7.5, isDriving: true, anchor: xy(21, 17) }] };
    return { document, xy };
}

function drag(document, grip, delta) {
    const circle = circleOf(document);
    const start = grip === 'center' ? circle.center : add(circle.center, p(circle.radius, 0));
    return applyGeometryDragPreview(document, `circle|point|${grip}|${start.x}|${start.y}`, start, add(start, delta));
}

function assertCircleTranslation(original, next, delta) {
    assert.ok(next, 'valid tangent translation must have a preview');
    near(circleOf(next).center, add(circleOf(original).center, delta));
    assert.equal(circleOf(next).radius, 7.5);
    for (let index = 0; index < original.dimensions.length; index++) {
        const before = original.dimensions[index], after = next.dimensions[index];
        assert.equal(after.value, before.value);
        assert.equal(after.isDriving, before.isDriving);
        assert.deepEqual(after.referenceKeys, before.referenceKeys);
        if (before.anchor && before.referenceKeys.includes('circle')) near(after.anchor, add(before.anchor, delta));
    }
    assert.deepEqual(next.constraints, original.constraints);
}

for (const angle of [0, 90, 37]) for (const dimension of ['radius', 'diameter'])
for (const grip of ['center', 'quadrant-0']) for (const polyline of [false, true]) for (const reverse of [false, true]) {
    test(`tangent circle angle=${angle} ${dimension} ${grip} polyline=${polyline} reverse=${reverse}`, () => {
        const { document, xy } = fixture({ angle, dimension, polyline, reverse });
        const before = structuredClone(document);
        const next = drag(document, grip, xy(4, 1.5));
        assertCircleTranslation(document, next, xy(4, 0));
        assert.deepEqual(next.entities[0].points, document.entities[0].points);
        assertCircleTranslation(document, drag(document, grip, xy(4, 0)), xy(4, 0));
        assert.deepEqual(document, before);
    });
}

for (const fixedKey of ['circle', 'circle:center']) test(`fixed ${fixedKey} refuses and recovers`, () => {
    const { document } = fixture();
    document.constraints.push(constraint('fixed-circle', 'fix', [fixedKey]));
    const before = structuredClone(document);
    assert.equal(drag(document, 'center', p(4, 1.5)), null);
    assert.equal(drag(document, 'quadrant-0', p(4, 1.5)), null);
    assert.deepEqual(document, before);
    document.constraints.at(-1).state = 'suppressed';
    assertCircleTranslation(document, drag(document, 'center', p(4, 1.5)), p(4, 0));
});

for (const dimension of ['radius', 'diameter']) test(`small repeated dimensioned perimeter motion is not restored to a no-op: ${dimension}`, () => {
    let { document } = fixture({ dimension });
    for (let index = 0; index < 5; index++) {
        const before = structuredClone(document);
        const next = drag(document, 'quadrant-0', p(0.1, 0.01));
        assertCircleTranslation(document, next, p(0.1, 0));
        near(circleOf(next).center, p(10 + (index + 1) * 0.1, 7.5));
        assert.deepEqual(document, before);
        document = next;
    }
});

for (const along of [0, 1e-9]) test(`zero/epsilon projected movement refuses: ${along}`, () => {
    const { document } = fixture();
    assert.equal(drag(document, 'center', p(along, 1.5)), null);
});

test('suppressed tangent does not project or propagate', () => {
    const { document } = fixture();
    document.constraints.find(c => c.id === 'tangent').state = 'suppressed';
    assertCircleTranslation(document, drag(document, 'center', p(4, 1.5)), p(4, 1.5));
});

test('valid full motion is preferred when tangent line is free', () => {
    const { document } = fixture({ fixed: false });
    const next = drag(document, 'center', p(4, 1.5));
    assertCircleTranslation(document, next, p(4, 1.5));
    near(next.entities[0].points[0], p(0, 1.5));
    near(next.entities[0].points[1], p(20, 1.5));
});

test('valid free-radius center motion is not replaced by a projection', () => {
    const { document } = fixture();
    document.dimensions[0].isDriving = false;
    const next = drag(document, 'center', p(4, 1.5));
    assert.ok(next);
    near(circleOf(next).center, p(14, 9));
    assert.equal(circleOf(next).radius, 9);
});

for (const sizeState of ['absent', 'reference', 'suppressed']) {
    test(`Equal/Tangent/Fixed free-radius grip refuses translation: size=${sizeState}`, () => {
        const { document } = fixture({ fixed: false });
        document.entities.push({ id: 'peer', kind: 'circle', center: p(40, 7.5), radius: 7.5 });
        document.constraints.push(constraint('equal', 'equal', ['circle', 'peer']), constraint('fixed-peer', 'fix', ['peer']));
        if (sizeState === 'absent') document.dimensions = [];
        if (sizeState === 'reference') document.dimensions[0].isDriving = false;
        if (sizeState === 'suppressed') document.dimensions[0].state = 'suppressed';
        const before = structuredClone(document);
        assert.equal(drag(document, 'quadrant-0', p(4, 1.5)), null);
        assert.deepEqual(document, before);
        assertCircleTranslation(document, drag(document, 'center', p(4, 1.5)), p(4, 1.5));
    });
}

for (const reverse of [false, true]) test(`two incompatible active tangents refuse: reverse=${reverse}`, () => {
    const { document } = fixture();
    circleOf(document).center = p(7.5, 7.5);
    document.entities.push({ id: 'left', kind: 'line', points: [p(0, 0), p(0, 20)] });
    document.constraints.push(constraint('fixed-left', 'fix', ['left']), constraint('left-tangent', 'tangent', ['circle', 'left']));
    if (reverse) document.constraints.reverse();
    const before = structuredClone(document);
    assert.equal(drag(document, 'center', p(4, 1.5)), null);
    assert.deepEqual(document, before);
});

test('projection still validates unrelated driving dimensions and constraints', () => {
    for (const unsupported of [false, true]) {
        const { document } = fixture();
        if (unsupported) document.constraints.push(constraint('unsupported', 'unknown', ['edge']));
        else document.dimensions.push({ id: 'conflict', kind: 'linearDistance', referenceKeys: ['edge:start', 'edge:end'], value: 25, isDriving: true });
        assert.equal(drag(document, 'center', p(4, 1.5)), null);
    }
});

test('arcs and arbitrary point entities do not gain circle translation fallback', () => {
    const { document } = fixture();
    Object.assign(circleOf(document), { kind: 'arc', startAngleDegrees: 0, endAngleDegrees: 180 });
    assert.equal(drag(document, 'center', p(4, 1.5)), null);
    const pointDoc = structuredClone(document);
    Object.assign(circleOf(pointDoc), { kind: 'point', points: [p(10, 7.5)] });
    assert.equal(drag(pointDoc, 'center', p(4, 1.5)), null);
});

function rectangleFixture(reverse = false) {
    const { document } = fixture({ fixed: false });
    const corners = [p(0, 0), p(20, 0), p(20, 10), p(0, 10)];
    const ids = ['edge', 'right', 'top', 'left'];
    document.entities = ids.map((id, i) => ({ id, kind: 'line', points: [corners[i], corners[(i + 1) % 4]] })).concat(circleOf(document));
    for (let i = 0; i < 4; i++) {
        document.constraints.push(constraint(`weld-${i}`, 'coincident', [`${ids[i]}:end`, `${ids[(i + 1) % 4]}:start`]),
            constraint(`axis-${i}`, i % 2 ? 'vertical' : 'horizontal', [ids[i]]));
    }
    document.dimensions.push({ id: 'width', kind: 'linearDistance', referenceKeys: ['edge:start', 'edge:end'], value: 20, isDriving: true },
        { id: 'height', kind: 'linearDistance', referenceKeys: ['right:start', 'right:end'], value: 10, isDriving: true });
    if (reverse) document.constraints.reverse();
    return document;
}

for (const reverse of [false, true]) test(`free dimensioned 20x10 rectangle tangent circle reverse=${reverse}`, () => {
    const document = rectangleFixture(reverse);
    const before = structuredClone(document);
    const next = drag(document, 'center', p(4, 1.5));
    assertCircleTranslation(document, next, p(4, 0));
    for (let i = 0; i < 4; i++) assert.deepEqual(next.entities[i].points, document.entities[i].points);
    assert.deepEqual(document, before);
});

function canvasHarness(t, document, accepted) {
    const previous = globalThis.window;
    globalThis.window = { devicePixelRatio: 2, addEventListener() {}, removeEventListener() {} };
    const paint = [], calls = [], handlers = new Map();
    const context = new Proxy({ createRadialGradient: () => ({ addColorStop() {} }), measureText: text => ({ width: String(text).length * 7 }) }, {
        get: (object, key) => key in object ? object[key] : (...args) => paint.push([key, ...args])
    });
    const canvas = { width: 0, height: 0, clientWidth: 1000, clientHeight: 800, dataset: {}, style: {},
        getContext: () => context, getBoundingClientRect: () => ({ left: 0, top: 0, width: 1000, height: 800 }),
        addEventListener: (name, callback) => handlers.set(name, callback), removeEventListener() {},
        focus() {}, setPointerCapture() {}, releasePointerCapture() {} };
    const instance = createDrawingCanvas(canvas, { invokeMethodAsync: (...args) => {
        calls.push(args);
        return Promise.resolve(args[0] === 'OnGeometryDragRequested' ? accepted : undefined);
    } });
    t.after(() => { instance.dispose(); if (previous === undefined) delete globalThis.window; else globalThis.window = previous; });
    instance.setDocument(document, true);
    const scale = () => Number(canvas.dataset.scale);
    const screen = point => p(point.x * scale() + Number(canvas.dataset.offsetX), -point.y * scale() + Number(canvas.dataset.offsetY));
    return { calls, pointer(type, point) {
        const at = screen(point);
        paint.length = 0;
        handlers.get(type)({ pointerId: 1, pointerType: 'mouse', button: 0, buttons: type === 'pointerup' ? 0 : 1,
            clientX: at.x, clientY: at.y, preventDefault() {} });
    }, painted(center) {
        const at = screen(center);
        assert.ok(paint.some(call => call[0] === 'arc' && Math.abs(call[1] - at.x) < 1e-7
            && Math.abs(call[2] - at.y) < 1e-7 && Math.abs(call[3] - 7.5 * scale()) < 1e-7), 'circle preview painted');
        assert.deepEqual([canvas.width, canvas.height], [2000, 1600]);
    } };
}

for (const accepted of [false, true]) for (const grip of ['center', 'quadrant-0']) {
    test(`registered canvas tangent ${grip} preview/release accepted=${accepted}`, async t => {
        const { document } = fixture();
        const before = structuredClone(document);
        const h = canvasHarness(t, document, accepted);
        const start = grip === 'center' ? p(10, 7.5) : p(17.5, 7.5);
        const end = add(start, p(4, 1.5));
        h.pointer('pointerdown', start);
        h.pointer('pointermove', end);
        h.painted(p(14, 7.5));
        assert.equal(h.calls.filter(c => c[0] === 'OnGeometryDragRequested').length, 0);
        h.pointer('pointerup', end);
        await new Promise(resolve => setImmediate(resolve));
        h.painted(accepted ? p(14, 7.5) : p(10, 7.5));
        const requests = h.calls.filter(c => c[0] === 'OnGeometryDragRequested');
        assert.equal(requests.length, 1);
        assert.ok(requests[0][1].startsWith(`circle|point|${grip}|`));
        near(p(requests[0][2], requests[0][3]), start);
        near(p(requests[0][4], requests[0][5]), end);
        assert.deepEqual(document, before);
    });
}

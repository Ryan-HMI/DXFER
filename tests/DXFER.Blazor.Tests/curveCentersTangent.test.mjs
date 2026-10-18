import assert from 'node:assert/strict';
import test from 'node:test';
import { createDrawingCanvas, getDynamicSketchSnapHit, shouldShowPersistentCurveCenter } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
const p = (x, y) => ({ x, y });
const curve = kind => ({ id: 'curve', kind, center: p(10, 10), radius: 5, startAngleDegrees: 0, endAngleDegrees: 90 });
const screen = point => p(point.x * 20, 500 - point.y * 20);
function snapState(entity, start = p(13, 14)) {
    return { activeTool: 'line', document: { entities: [entity] }, acquiredSnapPoints: [],
        toolDraft: { points: [start], startSnapTarget: { kind: 'entity', key: 'curve', entityId: 'curve', entity, snapPoint: start } },
        view: { scale: 20, offsetX: 0, offsetY: 500 } };
}
for (const kind of ['circle', 'arc']) {
    test(`${kind} center is discoverable before selection and persists after deselection`, () => {
        const entity = curve(kind), state = { selectedKeys: new Set() };
        assert.equal(shouldShowPersistentCurveCenter(state, entity), true);
        state.selectedKeys.add('curve'); assert.equal(shouldShowPersistentCurveCenter(state, entity), true);
        state.selectedKeys = new Set(['curve|point|quadrant-0|15|10']);
        assert.equal(shouldShowPersistentCurveCenter(state, entity), true);
        state.selectedKeys.clear(); assert.equal(shouldShowPersistentCurveCenter(state, entity), true);
    });
    for (const direction of [1, -1]) test(`${kind} outgoing tangent direction ${direction}`, () => {
        const state = snapState(curve(kind));
        const expected = p(13 - 4 * direction, 14 + 3 * direction);
        const hit = getDynamicSketchSnapHit(state, screen(expected));
        assert.equal(hit?.target.label, 'tangent-curve');
        assert.ok(Math.hypot(hit.target.point.x - expected.x, hit.target.point.y - expected.y) < 1e-9);
        assert.equal(getDynamicSketchSnapHit(state, screen(p(expected.x + .4, expected.y + .4))), null);
    });
    test(`${kind} interior/center are not tangent starts; normal survives`, () => {
        for (const start of [p(10, 10), p(11, 11)]) {
            const hit = getDynamicSketchSnapHit(snapState(curve(kind), start), screen(p(start.x - 4, start.y + 3)));
            assert.ok(!hit?.target.label.startsWith('tangent'));
        }
        assert.equal(getDynamicSketchSnapHit(snapState(curve(kind)), screen(p(16, 18)))?.target.label, 'normal-curve');
    });
}
test('arc tangent contact must be on its actual sweep', () => {
    const state = snapState(curve('arc'), p(7, 6));
    assert.ok(!getDynamicSketchSnapHit(state, screen(p(11, 3)))?.target.label.startsWith('tangent'));
});

for (const kind of ['circle', 'arc']) test(`registered canvas paints selected ${kind} center after pointer leaves`, t => {
    const previous = globalThis.window;
    globalThis.window = { devicePixelRatio: 1, addEventListener() {}, removeEventListener() {} };
    const calls = [], handlers = new Map();
    const context = new Proxy({ createRadialGradient: () => ({ addColorStop() {} }), measureText: text => ({ width: String(text).length * 7 }) }, {
        get: (target, key) => key in target ? target[key] : (...args) => calls.push([key, ...args])
    });
    const canvas = { clientWidth: 1000, clientHeight: 800, dataset: {}, style: {}, getContext: () => context,
        getBoundingClientRect: () => ({ left: 0, top: 0, width: 1000, height: 800 }),
        addEventListener: (key, handler) => handlers.set(key, handler), removeEventListener() {},
        focus() {}, setPointerCapture() {}, releasePointerCapture() {} };
    const instance = createDrawingCanvas(canvas, { invokeMethodAsync: () => Promise.resolve() });
    t.after(() => { instance.dispose(); if (previous === undefined) delete globalThis.window; else globalThis.window = previous; });
    instance.setDocument({ entities: [curve(kind)], dimensions: [], constraints: [] }, true);
    const world = point => p(point.x * Number(canvas.dataset.scale) + Number(canvas.dataset.offsetX),
        -point.y * Number(canvas.dataset.scale) + Number(canvas.dataset.offsetY));
    const selected = world(p(13, 14));
    const event = point => ({ clientX: point.x, clientY: point.y, pointerId: 1, pointerType: 'mouse', button: 0, buttons: 0, preventDefault() {} });
    handlers.get('pointerdown')(event(selected)); handlers.get('pointerup')(event(selected));
    calls.length = 0; handlers.get('pointermove')(event(world(p(0, 0))));
    const center = world(p(10, 10));
    const markers = () => calls.filter(c => c[0] === 'arc' && Math.abs(c[1] - center.x) < 1e-7
        && Math.abs(c[2] - center.y) < 1e-7 && c[3] === 3.5);
    assert.ok(markers().length > 0);
    calls.length = 0; instance.clearSelection(); assert.ok(markers().length > 0);
});

import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview, findNearestTarget, getDimensionSelectionKey, isDimensionReferenceSetComplete } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
import { isTargetEligibleForConstraintTool } from '../../src/DXFER.Blazor/wwwroot/canvas/targets.js';

const p = (x, y) => ({ x, y });
const line = { id: 'line', kind: 'line', points: [p(2, 2), p(6, 2)] };
const state = tool => ({ activeTool: tool, document: { entities: [line] },
    view: { scale: 50, offsetX: 200, offsetY: 300 }, selectedKeys: new Set() });

for (const tool of ['select', 'dimension', 'coincident', 'horizontal', 'vertical', 'midpoint']) {
    test(`origin is a selectable persistent point for ${tool}`, () => {
        const s = state(tool), target = findNearestTarget(s, p(200, 300));
        assert.equal(target?.key, '$origin:center');
        assert.equal(target.entity, null);
        assert.equal(target.dynamic, undefined);
        assert.equal(getDimensionSelectionKey(target), '$origin:center');
        if (!['select', 'dimension'].includes(tool)) assert.ok(isTargetEligibleForConstraintTool(tool, target));
        assert.ok(isDimensionReferenceSetComplete(s, ['$origin:center', 'line:start'], target));
        assert.ok(isDimensionReferenceSetComplete(s, ['line:start', '$origin:center'], target));
    });
}

test('origin does not intercept geometry tools or point handles', () => {
    assert.equal(findNearestTarget(state('powertrim'), p(200, 300)), null);
    const s = state('select'); s.document.entities = [{ ...line, points: [p(0, 0), p(6, 2)] }];
    const point = findNearestTarget(s, p(200, 300));
    assert.notEqual(point.key, '$origin:center');
    s.activeTool = 'coincident'; s.selectedKeys.add(point.key);
    assert.equal(findNearestTarget(s, p(200, 300)).key, '$origin:center');
});

test('partial endpoint motion follows the measured span without changing driving X', () => {
    const original = { entities: [line], constraints: [], dimensions: [
        { id: 'width', kind: 'horizontaldistance', referenceKeys: ['line:start', 'line:end'], value: 4, isDriving: true, anchor: p(4, 0) }
    ] };
    const next = applyGeometryDragPreview(original, 'line|point|end|6|2', p(6, 2), p(6, 4));
    assert.ok(next);
    assert.deepEqual(next.dimensions[0].anchor, p(4, 1));
    assert.equal(next.dimensions[0].value, 4);
    assert.deepEqual(original.dimensions[0].anchor, p(4, 0));
    const again = applyGeometryDragPreview(next, 'line|point|end|6|4', p(6, 4), p(6, 6));
    assert.deepEqual(again.dimensions[0].anchor, p(4, 2));
});

test('origin horizontal relation permits only compatible free-axis motion', () => {
    const original = { entities: [line], constraints: [
        { id: 'y', kind: 'horizontal', state: 'satisfied', referenceKeys: ['$origin:center', 'line:start'] }
    ], dimensions: [] };
    original.entities = [{ ...line, points: [p(2, 0), p(6, 2)] }];
    const next = applyGeometryDragPreview(original, 'line|point|start|2|0', p(2, 0), p(3, 0));
    assert.ok(next);
    assert.deepEqual(next.entities[0].points[0], p(3, 0));
    const bad = applyGeometryDragPreview(original, 'line|point|start|2|0', p(2, 0), p(2, 1));
    assert.ok(!bad || bad.entities[0].points[0].y === 0);
});

test('fixed-start rotation carries the label in the line frame', () => {
    const original = { entities: [{ ...line, points: [p(0, 0), p(10, 0)] }], constraints: [
        { id: 'fixed', kind: 'fix', state: 'satisfied', referenceKeys: ['line:start'] }
    ], dimensions: [{ id: 'length', kind: 'lineardistance', referenceKeys: ['line:start', 'line:end'], value: 10, isDriving: true, anchor: p(5, -2) }] };
    const next = applyGeometryDragPreview(original, 'line|point|end|10|0', p(10, 0), p(8, 6));
    assert.ok(next);
    assert.ok(Math.abs(next.dimensions[0].anchor.x - 5.2) < 1e-8);
    assert.ok(Math.abs(next.dimensions[0].anchor.y - 1.4) < 1e-8);
    assert.equal(next.dimensions[0].value, 10);
    assert.deepEqual(original.dimensions[0].anchor, p(5, -2));
});

import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createPlaneGcsSolver } from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';

const p = (x, y) => ({ x, y });
const line = (id, a, b) => ({ id, kind: 'line', points: [a, b] });
const circle = (id, radius) => ({ id, kind: 'circle', points: [], center: p(0, 0), radius });
const rectangle = (id, gap = 0) => ({ id, kind: 'polyline',
    points: [p(-gap, -gap), p(8 + gap, -gap), p(8 + gap, 6 + gap), p(-gap, 6 + gap), p(-gap, -gap)] });
const intent = (kind, keys, id = kind) => ({ id, kind, referenceKeys: keys, state: 'Satisfied' });
const dim = (keys, value) => ({ id: 'gap', kind: 'Offset', referenceKeys: keys, value, isDriving: true });
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-6, `${a} != ${b}`);
let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(() => solver?.dispose());
function solve(entities, keys, constraints = [], dimensions = [], updates = {}) {
    const request = { document: { entities, constraints: [intent('Offset', keys), ...constraints], dimensions },
        attemptedDrivingDimensionIds: updates.dimensions?.filter(d => d.isDriving).map(d => d.id) ?? [],
        ...updates };
    const snapshot = structuredClone(request), result = solver.solve(request);
    assert.deepEqual(request, snapshot);
    assert.equal(result.engine, 'PlaneGCS');
    return result;
}
function passed(result) {
    assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
    assert.deepEqual(result.redundantDrivingDimensionIds, []);
}

for (const gap of [1, 2]) {
    test(`retained concentric circle offset solves driving gap ${gap}`, () => {
        const keys = ['source', 'copy'];
        const result = solve([circle('source', 2), circle('copy', 3)], keys, [],
            [dim(keys, 1)], { dimensions: [dim(keys, gap)] });
        passed(result);
        near(result.entities[0].radius, 2);
        near(result.entities[1].radius, 2 + gap);
        assert.deepEqual(result.entities[0].center, p(0, 0));
        assert.deepEqual(result.entities[1].center, p(0, 0));
    });
}
test('retained derived-circle tangent is solved by the same engine equations', () => {
    const edge = line('edge', p(-10, 5), p(10, 5));
    const result = solve([circle('source', 2), circle('copy', 3), edge], ['source', 'copy'],
        [intent('Fix', ['edge']), intent('Tangent', ['edge', 'copy'])]);
    passed(result);
    const [source, copy] = result.entities;
    near(copy.radius - source.radius, 1);
    near(copy.center.x, source.center.x);
    near(copy.center.y, source.center.y);
    near(Math.abs(copy.center.y - 5), copy.radius);
    assert.deepEqual(result.entities[2], edge);
});
test('fixed source and dependent refuse incompatible offset gap without partial updates', () => {
    const entities = [circle('source', 2), circle('copy', 3)], keys = ['source', 'copy'];
    const result = solve(entities, keys, [intent('Fix', keys)], [dim(keys, 1)],
        { dimensions: [dim(keys, 2)] });
    assert.equal(result.status, 'failed');
    assert.deepEqual(result.entities, entities);
});
for (const gap of [1, 2]) {
    test(`retained miter rectangle offset solves gap ${gap} and preserves source`, () => {
        const keys = ['source', 'copy'], entities = [rectangle('source'), rectangle('copy', 1)];
        const result = solve(entities, keys, [], [dim(keys, 1)], { dimensions: [dim(keys, gap)] });
        passed(result);
        assert.deepEqual(result.entities[0], entities[0], 'Gap edits preserve the complete source exactly');
        for (let i = 0; i < 5; i++) {
            near(result.entities[0].points[i].x, entities[0].points[i].x);
            near(result.entities[0].points[i].y, entities[0].points[i].y);
            near(result.entities[1].points[i].x, rectangle('copy', gap).points[i].x);
            near(result.entities[1].points[i].y, rectangle('copy', gap).points[i].y);
        }
    });
}
test('retained single-line offset preserves projected endpoints on gap edit', () => {
    const keys = ['source', 'copy'], entities = [
        line('source', p(0, 0), p(8, 0)), line('copy', p(0, 1), p(8, 1))];
    const result = solve(entities, keys, [intent('Fix', ['source'])],
        [dim(keys, 1)], { dimensions: [dim(keys, 2)] });
    passed(result);
    near(result.entities[1].points[0].x, 0); near(result.entities[1].points[0].y, 2);
    near(result.entities[1].points[1].x, 8); near(result.entities[1].points[1].y, 2);
});
test('orphan Offset dimension refuses instead of dropping its driving intent', () => {
    const entities = [circle('source', 2), circle('copy', 3)];
    const result = solver.solve({ document: { entities, constraints: [], dimensions: [dim(['source', 'copy'], 1)] } });
    assert.equal(result.status, 'unsupported');
    assert.deepEqual(result.entities, entities);
});

for (const attempt of [0, 1, 2, 3]) {
    const reverse = attempt % 2 === 1;
    test(`joined four-line Offset retains source joints on repeated solve ${attempt} reverse=${reverse}`, () => {
        const vertices = rectangle('outline').points;
        let edges = vertices.slice(1).map((b, i) => line(`edge${i}`, vertices[i], b));
        if (reverse) edges.reverse();
        const keys = [...edges.map(e => e.id), 'copy'];
        const entities = [...edges, rectangle('copy', 1)];
        const result = solve(entities, keys, [], [dim(keys, 1)], {
            drag: { key: 'edge0:start', start: p(0, 0), end: p(-1, -1), constrain: false }
        });
        passed(result);
        const sorted = [...result.entities.filter(e => e.kind === 'line')].sort((a, b) => a.id.localeCompare(b.id));
        for (let i = 0; i < 4; i++) {
            near(sorted[i].points[1].x, sorted[(i + 1) % 4].points[0].x);
            near(sorted[i].points[1].y, sorted[(i + 1) % 4].points[0].y);
        }
        assert.ok(Math.hypot(sorted[0].points[0].x, sorted[0].points[0].y) > 0.5);
    });
}

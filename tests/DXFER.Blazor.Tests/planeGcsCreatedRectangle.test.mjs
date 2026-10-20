import assert from 'node:assert/strict';
import {after, before, test} from 'node:test';
import {createPlaneGcsSolver} from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';

let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(async () => { await solver?.dispose(); });

// Same topology and constraint combination emitted by Two-point rectangle,
// including the redundant Perpendicular relation alongside the first two axes.
function rectangle(scale, shift, reverse) {
    const points = [[-3.625, 2.4166666666666665], [3.458333333333334, 2.4166666666666665],
        [3.458333333333334, -2.1666666666666665], [-3.625, -2.1666666666666665]]
        .map(([x, y]) => ({x: x * scale + shift, y: y * scale + shift}));
    const entities = points.map((p, i) => ({id: `edge${i}`, kind: 'line', points: [p, points[(i + 1) % 4]]}));
    const constraint = (id, kind, referenceKeys) => ({id, kind, referenceKeys, state: 'satisfied'});
    const constraints = points.map((_, i) => constraint(`weld${i}`, 'coincident',
        [`edge${i}:end`, `edge${(i + 1) % 4}:start`]));
    constraints.push(constraint('parallel0', 'parallel', ['edge0', 'edge2']),
        constraint('parallel1', 'parallel', ['edge1', 'edge3']),
        constraint('perpendicular', 'perpendicular', ['edge0', 'edge1']),
        constraint('horizontal', 'horizontal', ['edge0']),
        constraint('vertical', 'vertical', ['edge1']));
    return {entities: reverse ? entities.reverse() : entities,
        constraints: reverse ? constraints.reverse() : constraints, dimensions: []};
}

const point = (doc, corner) => doc.entities.find(e => e.id === `edge${corner}`).points[0];
function near(a, b, tolerance = 1e-6) {
    assert.ok(Math.abs(a - b) < tolerance, `${a} != ${b}`);
}
function validRectangle(result) {
    for (let i = 0; i < 4; ++i) {
        const edge = result.entities.find(e => e.id === `edge${i}`);
        const next = result.entities.find(e => e.id === `edge${(i + 1) % 4}`);
        near(edge.points[1].x, next.points[0].x);
        near(edge.points[1].y, next.points[0].y);
        const axis = i % 2 === 0 ? 'y' : 'x';
        near(edge.points[0][axis], edge.points[1][axis]);
        assert.ok(Math.hypot(edge.points[1].x - edge.points[0].x, edge.points[1].y - edge.points[0].y) > 1e-8);
    }
}

for (const corner of [0, 1, 2, 3]) for (const reverse of [false, true])
    for (const [scale, shift] of [[1, 0], [0.001, 15], [100, -2000]]) {
        test(`created rectangle corner ${corner}, reverse=${reverse}, scale=${scale} preserves all native relations`, () => {
            const doc = rectangle(scale, shift, reverse);
            const start = point(doc, corner);
            const end = {x: start.x + scale * (corner === 0 || corner === 3 ? -1.6666666667 : 1.6666666667),
                y: start.y + scale * (corner < 2 ? 2.0833333333 : -2.0833333333)};
            const request = {document: doc, constraints: [], dimensions: [], fixedReferenceKeys: [], initialGuesses: [],
                drag: {key: `edge${corner}:start`, start, end, constrain: false}};
            const snapshot = structuredClone(request);
            for (let repeat = 0; repeat < 3; ++repeat) {
                const result = solver.solve(request);
                assert.equal(result.engine, 'PlaneGCS');
                assert.equal(result.status, 'solved', result.diagnostics.join(' '));
                validRectangle(result);
                near(point(result, corner).x, end.x, Math.max(1e-6, scale * 1e-5));
                near(point(result, corner).y, end.y, Math.max(1e-6, scale * 1e-5));
                assert.deepEqual(request, snapshot);
            }
        });
    }

test('created rectangle fixed corner remains exact under an incompatible pointer target', () => {
    const doc = rectangle(1, 0, false), start = point(doc, 2);
    doc.constraints.push({id: 'fixed-corner', kind: 'fix', referenceKeys: ['edge2:start'], state: 'satisfied'});
    const result = solver.solve({document: doc, constraints: [], dimensions: [], fixedReferenceKeys: [], initialGuesses: [],
        drag: {key: 'edge2:start', start, end: {x: start.x + 2, y: start.y - 2}, constrain: false}});
    if (result.status === 'solved') {
        assert.deepEqual(point(result, 2), start);
        validRectangle(result);
    } else {
        assert.deepEqual(result.entities, doc.entities, 'Refusal cannot publish a partially solved rectangle');
    }
});

for (const edgeIndex of [0, 1, 2, 3]) for (const reverse of [false, true]) {
    test(`created rectangle edge ${edgeIndex} translates without breaking closure, reverse=${reverse}`, () => {
        const doc = rectangle(1, 0, reverse);
        const edge = doc.entities.find(e => e.id === `edge${edgeIndex}`);
        const start = {x: (edge.points[0].x + edge.points[1].x) / 2, y: (edge.points[0].y + edge.points[1].y) / 2};
        const end = {x: start.x + 1, y: start.y + 1};
        const result = solver.solve({document: doc, constraints: [], dimensions: [], fixedReferenceKeys: [], initialGuesses: [],
            drag: {key: edge.id, start, end, constrain: false}});
        assert.equal(result.status, 'solved', result.diagnostics.join(' '));
        validRectangle(result);
        const moved = result.entities.find(e => e.id === edge.id);
        for (let i = 0; i < 2; ++i) {
            near(moved.points[i].x, edge.points[i].x + 1);
            near(moved.points[i].y, edge.points[i].y + 1);
        }
    });
}

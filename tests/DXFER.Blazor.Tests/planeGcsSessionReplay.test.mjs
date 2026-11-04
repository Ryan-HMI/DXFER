import assert from 'node:assert/strict';
import {spawnSync} from 'node:child_process';
import {test} from 'node:test';
import {fileURLToPath} from 'node:url';
import {createPlaneGcsSolver} from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';
import {GcsWrapper} from '../../src/DXFER.Blazor/wwwroot/vendor/planegcs/dist/index.js';

// Synthetic lifecycle coverage, not Flux's unavailable exact 6524dc5 drawing.
// Reopen means deserializing the original unsolved baseline, never feeding back
// the previously solved geometry. One radius drives two free polygons; the fixed
// axis also has Vertical intent, leaving DOF 3 after adding Symmetric.
const p = (x, y) => ({x, y});
const relation = (id, kind, referenceKeys) => ({id, kind, referenceKeys, state: 'satisfied'});
const dimension = (id, kind, referenceKeys, value) => ({id, kind, referenceKeys, value, isDriving: true});
const reopen = request => JSON.parse(JSON.stringify(request));
const near = (actual, expected, context) => assert.ok(Number.isFinite(actual)
    && Math.abs(actual - expected) < 1e-6, `${context}: ${actual} != ${expected}`);

function polygonRequest({circumscribed = false, shift = 0, radius = 3} = {}) {
    const polygon = (id, center, size, angle) => {
        const sideCount = 5;
        const phase = angle * Math.PI / 180 + (circumscribed ? Math.PI / sideCount : 0);
        const vertexRadius = circumscribed ? size / Math.cos(Math.PI / sideCount) : size;
        return {id, kind: 'polygon', center, radius: size, rotationAngleDegrees: angle, sideCount, circumscribed,
            points: Array.from({length: sideCount}, (_, i) => p(
                center.x + vertexRadius * Math.cos(phase + i * 2 * Math.PI / sideCount),
                center.y + vertexRadius * Math.sin(phase + i * 2 * Math.PI / sideCount)))};
    };
    return {
        document: {
            entities: [{id: 'axis', kind: 'line', points: [p(shift, -10), p(shift, 10)]},
                polygon('a', p(shift - 5, 1), radius, 20), polygon('b', p(shift + 6, 2), 2, 130)],
            constraints: [relation('axis-vertical', 'Vertical', ['axis'])],
            dimensions: [dimension('radius', 'Radius', ['a'], radius)]
        },
        constraints: [relation('mirror', 'Symmetric', ['a', 'b', 'axis'])],
        dimensions: [], fixedReferenceKeys: ['axis'], initialGuesses: []
    };
}

function rectangleEdit() {
    const points = [p(0, 0), p(15, 0), p(15, 8), p(0, 8)];
    return {
        document: {
            entities: points.map((point, i) => ({id: `edge${i}`, kind: 'line',
                points: [point, points[(i + 1) % 4]]})),
            constraints: [
                ...points.map((_, i) => relation(`weld${i}`, 'Coincident',
                    [`edge${i}:end`, `edge${(i + 1) % 4}:start`])),
                relation('parallel0', 'Parallel', ['edge0', 'edge2']),
                relation('parallel1', 'Parallel', ['edge1', 'edge3']),
                relation('perpendicular', 'Perpendicular', ['edge0', 'edge1']),
                relation('horizontal', 'Horizontal', ['edge0']),
                relation('vertical', 'Vertical', ['edge1'])
            ],
            dimensions: [dimension('width', 'LinearDistance', ['edge0'], 15)]
        },
        dimensions: [dimension('width', 'LinearDistance', ['edge0'], 16)],
        attemptedDrivingDimensionIds: ['width']
    };
}

function solved(result, context) {
    assert.equal(result.engine, 'PlaneGCS', context);
    assert.equal(result.status, 'solved', `${context}: ${JSON.stringify(result.diagnostics)}`);
    assert.deepEqual(result.redundantDrivingDimensionIds, [], context);
    assert.ok(!result.diagnostics.some(message => message.startsWith('Conflicting intent:')), context);
}

function solveSnapshot(solver, request) {
    const snapshot = reopen(request);
    const result = solver.solve(request);
    assert.deepEqual(request, snapshot, 'Solving must not mutate the baseline request');
    return result;
}

function assertPolygon(result, request, context) {
    solved(result, context);
    assert.ok(result.diagnostics.includes('PlaneGCS mapped-system degrees of freedom: 3.'),
        `${context}: ${JSON.stringify(result.diagnostics)}`);
    assert.deepEqual(result.entities.map(entity => entity.id), ['axis', 'a', 'b'], context);
    const [axis, a, b] = result.entities;
    assert.deepEqual(axis, request.document.entities[0], `${context}: fixed axis`);
    const axisX = axis.points[0].x;
    near(a.radius, request.document.dimensions[0].value, `${context}: driving radius`);
    near(b.radius, a.radius, `${context}: mirrored radius`);
    near(b.center.x, 2 * axisX - a.center.x, `${context}: center x`);
    near(b.center.y, a.center.y, `${context}: center y`);
    for (const [index, entity] of [a, b].entries()) {
        assert.equal(entity.sideCount, 5, context);
        assert.equal(entity.circumscribed, request.document.entities[index + 1].circumscribed, context);
        assert.equal(entity.points.length, 5, context);
    }
    // Reflection reverses winding; circumscribed polygons also shift one vertex.
    const offset = a.circumscribed ? 1 : 0;
    a.points.forEach((point, i) => {
        const mirror = b.points[(5 - i - offset) % 5];
        near(mirror.x, 2 * axisX - point.x, `${context}: vertex ${i} x`);
        near(mirror.y, point.y, `${context}: vertex ${i} y`);
    });
}

function assertRectangle(result, context) {
    solved(result, context);
    assert.ok(result.diagnostics.some(message => message.startsWith('Native rank check width:')), context);
    result.entities.forEach((edge, i) => {
        const next = result.entities[(i + 1) % 4];
        near(edge.points[1].x, next.points[0].x, `${context}: weld ${i} x`);
        near(edge.points[1].y, next.points[0].y, `${context}: weld ${i} y`);
        const coordinate = i % 2 === 0 ? 'y' : 'x';
        near(edge.points[0][coordinate], edge.points[1][coordinate], `${context}: axis ${i}`);
    });
    const [a, b] = result.entities[0].points;
    near(Math.hypot(b.x - a.x, b.y - a.y), 16, `${context}: edited width`);
}

function sameGeometry(actual, expected, context) {
    if (typeof expected === 'number') return near(actual, expected, context);
    if (expected !== null && typeof expected === 'object') {
        assert.deepEqual(Object.keys(actual), Object.keys(expected), context);
        for (const key of Object.keys(expected)) sameGeometry(actual[key], expected[key], `${context}.${key}`);
    } else assert.equal(actual, expected, context);
}

async function freshResult(request) {
    const solver = await createPlaneGcsSolver();
    try { return solveSnapshot(solver, reopen(request)); }
    finally { solver.dispose(); }
}

for (const circumscribed of [false, true]) {
    test(`same-module baseline reopens match a fresh polygon solve, circumscribed=${circumscribed}`, async () => {
        const request = polygonRequest({circumscribed});
        const serialized = JSON.stringify(request);
        const fresh = await freshResult(request);
        assertPolygon(fresh, request, 'fresh control');
        const solver = await createPlaneGcsSolver();
        try {
            for (let i = 0; i < 4; ++i) {
                const result = solveSnapshot(solver, JSON.parse(serialized));
                assertPolygon(result, request, `reopen ${i}`);
                sameGeometry(result.entities, fresh.entities, `reopen ${i} versus fresh`);
            }
            assert.equal(JSON.stringify(request), serialized);
        } finally { solver.dispose(); }
    });

    test(`successful rectangle edit precedes FIRST polygon solve, circumscribed=${circumscribed}`, async () => {
        const request = polygonRequest({circumscribed});
        const fresh = await freshResult(request);
        assertPolygon(fresh, request, 'fresh control');
        const solver = await createPlaneGcsSolver();
        try {
            assertRectangle(solveSnapshot(solver, rectangleEdit()), 'width 15 -> 16');
            for (let i = 0; i < 3; ++i) {
                const result = solveSnapshot(solver, reopen(request));
                assertPolygon(result, request, `polygon after rectangle ${i}`);
                sameGeometry(result.entities, fresh.entities, `polygon after rectangle ${i} versus fresh`);
            }
        } finally { solver.dispose(); }
    });
}

test('documents reusing entity and intent IDs do not retain geometry or rank-probe state', async () => {
    const a = polygonRequest();
    const b = polygonRequest({shift: 20, radius: 4});
    b.attemptedDrivingDimensionIds = ['radius'];
    const freshA = await freshResult(a), freshB = await freshResult(b);
    assertPolygon(freshA, a, 'fresh A');
    assertPolygon(freshB, b, 'fresh B');
    const solver = await createPlaneGcsSolver();
    try {
        const first = solveSnapshot(solver, reopen(a)), saved = reopen(first);
        assertPolygon(first, a, 'first A');
        for (const [request, fresh, label] of [[b, freshB, 'B'], [a, freshA, 'reopened A']]) {
            const result = solveSnapshot(solver, reopen(request));
            assertPolygon(result, request, label);
            sameGeometry(result.entities, fresh.entities, `${label} versus fresh`);
        }
        assert.deepEqual(first, saved, 'Later documents must not mutate a published result');
    } finally { solver.dispose(); }
});

test('solving the open baseline before adding symmetry does not change subsequent reopens', async () => {
    const request = polygonRequest(), baseline = reopen(request);
    baseline.constraints = [];
    const fresh = await freshResult(request);
    assertPolygon(fresh, request, 'fresh control');
    const solver = await createPlaneGcsSolver();
    try {
        for (let i = 0; i < 3; ++i) {
            const opened = solveSnapshot(solver, reopen(baseline));
            solved(opened, `open baseline ${i}`);
            sameGeometry(opened.entities, baseline.document.entities, `unchanged baseline ${i}`);
            const result = solveSnapshot(solver, reopen(request));
            assertPolygon(result, request, `add Symmetric ${i}`);
            sameGeometry(result.entities, fresh.entities, `add Symmetric ${i} versus fresh`);
        }
    } finally { solver.dispose(); }
});

test('native refusal cannot poison the baseline, and disposing one solver leaves another usable', async () => {
    const request = polygonRequest(), conflict = reopen(request);
    conflict.dimensions = [dimension('contradiction', 'Radius', ['a'], 7)];
    const fresh = await freshResult(request);
    assertPolygon(fresh, request, 'fresh control');
    const first = await createPlaneGcsSolver(), second = await createPlaneGcsSolver();
    try {
        const refused = solveSnapshot(first, conflict);
        assert.equal(refused.status, 'failed', JSON.stringify(refused.diagnostics));
        assert.ok(refused.diagnostics.some(message => message.includes('status 2')),
            `Must reach a native failed solve: ${JSON.stringify(refused.diagnostics)}`);
        assert.ok(refused.diagnostics.some(message => message.startsWith('Conflicting intent: Dimension')));
        assert.deepEqual(refused.entities, conflict.document.entities);
        const recovered = solveSnapshot(first, reopen(request));
        assertPolygon(recovered, request, 'recovery');
        sameGeometry(recovered.entities, fresh.entities, 'recovery versus fresh');
        assertRectangle(solveSnapshot(second, rectangleEdit()), 'independent runtime warmup');
        first.dispose();
        first.dispose();
        const disposed = solveSnapshot(first, reopen(request));
        assert.equal(disposed.status, 'failed');
        assert.deepEqual(disposed.entities, request.document.entities);
        assert.ok(disposed.diagnostics.some(message => message.includes('disposed')));
        const surviving = solveSnapshot(second, reopen(request));
        assertPolygon(surviving, request, 'surviving runtime');
        sameGeometry(surviving.entities, fresh.entities, 'surviving runtime versus fresh');
    } finally { first.dispose(); second.dispose(); }
});

test('real native systems and rank probes are deleted; per-wrapper tags start empty', async t => {
    const allocations = new Map();
    const push = GcsWrapper.prototype.push_primitives_and_params;
    // Observe only: all mapping, allocation, solves and deletion use the real WASM.
    t.mock.method(GcsWrapper.prototype, 'push_primitives_and_params', function (primitives) {
        if (!allocations.has(this)) allocations.set(this, {
            system: this.gcs, module: this.module, tags: this.sketch_index.counter(), params: this.gcs.params_size()
        });
        return push.call(this, primitives);
    });
    const solver = await createPlaneGcsSolver();
    try {
        assertRectangle(solveSnapshot(solver, rectangleEdit()), 'rank probe');
        const initial = [...allocations.values()];
        assert.equal(initial.length, 2, 'A width edit allocates a main system and a rank probe');
        assert.equal(initial[0].system.isDeleted(), false);
        assert.equal(initial[1].system.isDeleted(), true, 'Rank probe must be deleted before solve returns');
        const request = polygonRequest();
        for (let i = 0; i < 3; ++i) {
            assertPolygon(solveSnapshot(solver, reopen(request)), request, `native replay ${i}`);
            const live = [...allocations.values()].filter(({system}) => !system.isDeleted());
            assert.equal(live.length, 1, 'Previous request systems cannot survive replacement');
        }
    } finally { solver.dispose(); }
    const observed = [...allocations.values()];
    assert.equal(observed.length, 5);
    assert.equal(new Set(observed.map(value => value.system)).size, observed.length);
    assert.equal(new Set(observed.map(value => value.module)).size, 1, 'Requests intentionally share one WASM module');
    for (const {system, tags, params} of observed) {
        assert.equal(system.isDeleted(), true, 'Dispose releases the final native system');
        assert.equal(tags, 0, 'Primitive/constraint tags must not carry over');
        assert.equal(params, 0, 'Native parameter addresses must start empty');
    }
});

function hostResults(requests) {
    const host = fileURLToPath(new URL('../../src/DXFER.Web/Solver/planeGcsHost.mjs', import.meta.url));
    const module = fileURLToPath(new URL('../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js', import.meta.url));
    const child = spawnSync(process.execPath, ['--max-old-space-size=128', '--wasm-max-mem-pages=4096',
        '--experimental-default-type=module', host, module], {
        input: requests.map((request, i) => JSON.stringify({id: i + 1, request}) + '\n').join(''),
        encoding: 'utf8', timeout: 10000, maxBuffer: 1024 * 1024, env: {LANG: 'C.UTF-8'}
    });
    assert.ifError(child.error);
    assert.equal(child.status, 0, `Real sidecar failed: ${child.signal}\n${child.stderr}`);
    const frames = child.stdout.trim().split('\n').map(line => JSON.parse(line));
    assert.equal(frames.length, requests.length, 'Exactly one framed response per request');
    frames.forEach((frame, i) => assert.equal(frame.id, i + 1, 'Response identity'));
    return frames.map(frame => frame.result);
}

for (const rectangleFirst of [false, true]) {
    test(`real sidecar process replays snapshots without restart, rectangleFirst=${rectangleFirst}`, () => {
        const request = polygonRequest();
        const [fresh] = hostResults([request]);
        assertPolygon(fresh, request, 'fresh sidecar');
        const requests = Array.from({length: 4}, () => reopen(request));
        if (rectangleFirst) requests.unshift(rectangleEdit());
        const results = hostResults(requests);
        if (rectangleFirst) assertRectangle(results.shift(), 'sidecar width 15 -> 16');
        results.forEach((result, i) => {
            assertPolygon(result, request, `sidecar reopen ${i}`);
            sameGeometry(result.entities, fresh.entities, `sidecar reopen ${i} versus fresh process`);
        });
    });
}

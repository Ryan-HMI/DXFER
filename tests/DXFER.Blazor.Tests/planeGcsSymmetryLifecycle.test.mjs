import assert from 'node:assert/strict';
import {test} from 'node:test';
import {spawnSync} from 'node:child_process';
import {fileURLToPath} from 'node:url';
import {createPlaneGcsSolver} from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';
import {GcsWrapper} from '../../src/DXFER.Blazor/wwwroot/vendor/planegcs/dist/index.js';

const p = (x, y) => ({x, y});
const relation = (id, kind, referenceKeys) => ({id, kind, referenceKeys});
const near = (actual, expected) => assert.ok(Number.isFinite(actual) && Math.abs(actual - expected) < 1e-6,
    `${actual} != ${expected}`);
function circles() {
    return {document: {entities: [
        {id: 'axis', kind: 'line', points: [p(0, -10), p(0, 10)]},
        {id: 'a', kind: 'circle', center: p(-3, 2), radius: 3},
        {id: 'b', kind: 'circle', center: p(3, 2), radius: 3}
    ], constraints: [relation('mirror', 'Symmetric', ['a', 'b', 'axis'])], dimensions: []},
    fixedReferenceKeys: ['axis']};
}
function seededDrag() {
    return {...circles(), drag: {key: 'a:center', start: p(-3, 2), end: p(3, 2)}};
}
function coincidentGuesses() {
    return {...circles(), initialGuesses: ['a', 'b'].map(id => ({referenceKey: `${id}:center`, point: p(0, 2)}))};
}
function collapseRank() {
    const request = circles();
    request.document.constraints.push(relation('centers', 'Coincident', ['a:center', 'b:center']));
    request.dimensions = [{id: 'd', kind: 'Diameter', referenceKeys: ['a'], value: 8, isDriving: true}];
    request.attemptedDrivingDimensionIds = ['d'];
    return request;
}
function solve(solver, request) {
    const before = structuredClone(request);
    const result = solver.solve(request);
    assert.deepEqual(request, before, 'Native construction must not perturb the request');
    return result;
}
function solved(result) {
    assert.equal(result.engine, 'PlaneGCS');
    assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
}
function mirrored(result, radius = 3) {
    solved(result);
    const [axis, a, b] = result.entities;
    assert.deepEqual(axis, circles().document.entities[0]);
    near(a.center.x, -b.center.x); near(a.center.y, b.center.y);
    near(a.radius, radius); near(b.radius, radius);
}
async function withSolver(action) {
    const solver = await createPlaneGcsSolver();
    try { return action(solver); } finally { solver.dispose(); }
}

test('circle center drag can seed an initially separated symmetric pair to the same point', async () => {
    await withSolver(solver => {
        const request = seededDrag();
        for (let repeat = 0; repeat < 3; repeat++) {
            const result = solve(solver, request);
            mirrored(result);
            near(result.entities[1].center.x, 3); near(result.entities[2].center.x, -3);
            near(result.entities[1].center.y, 2);
        }
    });
});

test('coincident initial guesses use the actual native construction coordinates', async () => {
    await withSolver(solver => {
        const result = solve(solver, coincidentGuesses());
        mirrored(result);
        near(result.entities[1].center.x, 0); near(result.entities[1].center.y, 2);
        assert.ok(result.diagnostics.includes('PlaneGCS mapped-system degrees of freedom: 3.'));
    });
});

test('native rank reconstruction handles a pair collapsed by the successful full solve', async () => {
    await withSolver(solver => {
        const request = collapseRank();
        const withoutProbe = structuredClone(request);
        delete withoutProbe.attemptedDrivingDimensionIds;
        mirrored(solve(solver, withoutProbe), 4);
        const result = solve(solver, request);
        mirrored(result, 4);
        assert.deepEqual(result.redundantDrivingDimensionIds, []);
        assert.ok(result.diagnostics.includes('Native rank check d: DOF 1, without intent 2.'));
        const reopened = {...request, document: {...request.document, entities: result.entities}};
        mirrored(solve(solver, reopened), 4);
    });
});

test('collapsed reconstruction still recognizes an actually redundant dimension', async () => {
    await withSolver(solver => {
        const request = collapseRank();
        request.dimensions.push({id: 'r', kind: 'Radius', referenceKeys: ['b'], value: 4, isDriving: true});
        request.attemptedDrivingDimensionIds = ['r'];
        const result = solve(solver, request);
        mirrored(result, 4);
        assert.ok(result.redundantDrivingDimensionIds.includes('r'));
        assert.ok(result.diagnostics.includes('Native rank check r: DOF 1, without intent 1.'));
    });
});

test('collapsed fixed geometry refuses incompatible intent and a later request recovers', async () => {
    await withSolver(solver => {
        const request = collapseRank();
        for (const entity of request.document.entities.slice(1)) entity.center.x = 0;
        request.fixedReferenceKeys = ['axis', 'a', 'b'];
        const result = solve(solver, request);
        assert.equal(result.status, 'failed');
        assert.deepEqual(result.entities, request.document.entities);
        mirrored(solve(solver, seededDrag()));
    });
});

for (const angle of [0, Math.PI / 2, .7]) {
    test(`collapsed point symmetry can separate on a fixed axis at angle ${angle}`, async () => {
        await withSolver(solver => {
            const request = {document: {entities: [
                {id: 'axis', kind: 'line', points: [p(-10 * Math.cos(angle), -10 * Math.sin(angle)),
                    p(10 * Math.cos(angle), 10 * Math.sin(angle))]},
                ...['a', 'b'].map(id => ({id, kind: 'point', points: [p(0, 0)]}))
            ], constraints: [relation('mirror', 'Symmetric', ['a', 'b', 'axis'])], dimensions: []},
            fixedReferenceKeys: ['axis'], drag: {key: 'a:center', start: p(0, 0), end: p(-3, 4)}};
            const result = solve(solver, request);
            solved(result);
            assert.deepEqual(result.entities[0], request.document.entities[0]);
            const a = result.entities[1].points[0], b = result.entities[2].points[0];
            near(a.x, -3); near(a.y, 4);
            const projection = a.x * Math.cos(angle) + a.y * Math.sin(angle);
            near(b.x, 2 * projection * Math.cos(angle) - a.x);
            near(b.y, 2 * projection * Math.sin(angle) - a.y);
            assert.ok(result.diagnostics.includes('PlaneGCS mapped-system degrees of freedom: 2.'));
        });
    });
}

test('collapsed fallback uses relative coordinates without moving a distant pair', async () => {
    await withSolver(solver => {
        const request = {document: {entities: [
            {id: 'axis', kind: 'line', points: [p(-10, 0), p(10, 0)]},
            ...['a', 'b'].map(id => ({id, kind: 'point', points: [p(10000.13, 0)]}))
        ], constraints: [relation('mirror', 'Symmetric', ['a', 'b', 'axis'])], dimensions: []},
        fixedReferenceKeys: ['axis', 'a']};
        const result = solve(solver, request);
        solved(result);
        assert.deepEqual(result.entities, request.document.entities);
    });
});

test('collapsed reflection follows a moving axis instead of freezing its initial normal', async () => {
    await withSolver(solver => {
        const request = {document: {entities: [
            {id: 'axis', kind: 'line', points: [p(0, -10), p(0, 10)]},
            ...['a', 'b'].map(id => ({id, kind: 'point', points: [p(0, 2)]}))
        ], constraints: [relation('mirror', 'Symmetric', ['a', 'b', 'axis'])], dimensions: []},
        fixedReferenceKeys: ['axis:start', 'a'],
        drag: {key: 'axis:end', start: p(0, 10), end: p(10, 10)}};
        const result = solve(solver, request);
        solved(result);
        near(result.entities[0].points[1].x, 10); near(result.entities[0].points[1].y, 10);
        assert.deepEqual(result.entities[0].points[0], p(0, -10));
        assert.deepEqual(result.entities[1], request.document.entities[1]);
        near(result.entities[2].points[0].x, 9.6); near(result.entities[2].points[0].y, -2.8);
    });
});

test('temporary collapsed symmetry adds no auxiliary hard-system freedom', async () => {
    await withSolver(solver => {
        const request = {document: {entities: [
            {id: 'axis', kind: 'line', points: [p(0, -10), p(0, 10)]},
            ...['a', 'b'].map(id => ({id, kind: 'point', points: [p(0, 2)]}))
        ], constraints: [], dimensions: []}, fixedReferenceKeys: ['axis'],
        drag: {key: 'axis:end', start: p(0, 10), end: p(0, 10),
            temporarySnapIntent: relation('snap', 'Symmetric', ['a', 'b', 'axis'])}};
        const result = solve(solver, request);
        solved(result);
        assert.ok(result.diagnostics.includes('PlaneGCS mapped-system degrees of freedom: 4.'));
        assert.deepEqual(result.entities, request.document.entities);
        request.fixedReferenceKeys.push('a');
        request.document.constraints.push(relation('centers', 'Coincident', ['a:center', 'b:center']));
        request.drag = {...request.drag, key: 'b:center', start: p(0, 2), end: p(-3, 4)};
        const fixed = solve(solver, request);
        solved(fixed);
        assert.ok(fixed.diagnostics.includes('PlaneGCS mapped-system degrees of freedom: 0.'));
        assert.deepEqual(fixed.entities, request.document.entities);
    });
});

test('collapsed fixed conflict retains the original intent tag and geometry', async () => {
    await withSolver(solver => {
        const request = {document: {entities: [
            {id: 'axis', kind: 'line', points: [p(0, -10), p(0, 10)]},
            ...['a', 'b'].map(id => ({id, kind: 'point', points: [p(2, 2)]}))
        ], constraints: [relation('mirror', 'Symmetric', ['a', 'b', 'axis'])], dimensions: []},
        fixedReferenceKeys: ['axis', 'a', 'b']};
        const result = solve(solver, request);
        assert.equal(result.status, 'failed');
        assert.deepEqual(result.entities, request.document.entities);
        assert.ok(result.diagnostics.includes('Conflicting intent: Constraint mirror (Symmetric).'),
            JSON.stringify(result.diagnostics));
    });
});

test('ordinary noncoincident pairs still delegate to native PPL without helper growth', async t => {
    let nativePplCalls = 0;
    const push = GcsWrapper.prototype.push_constraint;
    t.mock.method(GcsWrapper.prototype, 'push_constraint', function (constraint) {
        if (constraint.type === 'p2p_symmetric_ppl') nativePplCalls++;
        return push.call(this, constraint);
    });
    await withSolver(solver => {
        const request = {document: {entities: [
            {id: 'axis', kind: 'line', points: [p(0, -10), p(0, 10)]},
            {id: 'a', kind: 'point', points: [p(-10000.13, 10000.17)]},
            {id: 'b', kind: 'point', points: [p(10000.13, 10000.17)]}
        ], constraints: [relation('mirror', 'Symmetric', ['a', 'b', 'axis'])], dimensions: []},
        fixedReferenceKeys: ['axis', 'a']};
        const result = solve(solver, request);
        solved(result);
        assert.deepEqual(result.entities, request.document.entities);
        assert.equal(nativePplCalls, 1);
    });
});

test('real sidecar recovers all three lifecycle cases without a process restart', () => {
    const requests = [seededDrag(), coincidentGuesses(), collapseRank(), seededDrag()];
    const host = fileURLToPath(new URL('../../src/DXFER.Web/Solver/planeGcsHost.mjs', import.meta.url));
    const module = fileURLToPath(new URL('../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js', import.meta.url));
    const child = spawnSync(process.execPath, ['--experimental-default-type=module',
        '--wasm-max-mem-pages=4096', '--max-old-space-size=128', host, module], {
        input: requests.map((request, i) => JSON.stringify({id: i + 1, request}) + '\n').join(''),
        encoding: 'utf8', timeout: 10000, maxBuffer: 1024 * 1024, env: {LANG: 'C.UTF-8'}
    });
    assert.ifError(child.error);
    assert.equal(child.status, 0, child.stderr);
    const frames = child.stdout.trim().split('\n').map(line => JSON.parse(line));
    assert.equal(frames.length, requests.length);
    frames.forEach((frame, i) => {
        assert.equal(frame.id, i + 1);
        mirrored(frame.result, i === 2 ? 4 : 3);
    });
});

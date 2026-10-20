import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createPlaneGcsSolver } from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';

const p = (x, y) => ({ x, y });
const line = (id, start, end) => ({ id, kind: 'line', points: [start, end] });
const point = (id, at) => ({ id, kind: 'point', points: [at] });
const circle = (id, center, radius) => ({ id, kind: 'circle', center, radius });
const constraint = (id, kind, referenceKeys) => ({ id, kind, referenceKeys, state: 'satisfied' });
const dimension = (id, kind, referenceKeys, value) => ({ id, kind, referenceKeys, value, isDriving: true });
const document = (entities, constraints = [], dimensions = []) => ({ entities, constraints, dimensions });
const near = (actual, expected, tolerance = 1e-6) => assert.ok(Number.isFinite(actual) && Math.abs(actual - expected) <= tolerance,
    `${actual} != ${expected} (tolerance ${tolerance})`);
const nearPoint = (actual, expected) => { assert.ok(actual); near(actual.x, expected.x); near(actual.y, expected.y); };
const entity = (result, id) => { const found = result.entities.find(item => item.id === id); assert.ok(found, `missing ${id}`); return found; };
const length = edge => Math.hypot(edge.points[1].x - edge.points[0].x, edge.points[1].y - edge.points[0].y);
const status = result => String(result.status).toLowerCase();
let solver;
let wasmInstances = 0;

before(async () => {
    // Observe the real runtime, without supplying a mock module or solver.
    const instantiate = WebAssembly.instantiate;
    const instantiateStreaming = WebAssembly.instantiateStreaming;
    const observe = original => async function (...args) {
        const result = await Reflect.apply(original, WebAssembly, args);
        if (result instanceof WebAssembly.Instance || result?.instance instanceof WebAssembly.Instance) wasmInstances++;
        return result;
    };
    WebAssembly.instantiate = observe(instantiate);
    if (instantiateStreaming) WebAssembly.instantiateStreaming = observe(instantiateStreaming);
    try {
        solver = await createPlaneGcsSolver();
    } finally {
        WebAssembly.instantiate = instantiate;
        if (instantiateStreaming) WebAssembly.instantiateStreaming = instantiateStreaming;
    }
});

after(async () => { if (solver) await solver.dispose(); });

async function solve(doc, updates = {}) {
    const request = { document: doc, constraints: [], dimensions: [], fixedReferenceKeys: [], initialGuesses: [], ...updates };
    const snapshot = structuredClone(request);
    const result = await solver.solve(request);
    assert.equal(result.engine, 'PlaneGCS', 'acceptance must not use a legacy or unavailable adapter');
    assert.ok(Array.isArray(result.entities));
    assert.ok(Array.isArray(result.diagnostics));
    assert.deepEqual(request, snapshot, 'solving must not mutate the caller/history snapshot');
    assert.deepEqual(result.entities.map(item => item.id).sort(), doc.entities.map(item => item.id).sort());
    return result;
}

function solved(result) {
    assert.equal(status(result), 'solved', JSON.stringify(result.diagnostics));
}

function refused(result, original) {
    assert.ok(!['solved', 'underconstrained'].includes(status(result)), `unexpected success ${result.status}`);
    assert.ok(result.diagnostics.length > 0, 'refusal needs an actionable diagnostic');
    assert.deepEqual(result.entities, original.entities, 'failed solve must not publish partially solved geometry');
}

test('initialization instantiates the actual vendored PlaneGCS WebAssembly', () => {
    assert.ok(wasmInstances > 0, 'no real WebAssembly.Instance observed while initializing PlaneGCS');
});

for (const vertical of [false, true]) for (const reverse of [false, true]) {
    test(`real engine solves ${vertical ? 'V' : 'H'} with a fixed start and driving length, order=${reverse}`, async () => {
        const constraints = [constraint('axis', vertical ? 'vertical' : 'horizontal', ['edge']),
            constraint('fixed', 'fix', ['edge:start'])];
        if (reverse) constraints.reverse();
        const doc = document([line('edge', p(0, 0), p(3, 4))], constraints,
            [dimension('length', 'linearDistance', ['edge:start', 'edge:end'], 10)]);
        const result = await solve(doc);
        solved(result);
        const edge = entity(result, 'edge');
        nearPoint(edge.points[0], p(0, 0));
        nearPoint(edge.points[1], vertical ? p(0, 10) : p(10, 0));
        near(length(edge), 10);
    });
}

for (const reverse of [false, true]) {
    test(`fixed midpoint and Equal peer permit coupled rotation, order=${reverse}`, async () => {
        const constraints = [constraint('midpoint', 'midpoint', ['edge', 'marker:center']),
            constraint('equal', 'equal', ['edge', 'peer']), constraint('fix-marker', 'fix', ['marker:center']),
            constraint('fix-peer', 'fix', ['peer'])];
        if (reverse) constraints.reverse();
        const doc = document([line('edge', p(0, 0), p(10, 0)), point('marker', p(5, 0)),
            line('peer', p(0, 10), p(10, 10))], constraints);
        const result = await solve(doc, { drag: { key: 'edge|point|end|10|0', start: p(10, 0), end: p(5, 5), constrain: false } });
        solved(result);
        nearPoint(entity(result, 'edge').points[0], p(5, -5));
        nearPoint(entity(result, 'edge').points[1], p(5, 5));
        nearPoint(entity(result, 'marker').points[0], p(5, 0));
        nearPoint(entity(result, 'peer').points[0], p(0, 10));
        nearPoint(entity(result, 'peer').points[1], p(10, 10));
        near(length(entity(result, 'edge')), 10);
    });

    test(`parallel fixed-length lines rotate together about fixed starts, order=${reverse}`, async () => {
        const constraints = [constraint('parallel', 'parallel', reverse ? ['b', 'a'] : ['a', 'b'])];
        const dimensions = [dimension('a-length', 'linearDistance', ['a:start', 'a:end'], 10),
            dimension('b-length', 'linearDistance', ['b:start', 'b:end'], 10)];
        if (reverse) dimensions.reverse();
        const doc = document([line('a', p(0, 0), p(10, 0)), line('b', p(0, 5), p(10, 5))], constraints, dimensions);
        const result = await solve(doc, { fixedReferenceKeys: ['a:start', 'b:start'],
            drag: { key: 'a|point|end|10|0', start: p(10, 0), end: p(6, 8), constrain: false } });
        solved(result);
        const a = entity(result, 'a'), b = entity(result, 'b');
        nearPoint(a.points[0], p(0, 0));
        nearPoint(b.points[0], p(0, 5));
        nearPoint(a.points[1], p(6, 8));
        nearPoint(b.points[1], p(6, 13));
        near(length(a), 10);
        near(length(b), 10);
    });

    test(`fixed tangent line leaves radius free during circle center drag, order=${reverse}`, async () => {
        const constraints = [constraint('tangent', 'tangent', reverse ? ['circle', 'edge'] : ['edge', 'circle']),
            constraint('fixed', 'fix', ['edge'])];
        if (reverse) constraints.reverse();
        const doc = document([line('edge', p(-5, 3), p(5, 3)), circle('circle', p(0, 0), 3)], constraints);
        const result = await solve(doc, { drag: { key: 'circle|point|center|0|0', start: p(0, 0), end: p(0, 1), constrain: false } });
        solved(result);
        nearPoint(entity(result, 'edge').points[0], p(-5, 3));
        nearPoint(entity(result, 'edge').points[1], p(5, 3));
        nearPoint(entity(result, 'circle').center, p(0, 1));
        near(entity(result, 'circle').radius, 2);
    });
}

test('conflicting driving update rolls back and does not poison the next solve', async () => {
    const doc = document([line('edge', p(0, 0), p(4, 0))], [constraint('fixed', 'fix', ['edge'])],
        [dimension('length', 'linearDistance', ['edge:start', 'edge:end'], 4)]);
    const rejected = await solve(doc, { dimensions: [dimension('length', 'linearDistance', ['edge:start', 'edge:end'], 10)] });
    refused(rejected, doc);
    const recovered = await solve(doc);
    solved(recovered);
    nearPoint(entity(recovered, 'edge').points[0], p(0, 0));
    nearPoint(entity(recovered, 'edge').points[1], p(4, 0));
});

test('unsupported constraint returns explicit refusal instead of a legacy fallback', async () => {
    const doc = document([line('edge', p(0, 0), p(3, 4))]);
    refused(await solve(doc, { constraints: [constraint('unsupported', 'not-a-supported-constraint', ['edge'])] }), doc);
});

test('unresolved point reference returns explicit refusal without dropping the relation', async () => {
    const doc = document([line('edge', p(0, 0), p(3, 4))],
        [constraint('missing', 'coincident', ['edge:end', 'missing:center'])]);
    refused(await solve(doc), doc);
});

test('fresh vertical line can become Horizontal without collapsing its geometry', async t => {
    const doc = document([line('edge', p(0, 0), p(0, 10))]);
    const result = await solve(doc, { constraints: [constraint('horizontal', 'horizontal', ['edge'])] });
    if (status(result) !== 'solved') refused(result, doc);
    solved(result);
    const edge = entity(result, 'edge');
    near(edge.points[0].y, edge.points[1].y);
    assert.ok(Number.isFinite(length(edge)) && length(edge) > 1e-6, 'valid line must not collapse under axis alignment');
    t.diagnostic(`Horizontal result length=${length(edge)}; original length=10`);
});

test('free welded rectangle corner reaches pointer while preserving H/V and all corner joints', async () => {
    const ids = ['bottom', 'right', 'top', 'left'];
    const corners = [p(0, 0), p(10, 0), p(10, 5), p(0, 5)];
    const entities = ids.map((id, index) => line(id, corners[index], corners[(index + 1) % 4]));
    const constraints = ids.flatMap((id, index) => [
        constraint(`axis-${index}`, index % 2 ? 'vertical' : 'horizontal', [id]),
        constraint(`joint-${index}`, 'coincident', [`${id}:end`, `${ids[(index + 1) % 4]}:start`])
    ]);
    const doc = document(entities, constraints);
    const result = await solve(doc, { drag: { key: 'right|point|end|10|5', start: p(10, 5), end: p(12, 8), constrain: false } });
    if (status(result) !== 'solved') refused(result, doc);
    solved(result);
    nearPoint(entity(result, 'right').points[1], p(12, 8));
    for (let index = 0; index < ids.length; index++) {
        const edge = entity(result, ids[index]);
        const adjacent = entity(result, ids[(index + 1) % 4]);
        nearPoint(edge.points[1], adjacent.points[0]);
        const coordinate = index % 2 ? 'x' : 'y';
        near(edge.points[0][coordinate], edge.points[1][coordinate]);
        assert.ok(length(edge) > 1e-6, `rectangle edge ${ids[index]} collapsed`);
    }
});

test('diagonal circle-center drag projects onto hard Horizontal midpoint axis', async () => {
    const doc = document([line('source', p(2, 0), p(2, 10)), circle('circle', p(8, 5), 3)],
        [constraint('horizontal', 'horizontal', ['circle:center', 'source:mid']),
            constraint('fixed-source', 'fix', ['source'])],
        [dimension('radius', 'radius', ['circle'], 3)]);
    const result = await solve(doc, { drag: { key: 'circle|point|center|8|5', start: p(8, 5), end: p(11, 9), constrain: false } });
    if (status(result) !== 'solved') refused(result, doc);
    solved(result);
    nearPoint(entity(result, 'circle').center, p(11, 5));
    near(entity(result, 'circle').radius, 3);
    nearPoint(entity(result, 'source').points[0], p(2, 0));
    nearPoint(entity(result, 'source').points[1], p(2, 10));
});

for (const vertical of [false, true]) {
    test(`existing ${vertical ? 'V' : 'H'} point relation projects drag without moving Fixed peer`, async () => {
        const moving = vertical ? p(2, 12) : p(8, 5);
        const end = vertical ? p(6, 15) : p(11, 9);
        const expected = vertical ? p(2, 15) : p(11, 5);
        const doc = document([point('peer', p(2, 5)), point('moving', moving)],
            [constraint('axis', vertical ? 'vertical' : 'horizontal', ['moving:center', 'peer:center']),
                constraint('fixed-peer', 'fix', ['peer:center'])]);
        const result = await solve(doc, { drag: { key: `moving|point|center|${moving.x}|${moving.y}`,
            start: moving, end, constrain: false } });
        if (status(result) !== 'solved') refused(result, doc);
        solved(result);
        nearPoint(entity(result, 'moving').points[0], expected);
        nearPoint(entity(result, 'peer').points[0], p(2, 5));
    });
}

for (const fixedCenter of [false, true]) for (const grip of ['quadrant-0', 'perimeter', 'radius']) {
    test(`actual circle ${grip} grip resizes R3 to R5 with center-only Fix=${fixedCenter}`, async () => {
        const doc = document([circle('circle', p(2, 3), 3)],
            fixedCenter ? [constraint('fixed-center', 'fix', ['circle:center'])] : []);
        const key = grip === 'radius' ? 'circle:radius' : `circle|point|${grip}|5|3`;
        const result = await solve(doc, { drag: { key, start: p(5.2, 3.1), end: p(5, 7), constrain: false } });
        if (status(result) !== 'solved') refused(result, doc);
        solved(result);
        nearPoint(entity(result, 'circle').center, p(2, 3));
        near(entity(result, 'circle').radius, 5);
    });
}

for (const kind of ['point', 'line-end', 'circle-center']) {
    test(`off-center ${kind} grab uses pointer delta, not absolute pointer or stale key coordinates`, async () => {
        const geometry = kind === 'point' ? point('moving', p(4, 6))
            : kind === 'line-end' ? line('moving', p(0, 0), p(4, 6)) : circle('moving', p(4, 6), 3);
        const doc = document([geometry]);
        const label = kind === 'line-end' ? 'end' : 'center';
        const result = await solve(doc, { drag: { key: `moving|point|${label}|999|999`,
            start: p(4.2, 5.9), end: p(7.2, 7.9), constrain: false } });
        if (status(result) !== 'solved') refused(result, doc);
        solved(result);
        const moved = entity(result, 'moving');
        nearPoint(kind === 'point' ? moved.points[0] : kind === 'line-end' ? moved.points[1] : moved.center, p(7, 8));
        if (kind === 'line-end') nearPoint(moved.points[0], p(0, 0));
        if (kind === 'circle-center') near(moved.radius, 3);
    });
}

for (const grip of ['center', 'body', 'quadrant-0']) {
    test(`sized tangent circle ${grip} grip orbits fixed-origin sized peer without resizing`, async () => {
        const doc = document([circle('large', p(0, 0), 10), circle('small', p(13, 0), 3)],
            [constraint('fixed-origin-center', 'fix', ['large:center']),
                constraint('tangent', 'tangent', ['small', 'large'])],
            [dimension('large-radius', 'radius', ['large'], 10), dimension('small-radius', 'radius', ['small'], 3)]);
        const start = grip === 'center' ? p(13, 0) : p(16, 0);
        const end = grip === 'center' ? p(12, 5) : p(15, 5);
        const key = grip === 'body' ? 'small' : `small|point|${grip}|${start.x}|${start.y}`;
        const result = await solve(doc, { drag: { key, start, end, constrain: false } });
        if (status(result) !== 'solved') refused(result, doc);
        solved(result);
        const small = entity(result, 'small'), large = entity(result, 'large');
        nearPoint(large.center, p(0, 0));
        near(large.radius, 10);
        near(small.radius, 3);
        if (grip === 'center') nearPoint(small.center, p(12, 5));
        // A perimeter pick specifies incidence, not a particular center on the tangent locus.
        else near(Math.hypot(end.x - small.center.x, end.y - small.center.y), 3);
        near(Math.hypot(small.center.x - large.center.x, small.center.y - large.center.y), 13);
        assert.ok(Math.hypot(small.center.x - 13, small.center.y) > 1, `${grip} gesture must not silently succeed without moving`);
    });
}

import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createPlaneGcsSolver } from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';

const solver = await createPlaneGcsSolver();
after(() => solver.dispose());
const point = (x, y) => ({ x, y });
const line = (id, start, end) => ({ id, kind: 'line', points: [start, end],
    layerName: 'COUPLED', native: { sourceId: `native-${id}` } });
const constraint = (id, kind, ...referenceKeys) => ({ id, kind, referenceKeys });
const dimension = (id, kind, value, ...referenceKeys) => ({ id, kind, value, referenceKeys, isDriving: true });
const near = (a, b) => assert.ok(Number.isFinite(a) && Math.abs(a - b) <= 1e-6, `${a} != ${b}`);
const nearPoint = (a, b) => { near(a.x, b.x); near(a.y, b.y); };
const entity = (document, id) => document.entities.find(item => item.id === id);
const vector = line => point(line.points[1].x - line.points[0].x, line.points[1].y - line.points[0].y);
const length = line => {
    const v = vector(line);
    return Math.hypot(v.x, v.y);
};
const unit = line => {
    const v = vector(line), magnitude = Math.hypot(v.x, v.y);
    assert.ok(magnitude > 1e-6, 'A coupled line must not collapse to satisfy orientation');
    return point(v.x / magnitude, v.y / magnitude);
};
const cross = (a, b) => a.x * b.y - a.y * b.x;
const dot = (a, b) => a.x * b.x + a.y * b.y;

function ordered(document, reverse) {
    if (reverse) {
        document.entities.reverse();
        document.constraints.reverse();
        document.dimensions.reverse();
    }
    return document;
}

function run(request) {
    const snapshot = structuredClone(request);
    const result = solver.solve(request);
    assert.deepEqual(request, snapshot, 'Coupled solving must not mutate its input or history');
    assert.equal(result.engine, 'PlaneGCS');
    assert.ok(['solved', 'failed'].includes(result.status), result.diagnostics.join(' '));
    if (result.status === 'failed') assert.deepEqual(result.entities, request.document.entities);
    assert.deepEqual(result.entities.map(item => item.id), request.document.entities.map(item => item.id));
    for (const actual of result.entities) {
        const original = entity(request.document, actual.id);
        assert.equal(actual.kind, original.kind);
        assert.equal(actual.layerName, original.layerName);
        assert.deepEqual(actual.native, original.native);
    }
    return result;
}

function solve(request) {
    const result = run(request);
    assert.equal(result.status, 'solved', result.diagnostics.join(' '));
    return result;
}

function refused(request) {
    const result = run(request);
    assert.equal(result.status, 'failed', result.diagnostics.join(' '));
    assert.deepEqual(result.entities, request.document.entities, 'A contradictory edit must preserve every original entity');
    return result;
}

function chain(angled, reverse) {
    return ordered({
        entities: [
            line('a', point(0, 0), point(12, 0)),
            line('b', point(0, 4), angled ? point(6, 10) : point(8, 4)),
            line('c', point(10, 4), angled ? point(15, 9) : point(10, 10))
        ],
        constraints: [
            constraint('fixed-start', 'Fix', 'a:start'),
            ...(angled ? [constraint('parallel', 'Parallel', 'b', 'c')]
                : [constraint('parallel', 'Parallel', 'a', 'b'),
                    constraint('perpendicular', 'Perpendicular', 'b', 'c')])
        ],
        dimensions: [
            dimension('length', 'LinearDistance', 12, 'a'),
            ...(angled ? [dimension('angle', 'Angle', 45, 'a', 'b')] : [])
        ]
    }, reverse);
}

function assertChain(result, angled, angleDegrees = 45, { expectedLength = 12, expectedDof = 7 } = {}) {
    const a = entity(result, 'a'), b = entity(result, 'b'), c = entity(result, 'c');
    nearPoint(a.points[0], point(0, 0));
    near(length(a), expectedLength);
    const av = unit(a), bv = unit(b), cv = unit(c);
    if (angled) {
        near(dot(av, bv), Math.cos(angleDegrees * Math.PI / 180));
        near(cross(av, bv), Math.sin(angleDegrees * Math.PI / 180));
        near(cross(bv, cv), 0);
    } else {
        near(cross(av, bv), 0);
        near(dot(bv, cv), 0);
    }
    assert.ok(result.diagnostics.includes(`PlaneGCS mapped-system degrees of freedom: ${expectedDof}.`));
}

for (const angled of [false, true]) for (const reverse of [false, true])
    for (const [label, target] of [
        ['on-length target', point(9.6, 7.2)],
        ['off-length projection', point(9, 9)],
        ['ninety-degree rotation', point(0, 12)]
    ]) {
        test(`${angled ? 'driving Angle/Parallel' : 'Parallel/Perpendicular'} chain ${label}, reverse=${reverse}`, () => {
            const document = chain(angled, reverse);
            const request = { document, drag: { key: 'a:end', start: point(12, 0), end: target, constrain: false } };
            for (let repeat = 0; repeat < 2; repeat++) {
                const result = solve(request);
                assertChain(result, angled);
                const scale = 12 / Math.hypot(target.x, target.y);
                nearPoint(entity(result, 'a').points[1], point(target.x * scale, target.y * scale));
                assert.ok(entity(result, 'a').points[1].y > 7, 'A solved no-op must not pass as free rotation');
                // B and C have no driving lengths or positions; only their orientations are contractual.
            }
        });
    }

for (const reverse of [false, true]) {
    test(`driving angle update rotates a coupled Parallel peer without fixing absolute heading, reverse=${reverse}`, () => {
        const document = chain(true, reverse);
        const result = solve({ document, dimensions: [dimension('angle', 'Angle', 60, 'a', 'b')],
            attemptedDrivingDimensionIds: ['angle'] });
        assertChain(result, true, 60);
        const before = unit(entity(document, 'b')), after = unit(entity(result, 'b'));
        assert.ok(Math.abs(cross(before, after)) > 1e-3, 'The coupled peer orientation must actually change');
        assert.deepEqual(result.redundantDrivingDimensionIds, []);
        assert.ok(result.diagnostics.includes('Native rank check angle: DOF 7, without intent 8.'));
    });
}

for (const kind of ['Midpoint', 'Midpoint2']) for (const rotated of [false, true]) for (const reverse of [false, true]) {
    test(`${kind} to Fixed point grows driving length 10 to 12, rotated=${rotated}, reverse=${reverse}`, () => {
        const center = point(2, -3), offset = rotated ? point(3, 4) : point(5, 0);
        const document = ordered({
            entities: [
                line('a', point(center.x - offset.x, center.y - offset.y), point(center.x + offset.x, center.y + offset.y)),
                { id: 'anchor', kind: 'point', points: [center], layerName: 'COUPLED', native: { sourceId: 'native-anchor' } }
            ],
            constraints: [constraint('fix', 'Fix', 'anchor'), constraint('midpoint', kind, 'anchor:center', 'a')],
            dimensions: [dimension('length', 'LinearDistance', 10, 'a')]
        }, reverse);
        const request = { document, dimensions: [dimension('length', 'LinearDistance', 12, 'a')],
            attemptedDrivingDimensionIds: ['length'] };
        for (let repeat = 0; repeat < 2; repeat++) {
            const result = solve(request), actual = entity(result, 'a');
            near(length(actual), 12);
            nearPoint(point((actual.points[0].x + actual.points[1].x) / 2,
                (actual.points[0].y + actual.points[1].y) / 2), center);
            assert.deepEqual(entity(result, 'anchor'), entity(document, 'anchor'));
            assert.ok(result.diagnostics.includes('PlaneGCS mapped-system degrees of freedom: 1.'));
            assert.ok(result.diagnostics.includes('Native rank check length: DOF 1, without intent 2.'));
            assert.deepEqual(result.redundantDrivingDimensionIds, []);
        }
    });
}

for (const angled of [false, true]) for (const reverse of [false, true]) for (const peer of ['b', 'c']) {
    test(`${angled ? 'Angle/Parallel' : 'Parallel/Perpendicular'} fixed ${peer} blocks rotation then releases, reverse=${reverse}`, () => {
        const document = chain(angled, false);
        document.constraints.push(constraint('fixed-peer', 'Fix', peer));
        ordered(document, reverse);
        const drag = { key: 'a:end', start: point(12, 0), end: point(9.6, 7.2), constrain: false };
        const blocked = run({ document, drag });
        if (blocked.status === 'solved') {
            assertChain(blocked, angled, 45, { expectedDof: 3 });
            nearPoint(entity(blocked, 'a').points[1], point(12, 0));
            assert.deepEqual(entity(blocked, peer), entity(document, peer));
        }
        // A refused edit or a solved projection is blocked, never successful rotation.
        assert.ok(Math.hypot(entity(blocked, 'a').points[1].x - drag.end.x,
            entity(blocked, 'a').points[1].y - drag.end.y) > 1);
        const released = solve({ document: { ...document, entities: blocked.entities,
            constraints: document.constraints.filter(item => item.id !== 'fixed-peer') }, drag });
        assertChain(released, angled);
        nearPoint(entity(released, 'a').points[1], drag.end);
        assert.ok(entity(released, 'a').points[1].y > 7, 'Removing Fix must restore actual rotation');
    });
}

for (const angled of [false, true]) for (const reverse of [false, true]) {
    test(`${angled ? 'Angle/Parallel' : 'Parallel/Perpendicular'} fixed line rejects length change then recovers, reverse=${reverse}`, () => {
        const document = chain(angled, false);
        document.constraints.push(constraint('fixed-line', 'Fix', 'a'));
        ordered(document, reverse);
        const update = dimension('length', 'LinearDistance', 14, 'a');
        const request = { document, dimensions: [update], attemptedDrivingDimensionIds: ['length'] };
        refused(request);
        const released = solve({ ...request, document: { ...document,
            constraints: document.constraints.filter(item => item.id !== 'fixed-line') } });
        assertChain(released, angled, 45, { expectedLength: 14 });
        assert.deepEqual(released.redundantDrivingDimensionIds, []);
        assert.ok(released.diagnostics.includes('Native rank check length: DOF 7, without intent 8.'));
        assert.ok(length(entity(released, 'a')) - length(entity(document, 'a')) > 1,
            'Recovery must apply the requested dimension, not return unchanged geometry');
    });
}

for (const reverse of [false, true]) {
    test(`fixed circle rejects radius change without mutation then recovers, reverse=${reverse}`, () => {
        const document = ordered({
            entities: [{ id: 'circle', kind: 'circle', center: point(2, -3), radius: 3, points: [],
                layerName: 'COUPLED', native: { sourceId: 'native-circle' } }],
            constraints: [constraint('fixed-center', 'Fix', 'circle:center'),
                constraint('fixed-circle', 'Fix', 'circle')],
            dimensions: [dimension('radius', 'Radius', 3, 'circle')]
        }, reverse);
        const request = { document, dimensions: [dimension('radius', 'Radius', 4, 'circle')],
            attemptedDrivingDimensionIds: ['radius'] };
        refused(request);
        const released = solve({ ...request, document: { ...document,
            constraints: document.constraints.filter(item => item.id !== 'fixed-circle') } });
        nearPoint(entity(released, 'circle').center, point(2, -3));
        near(entity(released, 'circle').radius, 4);
        assert.deepEqual(released.redundantDrivingDimensionIds, []);
        assert.ok(released.diagnostics.includes('Native rank check radius: DOF 0, without intent 1.'));
    });
}

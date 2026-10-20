import assert from 'node:assert/strict';
import { before, after, test } from 'node:test';
import { createPlaneGcsSolver } from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';

const p = (x, y) => ({ x, y });
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-6, `${a} != ${b}`);
const constraint = (id, kind, referenceKeys) => ({ id, kind, referenceKeys, state: 'Satisfied' });
let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(() => solver?.dispose());
function fixture(kind, options = {}) {
    const pattern = { id: 'array', kind, sourceEntityIds: ['seed'], rows: 2, columns: 3,
        xPitch: 8, yPitch: 6, angleDegrees: 0, count: 5, center: p(1, -2),
        stepAngleDegrees: 45, fullCircle: true, members: [], ...options };
    const seed = { id: 'seed', kind: 'circle', points: [], center: p(3, 2), radius: 1 };
    const entities = [seed], radial = kind === 'Radial';
    for (let row = 0; row < (radial ? 1 : pattern.rows); row++) {
        for (let column = 0; column < (radial ? pattern.count : pattern.columns); column++) {
            if (!row && !column) continue;
            const id = `copy-${row}-${column}`;
            const copy = { ...structuredClone(seed), id, isGenerated: true };
            copy.center = expected(pattern, seed.center, row, column);
            entities.push(copy); pattern.members.push({ sourceEntityId: 'seed', row, column, entityId: id });
        }
    }
    return { entities, constraints: [], dimensions: [], hasPatterns: true, metadata: { patterns: [pattern] } };
}
function expected(pattern, source, row, column) {
    if (pattern.kind === 'Radial') {
        const a = column * (pattern.fullCircle ? 360 / pattern.count : pattern.stepAngleDegrees) * Math.PI / 180;
        const x = source.x - pattern.center.x, y = source.y - pattern.center.y;
        return p(pattern.center.x + x * Math.cos(a) - y * Math.sin(a),
            pattern.center.y + x * Math.sin(a) + y * Math.cos(a));
    }
    const x = (column + (pattern.kind === 'Staggered' && row % 2 ? 0.5 : 0)) * pattern.xPitch;
    const y = row * pattern.yPitch, a = pattern.angleDegrees * Math.PI / 180;
    return p(source.x + x * Math.cos(a) - y * Math.sin(a),
        source.y + x * Math.sin(a) + y * Math.cos(a));
}
function solve(document, updates) {
    const request = { document, ...updates }, snapshot = structuredClone(request);
    const result = solver.solve(request);
    assert.deepEqual(request, snapshot);
    assert.equal(result.engine, 'PlaneGCS');
    return result;
}
function passed(result) { assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics)); }
function checkMembers(document, result) {
    const pattern = document.metadata.patterns[0], seed = result.entities[0];
    for (const member of pattern.members) {
        const actual = result.entities.find(e => e.id === member.entityId);
        const target = expected(pattern, seed.center, member.row, member.column);
        near(actual.center.x, target.x); near(actual.center.y, target.y); near(actual.radius, seed.radius);
    }
}
for (const kind of ['Rectangular', 'Staggered', 'Radial']) {
    test(`${kind} retained pattern follows seed movement through engine equations`, () => {
        const document = fixture(kind, { angleDegrees: 32 });
        const result = solve(document, { drag: { key: 'seed:center', start: p(3, 2), end: p(4, 3) } });
        passed(result);
        near(result.entities[0].center.x, 4); near(result.entities[0].center.y, 3);
        near(result.entities[0].radius, 1);
        checkMembers(document, result);
    });
    test(`${kind} retained pattern follows a driving seed-radius edit`, () => {
        const document = fixture(kind);
        const result = solve(document, { dimensions: [
            { id: 'radius', kind: 'Radius', referenceKeys: ['seed'], value: 2, isDriving: true }] });
        passed(result);
        near(result.entities[0].radius, 2);
        checkMembers(document, result);
    });
}
test('pattern member can reference another constraint without unlinking or freezing source freedom', () => {
    const document = fixture('Radial');
    document.entities.push({ id: 'peer', kind: 'point', points: [p(12, 4)] });
    const result = solve(document, { constraints: [
        constraint('fixed-peer', 'Fix', ['peer']),
        constraint('join', 'Coincident', ['copy-0-1:center', 'peer:center'])
    ] });
    passed(result);
    const copy = result.entities.find(e => e.id === 'copy-0-1');
    near(copy.center.x, 12); near(copy.center.y, 4);
    checkMembers(document, result);
});
test('locked generated member blocks incompatible seed movement', () => {
    const document = fixture('Rectangular');
    document.entities[1].isLocked = true;
    const result = solve(document, { dimensions: [
        { id: 'radius', kind: 'Radius', referenceKeys: ['seed'], value: 2, isDriving: true }] });
    assert.equal(result.status, 'failed');
    assert.deepEqual(result.entities, document.entities);
});
test('missing or duplicate pattern members refuse without dropping retained intent', () => {
    for (const damage of ['missing', 'duplicate']) {
        const document = fixture('Staggered');
        if (damage === 'missing') document.metadata.patterns[0].members.pop();
        else document.metadata.patterns[0].members[1] = document.metadata.patterns[0].members[0];
        const result = solve(document, {});
        assert.equal(result.status, 'unsupported');
        assert.deepEqual(result.entities, document.entities);
    }
});

for (const kind of ['line', 'polyline', 'arc']) {
    test(`radial ${kind} pattern preserves exact topology through a source edit`, () => {
        const document = fixture('Radial', { count: 4 });
        const pattern = document.metadata.patterns[0];
        const seedPoints = kind === 'line' ? [p(3, 2), p(5, 2)]
            : [p(3, 2), p(5, 2), p(5, 4), p(3, 2)];
        document.entities = document.entities.map((entity, index) => {
            if (kind === 'arc') return { ...entity, kind, startAngleDegrees: index * 90,
                endAngleDegrees: (index * 90 + 90) % 360 };
            return { id: entity.id, kind, isGenerated: index > 0,
                points: seedPoints.map(at => expected(pattern, at, 0, index)) };
        });
        const result = solve(document, kind === 'arc' ? {
            dimensions: [{ id: 'radius', kind: 'Radius', referenceKeys: ['seed'], value: 2, isDriving: true }]
        } : { drag: { key: kind === 'line' ? 'seed:end' : 'seed|segment|0:end',
            start: p(5, 2), end: p(6, 3) } });
        passed(result);
        const source = result.entities[0];
        result.entities.slice(1).forEach((entity, index) => {
            if (kind === 'arc') {
                near(entity.radius, 2);
                const target = expected(pattern, source.center, 0, index + 1);
                near(entity.center.x, target.x); near(entity.center.y, target.y);
                near((entity.startAngleDegrees - source.startAngleDegrees + 360) % 360, (index + 1) * 90);
                near((entity.endAngleDegrees - source.endAngleDegrees + 360) % 360, (index + 1) * 90);
            } else {
                assert.equal(entity.points.length, source.points.length);
                entity.points.forEach((at, vertex) => {
                    const target = expected(pattern, source.points[vertex], 0, index + 1);
                    near(at.x, target.x); near(at.y, target.y);
                });
            }
        });
    });
}

for (const partial of [false, true]) {
    test(`radial ${partial ? 'partial' : 'full'} ellipse pattern retains axes and parameters`, () => {
        const document = fixture('Radial', { count: 4 }), pattern = document.metadata.patterns[0];
        document.entities = document.entities.map((entity, index) => {
            const angle = index * Math.PI / 2;
            return { id: entity.id, kind: 'ellipse', points: [], center: entity.center,
                majorAxisEndPoint: p(2 * Math.cos(angle) - Math.sin(angle), 2 * Math.sin(angle) + Math.cos(angle)),
                minorRadiusRatio: 0.5, startAngleDegrees: partial ? 330 : 0,
                endAngleDegrees: partial ? 80 : 360, isGenerated: index > 0 };
        });
        const result = solve(document, { drag: { key: 'seed:center', start: p(3, 2), end: p(4, 3) } });
        passed(result);
        const seed = result.entities[0];
        result.entities.forEach((entity, index) => {
            const target = expected(pattern, seed.center, 0, index), angle = index * Math.PI / 2;
            near(entity.center.x, target.x); near(entity.center.y, target.y);
            near(entity.minorRadiusRatio, seed.minorRadiusRatio);
            near(entity.majorAxisEndPoint.x, seed.majorAxisEndPoint.x * Math.cos(angle) - seed.majorAxisEndPoint.y * Math.sin(angle));
            near(entity.majorAxisEndPoint.y, seed.majorAxisEndPoint.x * Math.sin(angle) + seed.majorAxisEndPoint.y * Math.cos(angle));
            near(entity.startAngleDegrees, seed.startAngleDegrees);
            near(entity.endAngleDegrees, seed.endAngleDegrees);
        });
    });
}
test('radial rational spline pattern retains exact degree, knots, weights and transformed poles', () => {
    const document = fixture('Radial', { count: 4 }), pattern = document.metadata.patterns[0];
    const controls = [p(3, 2), p(4, 3), p(5, 1), p(6, 2)];
    const knots = [0, 0, 0, 0, 1, 1, 1, 1], weights = [1, 0.8, 1.2, 1];
    document.entities = document.entities.map((entity, index) => ({
        id: entity.id, kind: 'spline', points: [], isGenerated: index > 0,
        controlPoints: controls.map(at => expected(pattern, at, 0, index)),
        splineDegree: 3, splineKnots: knots, splineWeights: weights
    }));
    const result = solve(document, { drag: { key: 'seed:control-1', start: p(4, 3), end: p(5, 4) } });
    passed(result);
    const seed = result.entities[0];
    near(seed.controlPoints[1].x, 5); near(seed.controlPoints[1].y, 4);
    result.entities.forEach((entity, index) => {
        assert.equal(entity.splineDegree, 3);
        assert.deepEqual(entity.splineKnots, knots); assert.deepEqual(entity.splineWeights, weights);
        entity.controlPoints.forEach((at, pole) => {
            const target = expected(pattern, seed.controlPoints[pole], 0, index);
            near(at.x, target.x); near(at.y, target.y);
        });
    });
});

test('radial Hermite pattern retains authoritative fit points and tangent handles', () => {
    const document = fixture('Radial', { count: 4 }), pattern = document.metadata.patterns[0];
    const fits = [p(3, 2), p(4, 3), p(6, 2)];
    document.entities = document.entities.map((entity, index) => ({
        id: entity.id, kind: 'spline', points: [], isGenerated: index > 0,
        controlPoints: fits.map(at => expected(pattern, at, 0, index)),
        fitPoints: fits.map(at => expected(pattern, at, 0, index)),
        startTangentHandle: expected(pattern, p(4, 2), 0, index),
        endTangentHandle: expected(pattern, p(5, 2), 0, index),
        splineDegree: 2, splineKnots: [0, 0, 0, 1, 1, 1], splineWeights: [1, 1, 1]
    }));
    const result = solve(document, { drag: { key: 'seed:start', start: p(3, 2), end: p(4, 1) } });
    passed(result);
    const seed = result.entities[0];
    near(seed.fitPoints[0].x, 4); near(seed.fitPoints[0].y, 1);
    result.entities.forEach((entity, index) => {
        for (const field of ['fitPoints', 'controlPoints']) {
            entity[field].forEach((at, fit) => {
                const target = expected(pattern, seed[field][fit], 0, index);
                near(at.x, target.x); near(at.y, target.y);
            });
        }
        for (const field of ['startTangentHandle', 'endTangentHandle']) {
            const target = expected(pattern, seed[field], 0, index);
            near(entity[field].x, target.x); near(entity[field].y, target.y);
        }
        assert.equal(entity.splineDegree, 2);
        assert.deepEqual(entity.splineKnots, [0, 0, 0, 1, 1, 1]);
        assert.deepEqual(entity.splineWeights, [1, 1, 1]);
    });
});

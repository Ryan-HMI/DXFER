import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createPlaneGcsSolver } from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';
import { GcsWrapper } from '../../src/DXFER.Blazor/wwwroot/vendor/planegcs/dist/index.js';

const p = (x, y) => ({ x, y });
const intent = (id, kind, ...referenceKeys) => ({ id, kind, referenceKeys, state: 'Satisfied' });
const near = (actual, expected) => assert.ok(Number.isFinite(actual) && Math.abs(actual - expected) < 1e-6, `${actual} != ${expected}`);
const nearPoint = (actual, expected) => { near(actual.x, expected.x); near(actual.y, expected.y); };
const entity = (result, id) => result.entities.find(e => e.id === id);
let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(() => solver?.dispose());

function rotate(source, center, degrees) {
    const a = degrees * Math.PI / 180, x = source.x - center.x, y = source.y - center.y;
    return p(center.x + x * Math.cos(a) - y * Math.sin(a), center.y + x * Math.sin(a) + y * Math.cos(a));
}
function fixture({ live = true, atCenter = false, count = 4, fullCircle = true, stepAngleDegrees = 37 } = {}) {
    const center = p(2, 3), source = atCenter ? structuredClone(center) : p(8, 4);
    const pattern = { id: 'radial', kind: 'Radial', sourceEntityIds: ['seed'], count,
        center, centerPointId: live ? 'center' : null, fullCircle, stepAngleDegrees, members: [] };
    const entities = [
        { id: 'seed', kind: 'circle', points: [], center: source, radius: 1.5 },
        { id: 'center', kind: 'point', points: [structuredClone(center)], isConstruction: true }
    ];
    for (let column = 1; column < count; column++) {
        const id = `member-${column}`;
        entities.push({ id, kind: 'circle', points: [], isGenerated: true, radius: 1.5,
            center: rotate(source, center, column * (fullCircle ? 360 / count : stepAngleDegrees)) });
        pattern.members.push({ sourceEntityId: 'seed', entityId: id, row: 0, column });
    }
    return { entities, constraints: [intent('fixed-source', 'Fix', 'seed')], dimensions: [],
        hasPatterns: true, metadata: { patterns: [pattern] } };
}
function run(document, updates = {}) {
    const request = { document, ...updates }, snapshot = structuredClone(request);
    const result = solver.solve(request);
    assert.deepEqual(request, snapshot, 'Requests and history must remain immutable');
    assert.equal(result.engine, 'PlaneGCS');
    return result;
}
function coupled(document, result, center = entity(result, 'center').points[0]) {
    assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
    assert.deepEqual(entity(result, 'seed'), entity(document, 'seed'), 'Fixture fixes the source, including its radius');
    assert.deepEqual(result.entities.map(e => e.id), document.entities.map(e => e.id));
    assert.equal(entity(result, 'center').isConstruction, true);
    const pattern = document.metadata.patterns[0];
    for (const member of pattern.members) {
        const actual = entity(result, member.entityId);
        nearPoint(actual.center, rotate(entity(result, 'seed').center, center,
            member.column * (pattern.fullCircle ? 360 / pattern.count : pattern.stepAngleDegrees)));
        near(actual.radius, 1.5);
        assert.equal(actual.isGenerated, true);
    }
}

for (const reverse of [false, true]) {
    test(`persistent pattern center Coincident to origin drives coupled copies, reverse=${reverse}`, () => {
        const document = fixture();
        const keys = ['center:center', '$origin:center'];
        if (reverse) keys.reverse();
        const result = run(document, { constraints: [intent('join-origin', 'Coincident', ...keys)] });
        coupled(document, result);
        nearPoint(entity(result, 'center').points[0], p(0, 0));
    });
}

test('persistent center Coincident to an ordinary fixed point moves the pattern, not the fixed source', () => {
    const document = fixture({ count: 5, fullCircle: false });
    document.entities.push({ id: 'peer', kind: 'point', points: [p(-3, 1)] });
    document.constraints.push(intent('fixed-peer', 'Fix', 'peer'));
    const result = run(document, { constraints: [intent('join-peer', 'Coincident', 'peer', 'center')] });
    coupled(document, result);
    nearPoint(entity(result, 'center').points[0], p(-3, 1));
    assert.deepEqual(entity(result, 'peer'), entity(document, 'peer'));
});

for (const axis of ['Horizontal', 'Vertical']) {
    test(`center ${axis} relation preserves orthogonal drag freedom and pattern coupling`, () => {
        const document = fixture();
        document.constraints.push(intent('axis', axis, 'center', '$origin:center'));
        const target = axis === 'Horizontal' ? p(5, 0) : p(0, 6);
        const result = run(document, { drag: { key: 'center:center', start: p(2, 3), end: target } });
        coupled(document, result);
        nearPoint(entity(result, 'center').points[0], target);
    });
}

test('horizontal center distance leaves vertical drag freedom, including reopen and edit again', () => {
    const document = fixture();
    document.dimensions.push({ id: 'center-x', kind: 'HorizontalDistance', referenceKeys: ['$origin:center', 'center:center'], value: 2, isDriving: true });
    const result = run(document, { drag: { key: 'center:center', start: p(2, 3), end: p(2, 6) } });
    coupled(document, result);
    nearPoint(entity(result, 'center').points[0], p(2, 6));
    // The C# adapter synchronizes the solved point into metadata before persistence.
    const reopened = structuredClone({ ...document, entities: result.entities });
    reopened.metadata.patterns[0].center = structuredClone(entity(result, 'center').points[0]);
    const again = run(reopened, { dimensions: [{ ...document.dimensions[0], value: 4 }] });
    coupled(reopened, again);
    near(entity(again, 'center').points[0].x, 4);
});

test('generic radial distance applies to the same live construction center', () => {
    const document = fixture();
    const result = run(document, { dimensions: [{ id: 'center-distance', kind: 'LinearDistance',
        referenceKeys: ['$origin:center', 'center'], value: 5, isDriving: true }] });
    coupled(document, result);
    const center = entity(result, 'center').points[0];
    near(Math.hypot(center.x, center.y), 5);
});

for (const atCenter of [false, true]) {
    test(`drag initialization uses live origin even with coincident source=${atCenter}`, () => {
        const document = fixture({ atCenter });
        const result = run(document, { drag: { key: 'center', start: p(2, 3), end: p(4, 5) } });
        coupled(document, result);
        nearPoint(entity(result, 'center').points[0], p(4, 5));
    });
}

test('Fixed center rejects incompatible hard intent atomically and the solver recovers', () => {
    const document = fixture();
    document.constraints.push(intent('fixed-center', 'Fix', 'center'));
    const failed = run(document, { constraints: [intent('join-origin', 'Coincident', 'center', '$origin:center')] });
    assert.equal(failed.status, 'failed');
    assert.deepEqual(failed.entities, document.entities);
    const recovery = fixture();
    const result = run(recovery, { drag: { key: 'center', start: p(2, 3), end: p(4, 5) } });
    coupled(recovery, result);
    nearPoint(entity(result, 'center').points[0], p(4, 5));
});

test('native drag seeds initialize copies around the current center point rather than stored metadata', () => {
    const document = fixture(), target = p(4, 5), insertions = [];
    const push = GcsWrapper.prototype.push_primitives_and_params;
    // Observe real native inputs without replacing any solve operation.
    GcsWrapper.prototype.push_primitives_and_params = function (primitives) {
        insertions.push(structuredClone(primitives));
        return push.call(this, primitives);
    };
    let result;
    try { result = run(document, { drag: { key: 'center', start: p(2, 3), end: target } }); }
    finally { GcsWrapper.prototype.push_primitives_and_params = push; }
    coupled(document, result);
    const nodes = new Map(insertions[0].map(node => [node.id, node]));
    const actual = insertions[0].filter(node => node.type === 'circle').map(node => nodes.get(node.c_id));
    const expected = [entity(document, 'seed').center, ...document.metadata.patterns[0].members.map(member =>
        rotate(entity(document, 'seed').center, target, member.column * 90))];
    assert.equal(actual.length, expected.length);
    for (const at of expected) {
        const index = actual.findIndex(candidate => Math.hypot(candidate.x - at.x, candidate.y - at.y) < 1e-6);
        assert.notEqual(index, -1, `Missing dynamically centered native seed ${JSON.stringify(at)}`);
        actual.splice(index, 1);
    }
});

test('locked pattern member blocks incompatible center relation without partial geometry changes', () => {
    const document = fixture();
    entity(document, 'member-1').isLocked = true;
    const result = run(document, { constraints: [intent('join-origin', 'Coincident', 'center', '$origin:center')] });
    assert.equal(result.status, 'failed');
    assert.deepEqual(result.entities, document.entities);
});

test('live center at a radial line endpoint moves without introducing a zero-length guide', () => {
    const document = fixture(), pattern = document.metadata.patterns[0];
    for (const shape of document.entities.filter(e => e.kind === 'circle')) {
        shape.kind = 'line';
        shape.points = [structuredClone(pattern.center), shape.center];
        delete shape.center;
        delete shape.radius;
    }
    document.entities.reverse();
    pattern.members.reverse();
    const result = run(document, { drag: { key: 'center', start: p(2, 3), end: p(4, 5) } });
    assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
    assert.deepEqual(entity(result, 'seed'), entity(document, 'seed'));
    nearPoint(entity(result, 'center').points[0], p(4, 5));
    for (const member of pattern.members)
        entity(result, member.entityId).points.forEach((at, i) =>
            nearPoint(at, rotate(entity(result, 'seed').points[i], p(4, 5), member.column * 90)));
});

test('legacy null centerPointId retains fixed metadata origin independently of ordinary point constraints', () => {
    const document = fixture({ live: false });
    const result = run(document, { constraints: [intent('ordinary-point', 'Coincident', 'center', '$origin:center')] });
    coupled(document, result, document.metadata.patterns[0].center);
    nearPoint(entity(result, 'center').points[0], p(0, 0));
    for (const member of document.metadata.patterns[0].members)
        assert.deepEqual(entity(result, member.entityId), entity(document, member.entityId));
});

for (const live of [false, true]) {
    test(`sixteen radial instances solve with ${live ? 'persistent affine' : 'legacy fixed'} center`, () => {
        const document = fixture({ count: 16, live });
        const result = run(document, { constraints: [intent('join-origin', 'Coincident', 'center', '$origin:center')] });
        coupled(document, result, live ? entity(result, 'center').points[0] : document.metadata.patterns[0].center);
        nearPoint(entity(result, 'center').points[0], p(0, 0));
        assert.equal(result.entities.filter(e => e.isGenerated).length, 15);
    });
}

function linearWithRetainedCenter() {
    const document = fixture(), pattern = document.metadata.patterns[0];
    Object.assign(pattern, { kind: 'Rectangular', rows: 1, columns: 4, xPitch: 3, yPitch: 2, angleDegrees: 0 });
    for (const member of pattern.members)
        entity(document, member.entityId).center = p(8 + member.column * 3, 4);
    return document;
}

test('linear pattern retains a validated dormant center binding without coupling copies to that point', () => {
    const document = linearWithRetainedCenter();
    const result = run(document, { constraints: [intent('join-origin', 'Coincident', 'center', '$origin:center')] });
    assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
    nearPoint(entity(result, 'center').points[0], p(0, 0));
    for (const shape of document.entities.filter(e => e.id !== 'center'))
        assert.deepEqual(entity(result, shape.id), shape);
});

for (const damage of ['missing', 'nonconstruction', 'stale', 'nonfinite']) {
    test(`linear retained center ${damage} is validated rather than silently ignored`, () => {
        const document = linearWithRetainedCenter();
        if (damage === 'missing') document.metadata.patterns[0].centerPointId = 'missing';
        if (damage === 'nonconstruction') entity(document, 'center').isConstruction = false;
        if (damage === 'stale') document.metadata.patterns[0].center.x += 1e-9;
        if (damage === 'nonfinite') document.metadata.patterns[0].center.x = Infinity;
        const result = run(document);
        assert.equal(result.status, 'unsupported', JSON.stringify(result.diagnostics));
        assert.deepEqual(result.entities, document.entities);
    });
}

for (const damage of ['missing', 'circle', 'member', 'not-construction', 'nonfinite', 'blank', 'point-reference', 'stale-position']) {
    test(`invalid persistent center ${damage} refuses without dropping pattern intent`, () => {
        const document = fixture(), pattern = document.metadata.patterns[0];
        if (damage === 'missing') pattern.centerPointId = 'missing';
        if (damage === 'circle') pattern.centerPointId = 'seed';
        if (damage === 'member') pattern.centerPointId = 'member-1';
        if (damage === 'not-construction') entity(document, 'center').isConstruction = false;
        if (damage === 'nonfinite') entity(document, 'center').points[0].x = NaN;
        if (damage === 'blank') pattern.centerPointId = '';
        if (damage === 'point-reference') pattern.centerPointId = 'center:center';
        if (damage === 'stale-position') entity(document, 'center').points[0].x += 1;
        const result = run(document);
        assert.equal(result.status, 'unsupported', JSON.stringify(result.diagnostics));
        assert.deepEqual(result.entities, document.entities);
    });
}

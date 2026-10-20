import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createPlaneGcsSolver } from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';
import { GcsWrapper } from '../../src/DXFER.Blazor/wwwroot/vendor/planegcs/dist/index.js';

const p = (x, y) => ({ x, y });
const near = (actual, expected) => assert.ok(Number.isFinite(actual)
    && Math.abs(actual - expected) < 1e-6, `${actual} != ${expected}`);
const nearPoint = (actual, expected) => { near(actual.x, expected.x); near(actual.y, expected.y); };
const angleNear = (actual, expected) => near(Math.atan2(
    Math.sin((actual - expected) * Math.PI / 180),
    Math.cos((actual - expected) * Math.PI / 180)), 0);
const intent = (id, kind, referenceKeys) => ({ id, kind, referenceKeys, state: 'Satisfied' });
let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(() => solver?.dispose());

function freeze(value) {
    if (value && typeof value === 'object' && !Object.isFrozen(value)) {
        Object.values(value).forEach(freeze);
        Object.freeze(value);
    }
    return value;
}

function source(kind, id = 'seed') {
    if (kind === 'line') return { id, kind, points: [p(3, 2), p(5, 2)] };
    const entity = { id, kind, points: [], center: p(3, 2) };
    if (kind === 'ellipse') return { ...entity, majorAxisEndPoint: p(2, 1),
        minorRadiusRatio: 0.5, startAngleDegrees: 0, endAngleDegrees: 360 };
    return { ...entity, radius: 1, ...(kind === 'arc'
        ? { startAngleDegrees: 10, endAngleDegrees: 110 } : {}) };
}

function angle(pattern, column) {
    return pattern.kind === 'Radial'
        ? column * (pattern.fullCircle ? 360 / pattern.count : pattern.stepAngleDegrees) : 0;
}

function rotate(point, degrees) {
    const a = degrees * Math.PI / 180;
    return p(point.x * Math.cos(a) - point.y * Math.sin(a),
        point.x * Math.sin(a) + point.y * Math.cos(a));
}

function transformed(pattern, point, row, column) {
    if (pattern.kind === 'Radial') {
        const rotated = rotate(p(point.x - pattern.center.x, point.y - pattern.center.y), angle(pattern, column));
        return p(rotated.x + pattern.center.x, rotated.y + pattern.center.y);
    }
    const shift = rotate(p((column + (pattern.kind === 'Staggered' && row % 2 ? 0.5 : 0)) * pattern.xPitch,
        row * pattern.yPitch), pattern.angleDegrees);
    return p(point.x + shift.x, point.y + shift.y);
}

function fixture(kind, seeds = [source('circle')], options = {}) {
    const pattern = { id: `pattern-${seeds[0].id}`, kind, sourceEntityIds: seeds.map(e => e.id),
        rows: 2, columns: 2, xPitch: 8, yPitch: 6, angleDegrees: 27,
        count: 4, center: p(1, -2), stepAngleDegrees: 45, fullCircle: true, members: [], ...options };
    const entities = structuredClone(seeds);
    for (let row = 0; row < (kind === 'Radial' ? 1 : pattern.rows); row++) {
        for (let column = 0; column < (kind === 'Radial' ? pattern.count : pattern.columns); column++) {
            if (!row && !column) continue;
            for (const seed of seeds) {
                const entity = { ...structuredClone(seed), id: `${seed.id}-${row}-${column}`, isGenerated: true };
                entity.points = seed.points.map(point => transformed(pattern, point, row, column));
                if (seed.center) entity.center = transformed(pattern, seed.center, row, column);
                if (seed.kind === 'arc') {
                    entity.startAngleDegrees = (seed.startAngleDegrees + angle(pattern, column)) % 360;
                    entity.endAngleDegrees = (seed.endAngleDegrees + angle(pattern, column)) % 360;
                }
                if (seed.kind === 'ellipse') entity.majorAxisEndPoint = rotate(seed.majorAxisEndPoint, angle(pattern, column));
                entities.push(entity);
                pattern.members.push({ sourceEntityId: seed.id, entityId: entity.id, row, column });
            }
        }
    }
    return { entities, constraints: [], dimensions: [], hasPatterns: true, metadata: { patterns: [pattern] } };
}

function drag(seed, end = null) {
    const start = seed.kind === 'line' ? seed.points[1] : seed.center;
    return { key: `${seed.id}:${seed.kind === 'line' ? 'end' : 'center'}`,
        start: structuredClone(start), end: end ?? p(start.x + 1, start.y + 1) };
}

function run(document, updates = {}) {
    const request = freeze({ document, ...updates }), snapshot = structuredClone(request);
    const insertions = [];
    const push = GcsWrapper.prototype.push_primitives_and_params;
    // Observe real native inputs without replacing any solver operation.
    GcsWrapper.prototype.push_primitives_and_params = function (primitives) {
        insertions.push(structuredClone(primitives));
        return push.call(this, primitives);
    };
    let result;
    try { result = solver.solve(request); }
    finally { GcsWrapper.prototype.push_primitives_and_params = push; }
    assert.deepEqual(request, snapshot, 'The complete request must remain immutable');
    assert.equal(result.engine, 'PlaneGCS');
    assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
    assert.deepEqual(result.entities.map(e => e.id), document.entities.map(e => e.id));
    return { result, insertions };
}

function entity(result, id) {
    const found = result.entities.find(e => e.id === id);
    assert.ok(found, `Missing entity ${id}`);
    return found;
}

function checkMembers(document, result) {
    for (const pattern of document.metadata.patterns) {
        for (const member of pattern.members) {
            const seed = entity(result, member.sourceEntityId), actual = entity(result, member.entityId);
            assert.equal(actual.kind, seed.kind);
            assert.equal(actual.isGenerated, true);
            assert.equal(actual.points.length, seed.points.length);
            actual.points.forEach((point, i) => nearPoint(point, transformed(pattern, seed.points[i], member.row, member.column)));
            if (seed.center) nearPoint(actual.center, transformed(pattern, seed.center, member.row, member.column));
            if (seed.radius !== undefined) near(actual.radius, seed.radius);
            if (seed.kind === 'arc') {
                angleNear(actual.startAngleDegrees, seed.startAngleDegrees + angle(pattern, member.column));
                angleNear(actual.endAngleDegrees, seed.endAngleDegrees + angle(pattern, member.column));
            }
            if (seed.kind === 'ellipse') {
                nearPoint(actual.majorAxisEndPoint, rotate(seed.majorAxisEndPoint, angle(pattern, member.column)));
                near(actual.minorRadiusRatio, seed.minorRadiusRatio);
                angleNear(actual.startAngleDegrees, seed.startAngleDegrees);
                angleNear(actual.endAngleDegrees, seed.endAngleDegrees);
            }
        }
    }
}

function checkNativeCircleGuesses(insertions, expected) {
    const primitives = insertions[0], nodes = new Map(primitives.map(node => [node.id, node]));
    const actual = primitives.filter(node => node.type === 'circle').map(node => nodes.get(node.c_id));
    assert.equal(actual.length, expected.length);
    const remaining = [...actual];
    for (const point of expected) {
        const index = remaining.findIndex(at => Math.hypot(at.x - point.x, at.y - point.y) < 1e-6);
        assert.notEqual(index, -1, `Native circle guess missing (${point.x}, ${point.y})`);
        remaining.splice(index, 1);
    }
}

function checkCircleDragGoals(insertions, target, radius = 1) {
    const goals = insertions.flat().filter(node => node.temporary);
    assert.equal(goals.length, 3);
    for (const prop of ['x', 'y']) {
        const goal = goals.find(node => node.type === `coordinate_${prop}`);
        assert.ok(goal);
        near(goal[prop], target[prop]);
        assert.equal(goal.scale, 1);
    }
    const stability = goals.find(node => node.type === 'equal');
    assert.equal(stability.param1.prop, 'radius');
    assert.equal(stability.param2, radius);
    assert.equal(stability.scale, 1e-4);
}

for (const kind of ['Radial', 'Rectangular', 'Staggered']) {
    for (const shape of ['line', 'circle', 'arc', 'ellipse']) {
        test(`guarded ${kind} ${shape} seeds preserve native pattern relations and soft drag intent`, () => {
            const seed = source(shape), document = fixture(kind, [seed]), gesture = drag(seed);
            if (shape === 'arc') {
                // Arc shape preferences are soft; specify the shape this fixture must retain.
                document.dimensions.push(
                    { id: 'arc-radius', kind: 'Radius', referenceKeys: [seed.id], value: seed.radius, isDriving: true },
                    { id: 'arc-sweep', kind: 'Angle', referenceKeys: [seed.id], value: 100, isDriving: true });
            }
            const { result, insertions } = run(document, { drag: gesture });
            const actual = entity(result, seed.id);
            nearPoint(shape === 'line' ? actual.points[1] : actual.center, gesture.end);
            if (shape === 'circle' || shape === 'arc') near(actual.radius, seed.radius);
            if (shape === 'arc') angleNear(actual.endAngleDegrees - actual.startAngleDegrees, 100);
            checkMembers(document, result);
            if (shape === 'circle') {
                const pattern = document.metadata.patterns[0];
                checkNativeCircleGuesses(insertions, [gesture.end, ...pattern.members.map(member =>
                    transformed(pattern, gesture.end, member.row, member.column))]);
                const preferences = insertions.flat().filter(node => node.temporary);
                assert.equal(preferences.filter(node => ['coordinate_x', 'coordinate_y'].includes(node.type)).length, 2);
                assert.ok(preferences.some(node => node.type === 'equal' && node.scale === 1e-4),
                    'Radius stability preference must not be dropped');
                assert.ok(result.diagnostics.includes('PlaneGCS mapped-system degrees of freedom: 3.'));
            }
        });
    }

    for (const lock of ['Fix', 'isLocked', 'fixedReferenceKeys']) {
        test(`${kind} seed rollback preserves a later ${lock} member and all earlier guesses`, () => {
            const document = fixture(kind), pattern = document.metadata.patterns[0];
            const memberId = pattern.members.at(-1).entityId;
            const member = document.entities.find(e => e.id === memberId);
            const lockedCenter = structuredClone(member.center);
            const updates = { drag: drag(document.entities[0]) };
            if (lock === 'Fix') document.constraints.push(intent('fixed-member', 'Fix', [memberId]));
            else if (lock === 'isLocked') member.isLocked = true;
            else updates.fixedReferenceKeys = [`${memberId}:center`];
            const { result, insertions } = run(document, updates);
            nearPoint(entity(result, memberId).center, lockedCenter);
            nearPoint(entity(result, 'seed').center, document.entities[0].center);
            assert.ok(Math.hypot(entity(result, 'seed').center.x - updates.drag.end.x,
                entity(result, 'seed').center.y - updates.drag.end.y) > 1);
            checkMembers(document, result);
            checkNativeCircleGuesses(insertions, document.entities.map(e => e.center));
            checkCircleDragGoals(insertions, updates.drag.end);
        });
    }

    test(`${kind} full hard-residual guard rejects an incompatible driving distance without fixing the pointer`, () => {
        const document = fixture(kind), originalDistance = Math.hypot(3, 2);
        document.dimensions.push({ id: 'source-distance', kind: 'LinearDistance',
            referenceKeys: ['$origin:center', 'seed:center'], value: originalDistance, isDriving: true });
        const gesture = drag(document.entities[0]), { result, insertions } = run(document, { drag: gesture });
        const center = entity(result, 'seed').center;
        near(Math.hypot(center.x, center.y), originalDistance);
        assert.ok(Math.hypot(center.x - gesture.end.x, center.y - gesture.end.y) > 0.1);
        checkMembers(document, result);
        checkNativeCircleGuesses(insertions, document.entities.map(e => e.center));
        checkCircleDragGoals(insertions, gesture.end);
        const scale = originalDistance / Math.hypot(gesture.end.x, gesture.end.y);
        nearPoint(center, p(gesture.end.x * scale, gesture.end.y * scale));
    });
}

for (const direction of [1, -1]) {
    test(`invalid pre-drag driving distance retains post-drag guesses and native projection direction=${direction}`, () => {
        const document = fixture('Radial'), requiredDistance = Math.hypot(3, 2) + 0.5;
        document.dimensions.push({ id: 'new-source-distance', kind: 'LinearDistance',
            referenceKeys: ['$origin:center', 'seed:center'], value: requiredDistance, isDriving: true });
        const gesture = drag(document.entities[0], p(3 + direction, 2 + direction));
        const { result, insertions } = run(document, { drag: gesture });
        checkNativeCircleGuesses(insertions, document.entities.map(e => e.id === 'seed' ? gesture.end : e.center));
        checkCircleDragGoals(insertions, gesture.end);
        const center = entity(result, 'seed').center;
        near(Math.hypot(center.x, center.y), requiredDistance);
        const scale = requiredDistance / Math.hypot(gesture.end.x, gesture.end.y);
        nearPoint(center, p(gesture.end.x * scale, gesture.end.y * scale));
        near(entity(result, 'seed').radius, 1);
        checkMembers(document, result);
    });
}

test('zero-origin radial circle seeds retain the native affine auxiliary equations', () => {
    const seed = { ...source('circle'), center: p(0, 0) };
    const document = fixture('Radial', [seed], { center: p(0, 0) });
    const { result } = run(document, { drag: drag(seed, p(1, 1)) });
    nearPoint(entity(result, 'seed').center, p(1, 1));
    checkMembers(document, result);
});

test('radial line with a source endpoint at the origin keeps topology through auxiliary fallback', () => {
    const seed = { id: 'seed', kind: 'line', points: [p(0, 0), p(2, 1)] };
    const document = fixture('Radial', [seed], { center: p(0, 0) });
    const { result } = run(document, { drag: { key: 'seed:start', start: p(0, 0), end: p(1, 0.5) } });
    nearPoint(entity(result, 'seed').points[0], p(1, 0.5));
    checkMembers(document, result);
});

test('partial radial ellipse seeds preserve stored parameter frame and sweep', () => {
    const seed = { ...source('ellipse'), startAngleDegrees: 330, endAngleDegrees: 80 };
    const document = fixture('Radial', [seed]);
    const { result } = run(document, { drag: drag(seed) });
    checkMembers(document, result);
    near(entity(result, 'seed').startAngleDegrees, 330);
    near(entity(result, 'seed').endAngleDegrees, 80);
});

for (const kind of ['Radial', 'Rectangular', 'Staggered']) {
    test(`${kind} multi-source sets propagate each source without moving an unrelated source set`, () => {
        const first = source('circle'), second = source('line', 'other');
        second.points = [p(8, -1), p(10, 0)];
        const document = fixture(kind, [first, second]);
        const { result } = run(document, { drag: drag(first) });
        nearPoint(entity(result, first.id).center, p(4, 3));
        entity(result, second.id).points.forEach((point, i) => nearPoint(point, second.points[i]));
        checkMembers(document, result);
    });
}

test('separate radial and staggered patterns retain independent source sets', () => {
    const first = fixture('Radial'), second = fixture('Staggered', [source('circle', 'other')]);
    const document = { ...first, entities: [...second.entities, ...first.entities],
        metadata: { patterns: [...second.metadata.patterns, ...first.metadata.patterns] } };
    const { result } = run(document, { drag: drag(first.entities[0]) });
    nearPoint(entity(result, 'seed').center, p(4, 3));
    for (const original of second.entities) nearPoint(entity(result, original.id).center, original.center);
    checkMembers(document, result);
});

for (const reverseEntities of [false, true]) {
    test(`reversed pattern member order is independent of entity order reverseEntities=${reverseEntities}`, () => {
        const document = fixture('Radial'), seed = document.entities[0];
        document.metadata.patterns[0].members.reverse();
        if (reverseEntities) document.entities.reverse();
        const { result, insertions } = run(document, { drag: drag(seed) });
        checkMembers(document, result);
        const pattern = document.metadata.patterns[0];
        checkNativeCircleGuesses(insertions, [p(4, 3), ...pattern.members.map(member =>
            transformed(pattern, p(4, 3), member.row, member.column))]);
    });
}

for (const locked of [false, true]) {
    test(`pattern member concentric Offset triggers whole-seed rollback and native fallback locked=${locked}`, () => {
        const document = fixture('Radial'), pattern = document.metadata.patterns[0];
        const memberId = pattern.members.at(-1).entityId;
        const member = document.entities.find(e => e.id === memberId);
        const outer = { id: 'outer', kind: 'circle', points: [],
            center: structuredClone(member.center), radius: 2, isLocked: locked };
        document.entities.push(outer);
        document.constraints.push(intent('retained-offset', 'Offset', [memberId, 'outer']));
        document.dimensions.push({ id: 'gap', kind: 'Offset', referenceKeys: [memberId, 'outer'], value: 1, isDriving: true });
        const gesture = drag(document.entities[0]), { result, insertions } = run(document, { drag: gesture });
        checkNativeCircleGuesses(insertions, document.entities.map(e => e.center));
        const derived = entity(result, 'outer'), actualMember = entity(result, memberId);
        nearPoint(derived.center, actualMember.center);
        near(derived.radius - actualMember.radius, 1);
        nearPoint(entity(result, 'seed').center, locked ? document.entities[0].center : gesture.end);
        if (locked) {
            nearPoint(derived.center, outer.center);
            near(derived.radius, outer.radius);
        }
        checkMembers(document, result);
    });

    test(`pattern member line Offset retains signed gap and endpoint projection locked=${locked}`, () => {
        const seed = source('line'), document = fixture('Radial', [seed]);
        const memberId = document.metadata.patterns[0].members.at(-1).entityId;
        const member = document.entities.find(e => e.id === memberId);
        const [a, b] = member.points, length = Math.hypot(b.x - a.x, b.y - a.y);
        const normal = p(-(b.y - a.y) / length, (b.x - a.x) / length);
        const outer = { id: 'outer', kind: 'line', isLocked: locked,
            points: member.points.map(at => p(at.x + normal.x, at.y + normal.y)) };
        document.entities.push(outer);
        document.constraints.push(intent('line-offset', 'Offset', [memberId, 'outer']));
        document.dimensions.push({ id: 'line-gap', kind: 'Offset',
            referenceKeys: [memberId, 'outer'], value: 1, isDriving: true });
        const gesture = drag(seed), { result } = run(document, { drag: gesture });
        const actual = entity(result, memberId), derived = entity(result, 'outer');
        const [start, end] = actual.points, dx = end.x - start.x, dy = end.y - start.y;
        const actualLength = Math.hypot(dx, dy);
        for (let i = 0; i < 2; i++) {
            const gap = p(derived.points[i].x - actual.points[i].x, derived.points[i].y - actual.points[i].y);
            near((dx * gap.y - dy * gap.x) / actualLength, 1);
            near((dx * gap.x + dy * gap.y) / actualLength, 0);
        }
        nearPoint(entity(result, seed.id).points[1], locked ? seed.points[1] : gesture.end);
        if (locked) derived.points.forEach((point, i) => nearPoint(point, outer.points[i]));
        checkMembers(document, result);
    });
}

test('successful seed then rejected seed then identical request does not retain native state', () => {
    const document = fixture('Radial'), gesture = drag(document.entities[0]);
    const first = run(document, { drag: gesture }).result;
    const blocked = fixture('Radial');
    blocked.entities.at(-1).isLocked = true;
    const denied = run(blocked, { drag: drag(blocked.entities[0]) }).result;
    nearPoint(entity(denied, 'seed').center, blocked.entities[0].center);
    const repeated = run(document, { drag: gesture }).result;
    assert.deepEqual(repeated, first);
    checkMembers(document, repeated);
});

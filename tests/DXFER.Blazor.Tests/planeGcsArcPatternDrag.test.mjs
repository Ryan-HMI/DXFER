import assert from 'node:assert/strict';
import { after, before, test } from 'node:test';
import { createPlaneGcsSolver } from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';
import { GcsWrapper } from '../../src/DXFER.Blazor/wwwroot/vendor/planegcs/dist/index.js';

const radians = Math.PI / 180;
const point = (x, y) => ({ x, y });
const near = (actual, expected) => assert.ok(Number.isFinite(actual)
    && Math.abs(actual - expected) < 1e-6, `${actual} != ${expected}`);
const nearPoint = (actual, expected) => { near(actual.x, expected.x); near(actual.y, expected.y); };
const nearAngle = (actual, expected) => near(Math.atan2(
    Math.sin((actual - expected) * radians), Math.cos((actual - expected) * radians)), 0);
const at = (arc, angle, radius = arc.radius) => point(
    arc.center.x + radius * Math.cos(angle * radians), arc.center.y + radius * Math.sin(angle * radians));
let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(() => solver?.dispose());

function transformed(pattern, position, row, column) {
    if (pattern.kind === 'Radial') {
        const angle = column * 360 / pattern.count * radians;
        const x = position.x - pattern.center.x, y = position.y - pattern.center.y;
        return point(pattern.center.x + x * Math.cos(angle) - y * Math.sin(angle),
            pattern.center.y + x * Math.sin(angle) + y * Math.cos(angle));
    }
    const x = (column + (pattern.kind === 'Staggered' && row % 2 ? 0.5 : 0)) * pattern.xPitch;
    const y = row * pattern.yPitch, angle = pattern.angleDegrees * radians;
    return point(position.x + x * Math.cos(angle) - y * Math.sin(angle),
        position.y + x * Math.sin(angle) + y * Math.cos(angle));
}

function fixture(kind, count) {
    const seed = { id: 'seed', kind: 'arc', points: [], center: point(3, 2),
        radius: 1, startAngleDegrees: 10, endAngleDegrees: 110 };
    const pattern = { id: 'array', kind, sourceEntityIds: [seed.id],
        rows: count === 2 ? 1 : 2, columns: 2, xPitch: 8, yPitch: 6, angleDegrees: 27,
        count, center: point(1, -2), stepAngleDegrees: 360 / count, fullCircle: true, members: [] };
    const entities = [seed];
    for (let row = 0; row < (kind === 'Radial' ? 1 : pattern.rows); row++) {
        for (let column = 0; column < (kind === 'Radial' ? count : pattern.columns); column++) {
            if (!row && !column) continue;
            const id = `copy-${row}-${column}`, angle = kind === 'Radial' ? column * 360 / count : 0;
            entities.push({ ...structuredClone(seed), id, isGenerated: true,
                center: transformed(pattern, seed.center, row, column),
                startAngleDegrees: (10 + angle) % 360, endAngleDegrees: (110 + angle) % 360 });
            pattern.members.push({ sourceEntityId: seed.id, entityId: id, row, column });
        }
    }
    return { entities, constraints: [], dimensions: [], hasPatterns: true, metadata: { patterns: [pattern] } };
}

function gesture(document, grip = 'center') {
    const seed = document.entities.find(entity => entity.id === 'seed');
    if (grip === 'radius') return { key: 'seed:radius', start: at(seed, 60), end: at(seed, 60, 1.25) };
    if (grip === 'end') return { key: 'seed:end', start: at(seed, 110), end: at(seed, 120, 1.2) };
    const delta = document.entities.length === 2 ? point(1, 1) : point(0.8, 0.5);
    return { key: 'seed:center', start: structuredClone(seed.center),
        end: point(seed.center.x + delta.x, seed.center.y + delta.y) };
}

function freeze(value) {
    if (value && typeof value === 'object') { Object.values(value).forEach(freeze); Object.freeze(value); }
    return value;
}

function run(document, drag = gesture(document)) {
    const request = freeze({ document, drag }), snapshot = structuredClone(request), insertions = [];
    const push = GcsWrapper.prototype.push_primitives_and_params;
    // Observe native inputs while retaining the real WASM solve and validation.
    GcsWrapper.prototype.push_primitives_and_params = function(primitives) {
        insertions.push(structuredClone(primitives));
        return push.call(this, primitives);
    };
    let result;
    try { result = solver.solve(request); }
    finally { GcsWrapper.prototype.push_primitives_and_params = push; }
    assert.deepEqual(request, snapshot);
    assert.equal(result.engine, 'PlaneGCS');
    assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
    assert.deepEqual(result.entities.map(entity => entity.id), document.entities.map(entity => entity.id));
    return { result, insertions };
}

function source(result) { return result.entities.find(entity => entity.id === 'seed'); }

function checkPattern(document, result) {
    const seed = source(result), pattern = document.metadata.patterns[0];
    for (const member of pattern.members) {
        const arc = result.entities.find(entity => entity.id === member.entityId);
        const rotation = pattern.kind === 'Radial' ? member.column * 360 / pattern.count : 0;
        nearPoint(arc.center, transformed(pattern, seed.center, member.row, member.column));
        near(arc.radius, seed.radius);
        nearAngle(arc.startAngleDegrees, seed.startAngleDegrees + rotation);
        nearAngle(arc.endAngleDegrees, seed.endAngleDegrees + rotation);
        assert.equal(arc.isGenerated, true);
    }
}

function checkArcInputs(primitives, expected) {
    const nodes = new Map(primitives.map(node => [node.id, node]));
    const arcs = primitives.filter(node => node.type === 'arc');
    assert.equal(arcs.length, expected.length);
    for (const entity of expected) {
        const index = arcs.findIndex(arc => {
            const center = nodes.get(arc.c_id);
            return Math.hypot(center.x - entity.center.x, center.y - entity.center.y) < 1e-6;
        });
        assert.notEqual(index, -1, `Missing initial native arc ${entity.id}`);
        const [arc] = arcs.splice(index, 1);
        near(arc.radius, entity.radius);
        nearAngle(arc.start_angle / radians, entity.startAngleDegrees);
        nearAngle(arc.end_angle / radians, entity.endAngleDegrees);
        nearPoint(nodes.get(arc.start_id), at(entity, entity.startAngleDegrees));
        nearPoint(nodes.get(arc.end_id), at(entity, entity.endAngleDegrees));
    }
}

function lockLast(document, guard) {
    const member = document.entities.at(-1);
    if (guard === 'Fix') document.constraints.push({
        id: 'fixed-member', kind: 'Fix', referenceKeys: [member.id], state: 'Satisfied' });
    else member.isLocked = true;
}

function checkOriginalGeometry(document, result) {
    for (const original of document.entities) {
        const actual = result.entities.find(entity => entity.id === original.id);
        nearPoint(actual.center, original.center);
        near(actual.radius, original.radius);
        nearAngle(actual.startAngleDegrees, original.startAngleDegrees);
        nearAngle(actual.endAngleDegrees, original.endAngleDegrees);
    }
}

for (const kind of ['Radial', 'Rectangular', 'Staggered']) {
    for (const count of [2, 4]) {
        test(`${kind} ${count} unconstrained arcs translate without radius or sweep distortion`, () => {
            const document = fixture(kind, count), drag = gesture(document);
            const { result, insertions } = run(document, drag), actual = source(result);
            assert.deepEqual(document.dimensions, []);
            assert.deepEqual(document.constraints, []);
            nearPoint(actual.center, drag.end);
            near(actual.radius, 1);
            nearAngle(actual.startAngleDegrees, 10);
            nearAngle(actual.endAngleDegrees, 110);
            checkPattern(document, result);
            checkArcInputs(insertions[0], result.entities);
            assert.ok(result.diagnostics.includes('PlaneGCS mapped-system degrees of freedom: 5.'));
            const goals = insertions.flat().filter(node => node.temporary);
            assert.equal(goals.length, 5);
            assert.equal(goals.filter(node => ['coordinate_x', 'coordinate_y'].includes(node.type)
                && node.scale === 1).length, 2);
            assert.equal(goals.filter(node => node.type === 'equal' && node.scale === 1e-4).length, 3);
            assert.equal(insertions[0].filter(node => !['point', 'line', 'arc'].includes(node.type)).length,
                count + 5 * (count - 1), 'No new hard shape or pointer equations');
        });
    }

    test(`${kind} unconstrained arc translation is independent of entity and member order`, () => {
        const original = fixture(kind, 4), reversed = structuredClone(original);
        reversed.entities.reverse();
        reversed.metadata.patterns[0].members.reverse();
        const first = run(original).result, second = run(reversed).result;
        for (const expected of first.entities) {
            const actual = second.entities.find(entity => entity.id === expected.id);
            nearPoint(actual.center, expected.center);
            near(actual.radius, expected.radius);
            nearAngle(actual.startAngleDegrees, expected.startAngleDegrees);
            nearAngle(actual.endAngleDegrees, expected.endAngleDegrees);
        }
        checkPattern(reversed, second);
    });
}

for (const guard of ['Fix', 'isLocked']) {
    test(`radial arc center seed rolls back for a later ${guard} member`, () => {
        const document = fixture('Radial', 4);
        lockLast(document, guard);
        const drag = gesture(document), { result, insertions } = run(document, drag);
        checkOriginalGeometry(document, result);
        checkPattern(document, result);
        checkArcInputs(insertions[0], document.entities);
    });
}

for (const grip of ['radius', 'end']) {
    test(`radial arc ${grip} drag propagates complete feasible arc parameters`, () => {
        const document = fixture('Radial', 4), drag = gesture(document, grip);
        const { result, insertions } = run(document, drag), actual = source(result);
        nearPoint(actual.center, point(3, 2));
        near(actual.radius, grip === 'radius' ? 1.25 : 1.2);
        nearAngle(actual.startAngleDegrees, 10);
        nearAngle(actual.endAngleDegrees, grip === 'radius' ? 110 : 120);
        if (grip === 'end') nearPoint(at(actual, actual.endAngleDegrees), drag.end);
        checkPattern(document, result);
        checkArcInputs(insertions[0], result.entities);
    });

    for (const guard of ['Fix', 'isLocked']) {
        test(`radial arc ${grip} seed restores earlier scalars and endpoints before later ${guard} fallback`, () => {
            const document = fixture('Radial', 4);
            lockLast(document, guard);
            const drag = gesture(document, grip), { result, insertions } = run(document, drag);
            checkOriginalGeometry(document, result);
            checkPattern(document, result);
            const expected = structuredClone(document.entities);
            if (grip === 'radius') expected[0].radius = 1.25;
            checkArcInputs(insertions[0], expected);
        });
    }
}

for (const grip of ['radius', 'end']) {
    test(`arc ${grip} fallback preserves soft priority and ${grip === 'radius' ? 'added-geometry rollback' : 'valid pre-drag geometry'}`, () => {
        const document = fixture('Radial', 4);
        lockLast(document, 'isLocked');
        const drag = gesture(document, grip), { result, insertions } = run(document, drag);
        checkOriginalGeometry(document, result);
        checkPattern(document, result);
        const primitives = insertions[0], nodes = new Map(primitives.map(node => [node.id, node]));
        const points = primitives.filter(node => node.type === 'point');
        assert.equal(points.length, 3 * document.entities.length + 1 + (grip === 'radius' ? 1 : 0));
        const sourceArc = primitives.find(node => node.type === 'arc');
        const goals = insertions.flat().filter(node => node.temporary);
        const stability = goals.filter(node => node.type === 'equal');
        assert.equal(stability.length, grip === 'radius' ? 2 : 1);
        for (const goal of stability) {
            assert.equal(goal.scale, 1e-4);
            assert.equal(goal.param1.o_id, sourceArc.id);
            assert.ok(['start_angle', 'end_angle'].includes(goal.param1.prop));
            near(goal.param2, (goal.param1.prop === 'start_angle' ? 10 : 110) * radians);
        }
        const centerGoals = goals.filter(node => node.p_id === sourceArc.c_id);
        assert.equal(centerGoals.length, 2);
        for (const prop of ['x', 'y']) {
            const goal = centerGoals.find(node => node.type === `coordinate_${prop}`);
            assert.equal(goal.scale, 1e-4);
            near(goal[prop], document.entities[0].center[prop]);
        }
        const incidence = primitives.find(node => node.type === 'point_on_arc');
        if (grip === 'radius') {
            assert.ok(incidence);
            assert.equal(incidence.a_id, sourceArc.id);
            assert.equal(incidence.temporary, false);
            assert.ok(![sourceArc.c_id, sourceArc.start_id, sourceArc.end_id].includes(incidence.p_id));
            nearPoint(nodes.get(incidence.p_id), drag.end);
        } else assert.equal(incidence, undefined);
        const targetId = grip === 'radius' ? incidence.p_id : sourceArc.end_id;
        const pointerGoals = goals.filter(node => node.p_id === targetId);
        assert.equal(pointerGoals.length, 2);
        for (const prop of ['x', 'y']) {
            const goal = pointerGoals.find(node => node.type === `coordinate_${prop}`);
            assert.equal(goal.scale, 1);
            near(goal[prop], drag.end[prop]);
        }
        assert.equal(goals.length, grip === 'radius' ? 6 : 5);
        const expected = structuredClone(document.entities);
        if (grip === 'radius') expected[0].radius = 1.25;
        checkArcInputs(primitives, expected);
    });
}

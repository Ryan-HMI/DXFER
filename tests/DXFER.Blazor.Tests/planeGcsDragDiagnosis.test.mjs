import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createPlaneGcsSolver } from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';

const solver = await createPlaneGcsSolver();
after(() => solver.dispose());
const point = (x, y) => ({ x, y });
const constraint = (id, kind, ...referenceKeys) => ({ id, kind, referenceKeys });
const dimension = (id, value) => ({ id, kind: 'Diameter', referenceKeys: ['circle'], value, isDriving: true });
const near = (a, b) => assert.ok(Number.isFinite(a) && Math.abs(a - b) <= 1e-6, `${a} != ${b}`);
const circle = () => ({ id: 'circle', kind: 'circle', center: point(12, 0), radius: 2, points: [],
    layerName: 'CUT', native: { sourceId: 'retained-circle' } });

function fixedPeer(axis, sized, reverse) {
    const entities = [{ id: 'peer', kind: 'line',
        points: axis === 'Vertical' ? [point(0, -3), point(0, 3)] : [point(-3, 0), point(3, 0)] }, circle()];
    // Fix alone does not reproduce the conflict: retain the compatible axis equation.
    const constraints = [constraint('peer-axis', axis, 'peer'), constraint('peer-fix', 'Fix', 'peer'),
        constraint('center-horizontal', 'Horizontal', 'circle:center', 'peer:mid')];
    if (reverse) { entities.reverse(); constraints.reverse(); }
    return { entities, constraints, dimensions: sized ? [dimension('diameter', 4)] : [] };
}

function drag(grip, diagonal = true) {
    return grip === 'center'
        ? { key: 'circle|point|center|12|0', start: point(12.1, -0.1),
            end: point(17.1, diagonal ? 2.9 : -0.1), constrain: false }
        : { key: 'circle', start: point(14, 0), end: point(19, diagonal ? 1 : 0), constrain: false };
}

function solve(request) {
    const before = structuredClone(request);
    const result = solver.solve(request);
    assert.deepEqual(request, before, 'Native solving cannot mutate the history snapshot');
    assert.equal(result.engine, 'PlaneGCS');
    assert.ok(Array.isArray(result.redundantDrivingDimensionIds));
    if (result.status !== 'solved') assert.deepEqual(result.entities, request.document.entities);
    return result;
}

function assertCircle(result, gesture, sized) {
    assert.equal(result.status, 'solved', result.diagnostics.join(' '));
    const actual = result.entities.find(entity => entity.id === 'circle');
    near(actual.center.y, 0);
    assert.equal(actual.kind, 'circle');
    assert.deepEqual(actual.native, circle().native);
    assert.equal(actual.layerName, 'CUT');
    if (gesture.key.includes('center')) {
        near(actual.center.x, 17);
        near(actual.radius, 2);
    } else {
        near(Math.hypot(actual.center.x - gesture.end.x, actual.center.y - gesture.end.y), actual.radius);
        if (sized) assert.ok(Math.abs(actual.center.x - 12) > 1, 'Sized circle must translate, not silently remain locked');
    }
    if (sized) near(actual.radius, 2);
}

for (const axis of ['Horizontal', 'Vertical']) for (const grip of ['center', 'perimeter'])
    for (const sized of [false, true]) for (const reverse of [false, true]) {
        test(`${axis} fixed peer: ${grip} diagonal drag, sized=${sized}, reverse=${reverse}`, () => {
            const document = fixedPeer(axis, sized, reverse), gesture = drag(grip);
            const request = { document, drag: gesture };
            for (let repeat = 0; repeat < 2; repeat++) {
                const result = solve(request);
                assertCircle(result, gesture, sized);
                assert.deepEqual(result.entities.find(e => e.id === 'peer'), document.entities.find(e => e.id === 'peer'));
                const dof = (grip === 'center' ? 2 : 3) - Number(sized);
                assert.ok(result.diagnostics.includes(`PlaneGCS mapped-system degrees of freedom: ${dof}.`));
            }
        });
    }

for (const reverse of [false, true]) {
    test(`sized circle horizontal perimeter drag retains compatible redundant Vertical, reverse=${reverse}`, () => {
        const document = fixedPeer('Vertical', true, reverse), gesture = drag('perimeter', false);
        const result = solve({ document, drag: gesture });
        assertCircle(result, gesture, true);
        assert.deepEqual(result.entities.find(e => e.id === 'peer'), document.entities.find(e => e.id === 'peer'));
    });

    test(`diagonal center projection verifies attempted diameter rank, reverse=${reverse}`, () => {
        const document = fixedPeer('Vertical', true, reverse), gesture = drag('center');
        const result = solve({ document, drag: gesture, attemptedDrivingDimensionIds: ['diameter'] });
        assertCircle(result, gesture, true);
        assert.deepEqual(result.redundantDrivingDimensionIds, []);
        assert.ok(result.diagnostics.includes('Native rank check diameter: DOF 1, without intent 2.'));
    });

    test(`whole-fixed circle retains native fixed parameters in diameter rank probe, reverse=${reverse}`, () => {
        const document = fixedPeer('Vertical', true, reverse);
        document.constraints.push(constraint('circle-fix', 'Fix', 'circle'));
        const result = solve({ document, drag: drag('perimeter'), attemptedDrivingDimensionIds: ['diameter'] });
        assert.equal(result.status, 'solved', result.diagnostics.join(' '));
        assert.deepEqual(result.entities, document.entities);
        assert.deepEqual(result.redundantDrivingDimensionIds, ['diameter']);
        assert.ok(result.diagnostics.includes('Native rank check diameter: DOF 1, without intent 1.'));
    });
}

function rectanglePeer(reverse) {
    const corners = [point(0, 0), point(7, 0), point(7, -4.6666667), point(0, -4.6666667)];
    const entities = corners.map((p, i) => ({ id: `edge${i}`, kind: 'line', points: [p, corners[(i + 1) % 4]] }));
    const constraints = corners.map((_, i) => constraint(`weld${i}`, 'Coincident',
        `edge${i}:end`, `edge${(i + 1) % 4}:start`));
    constraints.push(constraint('parallel0', 'Parallel', 'edge0', 'edge2'),
        constraint('parallel1', 'Parallel', 'edge1', 'edge3'),
        constraint('perpendicular', 'Perpendicular', 'edge0', 'edge1'),
        constraint('horizontal', 'Horizontal', 'edge0'), constraint('vertical', 'Vertical', 'edge1'),
        constraint('anchor', 'Fix', 'edge0:start'),
        constraint('center-horizontal', 'Horizontal', 'circle:center', 'edge0:mid'));
    entities.push(circle());
    if (reverse) { entities.reverse(); constraints.reverse(); }
    return { entities, constraints, dimensions: [dimension('diameter', 4)] };
}

for (const grip of ['center', 'perimeter']) for (const reverse of [false, true]) {
    test(`created rectangle anchored midpoint supports sized circle ${grip}, reverse=${reverse}`, () => {
        const document = rectanglePeer(reverse), gesture = drag(grip);
        const result = solve({ document, drag: gesture, attemptedDrivingDimensionIds: ['diameter'] });
        assertCircle(result, gesture, true);
        for (let i = 0; i < 4; i++) {
            const edge = result.entities.find(e => e.id === `edge${i}`);
            const next = result.entities.find(e => e.id === `edge${(i + 1) % 4}`);
            near(edge.points[1].x, next.points[0].x);
            near(edge.points[1].y, next.points[0].y);
            const axis = i % 2 === 0 ? 'y' : 'x';
            near(edge.points[0][axis], edge.points[1][axis]);
            assert.ok(Math.hypot(edge.points[1].x - edge.points[0].x, edge.points[1].y - edge.points[0].y) > 1e-6);
        }
        assert.deepEqual(result.entities.find(e => e.id === 'edge0').points[0], point(0, 0));
    });
}

for (const grip of ['center', 'perimeter']) for (const mismatch of ['diameters', 'fixed-radius']) {
    test(`soft ${grip} goal cannot hide actual hard ${mismatch} contradiction`, () => {
        const document = fixedPeer('Vertical', false, false);
        document.dimensions = [dimension('diameter6', 6)];
        if (mismatch === 'diameters') document.dimensions.push(dimension('diameter4', 4));
        else document.constraints.push(constraint('circle-fix', 'Fix', 'circle'));
        const result = solve({ document, drag: drag(grip) });
        assert.equal(result.status, 'failed', result.diagnostics.join(' '));
        assert.deepEqual(result.entities, document.entities);
        assert.ok(result.diagnostics.some(message => /conflict|not satisfied|not satisfy|did not satisfy/i.test(message)));
    });
}

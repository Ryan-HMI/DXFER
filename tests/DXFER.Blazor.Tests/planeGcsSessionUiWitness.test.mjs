import assert from 'node:assert/strict';
import {test} from 'node:test';
import {createPlaneGcsSolver} from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';

const p = (x, y) => ({x, y});
const constraint = (id, kind, referenceKeys) => ({id, kind, referenceKeys, state: 'Unknown'});
function witness() {
    const polygon = (id, center, radius, rotationAngleDegrees) => ({
        id, kind: 'polygon', center, radius, rotationAngleDegrees, sideCount: 6, circumscribed: false,
        points: Array.from({length: 6}, (_, i) => {
            const angle = rotationAngleDegrees * Math.PI / 180 + i * Math.PI / 3;
            return p(center.x + radius * Math.cos(angle), center.y + radius * Math.sin(angle));
        })
    });
    const mirror = constraint('mirror', 'Symmetric', ['a', 'b', 'axis']);
    return {
        document: {
            entities: [{id: 'axis', kind: 'line', points: [p(-7, -10), p(-7, 10)]},
                polygon('a', p(-14.13372651860118, 3.7773980130441487), 1.3941710200160742, 266.2151126470417),
                polygon('b', p(1.0080730505287647, -1.6462810314260423), 1.713422647677362, 72.93348181061447)],
            constraints: [constraint('axis-vertical', 'Vertical', ['axis']), constraint('fixed', 'Fix', ['axis']), mirror],
            dimensions: [{id: 'radius', kind: 'Radius', referenceKeys: ['a'], value: 1.3941710200160742,
                isDriving: true, state: 'Satisfied'}]
        },
        constraints: [mirror], dimensions: [], fixedReferenceKeys: ['axis', '$origin:center'],
        initialGuesses: [], attemptedDrivingDimensionIds: []
    };
}

function assertSolved(result, request, context) {
    assert.equal(result.status, 'solved', `${context}: ${JSON.stringify(result.diagnostics)}`);
    assert.deepEqual(result.entities[0], request.document.entities[0], 'The fixed axis must not move');
    const [a, b] = result.entities.slice(1), near = (x, y) => assert.ok(Math.abs(x - y) < 1e-6, `${x} != ${y}`);
    near(a.radius, request.document.dimensions[0].value); near(b.radius, a.radius);
    near(a.center.x + b.center.x, -14); near(a.center.y, b.center.y);
    a.points.forEach((v, i) => {
        const other = b.points[(6 - i) % 6]; near(v.x + other.x, -14); near(v.y, other.y);
    });
}

// Reduced from real UI request capture. The original generic session fixtures
// passed; this orientation exposes native hard-success/preference-failure.
test('reopening identical polygon baseline keeps a compatible symmetry solvable', async () => {
    const request = witness(), before = structuredClone(request), solver = await createPlaneGcsSolver();
    try {
        for (let i = 0; i < 4; i++) {
            assertSolved(solver.solve(structuredClone(request)), request, `reopen ${i}`);
            assert.deepEqual(request, before);
        }
    } finally { solver.dispose(); }
});

test('unrelated successful rectangle edit does not poison the first polygon operation', async () => {
    const request = witness(), solver = await createPlaneGcsSolver();
    const points = [p(0, 0), p(15, 0), p(15, 8), p(0, 8)];
    const width = value => ({id: 'width', kind: 'LinearDistance', referenceKeys: ['edge0:start', 'edge0:end'], value, isDriving: true});
    try {
        const rectangle = solver.solve({
            document: {
                entities: points.map((point, i) => ({id: `edge${i}`, kind: 'line', points: [point, points[(i + 1) % 4]]})),
                constraints: [...points.map((_, i) => constraint(`weld${i}`, 'Coincident', [`edge${i}:end`, `edge${(i + 1) % 4}:start`])),
                    constraint('parallel0', 'Parallel', ['edge0', 'edge2']), constraint('parallel1', 'Parallel', ['edge1', 'edge3']),
                    constraint('perpendicular', 'Perpendicular', ['edge0', 'edge1']), constraint('horizontal', 'Horizontal', ['edge0']),
                    constraint('vertical', 'Vertical', ['edge1'])],
                dimensions: [width(16)]
            }, dimensions: [width(16)], constraints: [], attemptedDrivingDimensionIds: ['width'],
            fixedReferenceKeys: ['$origin:center'], initialGuesses: []
        });
        assert.equal(rectangle.status, 'solved', JSON.stringify(rectangle.diagnostics));
        assertSolved(solver.solve(structuredClone(request)), request, 'first polygon after rectangle');
    } finally { solver.dispose(); }
});

test('fresh native modules consistently accept the witness', async () => {
    for (let i = 0; i < 3; i++) {
        const solver = await createPlaneGcsSolver(), request = witness();
        try { assertSolved(solver.solve(request), request, `fresh module ${i}`); }
        finally { solver.dispose(); }
    }
});

test('session recovery retains constrained pointer goals and refuses hard conflicts', async () => {
    const solver = await createPlaneGcsSolver(), request = witness();
    const circle = {id: 'circle', kind: 'circle', center: p(12, 0), radius: 2, points: []};
    const diameter = value => ({id: `diameter-${value}`, kind: 'Diameter', referenceKeys: ['circle'],
        value, isDriving: true});
    const drag = {
        document: {
            entities: [{id: 'peer', kind: 'line', points: [p(0, -3), p(0, 3)]}, circle],
            constraints: [constraint('fixed-peer', 'Fix', ['peer']), constraint('vertical', 'Vertical', ['peer']),
                constraint('alignment', 'Horizontal', ['circle:center', 'peer:mid'])],
            dimensions: [diameter(4)]
        },
        drag: {key: 'circle|point|center|12|0', start: p(12, 0), end: p(17, 3), constrain: false}
    };
    try {
        for (let i = 0; i < 3; i++) assertSolved(solver.solve(structuredClone(request)), request, `recovery ${i}`);
        const before = structuredClone(drag), moved = solver.solve(drag);
        assert.equal(moved.status, 'solved', JSON.stringify(moved.diagnostics));
        assert.deepEqual(moved.entities[0], drag.document.entities[0], 'The fixed peer must remain exact');
        const actual = moved.entities.find(entity => entity.id === 'circle');
        assert.ok(Math.abs(actual.center.x - 17) < 1e-6, 'The pointer goal must not be silently discarded');
        assert.ok(Math.abs(actual.center.y) < 1e-6, 'The allowed drag must project onto Horizontal');
        assert.ok(Math.abs(actual.radius - 2) < 1e-6);
        assert.deepEqual(drag, before);

        const conflict = structuredClone(drag);
        conflict.document.dimensions.push(diameter(6));
        const refused = solver.solve(conflict);
        assert.equal(refused.status, 'failed', JSON.stringify(refused.diagnostics));
        assert.deepEqual(refused.entities, conflict.document.entities, 'Failed native candidates must never publish');
        assertSolved(solver.solve(structuredClone(request)), request, 'witness after rejected drag');
    } finally { solver.dispose(); }
});

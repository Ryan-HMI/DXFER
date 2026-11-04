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

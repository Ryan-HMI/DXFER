import assert from 'node:assert/strict';
import { after, test } from 'node:test';
import { createPlaneGcsSolver } from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';

const solver = await createPlaneGcsSolver();
after(() => solver.dispose());
const p = (x, y) => ({ x, y });
const near = (a, b) => assert.ok(Number.isFinite(a) && Math.abs(a - b) <= 1e-6, `${a} != ${b}`);
const nearPoint = (a, b) => { near(a.x, b.x); near(a.y, b.y); };
const fix = key => ({ id: `fix-${key}`, kind: 'Fix', referenceKeys: [key] });
const ellipse = () => ({
    id: 'e', kind: 'ellipse', points: [], center: p(1, 2),
    majorAxisEndPoint: p(3, 4), minorRadiusRatio: 0.6,
    startAngleDegrees: 300, endAngleDegrees: 70,
    layerName: 'CURVES', native: { sourceId: 'partial-native-ellipse' }
});
const spline = hermite => ({
    id: 's', kind: 'spline', points: [], controlPoints: [p(0, 0), p(2, 4), p(4, 0)],
    fitPoints: hermite ? [p(0, 0), p(2, 4), p(4, 0)] : [],
    splineDegree: 2, splineKnots: [0, 0, 0, 1, 1, 1],
    splineWeights: hermite ? [1, 1, 1] : [1, 0.5, 1],
    ...(hermite ? { startTangentHandle: p(0.5, 1), endTangentHandle: p(3.5, 1) } : {}),
    layerName: 'CURVES', native: { sourceId: hermite ? 'fit-spline' : 'rational-spline' }
});

function run(entities, drag, constraints = []) {
    const request = { document: { entities, constraints, dimensions: [] }, drag };
    const before = structuredClone(request);
    const result = solver.solve(request);
    assert.equal(result.engine, 'PlaneGCS');
    assert.deepEqual(request, before, 'Solving must not mutate the input/history snapshot');
    if (result.status !== 'solved') assert.deepEqual(result.entities, entities);
    assert.deepEqual(result.entities.map(e => e.id), entities.map(e => e.id));
    for (const [i, entity] of result.entities.entries()) {
        assert.equal(entity.kind, entities[i].kind);
        assert.equal(entity.layerName, entities[i].layerName);
        assert.deepEqual(entity.native, entities[i].native);
    }
    return result;
}
function solved(result) {
    assert.equal(result.status, 'solved', JSON.stringify(result.diagnostics));
    return result.entities[0];
}
function unsupported(result, entities, diagnostic) {
    assert.equal(result.status, 'unsupported', JSON.stringify(result.diagnostics));
    assert.match(result.diagnostics.join('\n'), diagnostic);
    assert.deepEqual(result.entities, entities, 'Unsupported gestures must roll back every entity');
}
function preservedSpline(actual, original) {
    for (const key of ['splineDegree', 'splineKnots', 'splineWeights', 'points'])
        assert.deepEqual(actual[key], original[key]);
}

for (const grip of ['start', 'end']) {
    for (const raw of [false, true]) {
        const original = { id: 'l', kind: 'line', points: [p(0, 0), p(6, 8)] };
        const index = grip === 'start' ? 0 : 1;
        const start = original.points[index], end = p(start.x + 4, start.y + 2);
        const key = raw ? `l|point|${grip}|999|999` : `l:${grip}`;
        const constraints = [fix(`l:${index === 0 ? 'end' : 'start'}`)];
        const drag = { key, start, end, constrain: false };
        test(`unrestricted line ${grip}, raw=${raw}: opposite endpoint remains Fixed`, () => {
            const free = solved(run([original], drag, constraints));
            nearPoint(free.points[index], end);
            nearPoint(free.points[1 - index], original.points[1 - index]);
        });
        test(`Shift line ${grip}, raw=${raw}: projects onto original infinite line`, () => {
            const result = solved(run([original], { ...drag, constrain: true }, constraints));
            nearPoint(result.points[index], p(start.x + 2.4, start.y + 3.2));
            nearPoint(result.points[1 - index], original.points[1 - index]);
        });
    }
}

for (const key of ['e', 'e:center', 'e|point|center|999|999']) {
    test(`partial rotated ellipse ${key} translation preserves native sweep and axes`, () => {
        const original = ellipse();
        const result = solved(run([original], {
            key, start: p(1.2, 1.9), end: p(4.2, 5.9), constrain: false
        }));
        nearPoint(result.center, p(4, 6));
        nearPoint(result.majorAxisEndPoint, original.majorAxisEndPoint);
        near(result.minorRadiusRatio, original.minorRadiusRatio);
        near(result.startAngleDegrees, 300);
        near(result.endAngleDegrees, 70);
    });
}

for (const grip of ['start', 'end']) {
    test(`partial ellipse ${grip} edit explicitly unsupported, without flattening or sweep loss`, () => {
        const original = ellipse();
        unsupported(run([original], {
            key: `e:${grip}`, start: p(1, 2), end: p(7, 8), constrain: false
        }), [original], /Only partial ellipse endpoints at stored major-axis grips support resizing/);
    });
}

function preservedEllipseFrame(actual, original) {
    nearPoint(actual.center, original.center);
    near(actual.startAngleDegrees, original.startAngleDegrees);
    near(actual.endAngleDegrees, original.endAngleDegrees);
    assert.deepEqual(actual.points, original.points);
}

for (const major of [true, false]) {
    const grip = major ? 'major-end' : 'minor-end';
    const start = major ? p(4, 6) : p(-1.4, 3.8);
    const end = major ? p(7, 10) : p(-2, 6);
    for (const raw of [false, true]) {
        test(`partial ellipse ${grip} resize raw=${raw} preserves center, other axis length, and sweep`, () => {
            const original = ellipse();
            const key = raw ? `e|point|quadrant-${major ? 0 : 90}|999|999` : `e:${grip}`;
            const result = solved(run([original], { key, start, end, constrain: false },
                raw ? [fix('e:center')] : []));
            preservedEllipseFrame(result, original);
            nearPoint(result.majorAxisEndPoint, major ? p(6, 8) : p(3, 4));
            // Minor grip projects onto the original minor direction (-0.8, 0.6).
            near(result.minorRadiusRatio, major ? 0.3 : 0.96);
            near(Math.hypot(result.majorAxisEndPoint.x, result.majorAxisEndPoint.y)
                * result.minorRadiusRatio, major ? 3 : 4.8);
        });
    }
    test(`partial ellipse whole Fix prevents ${grip} resizing`, () => {
        const original = ellipse();
        const result = solved(run([original], {
            key: `e:${grip}`, start, end, constrain: false
        }, [fix('e')]));
        preservedEllipseFrame(result, original);
        nearPoint(result.majorAxisEndPoint, original.majorAxisEndPoint);
        near(result.minorRadiusRatio, original.minorRadiusRatio);
    });
    test(`partial ellipse ${grip} zero-length target refuses without native geometry loss`, () => {
        const original = ellipse();
        unsupported(run([original], {
            key: `e:${grip}`, start, end: original.center, constrain: false
        }), [original], /Ellipse (major|minor) axis must stay nonzero/);
    });
}

for (const grip of ['start', 'end']) {
    test(`partial ellipse ${grip} on stored major axis resizes while preserving parameter representatives`, () => {
        const original = { ...ellipse(), startAngleDegrees: 0, endAngleDegrees: 180 };
        const negative = grip === 'end';
        const result = solved(run([original], {
            key: `e:${grip}`, start: negative ? p(-2, -2) : p(4, 6),
            end: negative ? p(-5, -6) : p(7, 10), constrain: false
        }));
        preservedEllipseFrame(result, original);
        nearPoint(result.majorAxisEndPoint, p(6, 8));
        near(result.minorRadiusRatio, 0.3);
    });
}

test('partial ellipse whole Fix preserves the complete curve during center drag', () => {
    const original = ellipse();
    const result = solved(run([original], { key: 'e:center', start: p(1, 2), end: p(4, 6) }, [fix('e')]));
    nearPoint(result.center, original.center);
    nearPoint(result.majorAxisEndPoint, original.majorAxisEndPoint);
    near(result.minorRadiusRatio, original.minorRadiusRatio);
    near(result.startAngleDegrees, original.startAngleDegrees);
    near(result.endAngleDegrees, original.endAngleDegrees);
});

for (const hermite of [false, true]) {
    for (const grip of ['start', 'end']) {
        test(`${hermite ? 'Hermite' : 'rational'} spline ${grip} endpoint with other points Fixed`, () => {
            const original = spline(hermite), pointsKey = hermite ? 'fitPoints' : 'controlPoints';
            const index = grip === 'start' ? 0 : 2;
            const start = original[pointsKey][index], end = p(start.x + 2, start.y + 3);
            const constraints = original[pointsKey].flatMap((_, i) =>
                i === index ? [] : [fix(`s:${hermite ? 'fit' : 'control'}-${i}`)]);
            const drag = { key: `s|point|${grip}|999|999`,
                start: p(start.x + 0.2, start.y - 0.1), end: p(end.x + 0.2, end.y - 0.1), constrain: false };
            const result = solved(run([original], drag, constraints));
            preservedSpline(result, original);
            result[pointsKey].forEach((value, i) =>
                nearPoint(value, i === index ? end : original[pointsKey][i]));
            if (hermite) {
                const movingHandle = grip === 'start' ? 'startTangentHandle' : 'endTangentHandle';
                const stationaryHandle = grip === 'start' ? 'endTangentHandle' : 'startTangentHandle';
                nearPoint(result[movingHandle], p(original[movingHandle].x + 2, original[movingHandle].y + 3));
                nearPoint(result[stationaryHandle], original[stationaryHandle]);
            }
            const shifted = solved(run([original], { ...drag, constrain: true }, constraints));
            preservedSpline(shifted, original);
            shifted[pointsKey].forEach((value, i) =>
                nearPoint(value, i === index ? end : original[pointsKey][i]));
        });
    }
    test(`${hermite ? 'Hermite' : 'rational'} spline whole Fix protects all points and handles`, () => {
        const original = spline(hermite);
        const result = solved(run([original], {
            key: 's:end', start: p(4, 0), end: p(8, 5), constrain: false
        }, [fix('s')]));
        preservedSpline(result, original);
        for (const key of ['fitPoints', 'controlPoints'])
            result[key].forEach((value, i) => nearPoint(value, original[key][i]));
        if (hermite) {
            nearPoint(result.startTangentHandle, original.startTangentHandle);
            nearPoint(result.endTangentHandle, original.endTangentHandle);
        }
    });
}

for (const grip of ['start', 'end']) {
    const index = grip === 'start' ? 0 : 1;
    for (const [label, delta] of [
        ['zero movement', p(0, 0)],
        ['near-zero movement', p(1e-8, -2e-8)],
        ['opposite movement', p(-4, -2)],
        ['along original vector', p(3, 4)],
        ['opposite original vector', p(-3, -4)]
    ]) {
        test(`line ${grip} ${label} preserves Fixed opposite endpoint and native identity`, () => {
            const original = { id: 'l', kind: 'line', points: [p(0, 0), p(6, 8)],
                layerName: 'LINES', native: { sourceId: 'line-edit' } };
            const start = original.points[index], end = p(start.x + delta.x, start.y + delta.y);
            const result = solved(run([original], {
                key: `l:${grip}`, start, end, constrain: false
            }, [fix(`l:${index === 0 ? 'end' : 'start'}`)]));
            // Tighter than the requested near-zero motion, so a silent no-op cannot pass.
            assert.ok(Math.hypot(result.points[index].x - end.x, result.points[index].y - end.y) < 1e-10);
            assert.deepEqual(result.points[1 - index], original.points[1 - index]);
            assert.ok(Math.hypot(result.points[1].x - result.points[0].x,
                result.points[1].y - result.points[0].y) > 1);
        });
    }
    test(`Shift off-center ${grip} grab projects absolute pointer rather than pointer delta`, () => {
        const original = { id: 'l', kind: 'line', points: [p(0, 0), p(6, 8)] };
        const anchor = original.points[index];
        const result = solved(run([original], {
            key: `l|point|${grip}|999|999`,
            start: p(anchor.x + 0.2, anchor.y - 0.1),
            end: p(anchor.x + 4.2, anchor.y + 1.9), constrain: true
        }, [fix(`l:${index === 0 ? 'end' : 'start'}`)]));
        nearPoint(result.points[index], p(anchor.x + 2.424, anchor.y + 3.232));
        nearPoint(result.points[1 - index], original.points[1 - index]);
    });
}

for (const reversed of [false, true]) {
    test(`Shift opposite-direction extension is not clamped, reversed=${reversed}`, () => {
        const original = { id: 'l', kind: 'line', points: reversed ? [p(6, 8), p(0, 0)] : [p(0, 0), p(6, 8)] };
        const index = reversed ? 1 : 0;
        const result = solved(run([original], {
            key: `l:${index === 0 ? 'start' : 'end'}`,
            start: p(0, 0), end: p(-4, -2), constrain: true
        }, [fix(`l:${index === 0 ? 'end' : 'start'}`)]));
        nearPoint(result.points[index], p(-2.4, -3.2));
        nearPoint(result.points[1 - index], p(6, 8));
    });
    test(`small nondegenerate original line remains endpoint-editable, reversed=${reversed}`, () => {
        const points = [p(0, 0), p(6e-5, 8e-5)];
        if (reversed) points.reverse();
        const original = { id: 'l', kind: 'line', points };
        const index = reversed ? 1 : 0;
        const result = solved(run([original], {
            key: `l:${index === 0 ? 'start' : 'end'}`,
            start: p(0, 0), end: p(-4e-5, -2e-5), constrain: true
        }, [fix(`l:${index === 0 ? 'end' : 'start'}`)]));
        nearPoint(result.points[index], p(-2.4e-5, -3.2e-5));
        nearPoint(result.points[1 - index], p(6e-5, 8e-5));
    });
}

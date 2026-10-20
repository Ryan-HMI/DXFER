import assert from "node:assert/strict";
import { after, test } from "node:test";
import { createPlaneGcsSolver } from "../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js";

const solver = await createPlaneGcsSolver();
after(() => solver.dispose());
const p = (x, y) => ({ x, y });
const near = (a, b) => assert.ok(Number.isFinite(a) && Math.abs(a - b) < 1e-6, `${a} != ${b}`);
const nearPoint = (a, b) => { near(a.x, b.x); near(a.y, b.y); };
const c = (id, kind, ...referenceKeys) => ({ id, kind, referenceKeys });
const d = (id, kind, value, ...referenceKeys) => ({ id, kind, value, referenceKeys, isDriving: true });
const ellipse = (extra = {}) => ({
    id: "e", kind: "ellipse", points: [], center: p(1, 2),
    majorAxisEndPoint: p(5, 0), minorRadiusRatio: 0.6,
    startAngleDegrees: 0, endAngleDegrees: 360,
    layerName: "NATIVE", native: { sourceId: "ellipse-42" }, ...extra
});
const spline = (extra = {}) => ({
    id: "s", kind: "spline", points: [], controlPoints: [p(0, 0), p(2, 4), p(4, 0)],
    splineDegree: 2, splineKnots: [0, 0, 0, 1, 1, 1], splineWeights: [1, 0.5, 1],
    layerName: "NATIVE", native: { sourceId: "spline-42" }, ...extra
});
function solve(entities, constraints = [], dimensions = [], extra = {}, status = "solved") {
    const request = { document: { entities, constraints, dimensions }, ...extra };
    const before = structuredClone(request);
    const result = solver.solve(request);
    assert.equal(result.status, status, JSON.stringify(result.diagnostics));
    assert.equal(result.engine, "PlaneGCS");
    assert.deepEqual(request, before);
    if (status !== "solved") assert.deepEqual(result.entities, entities);
    for (const entity of result.entities) {
        const original = entities.find(value => value.id === entity.id);
        assert.equal(entity.layerName, original.layerName);
        assert.deepEqual(entity.native, original.native);
    }
    return result;
}
function preservedSpline(result, original) {
    assert.equal(result.splineDegree, original.splineDegree);
    assert.deepEqual(result.splineKnots, original.splineKnots);
    assert.deepEqual(result.splineWeights, original.splineWeights);
    assert.deepEqual(result.points, original.points);
    assert.equal(result.controlPoints.length, original.controlPoints.length);
}

test("native ellipse axis reference drives a coupled point without flattening", () => {
    const original = ellipse();
    const result = solve([original, { id: "p", kind: "point", points: [p(0, 0)] }],
        [c("fixed", "Fix", "e"), c("on-axis", "Coincident", "e:minor-end", "p:center")]);
    nearPoint(result.entities[1].points[0], p(1, 5));
    nearPoint(result.entities[0].majorAxisEndPoint, original.majorAxisEndPoint);
    near(result.entities[0].minorRadiusRatio, 0.6);
});

test("rotated ellipse center and major-axis reference retain the native focal frame", () => {
    const original = ellipse({ majorAxisEndPoint: p(3, 4) });
    const result = solve([original, { id: "p", kind: "point", points: [p(0, 0)] }],
        [c("fixed", "Fix", "e"), c("on-axis", "Coincident", "e:major-end", "p:center")]);
    nearPoint(result.entities[1].points[0], p(4, 6));
    nearPoint(result.entities[0].majorAxisEndPoint, p(3, 4));
});

for (const key of ["e", "e:center"]) {
    test(`native ellipse ${key} translation preserves axes and uses pointer delta`, () => {
        const result = solve([ellipse()], [], [], {
            drag: { key, start: p(1.1, 2.2), end: p(4.1, 6.2) }
        }).entities[0];
        nearPoint(result.center, p(4, 6));
        nearPoint(result.majorAxisEndPoint, p(5, 0));
        near(result.minorRadiusRatio, 0.6);
    });
}

test("ellipse major-axis dimension changes the native axis with center and focus fixed", () => {
    const result = solve([ellipse()],
        [c("center", "Fix", "e:center"), c("focus", "Fix", "e:focus1")],
        [d("major", "LinearDistance", 6, "e:center", "e:major-end")]).entities[0];
    nearPoint(result.center, p(1, 2));
    nearPoint(result.majorAxisEndPoint, p(6, 0));
    near(result.minorRadiusRatio, Math.sqrt(20) / 6);
});

for (const [start, end] of [[20, 130], [300, 70]]) {
    test(`partial native ellipse ${start}/${end} retains parameter representatives and endpoint coupling`, () => {
        const original = ellipse({ startAngleDegrees: start, endAngleDegrees: end });
        const result = solve([original, { id: "p", kind: "point", points: [p(0, 0)] }],
            [c("fixed", "Fix", "e"), c("endpoint", "Coincident", "e:end", "p:center")]);
        const angle = end * Math.PI / 180;
        nearPoint(result.entities[1].points[0], p(1 + 5 * Math.cos(angle), 2 + 3 * Math.sin(angle)));
        near(result.entities[0].startAngleDegrees, start);
        near(result.entities[0].endAngleDegrees, end);
    });
}

for (const fixture of [
    spline({ splineDegree: 1, controlPoints: [p(0, 0), p(4, 3)], splineKnots: [0, 0, 1, 1], splineWeights: [1, 1] }),
    spline(),
    spline({ splineDegree: 3, controlPoints: [p(0, 0), p(1, 4), p(3, 4), p(4, 0)],
        splineKnots: [0, 0, 0, 0, 1, 1, 1, 1], splineWeights: [1, 2, 2, 1] }),
    spline({ controlPoints: [p(0, 0), p(1, 4), p(2, 2), p(3, 4), p(4, 0)],
        splineKnots: [2, 2, 2, 3, 3, 5, 5, 5], splineWeights: [1, 0.5, 1, 2, 1] })
]) {
    test(`native spline degree ${fixture.splineDegree} with ${fixture.controlPoints.length} poles fixes without orphan DOF`, () => {
        const result = solve([fixture], [c("fix", "Fix", "s"), c("discover", "Fix", "s:control-0")]);
        preservedSpline(result.entities[0], fixture);
        result.entities[0].controlPoints.forEach((value, index) => nearPoint(value, fixture.controlPoints[index]));
        assert.ok(result.diagnostics.includes("PlaneGCS mapped-system degrees of freedom: 0."));
    });
}

test("native spline endpoint coincidence moves the actual endpoint pole", () => {
    const original = spline();
    const result = solve([original, { id: "p", kind: "point", points: [p(-2, 1)] }],
        [c("fixed", "Fix", "p"), c("start", "Coincident", "s:start", "p:center")]);
    nearPoint(result.entities[0].controlPoints[0], p(-2, 1));
    preservedSpline(result.entities[0], original);
});

for (const [key, index] of [["s:start", 0], ["s:end", 2], ["s|point|control-1|2|4", 1]]) {
    test(`native spline ${key} drag changes its pole without changing knots or weights`, () => {
        const original = spline();
        const before = original.controlPoints[index];
        const fixed = original.controlPoints.flatMap((_, i) => i === index ? [] : [c(`fix-${i}`, "Fix", `s:control-${i}`)]);
        const result = solve([original], fixed, [], {
            drag: { key, start: p(before.x + 0.1, before.y + 0.2), end: p(before.x + 2.1, before.y + 3.2) }
        }).entities[0];
        nearPoint(result.controlPoints[index], p(before.x + 2, before.y + 3));
        preservedSpline(result, original);
    });
}

test("native spline body translation keeps the rational representation", () => {
    const original = spline();
    const result = solve([original], [], [], { drag: { key: "s", start: p(0, 0), end: p(2, 3) } }).entities[0];
    result.controlPoints.forEach((value, index) => nearPoint(value,
        p(original.controlPoints[index].x + 2, original.controlPoints[index].y + 3)));
    preservedSpline(result, original);
});

for (const [label, entity] of [
    ["periodic", spline({ isPeriodic: true })],
    ["unclamped", spline({ splineKnots: [0, 1, 2, 3, 4, 5] })],
    ["invalid weights", spline({ splineWeights: [1, 0, 1] })],
    ["discontinuous", spline({ controlPoints: [p(0, 0), p(1, 1), p(2, 0), p(3, 0), p(4, 1), p(5, 0)],
        splineKnots: [0, 0, 0, 1, 1, 1, 2, 2, 2], splineWeights: [1, 1, 1, 1, 1, 1] })],
    ["zero-axis ellipse", ellipse({ minorRadiusRatio: 0 })]
]) {
    test(`${label} representation explicitly refuses when constrained`, () => {
        solve([entity], [c("fix", "Fix", `${entity.id}:start`)], [], {}, "unsupported");
    });
}

test("ellipse major grip edits its stored axis and preserves its other semiaxis", () => {
    const result = solve([ellipse()], [], [], { drag: { key: "e:major-end", start: p(6, 2), end: p(8, 2) } }).entities[0];
    nearPoint(result.center, p(1, 2));
    nearPoint(result.majorAxisEndPoint, p(7, 0));
    near(result.minorRadiusRatio, 3 / 7);
});

test("partial ellipse center translation preserves wrapped eccentric parameters", () => {
    const result = solve([ellipse({ startAngleDegrees: 300, endAngleDegrees: 70 })], [], [], {
        drag: { key: "e:center", start: p(1, 2), end: p(4, 6) }
    }).entities[0];
    nearPoint(result.center, p(4, 6));
    nearPoint(result.majorAxisEndPoint, p(5, 0));
    near(result.minorRadiusRatio, 0.6);
    near(result.startAngleDegrees, 300);
    near(result.endAngleDegrees, 70);
});

test("degree-one spline tangent grip refuses explicitly because it has no supported control handle", () => {
    solve([spline({ splineDegree: 1, controlPoints: [p(0, 0), p(4, 3)],
        splineKnots: [0, 0, 1, 1], splineWeights: [1, 1] })], [], [], {
        drag: { key: "s|point|tangent-start|0.5|1", start: p(0.5, 1), end: p(1, 2) }
    }, "unsupported");
});

for (const entity of [ellipse(), spline()]) {
    test(`unmapped ${entity.kind} tangency refuses the complete coupled request`, () => {
        solve([entity, { id: "l", kind: "line", points: [p(-10, 0), p(10, 0)] }],
            [c("tangent", "Tangent", entity.id, "l")], [], {}, "unsupported");
    });
}

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
const fits = [p(0, 0), p(2, 4), p(4, 0)];
const spline = (extra = {}) => ({
    id: "s", kind: "spline", points: [], controlPoints: structuredClone(fits),
    fitPoints: [], splineDegree: 2, splineKnots: [0, 0, 0, 1, 1, 1], splineWeights: [1, 1, 1],
    layerName: "NATIVE", native: { sourceId: "original" }, ...extra
});
const hermite = (extra = {}) => spline({
    fitPoints: structuredClone(fits), startTangentHandle: p(0.5, 1), endTangentHandle: p(3.5, 1), ...extra
});
const ellipse = (extra = {}) => ({
    id: "e", kind: "ellipse", points: [], center: p(1, 2), majorAxisEndPoint: p(3, 0),
    minorRadiusRatio: 4 / 3, startAngleDegrees: 0, endAngleDegrees: 360,
    layerName: "NATIVE", native: { sourceId: "original" }, ...extra
});
function solve(entities, constraints = [], dimensions = [], extra = {}, status = "solved") {
    const request = { document: { entities, constraints, dimensions }, ...extra };
    const before = structuredClone(request);
    const result = solver.solve(request);
    assert.equal(result.status, status, JSON.stringify(result.diagnostics));
    assert.deepEqual(request, before);
    if (status !== "solved") assert.deepEqual(result.entities, entities);
    for (const entity of result.entities) {
        const original = entities.find(e => e.id === entity.id);
        assert.equal(entity.layerName, original.layerName);
        assert.deepEqual(entity.native, original.native);
        if (entity.kind === "spline") {
            assert.equal(entity.splineDegree, original.splineDegree);
            assert.deepEqual(entity.splineKnots, original.splineKnots);
            assert.deepEqual(entity.splineWeights, original.splineWeights);
            assert.deepEqual(entity.points, original.points);
        }
    }
    return result;
}

test("new app-native Hermite snapshot is preserved without conversion", () => {
    const original = hermite();
    assert.deepEqual(solve([original]).entities[0], original);
    const fixed = solve([original], [c("fix", "Fix", "s"), c("discover", "Fix", "s:start")]);
    assert.deepEqual(fixed.entities[0], original);
    assert.ok(fixed.diagnostics.includes("PlaneGCS mapped-system degrees of freedom: 0."));
});

for (const [key, index, handle] of [
    ["s:start", 0, "startTangentHandle"], ["s:end", 2, "endTangentHandle"],
    ["s|point|fit-0|0|0", 0, "startTangentHandle"], ["s|point|fit-1|2|4", 1, null]
]) {
    test(`Hermite ${key} drag preserves the fit representation and carries its endpoint handle`, () => {
        const original = hermite(), before = original.fitPoints[index];
        const result = solve([original], [], [], {
            drag: { key, start: p(before.x + 0.1, before.y + 0.2), end: p(before.x + 2.1, before.y + 3.2) }
        }).entities[0];
        result.fitPoints.forEach((value, i) => nearPoint(value,
            i === index ? p(fits[i].x + 2, fits[i].y + 3) : fits[i]));
        result.controlPoints.forEach((value, i) => nearPoint(value, result.fitPoints[i]));
        for (const name of ["startTangentHandle", "endTangentHandle"]) {
            nearPoint(result[name], name === handle
                ? p(original[name].x + 2, original[name].y + 3) : original[name]);
        }
    });
}

for (const [key, name] of [["s:tangent-start", "startTangentHandle"], ["s:tangent-end", "endTangentHandle"]]) {
    test(`Hermite ${key} targets the absolute pointer without moving the fits`, () => {
        const original = hermite(), before = original[name];
        const result = solve([original], [], [], {
            drag: { key, start: p(before.x + 0.1, before.y + 0.2), end: p(1, 3) }
        }).entities[0];
        nearPoint(result[name], p(1, 3));
        result.fitPoints.forEach((value, i) => nearPoint(value, fits[i]));
        nearPoint(result[name === "startTangentHandle" ? "endTangentHandle" : "startTangentHandle"],
            original[name === "startTangentHandle" ? "endTangentHandle" : "startTangentHandle"]);
    });
}

test("Hermite body translation moves every fit, mirrored control, and explicit handle", () => {
    const original = hermite();
    const result = solve([original], [], [], { drag: { key: "s", start: p(0, 0), end: p(2, 3) } }).entities[0];
    for (const key of ["fitPoints", "controlPoints"]) result[key].forEach((value, i) =>
        nearPoint(value, p(original[key][i].x + 2, original[key][i].y + 3)));
    for (const name of ["startTangentHandle", "endTangentHandle"])
        nearPoint(result[name], p(original[name].x + 2, original[name].y + 3));
});

test("Hermite body translation preserves distinct stored controls without interpreting them as curve poles", () => {
    const original = hermite({ controlPoints: [p(-1, 0), p(3, 7), p(5, 0)] });
    const result = solve([original], [], [], { drag: { key: "s", start: p(0, 0), end: p(2, 3) } }).entities[0];
    for (const key of ["fitPoints", "controlPoints"]) result[key].forEach((value, i) =>
        nearPoint(value, p(original[key][i].x + 2, original[key][i].y + 3)));
});

test("Hermite endpoint constraints couple its authoritative point and carry a free handle", () => {
    const original = hermite();
    const result = solve([original, { id: "p", kind: "point", points: [p(2, 3)] }],
        [c("fixed", "Fix", "p"), c("coincident", "Coincident", "s:start", "p:center")]).entities[0];
    nearPoint(result.fitPoints[0], p(2, 3));
    nearPoint(result.startTangentHandle, p(2.5, 4));
});

test("a fixed Hermite handle takes precedence over endpoint handle stability", () => {
    const original = hermite();
    const result = solve([original], [c("fix", "Fix", "s:tangent-start")], [], {
        drag: { key: "s:start", start: p(0, 0), end: p(2, 3) }
    }).entities[0];
    nearPoint(result.startTangentHandle, original.startTangentHandle);
    nearPoint(result.fitPoints[0], p(2, 3));
});

test("Hermite point dimensions remain actual simultaneous driving equations", () => {
    const result = solve([hermite()], [c("start", "Fix", "s:start")],
        [d("end-x", "HorizontalDistance", 7, "s:start", "s:end")],
        { attemptedDrivingDimensionIds: ["end-x"] }).entities[0];
    nearPoint(result.fitPoints[0], p(0, 0));
    near(result.fitPoints[2].x, 7);
    near(result.endTangentHandle.x - result.fitPoints[2].x, -0.5);
});

for (const count of [2, 4]) {
    test(`app-native ${count}-fit creation remains endpoint-editable without changing degree or knot data`, () => {
        const fitPoints = Array.from({ length: count }, (_, i) => p(i * 2, i % 2));
        const degree = count - 1;
        const original = hermite({ controlPoints: structuredClone(fitPoints), fitPoints,
            splineDegree: degree, splineKnots: [...Array(count).fill(0), ...Array(count).fill(1)],
            splineWeights: Array(count).fill(1), startTangentHandle: p(0.5, 0.25),
            endTangentHandle: p((count - 1) * 2 - 0.5, 0.75) });
        const result = solve([original], [], [], {
            drag: { key: "s:start", start: p(0, 0), end: p(2, 3) }
        }).entities[0];
        nearPoint(result.fitPoints[0], p(2, 3));
        nearPoint(result.startTangentHandle, p(2.5, 3.25));
        for (let i = 1; i < count; i++) nearPoint(result.fitPoints[i], original.fitPoints[i]);
    });
}

test("Hermite absent handles stay absent through point and body edits", () => {
    const original = hermite({ startTangentHandle: null, endTangentHandle: null });
    const moved = solve([original], [], [], {
        drag: { key: "s:start", start: p(0, 0), end: p(1, 2) }
    }).entities[0];
    const translated = solve([moved], [], [], {
        drag: { key: "s", start: p(0, 0), end: p(2, 3) }
    }).entities[0];
    assert.equal(translated.startTangentHandle, null);
    assert.equal(translated.endTangentHandle, null);
    nearPoint(translated.fitPoints[0], p(3, 5));
});

test("Hermite curve Tangent refuses while preserving the complete authoritative snapshot", () => {
    solve([hermite(), { id: "l", kind: "line", points: [p(-1, 0), p(6, 0)] }],
        [c("tangent", "Tangent", "s", "l")], [], {}, "unsupported");
});

for (const [key, endpoint, adjacent] of [["s:tangent-start", 0, 1], ["s:tangent-end", 2, 1]]) {
    test(`native ${key} implements the quarter-control handle without Proportional`, () => {
        const original = spline({ splineWeights: [1, 0.5, 1] });
        const target = p(1, 3), ep = original.controlPoints[endpoint];
        const result = solve([original], [], [], {
            drag: { key, start: p(0.6, 1.2), end: target }
        }).entities[0];
        nearPoint(result.controlPoints[adjacent], p(ep.x + 4 * (target.x - ep.x), ep.y + 4 * (target.y - ep.y)));
        result.controlPoints.forEach((value, i) => {
            if (i !== adjacent) nearPoint(value, original.controlPoints[i]);
        });
    });
}

test("native tangent handle remains an exact coupled quarter-control reference in persistent constraints", () => {
    const result = solve([spline(), { id: "p", kind: "point", points: [p(1, 2)] }],
        [c("fix-start", "Fix", "s:start"), c("fix-p", "Fix", "p"),
            c("handle", "Coincident", "s:tangent-start", "p:center")]).entities[0];
    nearPoint(result.controlPoints[0], p(0, 0));
    nearPoint(result.controlPoints[1], p(4, 8));
});

test("native tangent grip cannot move fixed controls", () => {
    const original = spline();
    const result = solve([original], [c("fix", "Fix", "s")], [], {
        drag: { key: "s:tangent-start", start: p(0.5, 1), end: p(1, 3) }
    }).entities[0];
    result.controlPoints.forEach((value, i) => nearPoint(value, original.controlPoints[i]));
});

for (const [key, target] of [["e:major-end", p(4, 2)], ["e:minor-end", p(1, 6)]]) {
    test(`crossed 6x8 ellipse preserves stored axis identity at ${key}`, () => {
        const original = ellipse();
        const result = solve([original, { id: "p", kind: "point", points: [p(0, 0)] }],
            [c("fix", "Fix", "e"), c("axis", "Coincident", key, "p:center")]);
        nearPoint(result.entities[0].majorAxisEndPoint, p(3, 0));
        near(result.entities[0].minorRadiusRatio, 4 / 3);
        nearPoint(result.entities[1].points[0], target);
    });
}

test("crossed rotated ellipse retains the original directed stored axis", () => {
    const original = ellipse({ majorAxisEndPoint: p(1.8, 2.4) });
    const result = solve([original], [], [], {
        drag: { key: "e:center", start: p(1, 2), end: p(4, 6) }
    }).entities[0];
    nearPoint(result.center, p(4, 6));
    nearPoint(result.majorAxisEndPoint, original.majorAxisEndPoint);
    near(result.minorRadiusRatio, 4 / 3);
});

for (const [start, end] of [[20, 130], [300, 70]]) {
    test(`crossed partial ellipse ${start}/${end} keeps the original eccentric parameter frame`, () => {
        const original = ellipse({ startAngleDegrees: start, endAngleDegrees: end });
        const result = solve([original, { id: "p", kind: "point", points: [p(0, 0)] }],
            [c("fix", "Fix", "e"), c("endpoint", "Coincident", "e:end", "p:center")]);
        nearPoint(result.entities[0].majorAxisEndPoint, original.majorAxisEndPoint);
        near(result.entities[0].minorRadiusRatio, 4 / 3);
        near(result.entities[0].startAngleDegrees, start);
        near(result.entities[0].endAngleDegrees, end);
        const u = end * Math.PI / 180;
        nearPoint(result.entities[1].points[0], p(1 + 3 * Math.cos(u), 2 + 4 * Math.sin(u)));
    });
}

test("crossed ellipse dimensions drive the stored axis rather than swapping its identity", () => {
    const result = solve([ellipse()], [c("center", "Fix", "e:center"), c("focus", "Fix", "e:focus1")],
        [d("stored-axis", "LinearDistance", 2, "e:center", "e:major-end")]).entities[0];
    nearPoint(result.majorAxisEndPoint, p(2, 0));
    near(result.minorRadiusRatio, Math.sqrt(11) / 2);
});

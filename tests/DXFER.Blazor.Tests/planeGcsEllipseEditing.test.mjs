import assert from "node:assert/strict";
import { after, test } from "node:test";
import { createPlaneGcsSolver } from "../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js";

const solver = await createPlaneGcsSolver();
after(() => solver.dispose());
const p = (x, y) => ({ x, y });
const near = (a, b) => assert.ok(Math.abs(a - b) < 2e-5, `${a} != ${b}`);
const nearPoint = (a, b) => { near(a.x, b.x); near(a.y, b.y); };
const ellipse = (extra = {}) => ({ id: "e", kind: "ellipse", points: [], center: p(1, 2),
    majorAxisEndPoint: p(5, 0), minorRadiusRatio: 0.4, startAngleDegrees: 0, endAngleDegrees: 360,
    layerName: "CUT", native: { sourceId: "ellipse-native" }, ...extra });
const axisDimension = (id, axis, value) => ({ id, kind: "LinearDistance", isDriving: true, value,
    referenceKeys: [`e:${axis}-start`, `e:${axis}-end`] });
function run(original, extra = {}, status = "solved") {
    const request = { document: { entities: [original], constraints: [], dimensions: [] }, ...extra };
    const before = structuredClone(request), result = solver.solve(request);
    assert.equal(result.status, status, JSON.stringify(result.diagnostics));
    assert.deepEqual(request, before);
    if (status !== "solved") assert.deepEqual(result.entities, request.document.entities);
    const output = result.entities[0];
    for (const field of ["kind", "id", "native", "layerName", "startAngleDegrees", "endAngleDegrees"])
        assert.deepEqual(output[field], original[field]);
    assert.deepEqual(output.points, []);
    return { output, result };
}
for (const key of ["e", "e:center", "e|point|center|1|2"]) {
    test(`equal-axis ellipse ${key} translation preserves its directed axis and entity type`, () => {
        const { output, result } = run(ellipse({ minorRadiusRatio: 1, majorAxisEndPoint: p(3, 4) }), {
            drag: { key, start: p(1.2, 2.1), end: p(4.2, 6.1) }
        });
        nearPoint(output.center, p(4, 6)); nearPoint(output.majorAxisEndPoint, p(3, 4));
        near(output.minorRadiusRatio, 1);
        assert.ok(result.diagnostics.includes("PlaneGCS mapped-system degrees of freedom: 5."));
    });
}
for (const ratio of [0.4, 1, 4 / 3]) {
    for (const [key, sign] of [["major-end", 1], ["major-start", -1], ["quadrant-0", 1], ["quadrant-180", -1]]) {
        test(`ellipse ratio ${ratio} ${key} rotates 90 degrees and preserves other semiaxis`, () => {
            const { output } = run(ellipse({ minorRadiusRatio: ratio }), {
                drag: { key: `e:${key}`, start: p(6.1, 2.1), end: p(1, 2 + sign * 7) }
            });
            nearPoint(output.center, p(1, 2)); nearPoint(output.majorAxisEndPoint, p(0, 7));
            near(output.minorRadiusRatio, 5 * ratio / 7);
        });
    }
    for (const [key, sign] of [["minor-end", 1], ["minor-start", -1], ["quadrant-90", 1], ["quadrant-270", -1]]) {
        test(`ellipse ratio ${ratio} ${key} projects onto original minor direction`, () => {
            const { output } = run(ellipse({ minorRadiusRatio: ratio }), {
                drag: { key: `e:${key}`, start: p(1, 4), end: p(4, 2 + sign * 6) }
            });
            nearPoint(output.center, p(1, 2)); nearPoint(output.majorAxisEndPoint, p(5, 0));
            near(output.minorRadiusRatio, 6 / 5);
        });
    }
}
test("successive minor-grip edits pass through equal axes without swapping identity", () => {
    let original = ellipse();
    for (const size of [5, 8, 3, 5, 2]) {
        original = run(original, { drag: { key: "e:quadrant-90", start: p(1, 4), end: p(1, 2 + size) } }).output;
        nearPoint(original.majorAxisEndPoint, p(5, 0)); near(original.minorRadiusRatio, size / 5);
    }
});
test("two driving axis dimensions cross equal axes with native independent-rank evidence", () => {
    let original = ellipse();
    for (const [major, minor] of [[10, 10], [6, 8], [12, 4]]) {
        const { output, result } = run(original, { fixedReferenceKeys: ["e:center"],
            dimensions: [axisDimension("major", "major", major), axisDimension("minor", "minor", minor)],
            attemptedDrivingDimensionIds: ["major", "minor"] });
        nearPoint(output.center, p(1, 2));
        near(Math.hypot(output.majorAxisEndPoint.x, output.majorAxisEndPoint.y), major / 2);
        near(output.minorRadiusRatio, minor / major);
        assert.deepEqual(result.redundantDrivingDimensionIds, []);
        original = output;
    }
});
test("major grip projects to hard driving axes while allowing rotation", () => {
    const { output } = run(ellipse(), { fixedReferenceKeys: ["e:center"],
        dimensions: [axisDimension("major", "major", 10), axisDimension("minor", "minor", 4)],
        drag: { key: "e:major-end", start: p(6, 2), end: p(1, 10) } });
    nearPoint(output.center, p(1, 2));
    near(Math.hypot(output.majorAxisEndPoint.x, output.majorAxisEndPoint.y), 5);
    near(output.minorRadiusRatio, 0.4);
    nearPoint(output.majorAxisEndPoint, p(0, 5));
});
test("minor grip cannot change a hard driving axis", () => {
    const { output } = run(ellipse(), { fixedReferenceKeys: ["e:center", "e:major-end"],
        dimensions: [axisDimension("minor", "minor", 4)],
        drag: { key: "e:minor-end", start: p(1, 4), end: p(3, 10) } });
    nearPoint(output.center, p(1, 2)); nearPoint(output.majorAxisEndPoint, p(5, 0));
    near(output.minorRadiusRatio, 0.4);
});
for (const [start, end] of [[0, 180], [300, 70]]) {
    test(`equal-axis partial ellipse ${start}/${end} retains exact eccentric endpoints`, () => {
        const entity = ellipse({ minorRadiusRatio: 1, startAngleDegrees: start, endAngleDegrees: end });
        const target = p(1 + 5 * Math.cos(start * Math.PI / 180), 2 + 5 * Math.sin(start * Math.PI / 180));
        const { output } = run(entity, { fixedReferenceKeys: ["e:start"],
            drag: { key: "e:center", start: p(1, 2), end: p(1, 2) } });
        const a = output.majorAxisEndPoint, t = start * Math.PI / 180;
        nearPoint(p(output.center.x + a.x * Math.cos(t) - a.y * output.minorRadiusRatio * Math.sin(t),
            output.center.y + a.y * Math.cos(t) + a.x * output.minorRadiusRatio * Math.sin(t)), target);
    });
}
test("partial ellipse endpoint on stored major axis resizes without changing sweep", () => {
    const { output } = run(ellipse({ startAngleDegrees: 180, endAngleDegrees: 270 }), {
        drag: { key: "e:start", start: p(-4, 2), end: p(1, -5) }
    });
    nearPoint(output.majorAxisEndPoint, p(0, 7)); near(output.minorRadiusRatio, 2 / 7);
});
test("partial ellipse arbitrary endpoint grip explicitly refuses with original geometry", () => {
    run(ellipse({ startAngleDegrees: 40, endAngleDegrees: 200 }), {
        drag: { key: "e:start", start: p(1, 2), end: p(3, 4) }
    }, "unsupported");
});
test("equal-axis focus reference explicitly refuses instead of inventing a focal direction", () => {
    run(ellipse({ minorRadiusRatio: 1 }), { fixedReferenceKeys: ["e:focus1"] }, "unsupported");
});
test("whole fixed equal-axis ellipse projects a resize without changing its frame", () => {
    const entity = ellipse({ minorRadiusRatio: 1 });
    const { output } = run(entity, { fixedReferenceKeys: ["e"],
        drag: { key: "e:major-end", start: p(6, 2), end: p(1, 9) } });
    nearPoint(output.center, entity.center); nearPoint(output.majorAxisEndPoint, entity.majorAxisEndPoint);
    near(output.minorRadiusRatio, 1);
});

for (const ratio of [1 - 1e-12, 1 + 1e-12]) {
    test(`near-equal ellipse ratio ${ratio} stays editable without a focal singularity`, () => {
        let entity = ellipse({ minorRadiusRatio: ratio });
        for (let i = 0; i < 4; i++) {
            entity = run(entity, { drag: { key: "e:center", start: p(0, 0), end: p(1, 2) } }).output;
            nearPoint(entity.majorAxisEndPoint, p(5, 0));
            near(entity.minorRadiusRatio, ratio);
            assert.ok(Math.abs(entity.minorRadiusRatio - ratio) < 1e-14);
            assert.notEqual(entity.minorRadiusRatio, 1);
        }
    });
}
function patternDocument(radial, partial = false, mismatch = false) {
    const seed = ellipse(partial ? { startAngleDegrees: 330, endAngleDegrees: 80 } : {});
    const member = { ...structuredClone(seed), id: "member", isGenerated: true,
        center: radial ? p(-2, 1) : p(21, 2), majorAxisEndPoint: radial ? p(0, 5) : p(5, 0) };
    if (mismatch) member.endAngleDegrees = 90;
    return { entities: [seed, member], constraints: [], dimensions: [],
        metadata: { patterns: [{ id: "pattern", kind: radial ? "Radial" : "Rectangular", count: 2,
            fullCircle: false, stepAngleDegrees: 90, center: p(0, 0),
            rows: 1, columns: 2, xPitch: 20, yPitch: 0, angleDegrees: 0,
            sourceEntityIds: ["e"], members: [{ sourceEntityId: "e", entityId: "member", row: 0, column: 1 }] }] } };
}
for (const radial of [false, true]) {
    for (const partial of [false, true]) {
        test(`ellipse axis resize preserves ${radial ? "radial" : "rectangular"} pattern, partial=${partial}`, () => {
            const document = patternDocument(radial, partial);
            const request = { document, drag: { key: "e:major-end", start: p(6, 2), end: p(4, 6) } };
            const before = structuredClone(request), result = solver.solve(request);
            assert.equal(result.status, "solved", JSON.stringify(result.diagnostics));
            assert.deepEqual(request, before);
            const [seed, member] = result.entities;
            nearPoint(p(seed.center.x + seed.majorAxisEndPoint.x, seed.center.y + seed.majorAxisEndPoint.y), p(4, 6));
            nearPoint(member.center, radial ? p(-seed.center.y, seed.center.x) : p(seed.center.x + 20, seed.center.y));
            nearPoint(member.majorAxisEndPoint, radial
                ? p(-seed.majorAxisEndPoint.y, seed.majorAxisEndPoint.x) : seed.majorAxisEndPoint);
            near(member.minorRadiusRatio, seed.minorRadiusRatio);
            assert.equal(member.startAngleDegrees, seed.startAngleDegrees);
            assert.equal(member.endAngleDegrees, seed.endAngleDegrees);
        });
    }
}
test("axis-model pattern refuses inconsistent eccentric settings rather than silently retaining a stale member", () => {
    const document = patternDocument(false, true, true);
    const result = solver.solve({ document, drag: { key: "e:major-end", start: p(6, 2), end: p(4, 6) } });
    assert.equal(result.status, "unsupported", JSON.stringify(result.diagnostics));
    assert.deepEqual(result.entities, document.entities);
});

for (const radial of [false, true]) {
    test(`ellipse ${radial ? "radial" : "rectangular"} pattern respects hard source axes during resize`, () => {
        const document = patternDocument(radial);
        document.dimensions = [axisDimension("major", "major", 10), axisDimension("minor", "minor", 4)];
        const result = solver.solve({ document, fixedReferenceKeys: ["e:center"],
            drag: { key: "e:major-end", start: p(6, 2), end: p(4, 6) } });
        assert.equal(result.status, "solved", JSON.stringify(result.diagnostics));
        const [seed, member] = result.entities;
        nearPoint(seed.center, p(1, 2)); nearPoint(seed.majorAxisEndPoint, p(3, 4));
        near(seed.minorRadiusRatio, 0.4);
        nearPoint(member.majorAxisEndPoint, radial ? p(-4, 3) : p(3, 4));
        near(member.minorRadiusRatio, 0.4);
    });
    test(`locked ellipse ${radial ? "radial" : "rectangular"} member blocks incompatible source dimension`, () => {
        const document = patternDocument(radial);
        document.entities[1].isLocked = true;
        const result = solver.solve({ document, dimensions: [axisDimension("major", "major", 12)],
            attemptedDrivingDimensionIds: ["major"] });
        assert.equal(result.status, "failed", JSON.stringify(result.diagnostics));
        assert.deepEqual(result.entities, document.entities);
    });
}

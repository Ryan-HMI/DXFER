import assert from "node:assert/strict";
import { after, test } from "node:test";
import { createPlaneGcsSolver } from "../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js";

const solver = await createPlaneGcsSolver();
after(() => solver.dispose());
const p = (x, y) => ({ x, y });
const line = { id: "l", kind: "line", points: [p(0, 0), p(5, 0)] };
function polygon(circumscribed = false, sideCount = 5) {
    const center = p(10, 20), radius = 2, rotationAngleDegrees = 20;
    const vertexRadius = circumscribed ? radius / Math.cos(Math.PI / sideCount) : radius;
    const angle = rotationAngleDegrees * Math.PI / 180 + (circumscribed ? Math.PI / sideCount : 0);
    return { id: "poly", kind: "polygon", center, radius, sideCount, circumscribed, rotationAngleDegrees,
        points: Array.from({ length: sideCount }, (_, i) => p(
            center.x + vertexRadius * Math.cos(angle + i * 2 * Math.PI / sideCount),
            center.y + vertexRadius * Math.sin(angle + i * 2 * Math.PI / sideCount))),
        layerName: "CAD", native: { sourceId: "preserve" } };
}
const count = (value = 5) => ({ id: "count", kind: "Count", value, isDriving: true, referenceKeys: ["poly"] });
const length = { id: "len", kind: "LinearDistance", value: 8, isDriving: true, referenceKeys: ["l"] };
function run(entity, dimensions, extra = {}, status = "solved") {
    const request = { document: { entities: [entity, line], constraints: [], dimensions }, ...extra };
    const before = structuredClone(request);
    const result = solver.solve(request);
    assert.equal(result.status, status, JSON.stringify(result.diagnostics));
    assert.deepEqual(request, before);
    assert.deepEqual(result.entities[0], entity);
    if (status !== "solved") assert.deepEqual(result.entities, request.document.entities);
    return result;
}

for (const circumscribed of [false, true]) {
    for (const sideCount of [3, 5, 64]) {
        test(`app-created ${sideCount}-side polygon circumscribed=${circumscribed} cannot block an unrelated line edit`, () => {
            const result = run(polygon(circumscribed, sideCount), [count(sideCount), length],
                { attemptedDrivingDimensionIds: ["len"] });
            const [a, b] = result.entities[1].points;
            assert.ok(Math.abs(Math.hypot(b.x - a.x, b.y - a.y) - 8) < 1e-6);
            assert.deepEqual(result.redundantDrivingDimensionIds, []);
        });
    }
}

test("a newly attached matching Count is validated as topology, not rejected for zero continuous equations", () => {
    const result = run(polygon(), [], { dimensions: [count()], attemptedDrivingDimensionIds: ["count"] });
    assert.deepEqual(result.redundantDrivingDimensionIds, []);
    assert.ok(result.diagnostics.some(message => message.includes("Validated discrete topology dimension count")));
});

test("a Core-staged side-count change validates its new topology without mutating it in the solver", () => {
    const result = run(polygon(false, 6), [], { dimensions: [count(6)], attemptedDrivingDimensionIds: ["count"] });
    assert.equal(result.entities[0].sideCount, 6);
    assert.equal(result.entities[0].points.length, 6);
});

for (const value of [2, 6, 5.5, 65]) {
    test(`unstaged Count=${value} refuses instead of changing discrete topology`, () => {
        const result = run(polygon(), [count()], {
            dimensions: [count(value)], attemptedDrivingDimensionIds: ["count"]
        }, "unsupported");
        assert.ok(result.diagnostics.some(message => message.includes("discrete topology edit path")));
    });
}

test("whole-fixed unrelated polygon with its creation Count stays opaque and unchanged", () => {
    run({ ...polygon(true), isLocked: true }, [count(), length], { fixedReferenceKeys: ["poly"] });
});

test("Count cannot be attached to a non-polygon or missing entity", () => {
    for (const reference of ["l", "missing"])
        run(polygon(), [{ ...count(), referenceKeys: [reference] }], {}, "unsupported");
});

test("polygon arbitrary vertex edits remain explicitly unsupported", () => {
    run(polygon(), [count()], { drag: { key: "poly:vertex-0", start: p(10, 20), end: p(12, 23) } }, "unsupported");
});

test("polygon boundary tangency cannot silently constrain its guide circle", () => {
    run(polygon(), [count()], {
        constraints: [{ id: "tangent", kind: "Tangent", referenceKeys: ["poly", "l"] }]
    }, "unsupported");
});

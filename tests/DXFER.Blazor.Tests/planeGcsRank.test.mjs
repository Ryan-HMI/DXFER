import assert from "node:assert/strict";
import { after, test } from "node:test";
import { createPlaneGcsSolver } from "../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js";

const solver = await createPlaneGcsSolver();
after(() => solver.dispose());
const entities = [{ id: "edge", kind: "line", points: [{ x: 0, y: 0 }, { x: 3, y: 4 }] }];
const dim = (id, kind, value, extra = {}) => ({
    id, kind, value, isDriving: true, referenceKeys: ["edge:start", "edge:end"], ...extra
});
const width = dim("width", "HorizontalDistance", 3);
const height = dim("height", "VerticalDistance", 4);
const diagonal = dim("diagonal", "LinearDistance", 5);
function run(dimensions, updates = [], attempts = []) {
    const request = { document: { entities, constraints: [], dimensions },
        dimensions: updates, attemptedDrivingDimensionIds: attempts };
    const before = structuredClone(request);
    const result = solver.solve(request);
    assert.deepEqual(request, before);
    assert.equal(result.status, "solved", JSON.stringify(result.diagnostics));
    return result;
}
function dof(result) {
    return Number(result.diagnostics.find(value => value.includes("degrees of freedom")).match(/: (\d+)\./)[1]);
}

for (const dimensions of [[width, height, diagonal], [diagonal, width, height], [height, diagonal, width]]) {
    const original = dimensions.slice(0, 2), attempted = dimensions[2];
    test(`native 3/4/5 rank reports appended ${attempted.id} and unchanged rank`, () => {
        const before = run(original);
        const result = run(original, [attempted], [attempted.id]);
        assert.equal(dof(before), 2);
        assert.equal(dof(result), dof(before));
        assert.ok(result.redundantDrivingDimensionIds.includes(attempted.id));
        assert.ok(result.diagnostics.includes(`Native rank check ${attempted.id}: DOF 2, without intent 2.`));
    });
    test(`native 3/4/5 rank attributes an earlier-ID reference conversion to ${attempted.id}`, () => {
        const result = run([{ ...attempted, isDriving: false }, ...original], [attempted], [attempted.id]);
        assert.equal(dof(result), 2);
        assert.ok(result.redundantDrivingDimensionIds.includes(attempted.id),
            JSON.stringify(result.redundantDrivingDimensionIds));
    });
    test(`native 3/4/5 rank attributes a changed earlier driving value to ${attempted.id}`, () => {
        const result = run([{ ...attempted, value: attempted.value + 1 }, ...original], [attempted], [attempted.id]);
        assert.equal(dof(result), 2);
        assert.ok(result.redundantDrivingDimensionIds.includes(attempted.id));
    });
}

test("native rank distinguishes an independent attempted height from original redundant widths", () => {
    const original = [width, { ...width, id: "original-duplicate-width" }];
    const before = run(original);
    const result = run(original, [height], [height.id]);
    assert.equal(dof(before), 3);
    assert.equal(dof(result), 2);
    assert.ok(result.redundantDrivingDimensionIds.some(id => original.some(d => d.id === id)));
    assert.ok(!result.redundantDrivingDimensionIds.includes(height.id));
    assert.ok(result.diagnostics.includes("Native rank check height: DOF 2, without intent 3."));
});

test("an independent earlier-ID driving conversion increases native rank", () => {
    const result = run([{ ...height, isDriving: false }, width], [height], ["height"]);
    assert.deepEqual(result.redundantDrivingDimensionIds, []);
    assert.ok(result.diagnostics.includes("Native rank check height: DOF 2, without intent 3."));
});

test("a batch of dependent driving intents is checked per intent, not just by native basis tags", () => {
    const result = run([], [diagonal, width, height], ["diagonal", "width", "height"]);
    assert.deepEqual(new Set(result.redundantDrivingDimensionIds), new Set(["diagonal", "width", "height"]));
});

test("native 3/4/5 unchanged retry reports existing rank without inventing an attempted intent", () => {
    const result = run([width, height, diagonal], [{ ...diagonal, labelPosition: { x: 8, y: 9 } }]);
    assert.equal(dof(result), 2);
    assert.ok(result.redundantDrivingDimensionIds.every(id => ["width", "height", "diagonal"].includes(id)));
});

test("incompatible attempted hypotenuse preserves original geometry", () => {
    const result = solver.solve({ document: { entities, constraints: [], dimensions: [width, height] },
        dimensions: [{ ...diagonal, value: 6 }], attemptedDrivingDimensionIds: ["diagonal"] });
    assert.equal(result.status, "failed");
    assert.deepEqual(result.entities, entities);
});

test("an independent gap dimension is not rejected for its repeated parallel equation", () => {
    const result = solver.solve({
        document: {
            entities: [
                { id: "a", kind: "line", points: [{ x: 0, y: 0 }, { x: 5, y: 0 }] },
                { id: "b", kind: "line", points: [{ x: 0, y: 3 }, { x: 5, y: 3 }] }
            ],
            constraints: [
                { id: "fixed", kind: "Fix", referenceKeys: ["a"] },
                { id: "parallel", kind: "Parallel", referenceKeys: ["a", "b"] }
            ],
            dimensions: []
        },
        dimensions: [{ id: "gap", kind: "PointToLineDistance", value: 3, isDriving: true, referenceKeys: ["a", "b"] }],
        attemptedDrivingDimensionIds: ["gap"]
    });
    assert.equal(result.status, "solved", JSON.stringify(result.diagnostics));
    assert.ok(!result.redundantDrivingDimensionIds.includes("gap"), JSON.stringify(result.diagnostics));
    assert.ok(result.diagnostics.includes("Native rank check gap: DOF 2, without intent 3."));
});

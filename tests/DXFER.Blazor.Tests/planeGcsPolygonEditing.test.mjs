import assert from "node:assert/strict";
import { after, test } from "node:test";
import { createPlaneGcsSolver } from "../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js";

const solver = await createPlaneGcsSolver();
after(() => solver.dispose());
const p = (x, y) => ({ x, y });
const near = (a, b) => assert.ok(Math.abs(a - b) < 2e-5, `${a} != ${b}`);
function polygon(circumscribed, sideCount = 5) {
    const radius = 2, center = p(10, 20), rotationAngleDegrees = 20;
    const phase = 20 * Math.PI / 180 + (circumscribed ? Math.PI / sideCount : 0);
    const r = circumscribed ? radius / Math.cos(Math.PI / sideCount) : radius;
    return { id: "poly", kind: "polygon", center, radius, rotationAngleDegrees, sideCount, circumscribed,
        layerName: "CUT", native: { sourceId: "unchanged" },
        points: Array.from({ length: sideCount }, (_, i) => p(
            center.x + r * Math.cos(phase + i * 2 * Math.PI / sideCount),
            center.y + r * Math.sin(phase + i * 2 * Math.PI / sideCount))) };
}
const dimension = (value, kind = "Radius") => ({
    id: "r", kind, value, isDriving: true, referenceKeys: ["poly"]
});
function solve(entity, extra = {}, dimensions = []) {
    const request = { document: { entities: [entity], constraints: [], dimensions: [
        { id: "count", kind: "Count", value: entity.sideCount, isDriving: true, referenceKeys: ["poly"] },
        ...dimensions
    ] }, ...extra };
    const before = structuredClone(request);
    const result = solver.solve(request);
    assert.equal(result.status, "solved", JSON.stringify(result.diagnostics));
    assert.deepEqual(request, before);
    const output = result.entities[0];
    assert.equal(output.kind, entity.kind);
    assert.equal(output.sideCount, entity.sideCount);
    assert.equal(output.circumscribed, entity.circumscribed);
    assert.equal(output.layerName, entity.layerName);
    assert.deepEqual(output.native, entity.native);
    const vertexRadius = output.circumscribed ? output.radius / Math.cos(Math.PI / output.sideCount) : output.radius;
    const phase = output.rotationAngleDegrees * Math.PI / 180 + (output.circumscribed ? Math.PI / output.sideCount : 0);
    output.points.forEach((point, i) => {
        near(point.x, output.center.x + vertexRadius * Math.cos(phase + i * 2 * Math.PI / output.sideCount));
        near(point.y, output.center.y + vertexRadius * Math.sin(phase + i * 2 * Math.PI / output.sideCount));
    });
    return { output, result };
}
for (const circumscribed of [false, true]) {
    for (const key of ["poly", "poly:center", "poly:mid-2", "poly|point|center|10|20"]) {
        test(`polygon ${key} translates with off-center grab, circumscribed=${circumscribed}`, () => {
            const { output, result } = solve(polygon(circumscribed), {
                drag: { key, start: p(10.2, 20.1), end: p(13.2, 24.1) }
            });
            near(output.center.x, 13); near(output.center.y, 24);
            near(output.radius, 2); near(output.rotationAngleDegrees, 20);
            assert.ok(result.diagnostics.includes("PlaneGCS mapped-system degrees of freedom: 4."));
        });
    }
    for (const kind of ["Radius", "Diameter"]) {
        test(`polygon ${kind} edits its guide/apothem, circumscribed=${circumscribed}`, () => {
            const { output, result } = solve(polygon(circumscribed), {
                dimensions: [dimension(kind === "Radius" ? 3 : 6, kind)],
                fixedReferenceKeys: ["poly:center"], attemptedDrivingDimensionIds: ["r"]
            });
            near(output.radius, 3); near(output.rotationAngleDegrees, 20);
            near(output.center.x, 10); near(output.center.y, 20);
            assert.deepEqual(result.redundantDrivingDimensionIds, []);
        });
    }
    test(`polygon driving guide radius survives body dragging, circumscribed=${circumscribed}`, () => {
        const { output } = solve(polygon(circumscribed), {
            drag: { key: "poly", start: p(10, 20), end: p(13, 24) }
        }, [dimension(2)]);
        near(output.radius, 2); near(output.center.x, 13); near(output.center.y, 24);
    });
    test(`whole fixed polygon projects translation to original geometry, circumscribed=${circumscribed}`, () => {
        const { output } = solve(polygon(circumscribed), {
            fixedReferenceKeys: ["poly"], drag: { key: "poly:center", start: p(10, 20), end: p(13, 24) }
        });
        near(output.center.x, 10); near(output.center.y, 20);
        near(output.radius, 2); near(output.rotationAngleDegrees, 20);
    });
    test(`center-only fixed polygon does not stretch toward the translated guide target, circumscribed=${circumscribed}`, () => {
        const { output } = solve(polygon(circumscribed), {
            fixedReferenceKeys: ["poly:center"], drag: { key: "poly", start: p(10, 20), end: p(13, 24) }
        });
        near(output.center.x, 10); near(output.center.y, 20);
        assert.ok(Math.abs(output.radius - 2) < 1e-6);
        assert.ok(Math.abs(output.rotationAngleDegrees - 20) < 1e-6);
    });
    test(`translation shape goals cannot override a changed driving radius, circumscribed=${circumscribed}`, () => {
        const { output } = solve(polygon(circumscribed), {
            dimensions: [dimension(3)], drag: { key: "poly", start: p(10, 20), end: p(13, 24) }
        });
        near(output.center.x, 13); near(output.center.y, 24);
        near(output.radius, 3); near(output.rotationAngleDegrees, 20);
    });
    test(`axis-projected polygon translation preserves its relative guide, circumscribed=${circumscribed}`, () => {
        const entity = polygon(circumscribed);
        entity.center.y = 0;
        entity.points = entity.points.map(point => ({ ...point, y: point.y - 20 }));
        const { output } = solve(entity, {
            constraints: [{ id: "axis", kind: "Horizontal", referenceKeys: ["poly:center", "$origin:center"] }],
            drag: { key: "poly", start: p(10, 0), end: p(13, 4) }
        });
        near(output.center.x, 13); near(output.center.y, 0);
        assert.ok(Math.abs(output.radius - 2) < 1e-6);
        assert.ok(Math.abs(output.rotationAngleDegrees - 20) < 1e-6);
    });
    test(`polygon dimensions remain stable across repeated snapshots, circumscribed=${circumscribed}`, () => {
        let entity = polygon(circumscribed, 64);
        for (const radius of [3, 5, 2]) {
            entity = solve(entity, { fixedReferenceKeys: ["poly:center"],
                dimensions: [dimension(radius)], attemptedDrivingDimensionIds: ["r"] }).output;
            near(entity.radius, radius); near(entity.rotationAngleDegrees, 20);
        }
    });
}

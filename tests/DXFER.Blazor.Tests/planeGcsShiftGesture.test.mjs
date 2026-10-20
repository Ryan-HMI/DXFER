import assert from "node:assert/strict";
import { after, test } from "node:test";
import { createPlaneGcsSolver } from "../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js";

const solver = await createPlaneGcsSolver();
after(() => solver.dispose());
const p = (x, y) => ({ x, y });
const near = (a, b) => assert.ok(Number.isFinite(a) && Math.abs(a - b) < 1e-6, `${a} != ${b}`);
const nearPoint = (a, b) => { near(a.x, b.x); near(a.y, b.y); };
const c = (id, kind, ...referenceKeys) => ({ id, kind, referenceKeys });
const line = { id: "l", kind: "line", points: [p(0, 0), p(3, 4)] };
const arc = { id: "a", kind: "arc", center: p(0, 0), radius: 3, points: [],
    startAngleDegrees: 0, endAngleDegrees: 90 };
function run(entities, drag, constraints = [], dimensions = []) {
    const request = { document: { entities, constraints, dimensions }, drag: { ...drag, constrain: true } };
    const before = structuredClone(request);
    const result = solver.solve(request);
    assert.equal(result.status, "solved", JSON.stringify(result.diagnostics));
    assert.deepEqual(request, before);
    return result;
}

test("Shift endpoint uses the original line vector, not the dominant world axis", () => {
    const result = run([line], { key: "l:end", start: p(3, 4), end: p(8, 6) },
        [c("fix", "Fix", "l:start")]);
    nearPoint(result.entities[0].points[0], p(0, 0));
    nearPoint(result.entities[0].points[1], p(5.76, 7.68));
});

test("Shift off-center endpoint grab projects the absolute pointer as the existing app does", () => {
    const result = run([line], { key: "l|point|end|300|400", start: p(3.1, 4.2), end: p(8, 6) },
        [c("fix", "Fix", "l:start")]);
    nearPoint(result.entities[0].points[1], p(5.76, 7.68));
});

test("Shift start endpoint projects onto the original infinite line", () => {
    const result = run([line], { key: "l:start", start: p(0, 0), end: p(-8, -6) },
        [c("fix", "Fix", "l:end")]);
    nearPoint(result.entities[0].points[0], p(-5.76, -7.68));
    nearPoint(result.entities[0].points[1], p(3, 4));
});

test("hard driving length overrides a Shift endpoint target without a temporary length constraint", () => {
    const result = run([line], { key: "l:end", start: p(3, 4), end: p(8, 6) },
        [c("fix", "Fix", "l:start")],
        [{ id: "len", kind: "LinearDistance", value: 5, isDriving: true, referenceKeys: ["l"] }]);
    nearPoint(result.entities[0].points[1], p(3, 4));
});

test("a hard Horizontal relation takes precedence over the projected Shift direction", () => {
    const result = run([line], { key: "l:end", start: p(3, 4), end: p(8, 6) },
        [c("fix", "Fix", "l:start"), c("axis", "Horizontal", "l")]);
    near(result.entities[0].points[1].y, 0);
    near(result.entities[0].points[1].x, 5.76);
});

test("Shift whole-line drag remains ordinary translation", () => {
    const result = run([line], { key: "l", start: p(1, 2), end: p(3, 5) });
    nearPoint(result.entities[0].points[0], p(2, 3));
    nearPoint(result.entities[0].points[1], p(5, 7));
});

test("Shift arc endpoint changes sweep using the original-radius pointer projection", () => {
    const result = run([arc], { key: "a:end", start: p(0.1, 3.1), end: p(3, 4) },
        [c("fix", "Fix", "a:center")]).entities[0];
    nearPoint(result.center, p(0, 0));
    near(result.radius, 3);
    near(result.startAngleDegrees, 0);
    near(result.endAngleDegrees, Math.atan2(4, 3) * 180 / Math.PI);
});

test("Shift arc projection cannot override a driving radius with an inherited original radius", () => {
    const result = run([arc], { key: "a:end", start: p(0, 3), end: p(3, 4) },
        [c("fix", "Fix", "a:center")],
        [{ id: "radius", kind: "Radius", value: 5, isDriving: true, referenceKeys: ["a"] }]).entities[0];
    near(result.radius, 5);
    near(result.endAngleDegrees, Math.atan2(4, 3) * 180 / Math.PI);
});

test("Shift circle center and point drags keep their normal delta semantics", () => {
    const circle = { id: "c", kind: "circle", center: p(1, 2), radius: 3, points: [] };
    const moved = run([circle], { key: "c:center", start: p(1.1, 2.2), end: p(3.1, 5.2) });
    nearPoint(moved.entities[0].center, p(3, 5));
    const point = { id: "p", kind: "point", points: [p(1, 2)] };
    const result = run([point], { key: "p:center", start: p(1.1, 2.2), end: p(3.1, 5.2) });
    nearPoint(result.entities[0].points[0], p(3, 5));
});

test("Shift arc radius grip remains a radius edit, not endpoint sweep projection", () => {
    const result = run([arc], { key: "a:perimeter", start: p(3, 0), end: p(3, 4) }).entities[0];
    nearPoint(result.center, p(0, 0));
    near(result.radius, 5);
});

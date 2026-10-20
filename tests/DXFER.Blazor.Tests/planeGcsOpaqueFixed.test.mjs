import assert from "node:assert/strict";
import { after, test } from "node:test";
import { createPlaneGcsSolver } from "../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js";

const solver = await createPlaneGcsSolver();
after(() => solver.dispose());
const p = (x, y) => ({ x, y });
const line = { id: "l", kind: "line", points: [p(0, 0), p(5, 0)] };
const opaque = { id: "opaque", kind: "unsupported-cad-native", native: { source: "preserve", values: [1, 2] } };
const fix = (...referenceKeys) => ({ id: "fix", kind: "Fix", referenceKeys });
const length = { id: "length", kind: "LinearDistance", referenceKeys: ["l"], value: 8, isDriving: true };
function run(entities, constraints = [], dimensions = [], extra = {}, status = "solved") {
    const request = { document: { entities, constraints, dimensions }, ...extra };
    const before = structuredClone(request);
    const result = solver.solve(request);
    assert.equal(result.status, status, JSON.stringify(result.diagnostics));
    assert.deepEqual(request, before);
    if (status !== "solved") assert.deepEqual(result.entities, entities);
    return result;
}
function movedLine(result) {
    const [a, b] = result.entities.find(e => e.id === "l").points;
    assert.ok(Math.abs(Math.hypot(b.x - a.x, b.y - a.y) - 8) < 1e-6);
}

for (const mode of ["whole-key", "Fix", "locked", "hidden"]) {
    test(`unreferenced ${mode} opaque geometry remains exact while another entity changes`, () => {
        const source = { ...opaque, ...(mode === "locked" ? { isLocked: true } : {}),
            ...(mode === "hidden" ? { isHidden: true } : {}) };
        const result = run([source, line], mode === "Fix" ? [fix("opaque")] : [], [length],
            mode === "whole-key" ? { fixedReferenceKeys: ["opaque"] } : {});
        assert.deepEqual(result.entities[0], source);
        movedLine(result);
    });
}

test("an unreferenced invalid curve representation can remain whole-entity fixed and opaque", () => {
    const source = { id: "e", kind: "ellipse", center: p(0, 0), majorAxisEndPoint: p(3, 0),
        minorRadiusRatio: 1, isLocked: true };
    const result = run([source, line], [fix("e")], [length], { fixedReferenceKeys: ["e"] });
    assert.deepEqual(result.entities[0], source);
    movedLine(result);
});

for (const intent of [
    { name: "point Fix", constraints: [fix("opaque:start")] },
    { name: "point fixed key", fixedReferenceKeys: ["opaque:start"] },
    { name: "relation", constraints: [{ id: "coupled", kind: "Coincident", referenceKeys: ["opaque:start", "l:start"] }] },
    { name: "dimension", dimensions: [{ ...length, referenceKeys: ["opaque"] }] },
    { name: "drag", drag: { key: "opaque", start: p(0, 0), end: p(1, 2) } },
    { name: "guess", initialGuesses: [{ referenceKey: "opaque:start", point: p(1, 2) }] }
]) {
    test(`an actual ${intent.name} involving opaque fixed geometry still refuses explicitly`, () => {
        const { constraints = [], dimensions = [], name, ...extra } = intent;
        run([{ ...opaque, isLocked: true }, line], [fix("opaque"), ...constraints], dimensions,
            { fixedReferenceKeys: ["opaque"], ...extra }, "unsupported");
    });
}

for (const order of ["before", "after"]) {
    test(`whole Fix ${order} model discovery still freezes involved geometry`, () => {
        const axis = { id: "axis", kind: "Horizontal", referenceKeys: ["l"] };
        run([line], order === "before" ? [fix("l"), axis] : [axis, fix("l")], [length], {}, "failed");
    });
}

test("a deferred whole fixed key discovered only by dragging remains fixed", () => {
    const result = run([line], [], [], {
        fixedReferenceKeys: ["l"], drag: { key: "l:end", start: p(5, 0), end: p(8, 3) }
    });
    assert.deepEqual(result.entities[0], line);
});

test("unknown whole Fix identities are not silently accepted", () => {
    run([line], [fix("missing")], [], {}, "unsupported");
    run([line], [], [], { fixedReferenceKeys: ["missing"] }, "unsupported");
});

test("large unrelated fixed CAD background adds no native unknowns", () => {
    const background = Array.from({ length: 1000 }, (_, i) => ({ ...opaque, id: `cad-${i}`, isHidden: true }));
    const baseline = run([line], [], [length]);
    const result = run([...background, line], [], [length], { fixedReferenceKeys: background.map(e => e.id) });
    assert.deepEqual(result.entities.slice(0, background.length), background);
    assert.deepEqual(result.diagnostics, baseline.diagnostics);
    movedLine(result);
});

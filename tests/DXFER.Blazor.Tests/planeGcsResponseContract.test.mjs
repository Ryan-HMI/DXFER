import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { after, test } from "node:test";
import { createPlaneGcsSolver } from "../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js";

const solver = await createPlaneGcsSolver();
after(() => solver.dispose());
const modulePath = fileURLToPath(new URL("../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js", import.meta.url));
const hostPath = fileURLToPath(new URL("../../src/DXFER.Web/Solver/planeGcsHost.mjs", import.meta.url));
const dimension = value => ({
    id: "length", kind: "LinearDistance", value, isDriving: true, referenceKeys: ["line"]
});
const request = () => ({
    document: {
        entities: [{ id: "line", kind: "line", points: [{ x: 0, y: 0 }, { x: 5, y: 0 }] }],
        constraints: [
            { id: "horizontal", kind: "Horizontal", referenceKeys: ["line"] },
            { id: "anchor", kind: "Fix", referenceKeys: ["line:start"] }
        ],
        dimensions: [dimension(5)]
    },
    dimensions: [dimension(9)],
    attemptedDrivingDimensionIds: ["length"]
});

function wireResult(result, status) {
    const wire = JSON.parse(JSON.stringify(result));
    assert.equal(wire.status, status, JSON.stringify(wire.diagnostics));
    assert.equal(wire.engine, "PlaneGCS");
    assert.ok(Object.hasOwn(wire, "redundantDrivingDimensionIds"));
    assert.ok(Array.isArray(wire.redundantDrivingDimensionIds));
    assert.ok(wire.redundantDrivingDimensionIds.every(id => typeof id === "string"));
    return wire;
}

test("driving-length update emits an explicit empty redundancy array through the real Node host", () => {
    const child = spawnSync(process.execPath, [hostPath, modulePath], {
        input: `${JSON.stringify({ id: 1, request: request() })}\n`,
        encoding: "utf8",
        timeout: 30000
    });
    assert.equal(child.status, 0, child.stderr || String(child.error));
    const response = JSON.parse(child.stdout.trim());
    assert.equal(response.id, 1);
    const result = wireResult(response.result, "solved");
    assert.deepEqual(result.redundantDrivingDimensionIds, []);
    const [start, end] = result.entities[0].points;
    assert.ok(Math.abs(Math.hypot(end.x - start.x, end.y - start.y) - 9) < 1e-6);
});

test("unsupported and conflicting requests retain the redundancy response field", () => {
    const unsupported = request();
    unsupported.constraints = [{ id: "unknown", kind: "Unknown", referenceKeys: ["line"] }];
    wireResult(solver.solve(unsupported), "unsupported");
    const conflict = request();
    conflict.constraints = [{ id: "freeze", kind: "Fix", referenceKeys: ["line"] }];
    wireResult(solver.solve(conflict), "failed");
});

test("disposed solver retains the redundancy response field", async () => {
    const disposed = await createPlaneGcsSolver();
    disposed.dispose();
    assert.deepEqual(wireResult(disposed.solve(request()), "failed").redundantDrivingDimensionIds, []);
});

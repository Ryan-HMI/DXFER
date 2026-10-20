import assert from "node:assert/strict";
import { after, test } from "node:test";
import { createPlaneGcsSolver } from "../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js";

const solver = await createPlaneGcsSolver();
after(() => solver.dispose());
const p = (x, y) => ({ x, y });
const point = (id, x, y) => ({ id, kind: "point", points: [p(x, y)] });
const line = (id, a = p(0, 0), b = p(5, 0)) => ({ id, kind: "line", points: [a, b] });
const circle = (id, center = p(0, 0), radius = 2) => ({ id, kind: "circle", points: [], center, radius });
const arc = (id, center = p(0, 0), radius = 2, startAngleDegrees = 0, endAngleDegrees = 90) =>
    ({ ...circle(id, center, radius), kind: "arc", startAngleDegrees, endAngleDegrees });
const c = (id, kind, ...referenceKeys) => ({ id, kind, referenceKeys });
const d = (id, kind, value, ...referenceKeys) => ({ id, kind, value, referenceKeys, isDriving: true });
const near = (a, b) => assert.ok(Number.isFinite(a) && Math.abs(a - b) <= 1e-6, `${a} != ${b}`);
const nearPoint = (a, b) => { near(a.x, b.x); near(a.y, b.y); };
const get = (result, id) => result.entities.find(entity => entity.id === id);
function run(entities, constraints = [], dimensions = [], updates = {}) {
    const request = { document: { entities, constraints, dimensions }, ...updates };
    const before = structuredClone(request);
    const result = solver.solve(request);
    assert.equal(typeof result.then, "undefined", "solve must remain synchronous");
    assert.deepEqual(request, before, "request must remain unchanged");
    assert.equal(result.engine, "PlaneGCS");
    assert.ok(Array.isArray(result.redundantDrivingDimensionIds));
    if (result.status !== "solved") assert.deepEqual(result.entities, entities, "failure must restore the exact input");
    return result;
}
function solved(result) {
    assert.equal(result.status, "solved", JSON.stringify(result.diagnostics));
    return result;
}

test("entity metadata and untouched unsupported geometry survive an empty solve", () => {
    const entities = [{ ...point("p", 2, 3), layerName: "STOCK", native: { sourceId: "42" } },
        { id: "s", kind: "spline", controlPoints: [p(0, 0), p(3, 4)], splineDegree: 1 }];
    assert.deepEqual(solved(run(entities)).entities, entities);
});

test("constraint and dimension updates replace by ID without dropping existing intent", () => {
    const result = solved(run([line("a", p(0, 0), p(3, 4))],
        [c("axis", "Vertical", "a"), c("fix", "Fix", "a:start")],
        [d("length", "LinearDistance", 5, "a:start", "a:end")],
        { constraints: [c("axis", "Horizontal", "a")], dimensions: [d("length", "LinearDistance", 9, "a")] }));
    nearPoint(get(result, "a").points[0], p(0, 0));
    nearPoint(get(result, "a").points[1], p(9, 0));
});

test("origin and moving point aliases use identities rather than stale canvas coordinates", () => {
    const result = solved(run([point("p", 3, 4), point("q", 8, 8)],
        [c("at-origin", "Coincident", "p:center", "$origin:center"),
            c("coincident", "Coincident", "p|point|center|300|400", "q:center")]));
    nearPoint(get(result, "p").points[0], p(0, 0));
    nearPoint(get(result, "q").points[0], p(0, 0));
});

test("three-point midpoint uses the first reference as the middle point", () => {
    const result = solved(run([point("m", 1, 1), point("a", -2, 0), point("b", 4, 6)],
        [c("m", "Midpoint", "m", "a", "b")], [], { fixedReferenceKeys: ["a", "b"] }));
    nearPoint(get(result, "m").points[0], p(1, 3));
});

test("derived midpoint references are coupled to their line endpoints", () => {
    const result = solved(run([line("a", p(0, 0), p(10, 0))],
        [c("mid", "Coincident", "a:mid", "$origin:center")], [d("len", "LinearDistance", 10, "a")]));
    const [a, b] = get(result, "a").points;
    near(a.x + b.x, 0);
    near(a.y + b.y, 0);
    near(Math.hypot(a.x - b.x, a.y - b.y), 10);
});

test("closed polyline shares vertices across segment and canvas aliases", () => {
    const poly = { id: "poly", kind: "polyline", points: [p(0, 0), p(4, 1), p(4, 4), p(0, 0)], layerName: "CUT" };
    const result = solved(run([poly], [c("h", "Horizontal", "poly|segment|0")], [],
        { fixedReferenceKeys: ["poly|point|vertex-0|100|100"], drag: {
            key: "poly|segment|0:end", start: p(4, 1), end: p(6, 0), constrain: false
        } }));
    const actual = get(result, "poly");
    nearPoint(actual.points[0], p(0, 0));
    nearPoint(actual.points.at(-1), p(0, 0));
    nearPoint(actual.points[1], p(6, 0));
    assert.equal(actual.layerName, "CUT");
});

test("point on line and collinear relations are solved together", () => {
    const result = solved(run([line("a"), line("b", p(1, 3), p(4, 4)), point("p", 2, 8)],
        [c("f", "Fix", "a"), c("collinear", "Collinear", "a", "b"), c("on", "PointOnLine", "p", "b")]));
    near(get(result, "p").points[0].y, 0);
    for (const at of get(result, "b").points) near(at.y, 0);
});

test("perpendicular and equal length remain simultaneous", () => {
    const result = solved(run([line("a"), line("b", p(2, 0), p(4, 3))],
        [c("f", "Fix", "a", "b:start"), c("perp", "Perpendicular", "a", "b"), c("eq", "Equal", "a", "b")]));
    nearPoint(get(result, "b").points[1], p(2, 5));
});

test("circle and arc radii can be equal with concentric centers", () => {
    const result = solved(run([circle("c"), arc("a", p(3, 4), 3)],
        [c("fix", "Fix", "c"), c("eq", "Equal", "a", "c"), c("con", "Concentric", "a", "c")]));
    nearPoint(get(result, "a").center, p(0, 0));
    near(get(result, "a").radius, 2);
});

test("concentric origin fixes center without fixing radius", () => {
    const result = solved(run([circle("c", p(2, 3))], [c("con", "Concentric", "$origin:center", "c")],
        [d("radius", "Radius", 5, "c")]));
    nearPoint(get(result, "c").center, p(0, 0));
    near(get(result, "c").radius, 5);
});

test("point on arc uses its real radius and center", () => {
    const result = solved(run([arc("a"), point("p", 3, 3)], [c("fix", "Fix", "a"), c("on", "PointOnCircle", "p", "a")]));
    const at = get(result, "p").points[0];
    near(Math.hypot(at.x, at.y), 2);
});

test("arc endpoint constraints are tied to angular and radius parameters", () => {
    const result = solved(run([arc("a")],
        [c("center", "Fix", "a:center")],
        [d("diameter", "Diameter", 8, "a"), d("sweep", "Angle", 60, "a")],
        { drag: { key: "a:start", start: p(2, 0), end: p(0, 4) } }));
    const a = get(result, "a");
    near(a.radius, 4);
    near(a.startAngleDegrees, 90);
    near(a.endAngleDegrees - a.startAngleDegrees, 60);
});

for (const y of [-4, 4]) {
    test(`point-to-line distance preserves side ${y}`, () => {
        const result = solved(run([line("a"), point("p", 2, y)], [c("f", "Fix", "a")],
            [d("distance", "PointToLineDistance", 2, "a", "p")]));
        near(get(result, "p").points[0].y, Math.sign(y) * 2);
    });
}

test("line-to-line distance keeps lines parallel", () => {
    const result = solved(run([line("a"), line("b", p(0, 4), p(5, 4))], [c("f", "Fix", "a")],
        [d("distance", "PointToLineDistance", 2, "b", "a")]));
    for (const at of get(result, "b").points) near(at.y, 2);
});

for (const [kind, index] of [["HorizontalDistance", "x"], ["VerticalDistance", "y"]]) {
    test(`${kind} preserves its negative coordinate branch`, () => {
        const result = solved(run([point("a", 0, 0), point("b", -4, -3)], [c("fix", "Fix", "a")],
            [d("distance", kind, 7, "a", "b")]));
        near(get(result, "b").points[0][index], -7);
    });
}

test("line angle uses degree input", () => {
    const result = solved(run([line("a"), line("b", p(0, 0), p(3, 4))], [c("fix", "Fix", "a", "b:start")],
        [d("angle", "Angle", 30, "a", "b"), d("length", "LinearDistance", 5, "b")]));
    const at = get(result, "b").points[1];
    near(Math.atan2(at.y, at.x) * 180 / Math.PI, 30);
});

test("driving diameter projects center drag and preserves tangent and fixed geometry", () => {
    const result = solved(run([line("l", p(-5, 3), p(5, 3)), circle("c", p(0, 0), 3)],
        [c("fix", "Fix", "l"), c("tan", "Tangent", "c", "l")], [d("diam", "Diameter", 6, "c")],
        { drag: { key: "c:center", start: p(0, 0), end: p(2, 1) } }));
    nearPoint(get(result, "c").center, p(2, 0));
    near(get(result, "c").radius, 3);
});

test("off-center center-handle grab uses delta and leaves tangent radius free", () => {
    const result = solved(run([line("l", p(-5, 3), p(5, 3)), circle("c", p(0, 0), 3)],
        [c("fix", "Fix", "l"), c("tan", "Tangent", "c", "l")], [],
        { drag: { key: "c|point|center|0|0", start: p(0.2, -0.1), end: p(0.2, 0.9) } }));
    nearPoint(get(result, "c").center, p(0, 1));
    near(get(result, "c").radius, 2);
});

for (const key of ["c", "c|point|quadrant-0|2|0", "c|point|perimeter|2|0", "c:radius"]) {
    test(`circle radius grip ${key} resizes without translating its center`, () => {
        const result = solved(run([circle("c")], [], [],
            { drag: { key, start: p(2.2, 0.1), end: p(3, 4) } }));
        nearPoint(get(result, "c").center, p(0, 0));
        near(get(result, "c").radius, 5);
    });
}

for (const key of ["a", "a|point|mid|1.414|1.414", "a|point|quadrant-90|0|2", "a:perimeter"]) {
    test(`arc radius grip ${key} preserves center and endpoint angles`, () => {
        const result = solved(run([arc("a")], [], [],
            { drag: { key, start: p(2, 0), end: p(3, 4) } }));
        const actual = get(result, "a");
        nearPoint(actual.center, p(0, 0));
        near(actual.radius, 5);
        near(actual.startAngleDegrees, 0);
        near(actual.endAngleDegrees, 90);
    });
}

for (const grip of ["start", "end"]) {
    test(`arc ${grip} grip uses absolute pointer and preserves the peer angle`, () => {
        const result = solved(run([arc("a")], [], [], {
            drag: { key: `a|point|${grip}|100|100`, start: p(0.25, 0.1), end: p(3, 4) }
        }));
        const actual = get(result, "a"), angle = Math.atan2(4, 3) / (Math.PI / 180);
        nearPoint(actual.center, p(0, 0));
        near(actual.radius, 5);
        near(actual.startAngleDegrees, grip === "start" ? angle : 0);
        near(actual.endAngleDegrees, grip === "end" ? angle : 90);
    });
}

test("free arc center grip translates the arc without changing its radius or sweep", () => {
    const result = solved(run([arc("a", p(2, 3), 4, 350, 70)], [], [], {
        drag: { key: "a:center", start: p(2.2, 3.1), end: p(7.2, 5.1) }
    }));
    const actual = get(result, "a");
    nearPoint(actual.center, p(7, 5));
    near(actual.radius, 4);
    near(actual.startAngleDegrees, 350);
    near(actual.endAngleDegrees, 70);
});

test("radius grip cannot override a driving diameter or move a fixed center", () => {
    const result = solved(run([circle("c")], [c("fix", "Fix", "c:center")],
        [d("diam", "Diameter", 4, "c")],
        { drag: { key: "c", start: p(2, 0), end: p(3, 4) } }));
    nearPoint(get(result, "c").center, p(0, 0));
    near(get(result, "c").radius, 2);
});

test("arc endpoint target projects onto a hard radius without inventing a fixed peer endpoint", () => {
    const result = solved(run([arc("a")], [c("fix", "Fix", "a:center")],
        [d("radius", "Radius", 2, "a")],
        { drag: { key: "a:end", start: p(0.1, 2.1), end: p(3, 4) } }));
    const actual = get(result, "a");
    nearPoint(actual.center, p(0, 0));
    near(actual.radius, 2);
    near(actual.startAngleDegrees, 0);
    near(actual.endAngleDegrees, Math.atan2(4, 3) / (Math.PI / 180));
});

for (const key of ["c", "c:radius", "c:perimeter", "c|point|quadrant-270|0|-3"]) {
    test(`tangent circle ${key} grip seeks pointer contact with a free center and radius`, () => {
        const fixedLine = line("l", p(-10, 3), p(10, 3));
        const constraints = [c("fix", "Fix", "l"), c("tan", "Tangent", "l", "c")];
        const target = p(0, -5);
        const result = solved(run([fixedLine, circle("c", p(0, 0), 3)], constraints, [],
            { drag: { key, start: p(0, -3), end: target } }));
        const actual = get(result, "c");
        near(Math.hypot(target.x - actual.center.x, target.y - actual.center.y), actual.radius);
        near(Math.abs(3 - actual.center.y), actual.radius);
        assert.ok(Math.hypot(actual.center.x, actual.center.y) > 1e-3, "center must remain free");
        assert.ok(actual.radius > 3, "free radius must follow the outward gesture");
        assert.deepEqual(get(result, "l"), fixedLine);
        assert.equal(result.entities.length, 2, "the transient pick must not become a document entity");

        const next = solved(run(result.entities, constraints, [d("diameter", "Diameter", 6, "c")]));
        near(get(next, "c").radius, 3);
        near(Math.abs(3 - get(next, "c").center.y), 3);
        assert.deepEqual(get(next, "l"), fixedLine);
    });
}

test("tangent arc center drag keeps radius free despite radius and angle stability preferences", () => {
    const result = solved(run([line("l", p(-10, 3), p(10, 3)), arc("a", p(0, 0), 3)],
        [c("fix", "Fix", "l"), c("tan", "Tangent", "a", "l")], [],
        { drag: { key: "a:center", start: p(0.2, 0.1), end: p(0.2, 1.1) } }));
    nearPoint(get(result, "a").center, p(0, 1));
    near(get(result, "a").radius, 2);
    assert.deepEqual(get(result, "l"), line("l", p(-10, 3), p(10, 3)));
});

test("a known radius grip is not silently accepted as a persistent point constraint", () => {
    assert.equal(run([circle("c"), point("p", 3, 4)],
        [c("bad", "Coincident", "c|point|quadrant-0|2|0", "p")]).status, "unsupported");
});

test("unknown curve grip still refuses explicitly", () => {
    assert.equal(run([circle("c")], [], [], {
        drag: { key: "c|point|unknown|2|0", start: p(2, 0), end: p(3, 4) }
    }).status, "unsupported");
});

test("off-locus endpoint drag projects onto driving length without moving fixed start", () => {
    const result = solved(run([line("l", p(0, 0), p(10, 0))], [c("fix", "Fix", "l:start")],
        [d("len", "LinearDistance", 10, "l")],
        { drag: { key: "l:end", start: p(10, 0), end: p(12, 16) } }));
    nearPoint(get(result, "l").points[0], p(0, 0));
    nearPoint(get(result, "l").points[1], p(6, 8));
});

test("compatible redundant driving dimensions expose native redundancy and rank", () => {
    const result = solved(run([line("l")], [c("fix", "Fix", "l:start"), c("h", "Horizontal", "l")],
        [d("a", "LinearDistance", 5, "l"), d("b", "HorizontalDistance", 5, "l:start", "l:end")]));
    assert.ok(result.diagnostics.some(message => /degrees of freedom: 0/.test(message)), result.diagnostics.join("\n"));
    assert.ok(result.diagnostics.some(message => /redundant equation.*Dimension/i.test(message)), result.diagnostics.join("\n"));
    assert.ok(result.redundantDrivingDimensionIds.length > 0);
    assert.ok(result.redundantDrivingDimensionIds.every(id => ["a", "b"].includes(id)));
    nearPoint(get(result, "l").points[1], p(5, 0));
});

test("independent driving dimension reports an explicit empty native redundancy array", () => {
    const result = solved(run([line("l")], [c("fix", "Fix", "l:start"), c("h", "Horizontal", "l")],
        [d("length", "LinearDistance", 7, "l")]));
    assert.deepEqual(result.redundantDrivingDimensionIds, []);
});

test("incompatible redundant driving dimensions fail with original geometry", () => {
    const result = run([line("l")], [c("fix", "Fix", "l:start"), c("h", "Horizontal", "l")],
        [d("a", "LinearDistance", 5, "l"), d("b", "HorizontalDistance", 6, "l:start", "l:end")]);
    assert.equal(result.status, "failed");
});

test("duplicate Fix aliases and compatible geometric relations do not trigger blanket rejection", () => {
    const result = solved(run([line("l")],
        [c("fix", "Fix", "l:start"), c("fix-alias", "Fix", "l|point|start|0|0"),
            c("h1", "Horizontal", "l"), c("h2", "Horizontal", "l:start", "l:end")],
        [d("len", "LinearDistance", 5, "l")]));
    nearPoint(get(result, "l").points[0], p(0, 0));
    nearPoint(get(result, "l").points[1], p(5, 0));
    assert.ok(result.diagnostics.some(message => /redundant equation/i.test(message)));
});

test("closed polyline has only shared-vertex degrees of freedom", () => {
    const result = solved(run([{ id: "poly", kind: "polyline", points: [p(0, 0), p(4, 0), p(4, 4), p(0, 0)] }],
        [c("h", "Horizontal", "poly|segment|0")]));
    assert.ok(result.diagnostics.some(message => /degrees of freedom: 5/.test(message)), result.diagnostics.join("\n"));
});

for (const fixed of [false, true]) {
    test(`90-degree horizontal edit rotates instead of collapsing, fixed-start=${fixed}`, () => {
        const result = solved(run([line("l", p(0, 0), p(0, 10))],
            [c("h", "Horizontal", "l"), ...(fixed ? [c("fix", "Fix", "l:start")] : [])]));
        const [a, b] = get(result, "l").points;
        near(a.y, b.y);
        near(Math.hypot(b.x - a.x, b.y - a.y), 10);
        if (fixed) nearPoint(a, p(0, 0));
    });
}

test("length stability preference yields to a length-changing endpoint drag", () => {
    const result = solved(run([line("l", p(0, 0), p(3, 0))],
        [c("fix", "Fix", "l:start"), c("h", "Horizontal", "l")], [],
        { drag: { key: "l:end", start: p(3, 0), end: p(10, 4) } }));
    nearPoint(get(result, "l").points[1], p(10, 0));
});

test("circle-to-circle tangency uses the existing external branch", () => {
    const result = solved(run([circle("a", p(0, 0), 2), circle("b", p(6, 0), 3)],
        [c("f", "Fix", "a"), c("tan", "Tangent", "a", "b")], [d("radius", "Radius", 3, "b")]));
    near(Math.hypot(get(result, "b").center.x, get(result, "b").center.y), 5);
});

test("initial guesses seed unknowns but cannot redefine fixed positions", () => {
    const result = solved(run([line("a")], [c("fix", "Fix", "a:start")],
        [d("length", "LinearDistance", 5, "a")], {
            initialGuesses: [{ referenceKey: "a:start", point: p(8, 8) }, { referenceKey: "a:end", point: p(0, 5) }]
        }));
    nearPoint(get(result, "a").points[0], p(0, 0));
    nearPoint(get(result, "a").points[1], p(0, 5));
});

test("hidden and locked entities remain fixed through a coupled solve", () => {
    const l = { ...line("l"), isLocked: true, layerName: "REFERENCE" };
    const result = solved(run([l, point("p", 3, 4)], [c("on", "PointOnLine", "p", "l")],
        [], { drag: { key: "l:end", start: p(5, 0), end: p(9, 4) } }));
    assert.deepEqual(get(result, "l"), l);
    near(get(result, "p").points[0].y, 0);
});

test("pattern flags and metadata cannot silently detach coupled geometry", () => {
    for (const extra of [{ hasPatterns: true }, { metadata: { patterns: [{ id: "p" }] } }]) {
        const document = { entities: [line("a")], constraints: [], dimensions: [], ...extra };
        const result = solver.solve({ document });
        assert.equal(result.status, "unsupported");
        assert.deepEqual(result.entities, document.entities);
    }
});

test("unsupported coupled intent and unknown constrained kinds refuse the entire snapshot", () => {
    for (const [entities, constraints, dimensions] of [
        [[line("a")], [c("unsupported", "Offset", "a")], []],
        [[line("a")], [], [d("unsupported", "Count", 3, "a")]],
        [[{ id: "s", kind: "spline", points: [p(0, 0), p(1, 1)] }], [c("unsupported", "Fix", "s:start")], []],
        [[line("a")], [c("unsupported", "Coincident", "a|point|mystery|0|0", "$origin:center")], []]
    ]) {
        assert.equal(run(entities, constraints, dimensions).status, "unsupported");
    }
});

test("duplicate IDs and malformed segment references refuse rather than partially solving", () => {
    assert.equal(run([line("a"), line("a")]).status, "unsupported");
    assert.equal(run([line("a")], [c("h", "Horizontal", "a|segment|0")]).status, "unsupported");
});

test("suppressed constraints and reference dimensions do not become driving intent", () => {
    const l = line("a", p(0, 0), p(3, 4));
    const result = solved(run([l], [{ ...c("h", "Horizontal", "a"), state: "Suppressed" }],
        [{ ...d("len", "LinearDistance", 100, "a"), isDriving: false }]));
    assert.deepEqual(get(result, "a"), l);
});

test("dispose is idempotent and a disposed solver cannot publish a solve", async () => {
    const instance = await createPlaneGcsSolver();
    instance.dispose();
    instance.dispose();
    const entities = [point("p", 1, 2)];
    const result = instance.solve({ document: { entities } });
    assert.equal(result.status, "failed");
    assert.deepEqual(result.entities, entities);
});

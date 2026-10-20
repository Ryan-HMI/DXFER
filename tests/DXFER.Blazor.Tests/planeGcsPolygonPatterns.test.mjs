import assert from "node:assert/strict";
import { after, test } from "node:test";
import { createPlaneGcsSolver } from "../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js";

const solver = await createPlaneGcsSolver();
after(() => solver.dispose());
const p = (x, y) => ({ x, y });
const near = (a, b) => assert.ok(Number.isFinite(a) && Math.abs(a - b) < 2e-5, `${a} != ${b}`);
const nearPoint = (a, b) => { near(a.x, b.x); near(a.y, b.y); };
const preservedTranslationShape = (before, after) => {
    assert.ok(Math.abs(before.radius - after.radius) < 1e-6, `Translation changed radius ${before.radius} -> ${after.radius}`);
    const angle = (after.rotationAngleDegrees - before.rotationAngleDegrees) * Math.PI / 180;
    assert.ok(Math.abs(Math.atan2(Math.sin(angle), Math.cos(angle))) < 1e-7,
        `Translation changed angle ${before.rotationAngleDegrees} -> ${after.rotationAngleDegrees}`);
};
const radians = Math.PI / 180;
function vertices(entity) {
    const r = entity.circumscribed ? entity.radius / Math.cos(Math.PI / entity.sideCount) : entity.radius;
    const a = entity.rotationAngleDegrees * radians + (entity.circumscribed ? Math.PI / entity.sideCount : 0);
    return Array.from({ length: entity.sideCount }, (_, i) => p(
        entity.center.x + r * Math.cos(a + i * 2 * Math.PI / entity.sideCount),
        entity.center.y + r * Math.sin(a + i * 2 * Math.PI / entity.sideCount)));
}
const guide = e => p(e.center.x + e.radius * Math.cos(e.rotationAngleDegrees * radians),
    e.center.y + e.radius * Math.sin(e.rotationAngleDegrees * radians));
function transform(pattern, value, row, column) {
    if (pattern.kind === "Radial") {
        const angle = column * (pattern.fullCircle ? 360 / pattern.count : pattern.stepAngleDegrees) * radians;
        const x = value.x - pattern.center.x, y = value.y - pattern.center.y;
        return p(pattern.center.x + x * Math.cos(angle) - y * Math.sin(angle),
            pattern.center.y + x * Math.sin(angle) + y * Math.cos(angle));
    }
    const angle = pattern.angleDegrees * radians;
    const x = (column + (pattern.kind === "Staggered" && row % 2 ? 0.5 : 0)) * pattern.xPitch;
    const y = row * pattern.yPitch;
    return p(value.x + x * Math.cos(angle) - y * Math.sin(angle),
        value.y + x * Math.sin(angle) + y * Math.cos(angle));
}
function fixture(kind, circumscribed, { sideCount = 5, center = p(3, 2), ...settings } = {}) {
    const pattern = { id: "array", kind, rows: 2, columns: 3, count: 4, fullCircle: true,
        stepAngleDegrees: 53, center: p(1, -2), xPitch: 8, yPitch: 6, angleDegrees: 32,
        sourceEntityIds: ["seed"], members: [], ...settings };
    const seed = { id: "seed", kind: "polygon", center, radius: 2, rotationAngleDegrees: 23,
        sideCount, circumscribed, layerName: "CUT", native: { sourceId: "native-poly" } };
    seed.points = vertices(seed);
    const document = { entities: [seed], constraints: [], dimensions: [], hasPatterns: true,
        metadata: { patterns: [pattern] } };
    for (let row = 0; row < (kind === "Radial" ? 1 : pattern.rows); row++) {
        for (let column = 0; column < (kind === "Radial" ? pattern.count : pattern.columns); column++) {
            if (!row && !column) continue;
            const entity = { ...structuredClone(seed), id: `member-${row}-${column}`, isGenerated: true,
                center: transform(pattern, center, row, column) };
            if (kind === "Radial")
                entity.rotationAngleDegrees += column * (pattern.fullCircle ? 360 / pattern.count : pattern.stepAngleDegrees);
            entity.points = vertices(entity);
            document.entities.push(entity);
            pattern.members.push({ sourceEntityId: seed.id, entityId: entity.id, row, column });
        }
    }
    document.dimensions = document.entities.map(entity => ({
        id: `count-${entity.id}`, kind: "Count", value: sideCount, isDriving: true, referenceKeys: [entity.id]
    }));
    return document;
}
function solve(document, extra = {}, status = "solved") {
    const request = { document, ...extra }, before = structuredClone(request);
    const result = solver.solve(request);
    assert.deepEqual(request, before);
    assert.equal(result.engine, "PlaneGCS");
    assert.equal(result.status, status, JSON.stringify(result.diagnostics));
    if (status !== "solved") assert.deepEqual(result.entities, document.entities);
    return result;
}
function checkMembers(document, result) {
    const pattern = document.metadata.patterns[0], seed = result.entities.find(e => e.id === "seed");
    for (const entity of result.entities.filter(e => e.kind === "polygon")) {
        const before = document.entities.find(e => e.id === entity.id);
        for (const field of ["kind", "sideCount", "circumscribed", "layerName", "native", "isGenerated"])
            assert.deepEqual(entity[field], before[field]);
        assert.equal(entity.points.length, entity.sideCount);
        entity.points.forEach((point, i) => nearPoint(point, vertices(entity)[i]));
    }
    for (const member of pattern.members) {
        const actual = result.entities.find(e => e.id === member.entityId);
        nearPoint(actual.center, transform(pattern, seed.center, member.row, member.column));
        nearPoint(guide(actual), transform(pattern, guide(seed), member.row, member.column));
        near(actual.radius, seed.radius);
        actual.points.forEach((point, i) => nearPoint(point, transform(pattern, seed.points[i], member.row, member.column)));
    }
}
const radius = (key, value, kind = "Radius") => ({
    id: "size", kind, value, isDriving: true, referenceKeys: [key]
});
for (const kind of ["Rectangular", "Staggered", "Radial"]) {
    for (const circumscribed of [false, true]) {
        test(`${kind} polygon circumscribed=${circumscribed} follows an off-center source drag`, () => {
            const document = fixture(kind, circumscribed);
            const result = solve(document, { drag: { key: "seed:center", start: p(3.2, 2.1), end: p(4.2, 3.1) } });
            nearPoint(result.entities[0].center, p(4, 3));
            near(result.entities[0].radius, 2);
            result.entities.forEach((entity, index) => preservedTranslationShape(document.entities[index], entity));
            checkMembers(document, result);
            assert.ok(result.diagnostics.includes("PlaneGCS mapped-system degrees of freedom: 4."));
        });
        test(`${kind} polygon circumscribed=${circumscribed} repeated body translations preserve radius and angle`, () => {
            let document = fixture(kind, circumscribed);
            const original = structuredClone(document.entities);
            for (const [dx, dy, key] of [[1, 1, "seed"], [-3, 2, "seed:mid-1"], [2, -3, "seed:center"]]) {
                const result = solve(document, { drag: { key, start: p(0.2, -0.1), end: p(dx + 0.2, dy - 0.1) } });
                nearPoint(result.entities[0].center, p(document.entities[0].center.x + dx, document.entities[0].center.y + dy));
                result.entities.forEach((entity, index) => preservedTranslationShape(original[index], entity));
                checkMembers(document, result);
                document = { ...document, entities: result.entities };
            }
        });
        test(`${kind} polygon circumscribed=${circumscribed} projected center drag preserves every member shape`, () => {
            const document = fixture(kind, circumscribed);
            document.entities.push({ id: "axis-anchor", kind: "point", points: [p(-1, 2)] });
            document.constraints = [
                { id: "fixed-anchor", kind: "Fix", referenceKeys: ["axis-anchor"] },
                { id: "center-axis", kind: "Horizontal", referenceKeys: ["seed:center", "axis-anchor:center"] }
            ];
            const result = solve(document, { drag: { key: "seed", start: p(3.2, 2.1), end: p(4.2, 3.1) } });
            nearPoint(result.entities[0].center, p(4, 2));
            result.entities.filter(entity => entity.kind === "polygon").forEach(entity =>
                preservedTranslationShape(document.entities.find(before => before.id === entity.id), entity));
            checkMembers(document, result);
            assert.ok(result.diagnostics.includes("PlaneGCS mapped-system degrees of freedom: 3."));
        });
        for (const dimensionKind of ["Radius", "Diameter"]) {
            test(`${kind} polygon circumscribed=${circumscribed} source ${dimensionKind} updates guide/apothem`, () => {
                const document = fixture(kind, circumscribed);
                const result = solve(document, { fixedReferenceKeys: ["seed:center"],
                    dimensions: [radius("seed", dimensionKind === "Radius" ? 3 : 6, dimensionKind)],
                    attemptedDrivingDimensionIds: ["size"] });
                nearPoint(result.entities[0].center, p(3, 2));
                near(result.entities[0].radius, 3);
                checkMembers(document, result);
                assert.deepEqual(result.redundantDrivingDimensionIds, []);
            });
        }
        test(`${kind} polygon circumscribed=${circumscribed} member radius can drive its source`, () => {
            const document = fixture(kind, circumscribed);
            const result = solve(document, { fixedReferenceKeys: ["seed:center"],
                dimensions: [radius("member-0-1", 3)], attemptedDrivingDimensionIds: ["size"] });
            near(result.entities[0].radius, 3);
            checkMembers(document, result);
            assert.deepEqual(result.redundantDrivingDimensionIds, []);
        });
        test(`${kind} polygon circumscribed=${circumscribed} member-center reference moves the coupled source`, () => {
            const document = fixture(kind, circumscribed);
            document.entities.push({ id: "peer", kind: "point", points: [p(12, 4)] });
            const result = solve(document, { constraints: [
                { id: "fix-peer", kind: "Fix", referenceKeys: ["peer"] },
                { id: "join", kind: "Coincident", referenceKeys: ["member-0-1:center", "peer:center"] }
            ] });
            nearPoint(result.entities.find(e => e.id === "member-0-1").center, p(12, 4));
            checkMembers(document, result);
        });
    }
    test(`${kind} locked polygon member blocks incompatible source resizing`, () => {
        const document = fixture(kind, true);
        document.entities[1].isLocked = true;
        solve(document, { dimensions: [radius("seed", 3)], attemptedDrivingDimensionIds: ["size"] }, "failed");
    });
    test(`${kind} generated polygon direct drag still refuses`, () => {
        const document = fixture(kind, false);
        solve(document, { drag: { key: "member-0-1:center", start: p(0, 0), end: p(1, 2) } }, "unsupported");
    });
}
for (const sideCount of [3, 64]) {
    test(`radial ${sideCount}-side circumscribed polygon uses center-degenerate affine equations`, () => {
        const document = fixture("Radial", true, { sideCount, center: p(1, -2), fullCircle: false });
        const result = solve(document, { fixedReferenceKeys: ["seed:center"],
            dimensions: [radius("seed", 4)], attemptedDrivingDimensionIds: ["size"] });
        near(result.entities[0].radius, 4);
        checkMembers(document, result);
        assert.deepEqual(result.redundantDrivingDimensionIds, []);
    });
}
for (const damage of ["sideCount", "circumscribed", "invalidCount", "memberKind", "missingMember", "duplicateMember"]) {
    test(`polygon pattern refuses ${damage} without dropping geometry or pattern intent`, () => {
        const document = fixture("Rectangular", false);
        if (damage === "sideCount") document.entities[1].sideCount = 6;
        if (damage === "invalidCount") document.entities[1].sideCount = 2;
        if (damage === "circumscribed") document.entities[1].circumscribed = true;
        if (damage === "memberKind") document.entities[1].kind = "circle";
        if (damage === "missingMember") document.metadata.patterns[0].members.pop();
        if (damage === "duplicateMember") document.metadata.patterns[0].members[1] = document.metadata.patterns[0].members[0];
        const result = solve(document, { dimensions: [radius("seed", 3)] }, "unsupported");
        if (["sideCount", "circumscribed"].includes(damage))
            assert.ok(result.diagnostics.some(message => message.includes("topology differs")));
    });
}
test("Count changes must be staged consistently across the retained polygon pattern", () => {
    const document = fixture("Rectangular", true, { sideCount: 6 });
    const result = solve(document, { attemptedDrivingDimensionIds: ["count-seed"],
        dimensions: [radius("seed", 3)] });
    checkMembers(document, result);
    assert.deepEqual(result.redundantDrivingDimensionIds, []);
    solve(fixture("Rectangular", true), { dimensions: [
        { id: "count-seed", kind: "Count", value: 6, isDriving: true, referenceKeys: ["seed"] }
    ] }, "unsupported");
});

import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const p = (x, y) => ({ x, y });
const add = (a, b) => p(a.x + b.x, a.y + b.y);
const circleOf = document => document.entities.find(entity => entity.id === 'circle');
const constraint = (id, kind, referenceKeys) => ({ id, kind, referenceKeys, state: 'satisfied' });
const near = (actual, expected) => assert.ok(actual && Math.hypot(actual.x - expected.x, actual.y - expected.y) < 1e-8,
    `${JSON.stringify(actual)} != ${JSON.stringify(expected)}`);

function fixture({ vertical = false, polyline = false, reverse = false, diameter = false } = {}) {
    const source = { id: 'source', kind: polyline ? 'polyline' : 'line',
        points: polyline ? [p(2, 0), p(2, 10), p(9, 10)] : [p(2, 0), p(2, 10)] };
    const refs = ['circle:center', polyline ? 'source|segment|0:mid' : 'source:mid'];
    if (reverse) refs.reverse();
    return { entities: [source, { id: 'circle', kind: 'circle', center: vertical ? p(2, 12) : p(8, 5), radius: 3 }],
        constraints: [constraint('axis', vertical ? 'vertical' : 'horizontal', refs)],
        dimensions: [{ id: 'size', kind: diameter ? 'diameter' : 'radius', referenceKeys: ['circle'],
            value: diameter ? 6 : 3, isDriving: true, anchor: p(12, 4) }] };
}

function drag(document, delta, grip = 'center') {
    const circle = circleOf(document);
    const start = grip === 'center' ? circle.center : add(circle.center, p(circle.radius, 0));
    return applyGeometryDragPreview(document, `circle|point|${grip}|${start.x}|${start.y}`, start, add(start, delta));
}

function assertTranslation(document, next, delta) {
    assert.ok(next, 'circle-center motion should have a preview');
    near(circleOf(next).center, add(circleOf(document).center, delta));
    assert.equal(circleOf(next).radius, 3);
    assert.equal(next.entities[0].id, document.entities[0].id);
    assert.equal(next.entities[0].kind, document.entities[0].kind);
    assert.deepEqual(next.entities[0].points, document.entities[0].points, 'midpoint source is read-only');
    assert.deepEqual(next.constraints, document.constraints);
    assert.equal(next.dimensions.length, document.dimensions.length);
    for (let index = 0; index < document.dimensions.length; index++) {
        const before = document.dimensions[index], after = next.dimensions[index];
        assert.equal(after.id, before.id);
        assert.equal(after.kind, before.kind);
        assert.equal(after.value, before.value);
        assert.equal(after.isDriving, before.isDriving);
        assert.deepEqual(after.referenceKeys, before.referenceKeys);
        near(after.anchor, add(before.anchor, delta));
    }
}

for (const vertical of [false, true]) for (const polyline of [false, true])
for (const reverse of [false, true]) for (const diameter of [false, true]) {
    test(`midpoint axis circle preview vertical=${vertical} polyline=${polyline} reverse=${reverse} diameter=${diameter}`, () => {
        const document = fixture({ vertical, polyline, reverse, diameter });
        const before = structuredClone(document);
        const along = vertical ? p(0, 3) : p(3, 0);
        assertTranslation(document, drag(document, along), along);
        assertTranslation(document, drag(document, p(3, 3)), along);
        assert.deepEqual(document, before);
    });
}

for (const vertical of [false, true]) for (const fixedKey of ['circle', 'circle:center']) {
    test(`fixed circle blocks projected preview vertical=${vertical} key=${fixedKey}`, () => {
        const document = fixture({ vertical });
        document.constraints.push(constraint('fix', 'fix', [fixedKey]));
        const before = structuredClone(document);
        assert.equal(drag(document, p(3, 3)), null);
        assert.deepEqual(document, before);
    });
}

for (const vertical of [false, true]) {
    test(`pure blocked direction has no preview/history candidate vertical=${vertical}`, () => {
        const document = fixture({ vertical });
        const before = structuredClone(document);
        assert.equal(drag(document, vertical ? p(3, 0) : p(0, 3)), null);
        assert.deepEqual(document, before);
    });
}

for (const vertical of [false, true]) for (const polyline of [false, true]) {
    test(`projection follows current midpoint geometry vertical=${vertical} polyline=${polyline}`, () => {
        const document = fixture({ vertical, polyline });
        const shift = vertical ? p(2, 0) : p(0, 2);
        document.entities[0].points = document.entities[0].points.map(point => add(point, shift));
        const before = structuredClone(document);
        assertTranslation(document, drag(document, p(3, 3)), vertical ? p(2, 3) : p(3, 2));
        assert.deepEqual(document, before);
    });
}

for (const vertical of [false, true]) {
    test(`missing midpoint reference refuses vertical=${vertical}`, () => {
        const document = fixture({ vertical });
        document.entities.shift();
        const before = structuredClone(document);
        assert.equal(drag(document, p(3, 3)), null);
        assert.deepEqual(document, before);
    });

    test(`free radius grip remains resize vertical=${vertical}`, () => {
        const document = fixture({ vertical });
        document.dimensions = [];
        const before = structuredClone(document);
        const next = drag(document, p(1, 0), 'quadrant-0');
        assert.ok(next);
        near(circleOf(next).center, circleOf(document).center);
        assert.equal(circleOf(next).radius, 4);
        assert.equal(next.entities[0].id, document.entities[0].id);
        assert.equal(next.entities[0].kind, document.entities[0].kind);
        assert.deepEqual(next.entities[0].points, document.entities[0].points);
        assert.deepEqual(next.constraints, document.constraints);
        assert.deepEqual(document, before);
    });

    test(`both axes constrain center and refuse projection vertical=${vertical}`, () => {
        const document = fixture({ vertical });
        circleOf(document).center = p(2, 5);
        document.constraints.push(constraint('other-axis', vertical ? 'horizontal' : 'vertical', ['source:mid', 'circle:center']));
        const before = structuredClone(document);
        assert.equal(drag(document, p(3, 3)), null);
        assert.deepEqual(document, before);
    });
}

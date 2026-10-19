import test from 'node:test';
import assert from 'node:assert/strict';
import {applyGeometryDragPreview, findNearestTarget, getConstraintGlyphIcon,
  isTargetEligibleForConstraintTool} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const p = (x, y) => ({x, y});
const circle = (id = 'circle', center = p(0, 0), radius = 5) => ({id, kind: 'circle', center, radius});
const point = (position = p(5, 0)) => ({id: 'point', kind: 'point', points: [position]});
const c = (kind, ...referenceKeys) => ({id: kind + referenceKeys.join('/'), kind, referenceKeys, state: 'Satisfied'});
const near = (actual, expected) => assert.ok(Math.hypot(actual.x - expected.x, actual.y - expected.y) < 1e-6,
  `${JSON.stringify(actual)} != ${JSON.stringify(expected)}`);
function fixture({fixed = true, reverse = false, location = p(5, 0)} = {}) {
  return {entities: [circle(), point(location)], dimensions: [], constraints: [
    c('PointOnCircle', ...(reverse ? ['circle', 'point'] : ['point', 'circle'])),
    ...(fixed ? [c('Fix', 'circle')] : [])]};
}

for (const reverse of [false, true]) {
  test(`fixed circle projects point onto circumference, preserving radius; reversed=${reverse}`, () => {
    const doc = fixture({reverse}), before = structuredClone(doc);
    const result = applyGeometryDragPreview(doc, 'point', p(5, 0), p(3, 7));
    assert.ok(result);
    near(result.entities[1].points[0], p(15 / Math.sqrt(58), 35 / Math.sqrt(58)));
    assert.deepEqual(result.entities[0].center, before.entities[0].center);
    assert.equal(result.entities[0].radius, before.entities[0].radius);
    assert.deepEqual(doc, before);
  });
  test(`dragged point translates a free target circle instead of resizing it; reversed=${reverse}`, () => {
    const doc = fixture({fixed: false, reverse});
    const result = applyGeometryDragPreview(doc, 'point', p(5, 0), p(7, 0));
    assert.ok(result);
    near(result.entities[0].center, p(2, 0));
    near(result.entities[1].points[0], p(7, 0));
    assert.equal(result.entities[0].radius, 5);
  });
}

test('dragged target circle projects free point and retains the requested circle center', () => {
  const doc = fixture({fixed: false});
  const result = applyGeometryDragPreview(doc, 'circle|point|center|0|0', p(0, 0), p(0, 2));
  assert.ok(result);
  near(result.entities[0].center, p(0, 2));
  near(result.entities[1].points[0], p(25 / Math.sqrt(29), 2 - 10 / Math.sqrt(29)));
  assert.equal(result.entities[0].radius, 5);
});

test('fixed point makes dragged free circle translate back onto incidence, never resize', () => {
  const doc = fixture({fixed: false});
  doc.constraints.push(c('Fix', 'point'));
  const result = applyGeometryDragPreview(doc, 'circle|point|center|0|0', p(0, 0), p(0, 2));
  assert.ok(result);
  near(result.entities[1].points[0], p(5, 0));
  near(result.entities[0].center, p(5 - 25 / Math.sqrt(29), 10 / Math.sqrt(29)));
  assert.equal(result.entities[0].radius, 5);
});

test('fixed circle center projects the free point rather than moving center or resizing radius', () => {
  const doc = fixture({fixed: false});
  doc.constraints.push(c('Fix', 'circle:center'));
  const result = applyGeometryDragPreview(doc, 'point', p(5, 0), p(8, 6));
  assert.ok(result);
  near(result.entities[1].points[0], p(4, 3));
  near(result.entities[0].center, p(0, 0));
  assert.equal(result.entities[0].radius, 5);
});

test('circle-center point reference translates its own circle without changing either radius', () => {
  const doc = {entities: [circle(), circle('moving', p(5, 0), 2)], dimensions: [],
    constraints: [c('PointOnCircle', 'moving:center', 'circle'), c('Fix', 'circle')]};
  const result = applyGeometryDragPreview(doc, 'moving|point|center|5|0', p(5, 0), p(6, 8));
  assert.ok(result);
  near(result.entities[1].center, p(3, 4));
  assert.equal(result.entities[0].radius, 5);
  assert.equal(result.entities[1].radius, 2);
});

test('origin point remains fixed while free circle translation preserves its incidence', () => {
  const doc = {entities: [circle('circle', p(5, 0))], dimensions: [],
    constraints: [c('PointOnCircle', '$origin:center', 'circle')]};
  const result = applyGeometryDragPreview(doc, 'circle|point|center|5|0', p(5, 0), p(5, 2));
  assert.ok(result);
  near(result.entities[0].center, p(25 / Math.sqrt(29), 10 / Math.sqrt(29)));
  assert.equal(result.entities[0].radius, 5);
});

test('point at center retains original radial direction', () => {
  const doc = fixture({location: p(0, 5)});
  const result = applyGeometryDragPreview(doc, 'point', p(0, 5), p(0, 0));
  assert.ok(result);
  near(result.entities[1].points[0], p(0, 5));
});

test('concentric point with no original direction falls back to positive X', () => {
  const doc = fixture({location: p(0, 0)});
  doc.entities.push({id: 'driver', kind: 'point', points: [p(20, 20)]});
  const result = applyGeometryDragPreview(doc, 'driver', p(20, 20), p(21, 20));
  assert.ok(result);
  near(result.entities[1].points[0], p(5, 0));
  assert.equal(result.entities[0].radius, 5);
});

test('both fixed references refuse incompatible drag and leave source unchanged', () => {
  const doc = fixture();
  doc.constraints.push(c('Fix', 'point'));
  const before = structuredClone(doc);
  assert.equal(applyGeometryDragPreview(doc, 'point', p(5, 0), p(6, 1)), null);
  assert.deepEqual(doc, before);
});

for (const radius of [0, -5, 1e-6, NaN, Infinity, -Infinity]) {
  test(`invalid circle radius ${radius} refuses preview without edits`, () => {
    const doc = fixture({fixed: false});
    doc.entities[0].radius = radius;
    const before = structuredClone(doc);
    assert.equal(applyGeometryDragPreview(doc, 'point', p(5, 0), p(6, 1)), null);
    assert.deepEqual(doc, before);
  });
}

for (const kind of ['arc', 'ellipse', 'polygon', 'spline']) {
  test(`unsupported ${kind} circumference refuses preview`, () => {
    const doc = fixture({fixed: false});
    Object.assign(doc.entities[0], {kind, startAngleDegrees: 0, endAngleDegrees: 90,
      points: [p(0, 0), p(5, 0), p(5, 5)]});
    const before = structuredClone(doc);
    assert.equal(applyGeometryDragPreview(doc, 'point', p(5, 0), p(6, 1)), null);
    assert.deepEqual(doc, before);
  });
}

for (const references of [['circle:center', 'circle'], ['point', 'circle:center'],
  ['point', 'circle|segment|0'], ['point', 'circle|point|quadrant-0|5|0'], ['point', 'circle:bogus'],
  ['point', 'circle', 'point']]) {
  test(`invalid or same-entity references refuse preview: ${references.join(', ')}`, () => {
    const doc = fixture({fixed: false});
    doc.constraints = [c('PointOnCircle', ...references)];
    const before = structuredClone(doc);
    assert.equal(applyGeometryDragPreview(doc, 'point', p(5, 0), p(6, 1)), null);
    assert.deepEqual(doc, before);
  });
}

test('whole-circle Coincident eligibility excludes other curve bodies and retains center points', () => {
  for (const kind of ['circle', 'arc', 'ellipse', 'polygon', 'spline']) {
    const entity = {...circle(), kind};
    assert.equal(isTargetEligibleForConstraintTool('coincident', {kind: 'entity', key: 'circle', entity}), kind === 'circle');
    assert.equal(isTargetEligibleForConstraintTool('coincident',
      {kind: 'point', key: 'circle|point|quadrant-0|5|0', entity, label: 'quadrant-0'}), false);
  }
  assert.equal(isTargetEligibleForConstraintTool('coincident',
    {kind: 'point', entity: circle(), label: 'center', key: 'circle:center'}), true);
  assert.equal(getConstraintGlyphIcon('pointoncircle'), 'coincident');
});

test('tool-first Coincident picks circle body at quadrants but retains its center grip', () => {
  const state = {activeTool: 'coincident', document: {entities: [circle('circle', p(10, 10))]},
    selectedKeys: new Set(), view: {scale: 20, offsetX: 200, offsetY: 400}};
  for (const position of [p(15, 10), p(10, 15), p(5, 10), p(10, 5)]) {
    const target = findNearestTarget(state, p(position.x * 20 + 200, 400 - position.y * 20));
    assert.equal(target?.kind, 'entity');
    assert.equal(target?.key, 'circle');
  }
  const center = findNearestTarget(state, p(400, 200));
  assert.equal(center?.kind, 'point');
  assert.equal(center?.label, 'center');
  state.activeTool = 'select';
  assert.equal(findNearestTarget(state, p(500, 200))?.label, 'quadrant-0');
});

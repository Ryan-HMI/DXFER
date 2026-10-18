import test from 'node:test';
import assert from 'node:assert/strict';
import {applyGeometryDragPreview, findNearestTarget, getConstraintGlyphIcon, isTargetEligibleForConstraintTool} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const p = (x, y) => ({x, y});
const line = (id, a, b) => ({id, kind: 'line', points: [a, b]});
const c = (kind, ...referenceKeys) => ({id: kind + referenceKeys.join('/'), kind, referenceKeys, state: 'Satisfied'});
function fixture(lines, fixed = true, reverse = false) {
  return {entities: [line('edge', p(0, 0), p(10, 0)), lines ? line('moving', p(12, 0), p(17, 0)) : {id: 'moving', kind: 'point', points: [p(15, 0)]}], dimensions: [],
    constraints: [c(lines ? 'Collinear' : 'PointOnLine', ...(reverse ? ['moving', 'edge'] : ['edge', 'moving'])), ...(fixed ? [c('Fix', 'edge')] : [])]};
}
for (const lines of [false, true]) for (const reverse of [false, true]) {
  test(`incidence projects diagonal drag onto fixed line without pinning along-line freedom: lines=${lines} reverse=${reverse}`, () => {
    const doc = fixture(lines, true, reverse), before = structuredClone(doc);
    const start = p(lines ? 14.5 : 15, 0);
    const result = applyGeometryDragPreview(doc, 'moving', start, p(start.x + 2, 1));
    assert.ok(result);
    assert.deepEqual(result.entities[0].points, before.entities[0].points);
    assert.deepEqual(result.entities[1].points, lines ? [p(14, 0), p(19, 0)] : [p(17, 0)]);
    assert.deepEqual(doc, before);
  });
  test(`incidence propagates to free peer: lines=${lines} reverse=${reverse}`, () => {
    const doc = fixture(lines, false, reverse);
    const result = applyGeometryDragPreview(doc, 'moving', p(lines ? 14.5 : 15, 0), p(lines ? 14.5 : 15, 2));
    assert.ok(result);
    assert.deepEqual(result.entities[0].points, [p(0, 2), p(10, 2)]);
  });
}
test('fixed incidence conflict refuses without geometry edits', () => {
  const doc = fixture(true); doc.constraints.push(c('Fix', 'moving'));
  const before = structuredClone(doc);
  assert.equal(applyGeometryDragPreview(doc, 'moving', p(14.5, 0), p(16.5, 1)), null);
  assert.deepEqual(doc, before);
});
test('Coincident admits straight edges and uses the same glyph for both incidence kinds', () => {
  const entity = line('edge', p(0, 0), p(10, 0));
  assert.ok(isTargetEligibleForConstraintTool('coincident', {key: 'edge', entity, kind: 'entity'}));
  assert.ok(isTargetEligibleForConstraintTool('coincident', {key: 'poly|segment|0', entity: {kind: 'polyline'}, kind: 'segment'}));
  assert.equal(getConstraintGlyphIcon('pointonline'), 'coincident');
  assert.equal(getConstraintGlyphIcon('collinear'), 'coincident');
});

test('selecting a standalone point first does not break subsequent constraint picking', () => {
  const state = {activeTool: 'coincident', document: {entities: [line('edge', p(0, 0), p(10, 0)), {id: 'p', kind: 'point', points: [p(15, 3)]}]},
    view: {scale: 50, offsetX: 200, offsetY: 300}, selectedKeys: new Set(['p'])};
  assert.equal(findNearestTarget(state, p(350, 300)).key, 'edge');
  state.document.entities[1].points = [p(0, 0)];
  state.document.entities.shift();
  assert.equal(findNearestTarget(state, p(200, 300)).key, '$origin:center');
});

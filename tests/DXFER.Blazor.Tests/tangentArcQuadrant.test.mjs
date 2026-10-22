import assert from 'node:assert/strict';
import test from 'node:test';
import {getDynamicSketchSnapHit, getTangentArc, getPostCommitSketchToolState,
  getCreationSnapIntents, applyDraftDimensionValue, getCurrentSnapCue} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
import {getSnapCue} from '../../src/DXFER.Blazor/wwwroot/canvas/snapCue.js';

const stateFor = (radius, scale = 100) => ({activeTool: 'tangentarc',
  document: {entities: []}, acquiredSnapPoints: [],
  toolDraft: {points: [{x: 0, y: 0}, {x: 1, y: 0}], dimensionValues: radius ? {radius} : {}},
  view: {scale, offsetX: 300, offsetY: 600}});
const screen = (s, p) => ({x: s.view.offsetX + p.x * s.view.scale, y: s.view.offsetY - p.y * s.view.scale});
const near = (a, b) => assert.ok(Math.abs(a - b) < 1e-9, `${a} != ${b}`);

for (const [name, point, sweep, relation] of [
  ['quarter CCW', {x: 2.025, y: 2}, 90, 'Horizontal'],
  ['half CCW', {x: 0.025, y: 4}, 180, 'Vertical'],
  ['quarter CW', {x: 2.025, y: -2}, 90, 'Horizontal'],
  ['half CW', {x: 0.025, y: -4}, 180, 'Vertical'],
  ['three-quarter', {x: -2.025, y: 2}, 270, 'Horizontal']]) {
  for (const keyed of [false, true]) test(`${name} quadrant snap, keyed radius ${keyed}`, () => {
    const state = stateFor(keyed ? 2 : null);
    const hit = getDynamicSketchSnapHit(state, screen(state, point));
    assert.ok(hit);
    assert.equal(hit.target.label, `arc-quadrant-${relation.toLowerCase()}`);
    const snapped = hit.target.point;
    const arc = getTangentArc(...state.toolDraft.points, snapped);
    near(arc.endAngleDegrees - arc.startAngleDegrees, sweep);
    if (keyed) {
      near(arc.radius, 2);
      state.toolDraft.previewPoint = snapped;
      applyDraftDimensionValue(state, 'radius', 2);
      near(state.toolDraft.previewPoint.x, snapped.x);
      near(state.toolDraft.previewPoint.y, snapped.y);
    }
    const intents = getCreationSnapIntents(state, hit.target, snapped);
    assert.equal(intents[0].kind, `ArcQuadrant${relation}`);
    assert.deepEqual(intents[0].referenceKeys, []);
    const cue = getSnapCue({target: hit.target, point: snapped, tool: 'tangentarc', pointCount: 2,
      intents, automaticConstraints: true});
    assert.equal(cue.snapType, 'Arc quadrant');
    assert.deepEqual(cue.constraintKinds, [relation]);
    assert.equal(getSnapCue({target: hit.target, point: snapped, tool: 'tangentarc', pointCount: 2,
      intents, automaticConstraints: false}).detail, 'Snap only (auto constraints off)');
  });
}

test('quadrants use screen tolerance at different zooms', () => {
  for (const scale of [10, 100, 1000]) {
    const state = stateFor(null, scale);
    assert.ok(getDynamicSketchSnapHit(state, screen(state, {x: 2 + 2 / scale, y: 2})));
    assert.equal(getDynamicSketchSnapHit(state, screen(state, {x: 2 + 35 / scale, y: 2})), null);
  }
});

test('invalid or incomplete tangent drafts do not invent quadrant targets', () => {
  const state = stateFor();
  assert.equal(getDynamicSketchSnapHit(state, screen(state, {x: 3, y: 0})), null);
  assert.equal(getDynamicSketchSnapHit(state, screen(state, {x: 0, y: 0})), null);
  state.toolDraft.points.pop();
  assert.equal(getDynamicSketchSnapHit(state, screen(state, {x: 2, y: 2})), null);
});

test('manual and hover arc creation both return to a tangent-capable Line chain', () => {
  for (const auto of [false, true]) {
    const next = getPostCommitSketchToolState('tangentarc', [{x: 0, y: 0}, {x: 1, y: 0}, {x: 2, y: 2}], auto);
    assert.equal(next.activeTool, 'line');
    assert.deepEqual(next.toolDraft.points, [{x: 2, y: 2}]);
    assert.equal(next.sketchChainContext.isTangentContinuation, true);
    assert.equal(next.sketchChainToggleRequiresExit, true);
  }
});

for (const [point, kind] of [[{x: 2.025, y: 2}, 'ArcQuadrantHorizontal'],
  [{x: 0.025, y: 4}, 'ArcQuadrantVertical'], [{x: 2.025, y: -2}, 'ArcQuadrantHorizontal']]) {
  test(`radius key-in remaps accepted ${kind} without reacquiring a mouse snap (${point.y})`, () => {
    const state = stateFor();
    state.pointerScreenPoint = screen(state, point);
    state.hoveredTarget = getDynamicSketchSnapHit(state, state.pointerScreenPoint).target;
    state.toolDraft.previewPoint = state.hoveredTarget.point;
    const original = {...state.hoveredTarget.point};
    for (const radius of [3, 3.125, 1.5]) {
      assert.equal(applyDraftDimensionValue(state, 'radius', radius), true);
      const target = state.toolDraft.keyedQuadrantTarget;
      assert.ok(target);
      const intents = getCreationSnapIntents(state, target, state.toolDraft.previewPoint);
      assert.deepEqual(intents, [{kind, referenceKeys: [], point: state.toolDraft.previewPoint}]);
      assert.equal(getCurrentSnapCue(state).detail, `Pending: ${kind.replace('ArcQuadrant', '')}`);
      near(getTangentArc(...state.toolDraft.points, state.toolDraft.previewPoint).radius, radius);
    }
    assert.deepEqual(state.hoveredTarget.point, original, 'original pointer snap remains immutable');
    state.automaticSnapConstraints = false;
    assert.equal(getCurrentSnapCue(state).detail, 'Snap only (auto constraints off)');
  });
}

test('radius entry does not infer quadrant intent merely from exact geometry', () => {
  const state = stateFor();
  state.toolDraft.previewPoint = {x: 2, y: 2};
  applyDraftDimensionValue(state, 'radius', 3);
  assert.equal(state.toolDraft.keyedQuadrantTarget, null);
});

test('accepted draft quadrant survives pointer leaving for radius input', () => {
  const state = stateFor();
  const target = getDynamicSketchSnapHit(state, screen(state, {x: 2.025, y: 2})).target;
  state.toolDraft.previewPoint = target.point;
  state.toolDraft.keyedQuadrantTarget = target;
  state.hoveredTarget = null;
  assert.equal(applyDraftDimensionValue(state, 'radius', 3), true);
  assert.deepEqual(getCreationSnapIntents(state, state.toolDraft.keyedQuadrantTarget,
    state.toolDraft.previewPoint), [{kind: 'ArcQuadrantHorizontal', referenceKeys: [],
    point: state.toolDraft.previewPoint}]);
});

test('radius entry does not move an external endpoint snap or retain mismatched intent', () => {
  const state = stateFor();
  state.toolDraft.previewPoint = {x: 2, y: 2};
  state.hoveredTarget = {point: {x: 2, y: 2}, creationRelations: [{kind: 'Coincident', referenceKeys: ['peer:end']}]};
  applyDraftDimensionValue(state, 'radius', 3);
  assert.equal(state.toolDraft.keyedQuadrantTarget, null);
  state.hoveredTarget = {point: {x: 2, y: 2}, creationRelations: [{kind: 'ArcQuadrantHorizontal', referenceKeys: []}]};
  applyDraftDimensionValue(state, 'radius', 4);
  assert.equal(state.toolDraft.keyedQuadrantTarget, null, 'old snap does not match the current preview');
});

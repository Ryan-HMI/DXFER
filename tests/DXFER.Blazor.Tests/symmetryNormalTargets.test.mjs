import test from 'node:test';
import assert from 'node:assert/strict';
import { isTargetEligibleForConstraintTool } from '../../src/DXFER.Blazor/wwwroot/canvas/targets.js';
import { getConstraintGlyphIcon, findNearestTarget, applyDirectSelectionClick } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

for (const tool of ['normal', 'symmetric']) {
  test(`${tool} uses the shared canvas glyph and supports curve perimeter and line body picks`, () => {
    assert.equal(getConstraintGlyphIcon(tool), tool);
    for (const kind of ['line', 'circle', 'arc'])
      assert.equal(isTargetEligibleForConstraintTool(tool, {kind: 'entity', entity: {kind}}), true);
    for (const kind of ['circle', 'arc'])
      assert.equal(isTargetEligibleForConstraintTool(tool, {kind: 'point', label: 'quadrant-90', entity: {kind}}), true);
    for (const kind of ['ellipse', 'spline', 'polygon'])
      assert.equal(isTargetEligibleForConstraintTool(tool, {kind: 'entity', entity: {kind}}), false);
    assert.equal(isTargetEligibleForConstraintTool(tool, {kind: 'point', label: 'vertex-2', entity: {kind: 'polyline'}}), true);
    assert.equal(isTargetEligibleForConstraintTool(tool, {kind: 'point', dynamic: true, label: 'end', entity: {kind: 'line'}}), false);
  });
}

test('Normal excludes line midpoint and non-line point picks; symmetry accepts durable point centers', () => {
  assert.equal(isTargetEligibleForConstraintTool('normal', {kind: 'point', label: 'mid', entity: {kind: 'line'}}), false);
  assert.equal(isTargetEligibleForConstraintTool('normal', {kind: 'entity', entity: {kind: 'point'}}), false);
  assert.equal(isTargetEligibleForConstraintTool('symmetric', {kind: 'point', label: 'center', entity: {kind: 'circle'}}), true);
  assert.equal(isTargetEligibleForConstraintTool('symmetric', {kind: 'point', key: '$origin:center'}), true);
});

test('tool-first canvas selection keeps pair order and last axis rather than treating the tool as creation', () => {
  const entities = [
    {id:'a',kind:'line',points:[{x:-3,y:0},{x:-3,y:4}]},
    {id:'b',kind:'line',points:[{x:3,y:0},{x:3,y:4}]},
    {id:'axis',kind:'line',points:[{x:0,y:-1},{x:0,y:5}]}
  ];
  const state = {activeTool:'symmetric',document:{entities,constraints:[],dimensions:[]},
    view:{scale:50,offsetX:300,offsetY:400},acquiredSnapPoints:[],selectedKeys:new Set(),activeSelectionKey:null};
  for (const [x,y,id] of [[150,340,'a'],[450,340,'b'],[300,340,'axis']]) {
    const target = findNearestTarget(state,{x,y});
    assert.equal(target?.key,id);
    applyDirectSelectionClick(state,target.key);
  }
  assert.deepEqual([...state.selectedKeys],['a','b','axis']);
  assert.equal(state.activeSelectionKey,'axis');
});

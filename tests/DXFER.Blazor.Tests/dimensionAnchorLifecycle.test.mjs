import assert from 'node:assert/strict';
import test from 'node:test';
import {getPersistentDimensionDescriptors, reconcileDimensionAnchorOverrides} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const make = () => ({
  document: {entities:[{id:'line',kind:'line',points:[{x:2,y:1},{x:12,y:1}]}],
    dimensions:[{id:'length',kind:'lineardistance',referenceKeys:['line:start','line:end'],value:10,anchor:{x:8,y:3}}]},
  view:{scale:50,offsetX:100,offsetY:500},
  dimensionAnchorOverrides:new Map([['length',{x:6,y:2}]]),dimensionDrag:null
});

test('authoritative update retires completed label override and follows the document', () => {
  const state=make();
  assert.deepEqual(getPersistentDimensionDescriptors(state)[0].anchorPoint,{x:6,y:2});
  reconcileDimensionAnchorOverrides(state);
  assert.equal(state.dimensionAnchorOverrides.size,0);
  assert.deepEqual(getPersistentDimensionDescriptors(state)[0].anchorPoint,{x:8,y:3});
  state.document.dimensions[0].anchor={x:6,y:2}; // Undo geometry.
  reconcileDimensionAnchorOverrides(state);
  assert.deepEqual(getPersistentDimensionDescriptors(state)[0].anchorPoint,{x:6,y:2});
});

test('held label override survives unrelated refresh but not deletion', () => {
  const state=make(); state.dimensionDrag={dimensionId:'length'};
  reconcileDimensionAnchorOverrides(state);
  assert.deepEqual(getPersistentDimensionDescriptors(state)[0].anchorPoint,{x:6,y:2});
  state.document.dimensions=[];
  reconcileDimensionAnchorOverrides(state);
  assert.equal(state.dimensionAnchorOverrides.size,0);
});

test('new/open clears held state and same-ID overrides', () => {
  const state=make(); state.dimensionDrag={dimensionId:'length'};
  reconcileDimensionAnchorOverrides(state,true);
  assert.equal(state.dimensionDrag,null);
  assert.equal(state.dimensionAnchorOverrides.size,0);
  assert.deepEqual(getPersistentDimensionDescriptors(state)[0].anchorPoint,{x:8,y:3});
});

import test from 'node:test';
import assert from 'node:assert/strict';
import {getVisibleConstraintGlyphGroups, retainDepartedConstraintGroups, applyConstraintVisibilityState}
  from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const target={kind:'entity',key:'line',entityId:'line'};
const icon={kind:'constraint',key:'constraint:h',constraintId:'h'};
const create=()=>({document:{entities:[{id:'line',kind:'line',points:[{x:0,y:0},{x:10,y:0}]}],
  constraints:[{id:'h',kind:'Horizontal',referenceKeys:['line'],state:'Satisfied'}],dimensions:[]},
  view:{scale:10,offsetX:100,offsetY:200},constraintGroupOffsets:new Map(),selectedKeys:new Set(),showAllConstraints:false});

test('leaving geometry retains full opacity for 1.25 seconds then fades until 1.5 seconds',()=>{
  const state=create(), original=JSON.stringify(state.document);
  retainDepartedConstraintGroups(state,target,null,100);
  assert.equal(getVisibleConstraintGlyphGroups(state,1349)[0].hoverOpacity,1);
  assert.equal(getVisibleConstraintGlyphGroups(state,1475)[0].hoverOpacity,.5);
  assert.equal(getVisibleConstraintGlyphGroups(state,1600).length,0);
  assert.equal(state.constraintHoverDeadlines.size,0);
  assert.equal(JSON.stringify(state.document),original);
});

test('crossing to a glyph during fade restores full opacity; leaving restarts grace',()=>{
  const state=create(); retainDepartedConstraintGroups(state,target,null,0);
  state.hoveredTarget=icon;
  assert.equal(getVisibleConstraintGlyphGroups(state,1400)[0].hoverOpacity,1);
  assert.equal(getVisibleConstraintGlyphGroups(state,8000)[0].hoverOpacity,1);
  retainDepartedConstraintGroups(state,icon,null,8000);state.hoveredTarget=null;
  assert.equal(getVisibleConstraintGlyphGroups(state,9250)[0].hoverOpacity,1);
  assert.equal(getVisibleConstraintGlyphGroups(state,9500).length,0);
});

test('selected constraints stay visible after pointer departure and expiry',()=>{
  const state=create(); retainDepartedConstraintGroups(state,target,null,0);
  state.selectedKeys.add('constraint:h');
  assert.equal(getVisibleConstraintGlyphGroups(state,9000)[0].hoverOpacity,1);
  state.selectedKeys.clear();assert.equal(getVisibleConstraintGlyphGroups(state,9001).length,0);
});

test('deleted constraints never leave stale retained groups',()=>{
  const state=create();retainDepartedConstraintGroups(state,target,null,0);
  state.document.constraints=[];
  assert.equal(getVisibleConstraintGlyphGroups(state,500).length,0);
  assert.equal(state.constraintHoverDeadlines.size,0);
});

test('show-all toggles clear transient retention without changing constraints',()=>{
  const state=create();retainDepartedConstraintGroups(state,target,null,0);
  assert.equal(applyConstraintVisibilityState(state,true),true);
  assert.equal(state.constraintHoverDeadlines.size,0);
  assert.equal(getVisibleConstraintGlyphGroups(state,9000).length,1);
  applyConstraintVisibilityState(state,false);
  assert.equal(getVisibleConstraintGlyphGroups(state,9000).length,0);
});

test('same related target transitions do not create timers and disposed states cannot retain',()=>{
  const state=create();retainDepartedConstraintGroups(state,target,icon,0);
  assert.equal(state.constraintHoverDeadlines,undefined);
  state.disposed=true;retainDepartedConstraintGroups(state,target,null,0);
  assert.equal(state.constraintHoverDeadlines,undefined);
});

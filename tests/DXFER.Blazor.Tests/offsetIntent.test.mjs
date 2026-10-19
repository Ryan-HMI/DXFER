import assert from 'node:assert/strict';
import test from 'node:test';
import { getConstraintGlyphIcon, getPersistentDimensionDescriptors, requestOffsetDragPreview } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

test('Offset shares its tool glyph and renders a true gap witness',()=>{
  assert.equal(getConstraintGlyphIcon('offset'),'offset');
  const dimension={id:'gap',kind:'Offset',referenceKeys:['a','b'],value:2,anchor:{x:4,y:2},isDriving:true,
    state:'Satisfied',offsetStart:{x:4,y:0},offsetEnd:{x:4,y:2}};
  const state={document:{entities:[],dimensions:[dimension]},view:{scale:10,offsetX:0,offsetY:0}};
  const descriptor=getPersistentDimensionDescriptors(state)[0];
  assert.equal(descriptor.label,'Offset distance');
  assert.deepEqual(descriptor.geometry,{type:'linear',start:dimension.offsetStart,end:dimension.offsetEnd,anchorPoint:dimension.anchor});
});

test('shared drag does not display a response computed with a superseded modifier mode',async()=>{
  const requests=[];
  const original={entities:[]};
  const drag={targetKey:'a',originalDocument:original,startWorldPoint:{x:0,y:0},currentWorldPoint:{x:1,y:1}};
  const state={disposed:true,canvas:{dataset:{}},geometryDrag:drag,document:original,
    dotnetRef:{invokeMethodAsync:(...args)=>new Promise(resolve=>requests.push({args,resolve}))}};
  requestOffsetDragPreview(state,drag);
  drag.constrainToCurrentVector=true;
  requestOffsetDragPreview(state,drag);
  requests[0].resolve({entities:['wrong modifier']});await new Promise(resolve=>setImmediate(resolve));
  assert.equal(state.document,original);
  requestOffsetDragPreview(state,drag);
  assert.equal(requests[1].args.at(-1),true);
  const projected={entities:['projected']};
  requests[1].resolve(projected);await new Promise(resolve=>setImmediate(resolve));
  assert.equal(state.document,projected);
});

test('shared-core drag preview advances during motion but discards released requests',async()=>{
  const requests=[];
  const original={entities:[]};
  const drag={targetKey:'a',originalDocument:original,startWorldPoint:{x:0,y:0},currentWorldPoint:{x:1,y:1}};
  const state={disposed:true,canvas:{dataset:{}},geometryDrag:drag,document:original,dotnetRef:{invokeMethodAsync:(...args)=>new Promise(resolve=>requests.push({args,resolve}))}};
  requestOffsetDragPreview(state,drag);
  drag.currentWorldPoint={x:2,y:2};requestOffsetDragPreview(state,drag);
  assert.equal(requests.length,1);
  const intermediate={entities:['intermediate']};
  requests[0].resolve(intermediate);await new Promise(resolve=>setImmediate(resolve));
  assert.equal(state.document,intermediate,'a continuous pointer must not starve completed previews');
  requestOffsetDragPreview(state,drag);
  const accepted={entities:['current']};requests[1].resolve(accepted);await new Promise(resolve=>setImmediate(resolve));
  assert.equal(state.document,accepted);
  drag.currentWorldPoint={x:3,y:3};requestOffsetDragPreview(state,drag);
  state.geometryDrag=null;requests[2].resolve({entities:['late']});await new Promise(resolve=>setImmediate(resolve));
  assert.equal(state.document,accepted);
});

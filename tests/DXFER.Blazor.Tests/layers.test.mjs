import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview, layerProtectedGeometryUnchanged } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

test('locked entity drag refuses preview; unlocked peer remains movable',()=>{
  const locked={id:'locked',kind:'line',points:[{x:0,y:0},{x:10,y:0}],isLocked:true};
  const free={id:'free',kind:'line',points:[{x:0,y:4},{x:10,y:4}],isLocked:false};
  const doc={entities:[locked,free],dimensions:[],constraints:[]};
  assert.equal(applyGeometryDragPreview(doc,'locked',{x:5,y:0},{x:5,y:2}),null);
  const preview=applyGeometryDragPreview(doc,'free',{x:5,y:4},{x:5,y:6});
  assert.ok(preview);
  assert.ok(layerProtectedGeometryUnchanged(doc,preview));
  assert.equal(layerProtectedGeometryUnchanged(doc,{entities:[free]}),false);
});

test('Equal radius propagation cannot move a locked layer peer; unlocking recovers',()=>{
  const doc={entities:[
    {id:'moving',kind:'circle',center:{x:0,y:0},radius:3},
    {id:'peer',kind:'circle',center:{x:10,y:0},radius:3,isLocked:true}],
    dimensions:[],constraints:[{id:'equal',kind:'equal',referenceKeys:['moving','peer'],state:'satisfied'}]};
  const original=structuredClone(doc);
  const key='moving|point|quadrant-0|3|0';
  assert.equal(applyGeometryDragPreview(doc,key,{x:3,y:0},{x:5,y:0}),null);
  assert.deepEqual(doc,original);
  doc.entities[1].isLocked=false;
  const preview=applyGeometryDragPreview(doc,key,{x:3,y:0},{x:5,y:0});
  assert.equal(preview.entities[0].radius,5);
  assert.equal(preview.entities[1].radius,5);
});

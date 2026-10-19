import assert from 'node:assert/strict';
import test from 'node:test';
import {getOffsetPreviewGeometry} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

test('contour preview advances during motion and keeps its input while a new distance is pending',async()=>{
    const requests=[];
    const state={disposed:true,document:{entities:[{id:'line',kind:'line',points:[{x:0,y:0},{x:10,y:0}]}]},
        selectedKeys:new Set(['line']),toolDraft:{dimensionValues:{}},view:{scale:10,offsetX:0,offsetY:0},
        dotnetRef:{invokeMethodAsync:(...args)=>new Promise(resolve=>requests.push({args,resolve}))}};
    const first={x:5,y:1},second={x:5,y:2};
    assert.equal(getOffsetPreviewGeometry(state,first).offsetPointSets.length,0);
    assert.equal(getOffsetPreviewGeometry(state,second).offsetPointSets.length,0);
    assert.equal(requests.length,1);
    const result={document:{entities:[{kind:'polyline',points:[{x:0,y:1},{x:10,y:1}]}]},anchor:{x:5,y:0},distance:1};
    requests[0].resolve(result);await new Promise(resolve=>setImmediate(resolve));
    const moving=getOffsetPreviewGeometry(state,second);
    assert.deepEqual(moving.offsetPointSets,[result.document.entities[0].points]);
    assert.ok(moving.dimension,'pending pointer must not remove the dimension input');
    assert.equal(requests.length,2);
    requests[1].resolve(result);await new Promise(resolve=>setImmediate(resolve));
    assert.deepEqual(getOffsetPreviewGeometry(state,second).offsetPointSets,[result.document.entities[0].points]);
    state.toolDraft.dimensionValues.offset=3;
    const typing=getOffsetPreviewGeometry(state,second);
    assert.equal(typing.offsetPointSets.length,0,'old keyed distance is not shown as current geometry');
    assert.ok(typing.dimension,'typing must retain the dimension input while recalculating');
    assert.equal(requests[2].args[3],3);
    requests[2].resolve(result);await new Promise(resolve=>setImmediate(resolve));
    state.document=structuredClone(state.document);
    assert.equal(getOffsetPreviewGeometry(state,second).offsetPointSets.length,0);
    requests[3].resolve(null);await new Promise(resolve=>setImmediate(resolve));
    assert.equal(getOffsetPreviewGeometry(state,second).offsetPointSets.length,0);
    assert.equal(requests.length,4,'failed previews do not retry forever at the same point');
});

test('offset preview ignores a prior draft and selection and retains the input after a rejected inset',async()=>{
    const requests=[];
    const source={entities:[{id:'a',kind:'line',points:[{x:0,y:0},{x:10,y:0}]},
      {id:'b',kind:'line',points:[{x:0,y:4},{x:10,y:4}]}]};
    const state={disposed:true,document:source,selectedKeys:new Set(['a']),toolDraft:{dimensionValues:{}},
      view:{scale:10,offsetX:0,offsetY:0},dotnetRef:{invokeMethodAsync:(...args)=>new Promise(resolve=>requests.push({args,resolve}))}};
    const point={x:5,y:1};
    const valid={document:{entities:[{kind:'polyline',points:[{x:0,y:1},{x:10,y:1}]}]},anchor:{x:5,y:0},distance:1};
    const settle=()=>new Promise(resolve=>setImmediate(resolve));
    getOffsetPreviewGeometry(state,point);
    state.toolDraft={dimensionValues:{}};
    requests[0].resolve(valid);await settle();
    assert.equal(getOffsetPreviewGeometry(state,point).dimension,null,'prior tool draft must not restore its preview');
    requests[1].resolve(valid);await settle();
    assert.ok(getOffsetPreviewGeometry(state,point).dimension);
    state.toolDraft.dimensionValues.offset=100;
    assert.ok(getOffsetPreviewGeometry(state,point).dimension);
    requests[2].resolve({document:null,anchor:null,distance:100});await settle();
    const refused=getOffsetPreviewGeometry(state,point);
    assert.equal(refused.offsetPointSets.length,0);
    assert.ok(refused.dimension,'a rejected inset must still allow editing its distance');
    assert.equal(requests.length,3,'no retry loop for refused result');
    state.selectedKeys=new Set(['b']);
    assert.equal(getOffsetPreviewGeometry(state,point).dimension,null,'selection changes cannot reuse the old witness');
    requests[3].resolve(valid);await settle();
    assert.ok(getOffsetPreviewGeometry(state,point).dimension);
});

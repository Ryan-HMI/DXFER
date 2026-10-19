import assert from 'node:assert/strict';
import test from 'node:test';
import {getOffsetPreviewGeometry} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

test('contour preview coalesces requests and never draws stale point, distance or document results',async()=>{
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
    assert.equal(getOffsetPreviewGeometry(state,second).offsetPointSets.length,0);
    assert.equal(requests.length,2);
    requests[1].resolve(result);await new Promise(resolve=>setImmediate(resolve));
    assert.deepEqual(getOffsetPreviewGeometry(state,second).offsetPointSets,[result.document.entities[0].points]);
    state.toolDraft.dimensionValues.offset=3;
    assert.equal(getOffsetPreviewGeometry(state,second).offsetPointSets.length,0);
    assert.equal(requests[2].args[3],3);
    requests[2].resolve(result);await new Promise(resolve=>setImmediate(resolve));
    state.document=structuredClone(state.document);
    assert.equal(getOffsetPreviewGeometry(state,second).offsetPointSets.length,0);
    requests[3].resolve(null);await new Promise(resolve=>setImmediate(resolve));
    assert.equal(getOffsetPreviewGeometry(state,second).offsetPointSets.length,0);
    assert.equal(requests.length,4,'failed previews do not retry forever at the same point');
});

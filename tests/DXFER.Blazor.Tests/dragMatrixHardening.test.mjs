import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview, updateGeometryDrag, finishGeometryDrag } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
const p=(x,y)=>({x,y});
const c=(id,kind,...referenceKeys)=>({id,kind,referenceKeys,state:'satisfied'});
const d=(kind,first,second)=>({id:'dimension',kind,referenceKeys:[first,second],value:10,isDriving:true,anchor:p(5,-2)});
const line={id:'edge',kind:'line',points:[p(0,0),p(10,0)]};
test('clicked polyline vertices retain driving length with stale coordinate suffixes',()=>{
    const original={entities:[{id:'poly',kind:'polyline',points:[p(0,0),p(10,0),p(10,6)]}],
        dimensions:[d('linearDistance','poly|point|vertex-0|0|0','poly|point|vertex-1|10|0')],constraints:[]};
    const next=applyGeometryDragPreview(original,'poly|point|vertex-1|10|0',p(10,0),p(10,2));
    assert.ok(next);assert.deepEqual(next.entities[0].points,[p(0,2),p(10,2),p(10,8)]);
    const again=applyGeometryDragPreview(next,'poly|point|vertex-1|10|2',p(10,2),p(11,4));
    assert.ok(again);assert.deepEqual(again.entities[0].points,[p(1,4),p(11,4),p(11,10)]);
});
test('missing clicked polyline vertex refuses rather than measuring the saved coordinates',()=>{
    const original={entities:[{id:'poly',kind:'polyline',points:[p(0,0),p(10,0),p(10,6)]}],
        dimensions:[d('linearDistance','poly|point|vertex-0|0|0','poly|point|vertex-9|10|0')],constraints:[]};
    assert.equal(applyGeometryDragPreview(original,'poly|point|vertex-1|10|0',p(10,0),p(10,2)),null);
});
for(const fixed of [false,true])test(`DRAG-LINE-002 perpendicular preview with fixed start=${fixed}`,()=>{
    const original={entities:[line],dimensions:[d('linearDistance','edge:start','edge:end')],constraints:fixed?[c('fix','fix','edge:start')]:[]};
    const copy=structuredClone(original);
    const next=applyGeometryDragPreview(original,'edge|point|end|10|0',p(10,0),p(10,3));
    assert.ok(next);
    const [a,b]=next.entities[0].points;
    assert.ok(Math.abs(Math.hypot(b.x-a.x,b.y-a.y)-10)<1e-7);
    assert.ok(b.y>0);
    if(fixed)assert.deepEqual(a,p(0,0));
    assert.equal(next.dimensions[0].value,10);
    assert.deepEqual(original,copy);
});
test('DRAG-POLYLINE-001 fixed peer preserves free vertex component',()=>{
    const original={entities:[{id:'poly',kind:'polyline',points:[p(0,0),p(10,0),p(14,5)],isClosed:false}],
        dimensions:[d('horizontalDistance','poly|segment|0:start','poly|segment|0:end')],
        constraints:[c('fix','fix','poly|segment|0:start'),c('fix-tail','fix','poly|segment|1:end')]};
    const copy=structuredClone(original);
    const next=applyGeometryDragPreview(original,'poly|point|vertex-1|10|0',p(10,0),p(13,3));
    assert.ok(next);
    assert.deepEqual(next.entities[0].points,[p(0,0),p(10,3),p(14,5)]);
    assert.deepEqual(original,copy);
});
for(const [x,y,accepted] of [[0,0,false],[1e-9,0,false],[1e-4,1e-4,true],[-10,1e-4,true],[10,-3,true]])
test(`anchored near-degenerate preview ${x},${y}`,()=>{
    const original={entities:[line],dimensions:[d('linearDistance','edge:start','edge:end')],constraints:[c('fix','fix','edge:start')]};
    const copy=structuredClone(original);
    const next=applyGeometryDragPreview(original,'edge|point|end|10|0',p(10,0),p(x,y));
    assert.equal(Boolean(next),accepted);
    if(next) {
        assert.deepEqual(next.entities[0].points[0],p(0,0));
        assert.ok(Math.abs(Math.hypot(next.entities[0].points[1].x,next.entities[0].points[1].y)-10)<1e-7);
    }
    assert.deepEqual(original,copy);
    assert.ok(applyGeometryDragPreview(original,'edge|point|end|10|0',p(10,0),p(10,3)));
});
for(const reverse of [false,true])test(`fixed mixed weld preview refusal and recovery reverse=${reverse}`,()=>{
    const constraints=[c('join','coincident','edge:end','point'),c('fix','fix','point')];
    const original={entities:[line,{id:'point',kind:'point',points:[p(10,0)]}],dimensions:[d('linearDistance','edge:start','edge:end')],
        constraints:reverse?constraints.reverse():constraints};
    const copy=structuredClone(original);
    assert.equal(applyGeometryDragPreview(original,'edge|point|end|10|0',p(10,0),p(13,3)),null);
    assert.deepEqual(original,copy);
    assert.ok(applyGeometryDragPreview({...original,constraints:constraints.filter(x=>x.kind!=='fix')},'edge|point|end|10|0',p(10,0),p(13,3)));
});
test('anchored actual pointer lifecycle recovers after rejected frame and releases a valid request',()=>{
    const original={entities:[line],dimensions:[d('linearDistance','edge:start','edge:end')],constraints:[c('fix','fix','edge:start')]};
    const calls=[];
    const state={activeTool:'select',acquiredSnapPoints:[],document:original,hoveredTarget:null,selectedKeys:new Set(),activeSelectionKey:null,
        geometryDrag:{pointerId:1,targetKey:'edge|point|end|10|0',startScreenPoint:p(100,0),startWorldPoint:p(10,0),originalDocument:original,moved:true},
        view:{scale:10,offsetX:0,offsetY:0},canvas:{dataset:{},getBoundingClientRect:()=>({left:0,top:0,width:800,height:600})},
        dotnetRef:{invokeMethodAsync:(...args)=>{calls.push(args);return Promise.resolve(true);}}};
    assert.equal(updateGeometryDrag(state,p(100,-30)),true);
    assert.equal(updateGeometryDrag(state,p(0,0)),false);
    assert.equal(state.document,original);
    assert.equal(updateGeometryDrag(state,p(100,-30)),true);
    const preview=structuredClone(state.document);
    assert.equal(finishGeometryDrag(state,p(100,-30)),true);
    assert.deepEqual(state.document,preview);
    assert.equal(state.geometryDrag,null);
    assert.equal(calls.filter(x=>x[0]==='OnGeometryDragRequested').length,1);
});
for(const reverse of [false,true])test(`DRAG-COINCIDENT-001 dimensioned mixed weld reverse=${reverse}`,()=>{
    const constraints=[c('join-poly','coincident','edge:end','poly|segment|0:start'),c('join-point','coincident','poly|segment|0:start','point')];
    const original={entities:[line,{id:'poly',kind:'polyline',points:[p(10,0),p(12,2),p(14,2)],isClosed:false},
        {id:'point',kind:'point',points:[p(10,0)]}],dimensions:[d('linearDistance','edge:start','edge:end')],
        constraints:reverse?constraints.reverse():constraints};
    const copy=structuredClone(original);
    const next=applyGeometryDragPreview(original,'edge|point|end|10|0',p(10,0),p(13,0));
    assert.ok(next);
    assert.deepEqual(next.entities[0].points,[p(3,0),p(13,0)]);
    assert.deepEqual(next.entities[1].points[0],p(13,0));
    assert.deepEqual(next.entities[2].points[0],p(13,0));
    assert.deepEqual(original,copy);
});

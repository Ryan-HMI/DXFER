import test from 'node:test';
import assert from 'node:assert/strict';
import {applyGeometryDragPreview, getCreationSnapIntents, getDynamicSketchSnapHit, rememberAcquiredSnapPoint} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const point=(x,y)=>({x,y});
const line=(id,start=point(0,0),end=point(10,0))=>({id,kind:'line',start,end});
const target=(entity,label,p)=>({entity,entityId:entity.id,kind:'point',label,
  key:`${entity.id}|point|${label}|${p.x}|${p.y}`,point:p});
const intent=(p,kind,...keys)=>keys.map(key=>({point:p,kind,referenceKeys:[key]}));

test('source endpoints use stable references instead of stored coordinates',()=>{
  const source=line('source'),p=point(10,0);
  assert.deepEqual(getCreationSnapIntents({},target(source,'end',p),p),intent(p,'Coincident','source:end'));
});

test('line and polyline midpoints retain the whole source segment',()=>{
  const p=point(5,0);
  assert.deepEqual(getCreationSnapIntents({},target(line('a'),'mid',p),p),intent(p,'Midpoint','a'));
  assert.deepEqual(getCreationSnapIntents({},target({id:'poly',kind:'polyline'},'mid-2',p),p),intent(p,'Midpoint','poly|segment|2'));
});

test('accepted line body snap records point-on-line, not a fixed coordinate',()=>{
  const p=point(3,0),source=line('a');
  assert.deepEqual(getCreationSnapIntents({}, {entity:source,entityId:'a',kind:'entity',key:'a',snapPoint:p},p),intent(p,'PointOnLine','a'));
});

test('line intersection retains both source references',()=>{
  const a=line('a'),b=line('b',point(3,-2),point(3,2)),p=point(3,0);
  assert.deepEqual(getCreationSnapIntents({document:{entities:[a,b]}},target(a,'intersection-b-0',p),p),intent(p,'PointOnLine','a','b'));
});

test('numeric override away from the cue must not attach stale snap intent',()=>{
  const source=line('a');
  assert.deepEqual(getCreationSnapIntents({},target(source,'start',point(0,0)),point(2,3)),[]);
});

test('intersection ids that share a prefix cannot bind the wrong source',()=>{
  const a=line('a'),b=line('b'),actual=line('b-long'),p=point(3,0);
  assert.deepEqual(getCreationSnapIntents({document:{entities:[a,b,actual]}},target(a,'intersection-b-long-0',p),p),intent(p,'PointOnLine','a','b-long'));
});

test('accepted projected alignment carries its source through the dynamic snap',()=>{
  const source=line('a'),p=point(0,3);
  const state={activeTool:'point',document:{entities:[source]},toolDraft:{points:[],dimensionValues:{}},
    acquiredSnapPoints:[{key:'a|point|start|0|0',label:'start',point:point(0,0),acquiredAt:Date.now()}],
    selectedKeys:new Set(),view:{scale:30,offsetX:400,offsetY:400}};
  const hit=getDynamicSketchSnapHit(state,point(400,310));
  assert.ok(hit);
  assert.deepEqual(getCreationSnapIntents(state,hit.target,p),intent(p,'Vertical','a:start'));
  state.acquiredSnapPoints=[{key:'point',referenceKey:'point',label:'point',point:point(0,0),acquiredAt:Date.now()}];
  const pointHit=getDynamicSketchSnapHit(state,point(400,310));
  assert.deepEqual(getCreationSnapIntents(state,pointHit.target,p),intent(p,'Vertical','point'));
});

for (const [label,source,key] of [
  ['mid',line('a'), 'a:mid'],
  ['mid-0',{id:'poly',kind:'polyline',points:[point(0,0),point(10,0),point(10,5)]}, 'poly|segment|0:mid']
]) for (const [kind,p] of [['Horizontal',point(8,0)],['Vertical',point(5,3)]]) {
  test(`hover-acquired ${label} persists ${kind} against the live midpoint`,()=>{
    const state={activeTool:'point',document:{entities:[source]},toolDraft:{points:[],dimensionValues:{}},
      acquiredSnapPoints:[],selectedKeys:new Set(),view:{scale:30,offsetX:400,offsetY:400}};
    rememberAcquiredSnapPoint(state,target(source,label,point(5,0)));
    const hit=getDynamicSketchSnapHit(state,point(400+p.x*30,400-p.y*30));
    assert.ok(hit);
    assert.deepEqual(getCreationSnapIntents(state,hit.target,p),intent(p,kind,key));
  });
}

for (const kind of ['horizontal','vertical']) for (const polyline of [false,true]) {
  test(`${kind} midpoint guide follows source edits in held preview, polyline=${polyline}`,()=>{
    const source={id:'source',kind:polyline?'polyline':'line',points:[point(2,3),point(6,3)]};
    const added={id:'point',kind:'point',points:[kind==='horizontal'?point(7,3):point(4,5)]};
    const reference=polyline?'source|segment|0:mid':'source:mid';
    const document={entities:[source,added],dimensions:[],constraints:[
      {id:'guide',kind,state:'satisfied',referenceKeys:[reference,'point']}]};
    const before=structuredClone(document);
    const end=kind==='horizontal'?point(6,5):point(8,3);
    const preview=applyGeometryDragPreview(document,`source|point|${polyline?'vertex-1':'end'}|6|3`,point(6,3),end);
    assert.ok(preview);
    assert.deepEqual(preview.entities[1].points[0],kind==='horizontal'?point(7,4):point(5,5));
    assert.deepEqual(document,before);
  });
}

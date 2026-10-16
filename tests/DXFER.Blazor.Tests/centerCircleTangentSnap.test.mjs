import test from 'node:test';
import assert from 'node:assert/strict';
import { getDynamicSketchSnapHit, findNearestTarget, createDrawingCanvas } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
const p=(x,y)=>({x,y});
const line={id:'edge',kind:'line',points:[p(0,0),p(20,0)]};
const circle={id:'curve',kind:'circle',center:p(0,0),radius:3};
const stateOf=(entities,center)=>({activeTool:'centercircle',document:{entities,dimensions:[],constraints:[]},
    toolDraft:{points:[center],dimensionValues:{}},acquiredSnapPoints:[],selectedKeys:new Set(),
    view:{scale:20,offsetX:100,offsetY:500}});
const screen=(state,point)=>p(point.x*state.view.scale+state.view.offsetX,-point.y*state.view.scale+state.view.offsetY);
const close=(a,b)=>assert.ok(Math.hypot(a.x-b.x,a.y-b.y)<1e-8);
for(const [name,entity,center,contact,key] of [
    ['line',line,p(7,5),p(7,0),'edge'],
    ['midpoint',line,p(10,5),p(10,0),'edge'],
    ['endpoint',line,p(0,5),p(0,0),'edge'],
    ['reversed',{...line,points:[...line.points].reverse()},p(7,5),p(7,0),'edge'],
    ['rotated',{...line,points:[p(0,0),p(12,16)]},p(.6,5.8),p(3,4),'edge'],
    ['polyline',{...line,kind:'polyline',points:[p(-5,-5),p(0,0),p(20,0)]},p(7,5),p(7,0),'edge|segment|1'],
    ['external circle',circle,p(8,0),p(3,0),'curve'],
    ['enclosing circle',circle,p(8,0),p(-3,0),'curve'],
    ['internal circle',circle,p(1,0),p(3,0),'curve'],
    ['rotated circle',circle,p(4.8,6.4),p(1.8,2.4),'curve'],
    ['arc',{...circle,kind:'arc',startAngleDegrees:0,endAngleDegrees:90},p(4.8,6.4),p(1.8,2.4),'curve']
]) test(`${name} radius contact carries precise tangent reference through picking`,()=>{
    const state=stateOf([entity],center),point=screen(state,contact);
    const snap=getDynamicSketchSnapHit(state,point);
    assert.equal(snap?.target.tangentReferenceKey,key);close(snap.target.point,contact);
    assert.equal(findNearestTarget(state,point)?.tangentReferenceKey,key);
    assert.equal(getDynamicSketchSnapHit(state,p(point.x+20,point.y+20)),null);
});
for(const [name,entities,center,contact] of [
    ['outside segment',[line],p(25,5),p(25,0)],
    ['zero radius',[line],p(7,0),p(7,0)],
    ['zero edge',[{...line,points:[p(0,0),p(0,0)]}],p(0,5),p(0,0)],
    ['coincident centers',[circle],p(0,0),p(3,0)],
    ['outside arc sweep',[{...circle,kind:'arc',startAngleDegrees:90,endAngleDegrees:180}],p(8,0),p(3,0)],
    ['spline is not a polyline',[{...line,kind:'spline'}],p(7,5),p(7,0)]
]) test(`${name} does not offer a false tangent`,()=>{
    const state=stateOf(entities,center);assert.equal(getDynamicSketchSnapHit(state,screen(state,contact)),null);
});
for(const takeSnap of [true,false]) test(`registered canvas commits ${takeSnap?'explicit tangent reference':'no relation for ordinary edge click'}`,t=>{
    const previous=globalThis.window;globalThis.window={devicePixelRatio:1,addEventListener(){},removeEventListener(){}};
    const callbacks=[],handlers=new Map();
    const context=new Proxy({measureText:text=>({width:String(text).length*7})},{get:(o,k)=>k in o?o[k]:()=>{}});
    const canvas={clientWidth:1000,clientHeight:800,dataset:{},style:{},getContext:()=>context,
        getBoundingClientRect:()=>({left:0,top:0,width:1000,height:800}),
        addEventListener:(k,v)=>handlers.set(k,v),removeEventListener(){},focus(){},setPointerCapture(){},releasePointerCapture(){}};
    const instance=createDrawingCanvas(canvas,{invokeMethodAsync:(method,...args)=>{callbacks.push({method,args});return Promise.resolve();}});
    t.after(()=>{instance.dispose();if(previous===undefined)delete globalThis.window;else globalThis.window=previous;});
    instance.setDocument({entities:[line],dimensions:[],constraints:[],bounds:{minX:0,minY:0,maxX:20,maxY:10}},true);
    instance.setActiveTool('centercircle');
    const click=point=>{
        const event={clientX:point.x*+canvas.dataset.scale+ +canvas.dataset.offsetX,clientY:-point.y*+canvas.dataset.scale+ +canvas.dataset.offsetY,
            pointerId:1,pointerType:'mouse',button:0,buttons:0,preventDefault(){}};
        handlers.get('pointermove')(event);handlers.get('pointerdown')(event);handlers.get('pointerup')(event);
    };
    click(p(7,5));click(p(takeSnap?7:9,0));
    const commit=callbacks.find(c=>c.method==='OnSketchToolCommitted');assert.ok(commit);
    assert.equal(commit.args[4],takeSnap?'edge':null);
});

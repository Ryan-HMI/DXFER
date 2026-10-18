import test from 'node:test';
import assert from 'node:assert/strict';
import {createDrawingCanvas} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

function harness(t, tool) {
  const previous=globalThis.window, handlers=new Map(), calls=[];
  globalThis.window={devicePixelRatio:1,addEventListener:(key,handler)=>handlers.set(key,handler),removeEventListener(){}};
  const context=new Proxy({createRadialGradient:()=>({addColorStop(){}}),measureText:text=>({width:String(text).length*7})},{get:(o,k)=>k in o?o[k]:()=>{}});
  const canvas={clientWidth:1000,clientHeight:800,dataset:{},style:{},getContext:()=>context,
    getBoundingClientRect:()=>({left:0,top:0,width:1000,height:800}),
    addEventListener:(key,handler)=>handlers.set(key,handler),removeEventListener(){},focus(){},setPointerCapture(){},releasePointerCapture(){}};
  const instance=createDrawingCanvas(canvas,{invokeMethodAsync:(...args)=>{calls.push(args);return Promise.resolve(true);}});
  instance.setDocument({entities:[],dimensions:[],constraints:[],bounds:{minX:0,minY:0,maxX:20,maxY:20}},true);
  instance.setActiveTool(tool);
  const pointer=(type,x,y,extra={})=>handlers.get(type)({clientX:x,clientY:y,pointerId:1,pointerType:'mouse',button:0,buttons:type==='pointerup'?0:1,preventDefault(){},...extra});
  const click=(x,y,moving=false,cancel=false)=>{
    pointer('pointermove',x-9,y-6,{buttons:0});
    pointer('pointerdown',x,y);
    if(moving)pointer('pointermove',x+18,y+13);
    pointer(cancel?'pointercancel':'pointerup',moving?x+18:x,moving?y+13:y);
  };
  t.after(()=>{instance.dispose();if(previous===undefined)delete globalThis.window;else globalThis.window=previous;});
  return {calls,click,pointer,canvas,instance,handlers,commits:()=>calls.filter(c=>c[0]==='OnSketchToolCommitted')};
}

for(const [tool,points] of [
  ['point',[[300,300]]],['line',[[300,300],[500,400]]],
  ['twopointrectangle',[[300,300],[500,400]]],['centerrectangle',[[300,300],[500,400]]],
  ['centercircle',[[300,300],[500,400]]],['threepointcircle',[[300,300],[500,300],[400,450]]],
  ['ellipse',[[300,300],[500,300],[400,450]]],['threepointarc',[[300,300],[500,300],[400,450]]],
  ['slot',[[300,300],[500,300],[400,450]]]
]) test(`${tool}: moving press/release commits the same points once as stationary clicks`,t=>{
  const h=harness(t,tool);
  for(const [x,y] of points)h.click(x,y);
  const expected=h.commits();assert.equal(expected.length,1);
  h.instance.setActiveTool('select');h.instance.setActiveTool(tool);h.calls.length=0;
  for(const [x,y] of points)h.click(x,y,true);
  assert.deepEqual(h.commits(),expected);
});

test('cancelled moving creation contributes no point; next gesture recovers',t=>{
  const h=harness(t,'line');h.click(300,300,true,true);h.click(400,400,true);
  assert.equal(h.commits().length,0);h.click(600,500,true);assert.equal(h.commits().length,1);
});

test('Escape during moving creation prevents release committing a point',t=>{
  const h=harness(t,'point');h.pointer('pointerdown',300,300);h.pointer('pointermove',320,320);
  h.handlers.get('keydown')({key:'Escape',target:h.canvas,preventDefault(){}});
  h.pointer('pointerup',320,320);assert.equal(h.commits().length,0);assert.equal(h.canvas.dataset.activeTool,'select');
});

test('middle-button panning does not create geometry',t=>{
  const h=harness(t,'point');h.pointer('pointerdown',300,300,{button:1,buttons:4});
  h.pointer('pointermove',320,320,{button:1,buttons:4});h.pointer('pointerup',320,320,{button:1});
  assert.equal(h.commits().length,0);
});

test('moving origin click keeps exact snap and records origin intent once',t=>{
  const h=harness(t,'line'),d=h.canvas.dataset,x=+d.offsetX,y=+d.offsetY;
  h.click(x,y,true);h.click(x+100,y-80,true);
  const [commit]=h.commits();assert.ok(commit);assert.deepEqual(commit[2].slice(0,2),[0,0]);
  assert.equal(commit[6],true);h.pointer('pointerup',x+120,y-70);assert.equal(h.commits().length,1);
});

test('changing tools during a held creation click cannot commit the old point',t=>{
  const h=harness(t,'point');h.pointer('pointerdown',300,300);h.pointer('pointermove',320,320);
  h.instance.setActiveTool('centercircle');h.pointer('pointerup',320,320);
  h.click(500,400);assert.equal(h.commits().length,0);
});

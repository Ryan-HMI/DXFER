import test from 'node:test';
import assert from 'node:assert/strict';
import { createPatternCardLayout } from '../../src/DXFER.Blazor/wwwroot/canvas/patternFloatingCard.js';

function fixture() {
  const handlers = new Map(), captures = new Set();
  const canvas = {offsetWidth:1000,offsetHeight:700,getBoundingClientRect:()=>({left:20,top:30,width:1000,height:700})};
  const header = {closest: selector => selector === '[data-pattern-card-drag]' ? header : null};
  const card = {offsetWidth:216,offsetHeight:140,style:{},contains:t=>t===header,
    addEventListener:(name,fn)=>handlers.set(name,fn),removeEventListener:name=>handlers.delete(name),
    setPointerCapture:id=>captures.add(id),hasPointerCapture:id=>captures.has(id),releasePointerCapture:id=>captures.delete(id)};
  const widget = {querySelector:()=>card};
  const layout = createPatternCardLayout(canvas);
  const size = {width:1000,height:700};
  const event = (type, data={}) => {
    const e = {target:header,pointerId:1,button:0,clientX:520,clientY:380,preventDefault(){this.prevented=true;},
      stopPropagation(){this.stopped=true;},...data};
    handlers.get(type)?.(e);return e;
  };
  return {layout,canvas,card,widget,size,event,handlers,captures,header};
}

test('initial placement is slightly off viewport center and independent of geometry',()=>{
  const h=fixture(),box=h.layout.place(h.widget,h.size,[]);
  assert.equal(box.x+box.width/2,532);assert.equal(box.y+box.height/2,374);
  assert.equal(h.card.style.visibility,'visible');
  h.layout.dispose();assert.equal(h.handlers.size,0);
});

test('header pointer capture moves card only and retains chosen coordinates across redraw/hide/show',()=>{
  const h=fixture(),original=h.layout.place(h.widget,h.size,[]);
  h.event('pointerdown');assert.equal(h.captures.size,1);
  h.event('pointermove',{clientX:650,clientY:440});h.event('pointerup',{clientX:650,clientY:440});
  assert.equal(h.captures.size,0);
  const moved=h.layout.place(h.widget,h.size,[]);
  assert.equal(moved.x,original.x+130);assert.equal(moved.y,original.y+60);
  h.layout.place(null,h.size,[]);
  assert.deepEqual(h.layout.place(h.widget,h.size,[{x:0,y:0,width:900,height:500}]),moved);
  h.layout.dispose();
});

test('inputs and non-header controls cannot drag or consume pointer events',()=>{
  const h=fixture(),original=h.layout.place(h.widget,h.size,[]);
  const e=h.event('pointerdown',{target:{closest:()=>null}});
  assert.equal(e.prevented,undefined);assert.equal(h.captures.size,0);
  h.event('pointermove',{clientX:990,clientY:690});
  assert.deepEqual(h.layout.place(h.widget,h.size,[]),original);h.layout.dispose();
});

test('viewport clamping keeps full card reachable on drag and resize',()=>{
  const h=fixture();h.layout.place(h.widget,h.size,[]);
  h.event('pointerdown');h.event('pointermove',{clientX:5000,clientY:5000});h.event('pointerup',{clientX:5000,clientY:5000});
  const edge=h.layout.place(h.widget,h.size,[]);
  assert.equal(edge.x,776);assert.equal(edge.y,552);
  const small=h.layout.place(h.widget,{width:390,height:300},[]);
  assert.equal(small.x,166);assert.equal(small.y,152);h.layout.dispose();
});

for(const cancellation of ['pointercancel','lostpointercapture','Escape']) test(`${cancellation} rolls back only card movement`,()=>{
  const h=fixture(),original=h.layout.place(h.widget,h.size,[]);
  h.event('pointerdown');h.event('pointermove',{clientX:800,clientY:500});
  if(cancellation==='Escape') {const e=h.event('keydown',{key:'Escape'});assert.equal(e.stopped,true);}
  else h.event(cancellation);
  assert.deepEqual(h.layout.place(h.widget,h.size,[]),original);assert.equal(h.captures.size,0);h.layout.dispose();
});

test('header arrow keys move locally; field arrows and Escape outside a card drag remain native',()=>{
  const h=fixture(),original=h.layout.place(h.widget,h.size,[]);
  h.event('keydown',{key:'ArrowRight'});h.event('keydown',{key:'ArrowDown',shiftKey:true});
  const moved=h.layout.place(h.widget,h.size,[]);
  assert.equal(moved.x,original.x+8);assert.equal(moved.y,original.y+1);
  assert.equal(h.event('keydown',{key:'ArrowRight',target:{closest:()=>null}}).prevented,undefined);
  assert.equal(h.event('keydown',{key:'Escape'}).stopped,undefined);h.layout.dispose();
});

test('scaled canvas uses layout pixels and ignores a second pointer during capture',()=>{
  const h=fixture();h.canvas.getBoundingClientRect=()=>({left:20,top:30,width:2000,height:1400});
  const original=h.layout.place(h.widget,h.size,[]);h.event('pointerdown');
  h.event('pointermove',{pointerId:2,clientX:1200});
  h.event('pointermove',{clientX:620,clientY:480});h.event('pointerup',{clientX:620,clientY:480});
  const moved=h.layout.place(h.widget,h.size,[]);
  assert.equal(moved.x,original.x+50);assert.equal(moved.y,original.y+50);h.layout.dispose();
});

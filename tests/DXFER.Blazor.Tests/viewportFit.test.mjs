import test from 'node:test';
import assert from 'node:assert/strict';
import { getUnobscuredViewport } from '../../src/DXFER.Blazor/wwwroot/canvas/viewportFit.js';
import { getFitViewForDocument } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

test('no palettes preserves the full viewport', () => {
  assert.deepEqual(getUnobscuredViewport({width:1000,height:600}), {x:0,y:0,width:1000,height:600});
});

test('side docks and top/bottom panels leave a clear fit rectangle', () => {
  assert.deepEqual(getUnobscuredViewport({width:1000,height:600}, [
    {x:0,y:0,width:200,height:600}, {x:900,y:0,width:100,height:600},
    {x:0,y:0,width:1000,height:50}, {x:0,y:550,width:1000,height:50}
  ]), {x:200,y:50,width:700,height:500});
});

test('floating panel excludes its occupied area without reserving the whole row', () => {
  const panel={x:400,y:200,width:200,height:200};
  const viewport=getUnobscuredViewport({width:1000,height:600},[panel]);
  assert.equal(viewport.width*viewport.height,240000);
  assert.ok(viewport.x+viewport.width<=panel.x || viewport.x>=panel.x+panel.width
    || viewport.y+viewport.height<=panel.y || viewport.y>=panel.y+panel.height);
});

test('overlapping, offscreen, zero-size and invalid panels are safely clipped', () => {
  assert.deepEqual(getUnobscuredViewport({width:320,height:600},[
    {x:-20,y:-40,width:180,height:700},{x:0,y:20,width:140,height:100},
    {x:500,y:0,width:200,height:600},{x:0,y:0,width:0,height:600},
    {x:NaN,y:0,width:320,height:600}
  ]), {x:160,y:0,width:160,height:600});
});

test('fully covered canvas does not claim an unobscured viewport', () => {
  assert.equal(getUnobscuredViewport({width:100,height:100},[{x:0,y:0,width:100,height:100}]),null);
});

test('fit transforms are centered inside the clear region, not behind a dock', () => {
  const doc={entities:[{id:'line',kind:'line',start:{x:0,y:0},end:{x:10,y:5}}],bounds:{minX:0,minY:0,maxX:10,maxY:5}};
  const rect={x:240,y:50,width:720,height:500};
  const view=getFitViewForDocument(doc,{width:1000,height:600},rect);
  assert.ok(view.offsetX>=rect.x);
  assert.ok(view.offsetX+10*view.scale<=rect.x+rect.width);
  assert.ok(view.offsetY-5*view.scale>=rect.y);
  assert.ok(view.offsetY<=rect.y+rect.height);
  assert.equal(view.offsetX+5*view.scale,rect.x+rect.width/2);
  assert.equal(view.offsetY-2.5*view.scale,rect.y+rect.height/2);
});

test('blank sketch origin uses the clear area center without changing sketch scale', () => {
  assert.deepEqual(getFitViewForDocument({entities:[]},{width:1000,height:600},
    {x:250,y:0,width:750,height:600}),{scale:24,offsetX:625,offsetY:300});
});

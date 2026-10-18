import test from 'node:test';
import assert from 'node:assert/strict';
import {normalizeCanvasBackground, isLightCanvasBackground, loadCanvasBackground, applyCanvasBackground, DEFAULT_CANVAS_BACKGROUND, CANVAS_BACKGROUND_KEY} from '../../src/DXFER.Blazor/wwwroot/canvasAppearance.js';
import {getDimensionRenderStyle} from '../../src/DXFER.Blazor/wwwroot/canvas/dimensionPresentation.js';

test('only six-digit hex backgrounds are stored/rendered',()=>{
  assert.equal(normalizeCanvasBackground('#ABC123'),'#abc123');
  for(const value of [null,undefined,'red','#fff','#12345678','url(x)',' #ffffff',{},42])
    assert.equal(normalizeCanvasBackground(value),DEFAULT_CANVAS_BACKGROUND);
  assert.equal(isLightCanvasBackground('#ffffff'),true);
  assert.equal(isLightCanvasBackground('#262626'),false);
});
test('missing, invalid and blocked storage use the neutral default',t=>{
  const prior=globalThis.localStorage;t.after(()=>{if(prior===undefined)delete globalThis.localStorage;else globalThis.localStorage=prior;});
  for(const value of [null,'invalid','#123abc']) {
    globalThis.localStorage={getItem:key=>{assert.equal(key,CANVAS_BACKGROUND_KEY);return value;}};
    assert.equal(loadCanvasBackground(),value==='#123abc'?value:DEFAULT_CANVAS_BACKGROUND);
  }
  globalThis.localStorage={getItem(){throw Error('blocked');}};
  assert.equal(loadCanvasBackground(),DEFAULT_CANVAS_BACKGROUND);
});
test('view preference changes CSS and redraw signal only, even with blocked storage',t=>{
  const prior=globalThis.localStorage;t.after(()=>{if(prior===undefined)delete globalThis.localStorage;else globalThis.localStorage=prior;});
  globalThis.localStorage={setItem(){throw Error('blocked');}};
  const properties={},events=[];
  const canvas={dataset:{},dispatchEvent:event=>events.push(event.type)};
  const root={style:{setProperty:(k,v)=>properties[k]=v},querySelectorAll:()=>[canvas]};
  assert.equal(applyCanvasBackground({closest:()=>root},'#ffffff',true),'#ffffff');
  assert.equal(properties['--dx-canvas-label'],'#202020');
  assert.equal(canvas.dataset.backgroundColor,'#ffffff');
  assert.deepEqual(events,['dxfer-background-changed']);
});
test('light-background dimension states use dark inks without changing line semantics',()=>{
  for(const state of [null,{selected:true},{active:true},{unsatisfied:true},{hovered:true}]) {
    const dark=getDimensionRenderStyle(false,state),light=getDimensionRenderStyle(false,state,true);
    assert.equal(light.lineWidth,dark.lineWidth);
    assert.deepEqual(light.lineDash,dark.lineDash);
    assert.ok(['#202020','#005a75','#a51628'].includes(light.strokeStyle));
    assert.equal(light.glow,false);
  }
  assert.equal(getDimensionRenderStyle(true,null,true).strokeStyle,'#805800');
});

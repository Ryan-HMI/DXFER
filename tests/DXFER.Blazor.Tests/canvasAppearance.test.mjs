import test from 'node:test';
import assert from 'node:assert/strict';
import {normalizeCanvasBackground, isLightCanvasBackground, loadCanvasAppearance, applyCanvasAppearance, normalizeCanvasAppearance, panelColors, paintCanvasBackground, FOUNDRY_BACKGROUND, DEFAULT_CANVAS_BACKGROUND, CANVAS_BACKGROUND_KEY, CANVAS_APPEARANCE_KEY} from '../../src/DXFER.Blazor/wwwroot/canvasAppearance.js';
import {getDimensionRenderStyle} from '../../src/DXFER.Blazor/wwwroot/canvas/dimensionPresentation.js';

test('only six-digit hex backgrounds are stored/rendered',()=>{
  assert.equal(normalizeCanvasBackground('#ABC123'),'#abc123');
  for(const value of [null,undefined,'red','#fff','#12345678','url(x)',' #ffffff',{},42])
    assert.equal(normalizeCanvasBackground(value),DEFAULT_CANVAS_BACKGROUND);
  assert.equal(isLightCanvasBackground('#ffffff'),true);
  assert.equal(isLightCanvasBackground('#262626'),false);
});
test('missing, invalid and previous neutral default restore Foundry; explicit custom colors survive migration',t=>{
  const prior=globalThis.localStorage;t.after(()=>{if(prior===undefined)delete globalThis.localStorage;else globalThis.localStorage=prior;});
  for(const value of [null,'invalid','#262626','#123abc']) {
    globalThis.localStorage={getItem:key=>key===CANVAS_BACKGROUND_KEY?value:null};
    assert.deepEqual(loadCanvasAppearance(),value==='#123abc'?{preset:'custom',color:value,panelColor:'#10141b'}:{preset:'foundry',color:'#172832',panelColor:'#10141b'});
  }
  globalThis.localStorage={getItem(){throw Error('blocked');}};
  assert.deepEqual(loadCanvasAppearance(),{preset:'foundry',color:'#172832',panelColor:'#10141b'});
});
test('view preference changes CSS and redraw signal only, even with blocked storage',t=>{
  const prior=globalThis.localStorage;t.after(()=>{if(prior===undefined)delete globalThis.localStorage;else globalThis.localStorage=prior;});
  globalThis.localStorage={setItem(){throw Error('blocked');}};
  const properties={},events=[];
  const canvas={dataset:{},dispatchEvent:event=>events.push(event.type)};
  const root={style:{setProperty:(k,v)=>properties[k]=v},querySelectorAll:()=>[canvas]};
  assert.deepEqual(applyCanvasAppearance({closest:()=>root},{preset:'custom',color:'#ffffff'},true),{preset:'custom',color:'#ffffff',panelColor:'#10141b'});
  assert.equal(properties['--dx-canvas-label'],'#202020');
  assert.equal(canvas.dataset.backgroundColor,'#ffffff');
  assert.deepEqual(events,['dxfer-background-changed']);
});
test('Foundry and neutral presets render their exact backgrounds and persist independently',t=>{
  const prior=globalThis.localStorage;t.after(()=>{if(prior===undefined)delete globalThis.localStorage;else globalThis.localStorage=prior;});
  const stored=new Map();globalThis.localStorage={getItem:key=>stored.get(key),setItem:(key,value)=>stored.set(key,value)};
  const properties={},canvas={dataset:{},dispatchEvent(){}};
  const control={closest:()=>({style:{setProperty:(k,v)=>properties[k]=v},querySelectorAll:()=>[canvas]})};
  for(const preset of ['foundry','neutral']) {
    applyCanvasAppearance(control,{preset},true);
    assert.equal(properties['--dx-canvas-background'],preset==='foundry'?FOUNDRY_BACKGROUND:'#202020');
    assert.equal(loadCanvasAppearance().preset,preset);
    assert.equal(canvas.dataset.backgroundPreset,preset);
    assert.ok(stored.has(CANVAS_APPEARANCE_KEY));
  }
  for(const input of [null,'url(x)',{preset:'unknown'},{preset:'custom',color:'url(x)'}])
    assert.deepEqual(normalizeCanvasAppearance(input),{preset:'foundry',color:'#172832',panelColor:'#10141b'});
});
test('panel colors are validated and shared surfaces adapt text contrast',()=>{
  assert.equal(normalizeCanvasAppearance({preset:'neutral',panelColor:'url(x)'}).panelColor,'#181818');
  assert.equal(panelColors('#ffffff').text,'#181818');
  assert.equal(panelColors('#000000').text,'#f4f7fb');
  const properties={},canvas={dataset:{},style:{},dispatchEvent(){}};
  const root={style:{setProperty:(k,v)=>properties[k]=v},querySelectorAll:()=>[canvas]};
  applyCanvasAppearance({ownerDocument:{documentElement:root}},{preset:'foundry',panelColor:'#eeeeee'});
  assert.equal(properties['--dx-panel'],'#eeeeee');
  assert.equal(properties['--dx-text'],'#181818');
  assert.equal(properties['--dx-canvas-background'],FOUNDRY_BACKGROUND);
  assert.equal(canvas.dataset.backgroundPreset,'foundry');
});
test('native controls follow panel input surfaces independently of canvas color',()=>{
  const properties={};
  const root={style:{setProperty:(k,v)=>properties[k]=v},querySelectorAll:()=>[]};
  const control={ownerDocument:{documentElement:root}};
  for(const [appearance,expected] of [
    [{preset:'foundry'},'dark'],
    [{preset:'neutral'},'dark'],
    [{preset:'custom',color:'#ffffff',panelColor:'#10141b'},'dark'],
    [{preset:'foundry',panelColor:'#eeeeee'},'light'],
    [{preset:'custom',color:'#000000',panelColor:'#ffffff'},'light'],
    [{preset:'custom',color:'#ffffff',panelColor:'invalid'},'dark']
  ]) {
    applyCanvasAppearance(control,appearance);
    assert.equal(properties['--dx-control-color-scheme'],expected);
    const normalized=normalizeCanvasAppearance(appearance);
    assert.equal(properties['--dx-input'],panelColors(normalized.panelColor).chrome);
  }
});
test('renderer paints exact Foundry stops or solid color without leaking transform state',()=>{
  const calls=[],gradient={addColorStop:(...args)=>calls.push(['stop',...args])};
  const context={save:()=>calls.push(['save']),restore:()=>calls.push(['restore']),translate:(...args)=>calls.push(['translate',...args]),scale:(...args)=>calls.push(['scale',...args]),createRadialGradient:(...args)=>{calls.push(['gradient',...args]);return gradient;},fillRect:(...args)=>calls.push(['fill',...args])};
  paintCanvasBackground(context,800,600,{preset:'foundry'});
  assert.deepEqual(calls,[['save'],['translate',400,300],['scale',400,300],['gradient',0,0,0,0,0,Math.SQRT2],['stop',0,'#172832'],['stop',.7,'#0b1016'],['fill',-1,-1,2,2],['restore']]);
  calls.length=0;paintCanvasBackground(context,800,600,{preset:'custom',color:'#ffffff'});
  assert.equal(context.fillStyle,'#ffffff');assert.deepEqual(calls,[['save'],['fill',0,0,800,600],['restore']]);
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

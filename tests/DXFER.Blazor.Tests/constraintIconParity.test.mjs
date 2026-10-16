import test from 'node:test';
import assert from 'node:assert/strict';
import {compileConstraintIcons,drawConstraintGlyphIcon} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

test('canvas compiles supplied shared paths once without inventing alternate geometry',()=>{
 const original=globalThis.Path2D;
 try{
  globalThis.Path2D=class{constructor(path){this.path=path;}};
  const paths={midpoint:{outline:'M12 4L21 20H3Z'},coincident:{outline:'M3 12h18M12 3v18',fill:'M14 12a2 2 0 1 0-4 0Z'}};
  const icons=compileConstraintIcons(paths);
  assert.equal(icons.midpoint.outline.path,paths.midpoint.outline);assert.equal(icons.midpoint.fill,null);
  assert.equal(icons.coincident.fill.path,paths.coincident.fill);
  const calls=[];const context={save(){},restore(){},translate(...p){calls.push(['translate',...p]);},scale(...s){calls.push(['scale',...s]);},stroke(path){calls.push(['stroke',path]);},fill(path){calls.push(['fill',path]);}};
  drawConstraintGlyphIcon(context,icons.coincident,{x:30,y:40},'#fff');
  assert.deepEqual(calls,[['translate',24,34],['scale',.5,.5],['stroke',icons.coincident.outline],['fill',icons.coincident.fill]]);
  assert.equal(context.lineWidth,1.6);assert.equal(context.strokeStyle,'#fff');assert.equal(context.fillStyle,'#fff');
  drawConstraintGlyphIcon(context,undefined,{x:0,y:0},'#fff');assert.equal(calls.length,4);
 }finally{globalThis.Path2D=original;}
});

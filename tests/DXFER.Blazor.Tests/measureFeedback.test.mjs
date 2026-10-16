import test from 'node:test';
import assert from 'node:assert/strict';
import {getMeasurementFeedback} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
const state=()=>({activeTool:'measure',view:{scale:10,offsetX:20,offsetY:100},
 measurement:{start:{x:0,y:0},end:{x:3,y:4},approximate:false}});
test('viewport values and witnesses share signed world deltas independent of zoom',()=>{
 const s=state(),before=JSON.stringify(s.measurement);
 assert.deepEqual(getMeasurementFeedback(s),{start:{x:20,y:100},end:{x:50,y:60},deltaX:3,deltaY:4,distance:5,approximate:false});
 s.view.scale=25;assert.equal(getMeasurementFeedback(s).distance,5);
 assert.equal(getMeasurementFeedback(s).end.x,95);assert.equal(JSON.stringify(s.measurement),before);
});
test('exit, empty and nonfinite measurement suppress feedback',()=>{
 const s=state();s.activeTool='select';assert.equal(getMeasurementFeedback(s),null);
 s.activeTool='measure';s.measurement.end.x=Infinity;assert.equal(getMeasurementFeedback(s),null);
 s.measurement=null;assert.equal(getMeasurementFeedback(s),null);
});
test('crossing negative axes and approximation remain explicit',()=>{
 const s=state();s.measurement.end={x:-3,y:-4};s.measurement.approximate=true;
 const result=getMeasurementFeedback(s);assert.equal(result.deltaX,-3);assert.equal(result.deltaY,-4);assert.equal(result.distance,5);assert.equal(result.approximate,true);
});

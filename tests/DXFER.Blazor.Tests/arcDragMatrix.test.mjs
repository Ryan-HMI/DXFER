import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
const p=(x,y)=>({x,y});
const arc={id:'arc',kind:'arc',center:p(0,0),radius:3,startAngleDegrees:0,endAngleDegrees:90};
for(const offset of [0,1e-9])test(`Shift endpoint has no direction at ${offset}`,()=>{
    const original={entities:[arc],dimensions:[],constraints:[]};
    assert.equal(applyGeometryDragPreview(original,'arc|point|end|0|3',p(0,3),p(offset,offset),true),null);
    const recovered=applyGeometryDragPreview(original,'arc|point|end|0|3',p(0,3),p(1e-4,1e-4),true);
    assert.equal(recovered.entities[0].endAngleDegrees,45);
    assert.equal(recovered.entities[0].radius,3);
});
for(const locked of [false,true]) for(const reverse of [false,true]) test(`arc radius/sweep weld locked=${locked} reverse=${reverse}`,()=>{
    const constraints=[{id:'weld',kind:'coincident',referenceKeys:['arc:end','peer:start'],state:'satisfied'}];
    if(locked)constraints.push({id:'fix',kind:'fix',referenceKeys:['peer:start'],state:'satisfied'});
    if(reverse)constraints.reverse();
    const dimensions=[{id:'radius',kind:'radius',referenceKeys:['arc'],value:3,isDriving:true},
        {id:'sweep',kind:'angle',referenceKeys:['arc'],value:90,isDriving:true}];
    if(reverse)dimensions.reverse();
    const original={entities:[arc,{id:'peer',kind:'line',points:[p(0,3),p(-2,3)]}],dimensions,constraints};
    const next=applyGeometryDragPreview(original,'arc|point|end|0|3',p(0,3),p(1,4));
    assert.equal(Boolean(next),!locked);
    if(next){assert.deepEqual(next.entities[0].center,p(1,1));assert.equal(next.entities[0].radius,3);
        assert.equal(next.entities[0].endAngleDegrees,90);assert.ok(Math.abs(next.entities[1].points[0].x-1)<1e-8);assert.equal(next.entities[1].points[0].y,4);}
});

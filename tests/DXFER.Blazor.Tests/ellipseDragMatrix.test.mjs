import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
const p=(x,y)=>({x,y});
const ellipse={id:'ellipse',kind:'ellipse',center:p(0,0),majorAxisEndPoint:p(4,0),minorRadiusRatio:0.5};
for(const offset of [0,1e-9])test(`ellipse degenerate axis ${offset} refuses and negative side recovers`,()=>{
    const original={entities:[ellipse],dimensions:[],constraints:[]};
    assert.equal(applyGeometryDragPreview(original,'ellipse|point|quadrant-0|4|0',p(4,0),p(offset,offset)),null);
    const next=applyGeometryDragPreview(original,'ellipse|point|quadrant-0|4|0',p(4,0),p(-6,0));
    assert.deepEqual(next.entities[0].majorAxisEndPoint,p(-6,0));
    assert.equal(next.entities[0].minorRadiusRatio,1/3);
});
for(const locked of [false,true])for(const reverse of [false,true])test(`ellipse center weld locked=${locked} reverse=${reverse}`,()=>{
    const constraints=[{id:'weld',kind:'coincident',referenceKeys:['ellipse:center','peer:start'],state:'satisfied'}];
    if(locked)constraints.push({id:'fix',kind:'fix',referenceKeys:['peer:start'],state:'satisfied'});
    if(reverse)constraints.reverse();
    const original={entities:[ellipse,{id:'peer',kind:'line',points:[p(0,0),p(-2,0)]}],dimensions:[],constraints};
    const next=applyGeometryDragPreview(original,'ellipse|point|center|0|0',p(0,0),p(1,1));
    assert.equal(Boolean(next),!locked);
    if(next){assert.deepEqual(next.entities[0].center,p(1,1));assert.deepEqual(next.entities[1].points[0],p(1,1));}
});
for(const major of [false,true])for(const locked of [false,true])test(`ellipse ${major?'major':'minor'} axis fixed center, both driving=${locked}`,()=>{
    const axis=major?'minor':'major';
    const original={entities:[{id:'ellipse',kind:'ellipse',center:p(0,0),majorAxisEndPoint:p(4,0),minorRadiusRatio:0.5}],
        dimensions:[{id:'diameter',kind:'lineardistance',referenceKeys:[`ellipse|point|${axis}-start|0|0`,`ellipse|point|${axis}-end|0|0`],value:major?4:8,isDriving:true}],
        constraints:[{id:'fix',kind:'fix',referenceKeys:['ellipse:center'],state:'satisfied'}]};
    if(locked){const other=major?'major':'minor';original.dimensions.push({id:'other',kind:'lineardistance',
        referenceKeys:[`ellipse|point|${other}-start|0|0`,`ellipse|point|${other}-end|0|0`],value:major?8:4,isDriving:true});}
    const next=applyGeometryDragPreview(original,major?'ellipse|point|quadrant-0|4|0':'ellipse|point|quadrant-90|0|2',major?p(4,0):p(0,2),major?p(6,0):p(0,3));
    if(locked){assert.equal(next,null);return;}
    assert.ok(next);
    assert.deepEqual(next.entities[0].center,p(0,0));
    assert.deepEqual(next.entities[0].majorAxisEndPoint,p(major?6:4,0));
    assert.ok(Math.abs(next.entities[0].majorAxisEndPoint.x*next.entities[0].minorRadiusRatio-(major?2:3))<1e-8);
});

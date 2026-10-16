import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
const p=(x,y)=>({x,y});
for(const fit of [false,true]) for(const missing of [false,true]) test(`spline live dimension fit=${fit} missing=${missing}`,()=>{
    const points=[p(0,0),p(1,0),p(3,1),p(4,1)],prefix=fit?'fit':'control';
    const entity={id:'spline',kind:'spline',points,controlPoints:points,fitPoints:fit?points:[],splineDegree:3,splineKnots:[0,0,0,0,1,1,1,1],splineWeights:[1,1,1,1]};
    const original={entities:[entity],constraints:[],dimensions:[{id:'length',kind:'linearDistance',
        referenceKeys:[`spline|point|${prefix}-0|0|0`,`spline|point|${prefix}-${missing?99:1}|1|0`],value:1,isDriving:true,anchor:p(.5,-1)}]};
    const next=applyGeometryDragPreview(original,`spline|point|${prefix}-1|1|0`,p(1,0),p(1,1));
    if(missing)assert.equal(next,null);
    else {
        assert.ok(next);
        const current=fit?next.entities[0].fitPoints:next.entities[0].controlPoints;
        assert.ok(Math.abs(Math.hypot(current[1].x-current[0].x,current[1].y-current[0].y)-1)<1e-8);
    }
});
for(const fixedSpline of [false,true]) test(`driving spline tangent with fixed=${fixedSpline}`,()=>{
    const points=[p(0,0),p(1,0),p(3,1),p(4,1)];
    const original={entities:[{id:'spline',kind:'spline',points,controlPoints:points,fitPoints:[],
        splineDegree:3,splineKnots:[0,0,0,0,1,1,1,1],splineWeights:[1,1,1,1],startTangentControlPointIndex:1,endTangentControlPointIndex:2}],
        constraints:fixedSpline?[{id:'fix',kind:'fix',referenceKeys:['spline'],state:'satisfied'}]:[],
        dimensions:[{id:'length',kind:'linearDistance',referenceKeys:['spline|point|control-0|0|0','spline|point|tangent-start|.25|0'],
            value:.25,isDriving:true,anchor:p(.5,-1)}]};
    const next=applyGeometryDragPreview(original,'spline|point|tangent-start|.25|0',p(.25,0),p(.25,.5));
    assert.equal(Boolean(next),!fixedSpline);
    if(next)assert.deepEqual(next.entities[0].controlPoints,points.map(a=>p(a.x,a.y+.5)));
});

import assert from 'node:assert/strict';
import {before, after, test} from 'node:test';
import {createPlaneGcsSolver} from '../../src/DXFER.Blazor/wwwroot/planeGcsSolver.js';
let solver;
before(async () => { solver = await createPlaneGcsSolver(); });
after(async () => { await solver.dispose(); });
const p = (x,y) => ({x,y});
const axis = {id:'axis',kind:'line',points:[p(0,-10),p(0,10)]};
const near = (x,y) => assert.ok(Math.abs(x-y)<1e-6, `${x} != ${y}`);
const solved = r => assert.equal(r.status,'solved',JSON.stringify(r.diagnostics));
const run = (a,b,reverse=false,extra={}) => solver.solve({document:{entities:[axis,a,b],dimensions:[],constraints:[]},
  constraints:[{id:'mirror',kind:'Symmetric',referenceKeys:[...(reverse?['b','a']:['a','b']),'axis']}],
  dimensions:[],initialGuesses:[],fixedReferenceKeys:['axis','a'],...extra});
const spline = (id, shift=0, fit=false) => ({id,kind:'spline',splineDegree:3,
  controlPoints:[p(-6+shift,0),p(-5+shift,2),p(-4+shift,-1),p(-3+shift,1)],
  splineKnots:[0,0,0,0,1,1,1,1],splineWeights:[1,1,1,1],
  ...(fit?{fitPoints:[p(-6+shift,0),p(-5+shift,2),p(-4+shift,-1),p(-3+shift,1)],
    startTangentHandle:p(-5.75+shift,.5),endTangentHandle:p(-3.25+shift,.5)}:{})});
for (const reverse of [false,true]) for (const fit of [false,true]) test(`exact spline symmetry fit=${fit} reverse=${reverse}`, async()=>{
  const a=spline('a',0,fit), b=spline('b',10,fit), result=await run(a,b,reverse); solved(result);
  assert.deepEqual(result.entities[1],a);
  for(const field of ['controlPoints',...(fit?['fitPoints']:[])]) a[field].forEach((v,i)=>{
    near(result.entities[2][field][i].x,-v.x);near(result.entities[2][field][i].y,v.y);
  });
  if(fit) for(const field of ['startTangentHandle','endTangentHandle']) {
    near(result.entities[2][field].x,-a[field].x);near(result.entities[2][field].y,a[field].y);
  }
  for(const field of ['splineDegree','splineKnots','splineWeights']) assert.deepEqual(result.entities[2][field],b[field]);
  const edit=await solver.solve({document:{entities:result.entities,constraints:[{id:'mirror',kind:'Symmetric',referenceKeys:['a','b','axis']}],dimensions:[]},
    constraints:[],dimensions:[],fixedReferenceKeys:['axis'],initialGuesses:[],
    drag:{key:`a:${fit?'fit':'control'}-1`,start:a.controlPoints[1],end:p(-7,3)}}); solved(edit);
  near(edit.entities[1][fit?'fitPoints':'controlPoints'][1].x,-7);
  near(edit.entities[2][fit?'fitPoints':'controlPoints'][1].x,7);
});
test('rational spline symmetry retains normalized basis and rejects incompatible weights', async()=>{
  const a=spline('a'),b=spline('b',10);a.splineWeights=[1,2,3,1];b.splineWeights=[2,4,6,2];
  b.splineKnots=[2,2,2,2,5,5,5,5];solved(await run(a,b));
  b.splineWeights[1]=3;const result=await run(a,b);
  assert.equal(result.status,'unsupported');assert.deepEqual(result.entities,[axis,a,b]);
});
for(const ratio of [.5,1,1.5]) for(const reverse of [false,true]) test(`full ellipse symmetry ratio=${ratio} reverse=${reverse}`,async()=>{
  const a={id:'a',kind:'ellipse',center:p(-5,1),majorAxisEndPoint:p(3,1),minorRadiusRatio:ratio};
  const b={id:'b',kind:'ellipse',center:p(6,2),majorAxisEndPoint:p(-2,1),minorRadiusRatio:.7};
  const result=await run(a,b,reverse);solved(result);const mirrored=result.entities[2];
  near(mirrored.center.x,5);near(mirrored.center.y,1);
  near(mirrored.majorAxisEndPoint.x,-3);near(mirrored.majorAxisEndPoint.y,1);near(mirrored.minorRadiusRatio,ratio);
});
test('partial ellipse symmetry reverses exact eccentric endpoints',async()=>{
  const a={id:'a',kind:'ellipse',center:p(-5,1),majorAxisEndPoint:p(3,0),minorRadiusRatio:.5,startAngleDegrees:20,endAngleDegrees:100};
  const b={id:'b',kind:'ellipse',center:p(6,2),majorAxisEndPoint:p(-2,1),minorRadiusRatio:.7,startAngleDegrees:250,endAngleDegrees:330};
  const result=await run(a,b);solved(result);near(result.entities[2].startAngleDegrees,260);near(result.entities[2].endAngleDegrees,340);
});
test('full ellipse symmetry retains focal-reference constraints',async()=>{
  const a={id:'a',kind:'ellipse',center:p(-5,1),majorAxisEndPoint:p(3,0),minorRadiusRatio:.5};
  const b={id:'b',kind:'ellipse',center:p(6,2),majorAxisEndPoint:p(-2,1),minorRadiusRatio:.7};
  const result=await run(a,b,false,{fixedReferenceKeys:['axis','a','a:focus1']});solved(result);
  assert.deepEqual(result.entities[1],a);near(result.entities[2].center.x,5);
});
test('a symmetry cycle retains independent ellipse orthogonality',async()=>{
  const a={id:'a',kind:'ellipse',center:p(-5,1),majorAxisEndPoint:p(3,1),minorRadiusRatio:.5};
  const b={id:'b',kind:'ellipse',center:p(6,2),majorAxisEndPoint:p(-2,1),minorRadiusRatio:.7};
  const result=await run(a,b,false,{constraints:[{id:'ab',kind:'Symmetric',referenceKeys:['a','b','axis']},
    {id:'ba',kind:'Symmetric',referenceKeys:['b','a','axis']}]});solved(result);
  near(result.entities[2].majorAxisEndPoint.x,-3);near(result.entities[2].minorRadiusRatio,.5);
});
test('unsupported partial equal-axis ellipse symmetry refuses unchanged',async()=>{
  const a={id:'a',kind:'ellipse',center:p(-5,1),majorAxisEndPoint:p(3,0),minorRadiusRatio:1,startAngleDegrees:20,endAngleDegrees:100};
  const b={...a,id:'b',center:p(5,1)};const result=await run(a,b);
  assert.equal(result.status,'unsupported');assert.deepEqual(result.entities,[axis,a,b]);
});
for(const circumscribed of [false,true]) test(`regular polygon symmetry circumscribed=${circumscribed}`,async()=>{
  const a={id:'a',kind:'polygon',center:p(-5,1),radius:3,rotationAngleDegrees:20,sideCount:5,circumscribed};
  const b={...a,id:'b',center:p(6,2),radius:2,rotationAngleDegrees:130};
  const result=await run(a,b);solved(result);near(result.entities[2].rotationAngleDegrees,160);near(result.entities[2].radius,3);
  const refusal=await run(a,b,false,{fixedReferenceKeys:['axis','a','b']});assert.notEqual(refusal.status,'solved');assert.deepEqual(refusal.entities,[axis,a,b]);
});

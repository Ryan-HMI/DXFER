import assert from 'node:assert/strict';
import test from 'node:test';
import { applyGeometryDragPreview } from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
const p=(x,y)=>({x,y});
const c=(id,kind,...referenceKeys)=>({id,kind,referenceKeys,state:'satisfied'});
const circle=(id,x,y,radius=3)=>({id,kind:'circle',center:p(x,y),radius});
const line=(id,a,b)=>({id,kind:'line',points:[a,b]});
for(const order of [[0,1,2],[0,2,1],[1,0,2],[1,2,0],[2,0,1],[2,1,0]])test(`Equal/Tangent/Fixed order ${order}`,()=>{
    const constraints=[c('equal','equal','moving','fixed'),c('tangent','tangent','line','moving'),c('fix','fix','fixed')];
    const original={entities:[circle('moving',0,0),circle('fixed',20,0),line('line',p(-5,3),p(5,3))],dimensions:[],constraints:order.map(i=>constraints[i])};
    const moved=applyGeometryDragPreview(original,'moving|point|center|0|0',p(0,0),p(2,.0001));
    assert.ok(moved);assert.deepEqual(moved.entities[1].center,original.entities[1].center);assert.equal(moved.entities[1].radius,3);
    assert.equal(moved.entities[0].radius,3);assert.ok(Math.abs(moved.entities[2].points[0].y-3.0001)<1e-8);
    assert.equal(applyGeometryDragPreview(original,'moving|point|quadrant-0|3|0',p(3,0),p(5,0)),null);
    const free={...original,constraints:original.constraints.filter(x=>x.kind!=='fix')};
    const recovered=applyGeometryDragPreview(free,'moving|point|quadrant-0|3|0',p(3,0),p(5,0));
    assert.ok(recovered);assert.equal(recovered.entities[0].radius,5);assert.equal(recovered.entities[1].radius,5);
});
for(const reverse of [false,true]) {
    test(`Tangent/Fixed line radius freedom reverse=${reverse}`,()=>{
        const constraints=[c('tangent','tangent','line','circle'),c('fix','fix','line')];
        const original={entities:[line('line',p(-5,3),p(5,3)),circle('circle',0,0)],dimensions:[],constraints:reverse?constraints.reverse():constraints};
        const moved=applyGeometryDragPreview(original,'circle|point|center|0|0',p(0,0),p(0,1));
        assert.ok(moved);assert.deepEqual(moved.entities[0].points,original.entities[0].points);assert.equal(moved.entities[1].radius,2);
        const restricted={...original,dimensions:[{id:'diameter',kind:'diameter',referenceKeys:['circle'],value:6,isDriving:true,anchor:p(4,0)}]};
        assert.equal(applyGeometryDragPreview(restricted,'circle|point|center|0|0',p(0,0),p(0,1)),null);
    });
    test(`Fixed midpoint symmetric-end solve remains unsupported reverse=${reverse}`,()=>{
        const constraints=[c('mid','midpoint','line','marker'),c('fix','fix','marker')];
        const original={entities:[line('line',p(0,0),p(10,0)),{id:'marker',kind:'point',points:[p(5,0)]}],dimensions:[],constraints:reverse?constraints.reverse():constraints};
        assert.equal(applyGeometryDragPreview(original,'line|point|end|10|0',p(10,0),p(12,4)),null);
        const next=applyGeometryDragPreview({...original,constraints:constraints.filter(x=>x.kind!=='fix')},'line|point|end|10|0',p(10,0),p(12,4));
        assert.ok(next);assert.deepEqual(next.entities[1].points,[p(6,2)]);
    });
}

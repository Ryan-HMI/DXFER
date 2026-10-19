import test from 'node:test';
import assert from 'node:assert/strict';
import {findNearestTarget,applyGeometryDragPreview} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';
const p=(x,y)=>({x,y});
const line=(id,a,b)=>({id,kind:'line',points:[a,b]});
const fixture=reverse=>{
    const entities=[line('carrier',p(2,4),p(5,-2)),line('branch',p(0,1),p(3,2))];
    if(reverse)entities.reverse();
    return {entities,dimensions:[],constraints:[{id:'incidence',kind:'PointOnLine',referenceKeys:['carrier','branch:end'],state:'Satisfied'}]};
};
const state=doc=>({activeTool:'select',document:doc,acquiredSnapPoints:[],selectedKeys:new Set(),view:{scale:50,offsetX:100,offsetY:300}});
test('creation retains intersection provenance; nearby unrelated endpoint is not substituted',()=>{
    const s=state(fixture(false));s.activeTool='line';
    assert.match(findNearestTarget(s,p(250,200)).key,/carrier\|point\|intersection-/);
    s.activeTool='select';s.document.entities[1].points[1]=p(3.04,2);
    s.document.entities.push(line('crossing',p(2,2),p(6,2)));
    assert.match(findNearestTarget(s,p(250,200)).key,/intersection-/);
});
test('locked endpoint does not become an editable substitute',()=>{
    const s=state(fixture(false));s.document.entities[1].isLocked=true;
    assert.notEqual(findNearestTarget(s,p(250,200))?.entityId,'branch');
});
test('crossing coincident with a real midpoint retains its translation grip',()=>{
    const doc={entities:[line('a',p(0,2),p(6,2)),line('b',p(3,0),p(3,5))],constraints:[],dimensions:[]};
    const target=findNearestTarget(state(doc),p(250,200));
    // The first line midpoint is a genuine translation grip, not a new endpoint.
    assert.equal(target.key,'a|point|mid|3|2');
    assert.equal(doc.entities.length,2);
});
test('a virtual crossing alone does not acquire a draggable endpoint',()=>{
    const doc={entities:[line('a',p(0,2),p(7,2)),line('b',p(3,0),p(3,5))],constraints:[],dimensions:[]};
    const target=findNearestTarget(state(doc),p(250,200));
    assert.match(target.key,/intersection-/);
    assert.equal(applyGeometryDragPreview(doc,target.key,p(3,2),p(4,3)),null);
    assert.equal(doc.entities.length,2);
});
for(const reverse of [false,true]) for(const offset of [0,2]) {
    test(`T-junction drag selects the actual endpoint, order=${reverse}, pick offset=${offset}`,()=>{
        const doc=fixture(reverse),before=structuredClone(doc),s=state(doc);
        const target=findNearestTarget(s,p(250+offset,200+offset));
        assert.equal(target.key,'branch|point|end|3|2');
        const next=applyGeometryDragPreview(doc,target.key,p(3,2),p(4,3));
        assert.ok(next);
        assert.deepEqual(next.entities.find(e=>e.id==='branch').points[1],p(4,3));
        const carrier=next.entities.find(e=>e.id==='carrier').points;
        assert.ok(Math.abs((4-carrier[0].x)*(carrier[1].y-carrier[0].y)-(3-carrier[0].y)*(carrier[1].x-carrier[0].x))<1e-8);
        assert.deepEqual(doc,before);
    });
    test(`Fixed junction refuses through the actual endpoint, order=${reverse}, pick offset=${offset}`,()=>{
        const doc=fixture(reverse);doc.constraints.push({id:'fixed',kind:'Fix',referenceKeys:['branch:end'],state:'Satisfied'});
        const before=structuredClone(doc),target=findNearestTarget(state(doc),p(250+offset,200+offset));
        assert.equal(target.key,'branch|point|end|3|2');
        assert.equal(applyGeometryDragPreview(doc,target.key,p(3,2),p(4,3)),null);
        assert.deepEqual(doc,before);
    });
}

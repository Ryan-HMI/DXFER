import test from 'node:test';
import assert from 'node:assert/strict';
import { getConstraintGlyphGroups, getConstraintGlyphLeader, applyGeometryDragPreview }
    from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const p=(x,y)=>({x,y});
const line={id:'edge',kind:'line',points:[p(0,0),p(20,0)]};
const circle={id:'circle',kind:'circle',center:p(6,3),radius:3};
const relation=(kind,keys,id=kind)=>({id,kind,state:'Satisfied',referenceKeys:keys});
const stateOf=(entities,keys,others=[])=>({document:{entities,dimensions:[],constraints:[relation('Tangent',keys),...others]},
    canvas:{getBoundingClientRect:()=>({width:1000,height:700})},
    view:{scale:10,offsetX:100,offsetY:300},showAllConstraints:true,constraintGroupOffsets:new Map()});
const tangent=state=>getConstraintGlyphGroups(state).find(g=>g.isTangent);
const close=(actual,expected)=>{assert.ok(Math.abs(actual.x-expected.x)<1e-7);assert.ok(Math.abs(actual.y-expected.y)<1e-7);};
const assertContact=(state,point)=>{
    const g=tangent(state);assert.ok(g?.hasContact);close(g.anchor,point);
    const leader=getConstraintGlyphLeader(g);assert.ok(leader,'Default contact leader must be visible');
    close(leader.start,p(point.x*state.view.scale+state.view.offsetX,-point.y*state.view.scale+state.view.offsetY));
    return g;
};

for(const reverse of [false,true])for(const reversedLine of [false,true]) {
    test(`line-circle contact rather than average center, refs=${reverse}, line=${reversedLine}`,()=>{
        const state=stateOf([{...line,points:reversedLine?[...line.points].reverse():line.points},circle],reverse?['circle','edge']:['edge','circle']);
        const g=assertContact(state,p(6,0));
        state.constraintGroupOffsets.set(g.key,p(70,-60));
        const moved=assertContact(state,p(6,0));assert.equal(moved.manualOffset,true);
        close(moved.point,p(230,240));
        state.view={scale:25,offsetX:50,offsetY:500};
        const zoomed=assertContact(state,p(6,0));close(zoomed.point,p(270,440));
    });
}

test('rotated line and polyline-segment references resolve the true foot',()=>{
    const edge={id:'edge',kind:'line',points:[p(0,0),p(12,16)]};
    assertContact(stateOf([edge,{...circle,center:p(.6,5.8)}],['edge','circle']),p(3,4));
    assertContact(stateOf([{...line,kind:'polyline',points:[p(-2,2),p(0,0),p(20,0)]},circle],['edge|segment|1','circle']),p(6,0));
});

for(const reverse of [false,true])for(const internal of [false,true]) {
    test(`circle-circle ${internal?'internal':'external'} contact, refs=${reverse}`,()=>{
        const a={id:'a',kind:'circle',center:p(0,0),radius:5};
        const b={id:'b',kind:'circle',center:internal?p(3,0):p(8,0),radius:internal?2:3};
        assertContact(stateOf([a,b],reverse?['b','a']:['a','b']),p(5,0));
    });
}

test('rotated external circle pair and arc contact use shared surface point',()=>{
    const a={id:'a',kind:'arc',center:p(0,0),radius:5,startAngleDegrees:0,endAngleDegrees:90};
    const b={id:'b',kind:'circle',center:p(4.8,6.4),radius:3};
    assertContact(stateOf([a,b],['a','b']),p(3,4));
    assertContact(stateOf([line,{...circle,kind:'arc',startAngleDegrees:180,endAngleDegrees:360}],['edge','circle']),p(6,0));
});

test('Equal and Tangent on the same pair cannot average away the contact',()=>{
    const a={id:'a',kind:'circle',center:p(0,0),radius:3},b={...a,id:'b',center:p(6,0)};
    const state=stateOf([a,b],['a','b'],[relation('Equal',['b','a'])]);
    assert.equal(getConstraintGlyphGroups(state).length,2);
    assertContact(state,p(3,0));
});

for(const [name,entities,keys] of [
    ['gap',[line,{...circle,center:p(6,4)}],['edge','circle']],
    ['outside segment',[line,{...circle,center:p(25,3)}],['edge','circle']],
    ['outside arc',[line,{...circle,kind:'arc',startAngleDegrees:0,endAngleDegrees:180}],['edge','circle']],
    ['zero edge',[{...line,points:[p(0,0),p(0,0)]},circle],['edge','circle']],
    ['coincident circles',[circle,{...circle,id:'other'}],['circle','other']],
    ['intersecting circles',[circle,{...circle,id:'other',center:p(8,3)}],['circle','other']],
    ['missing reference',[circle],['circle','missing']]
]) test(`no false contact leader for ${name}`,()=>{
    const state=stateOf(entities,keys),g=tangent(state);assert.ok(g);assert.equal(g.hasContact,false);
    assert.equal(getConstraintGlyphLeader(g),null);
    state.constraintGroupOffsets.set(g.key,p(100,-100));
    assert.equal(getConstraintGlyphLeader(tangent(state)),null);
});

test('contact follows constrained drag preview and does not alter native intent',()=>{
    const state=stateOf([line,circle],['edge','circle'],[relation('Fix',['edge'])]);
    state.document.dimensions=[{id:'diameter',kind:'diameter',referenceKeys:['circle'],value:6,isDriving:true}];
    const before=structuredClone(state.document);
    assertContact(state,p(6,0));
    const preview=applyGeometryDragPreview(state.document,'circle|point|center|6|3',p(6,3),p(9,4));assert.ok(preview);
    assertContact({...state,document:preview},p(9,0));
    assert.deepEqual(state.document,before);
});

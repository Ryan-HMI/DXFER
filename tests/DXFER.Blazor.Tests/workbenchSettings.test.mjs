import test from 'node:test';
import assert from 'node:assert/strict';
import {AUTO_CONSTRAINTS_KEY,loadAutomaticSnapConstraints,saveAutomaticSnapConstraints,
    INVERT_WHEEL_ZOOM_KEY,loadInvertWheelZoom,saveInvertWheelZoom} from '../../src/DXFER.Blazor/wwwroot/workbenchSettings.js';

test('wheel inversion defaults off and persists independently of snapping',t=>{
    const previous=globalThis.localStorage;
    t.after(()=>{if(previous===undefined)delete globalThis.localStorage;else globalThis.localStorage=previous;});
    const values=new Map();
    globalThis.localStorage={getItem:key=>values.get(key),setItem:(key,value)=>values.set(key,value)};
    assert.equal(loadInvertWheelZoom(),false);
    saveAutomaticSnapConstraints(false);
    for(const enabled of [true,false,true]) {
        saveInvertWheelZoom(enabled);
        assert.equal(loadInvertWheelZoom(),enabled);
        assert.equal(loadAutomaticSnapConstraints(),false);
    }
    for(const invalid of ['', 'null', '1', '{}', 'TRUE']) {
        values.set(INVERT_WHEEL_ZOOM_KEY,invalid);
        assert.equal(loadInvertWheelZoom(),false);
    }
});

test('snap constraint preference defaults on, round trips off and on, and ignores invalid values',t=>{
    const previous=globalThis.localStorage;
    t.after(()=>{if(previous===undefined)delete globalThis.localStorage;else globalThis.localStorage=previous;});
    const values=new Map();
    globalThis.localStorage={getItem:key=>values.get(key),setItem:(key,value)=>values.set(key,value)};
    assert.equal(loadAutomaticSnapConstraints(),true);
    for(const enabled of [false,true,false]) {
        saveAutomaticSnapConstraints(enabled);
        assert.equal(loadAutomaticSnapConstraints(),enabled);
    }
    for(const invalid of ['', 'null', '0', '{}']) {
        values.set(AUTO_CONSTRAINTS_KEY,invalid);
        assert.equal(loadAutomaticSnapConstraints(),true);
    }
});

test('unavailable preference storage does not break the editor',t=>{
    const previous=globalThis.localStorage;
    t.after(()=>{if(previous===undefined)delete globalThis.localStorage;else globalThis.localStorage=previous;});
    globalThis.localStorage={getItem(){throw Error('blocked');},setItem(){throw Error('blocked');}};
    assert.equal(loadAutomaticSnapConstraints(),true);
    assert.doesNotThrow(()=>saveAutomaticSnapConstraints(false));
    assert.equal(loadInvertWheelZoom(),false);
    assert.doesNotThrow(()=>saveInvertWheelZoom(true));
});

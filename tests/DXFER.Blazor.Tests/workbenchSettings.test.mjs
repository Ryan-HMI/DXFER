import test from 'node:test';
import assert from 'node:assert/strict';
import {AUTO_CONSTRAINTS_KEY,loadAutomaticSnapConstraints,saveAutomaticSnapConstraints} from '../../src/DXFER.Blazor/wwwroot/workbenchSettings.js';

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
});

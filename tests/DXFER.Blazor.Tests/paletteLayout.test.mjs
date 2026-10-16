import test from 'node:test';
import assert from 'node:assert/strict';
import { restoreLayout, loadLayout, saveLayout, storageKey, floatingLimits, observeLayout, getBounds }
  from '../../src/DXFER.Blazor/wwwroot/paletteLayout.js';

const state = (groupName, overrides = {}) => ({ groupName, side: 1, compaction: 2,
  dockOrder: 60, floatingX: 24, floatingY: 24, showLabels: false, ...overrides });
const defaults = [state('Sketch'), state('View', { dockOrder: 160 }), state('New')];
const encoded = (groups, densityVersion = 2) => JSON.stringify({ version: 1, densityVersion, groups });
const memoryStorage = (value = null) => ({ value, writes: 0,
  getItem(key) { assert.equal(key, storageKey); return this.value; },
  setItem(key, value) { assert.equal(key, storageKey); this.value = value; this.writes++; } });

test('labeled widths persist independently through label toggles and restricted saves', () => {
  const groups = [state('Sketch', {labelWidth: 360, showLabels: true}), state('View', {labelWidth: 176})];
  const storage = memoryStorage();
  assert.equal(saveLayout(groups, storage), true);
  assert.deepEqual(loadLayout(defaults.slice(0, 2), storage), groups);
  saveLayout([{...groups[0], showLabels: false}], storage);
  assert.deepEqual(loadLayout(defaults.slice(0, 2), storage), [{...groups[0], showLabels: false}, groups[1]]);
});

test('malformed widths do not discard valid dock and label preferences', () => {
  for (const labelWidth of [-1, 0, 175, 481, 1e9, null, '320', NaN, Infinity]) {
    const original = state('Sketch', {showLabels:true, side:2, floatingX:700});
    assert.deepEqual(restoreLayout(encoded([{...original, labelWidth}]), defaults)[0], original);
  }
  assert.deepEqual(restoreLayout(encoded([state('Sketch')]), defaults), defaults);
});

test('legacy untouched Cleanup and Grain migrate right without losing display preferences', () => {
  const old = [state('Cleanup', {dockOrder:660, floatingX:72, floatingY:132, compaction:1, showLabels:true}),
    state('Grain', {dockOrder:760, floatingX:96, floatingY:150})];
  const storage = memoryStorage(encoded(old));
  const migrated = old.map(s => ({...s, side:2}));
  assert.deepEqual(loadLayout(old, storage), migrated);
  assert.equal(storage.writes, 0);
  saveLayout(migrated, storage);
  assert.equal(JSON.parse(storage.value).dockingVersion, 1);
  assert.deepEqual(loadLayout(old, storage), migrated);
  // An intentional move back to the old left coordinates must survive subsequent reloads.
  saveLayout(old, storage);
  assert.deepEqual(loadLayout(migrated, storage), old);
});

test('legacy moved, reordered, floating and other palettes never migrate', () => {
  const original = state('Cleanup', {dockOrder:660, floatingX:72, floatingY:132});
  for (const change of [{side:0}, {side:2}, {dockOrder:500}, {floatingX:80}, {floatingY:200}, {groupName:'Sketch'}]) {
    const moved = {...original, ...change};
    assert.deepEqual(restoreLayout(encoded([moved]), [original, state('Sketch')]).find(s=>s.groupName===moved.groupName), moved);
  }
});

test('restricted saves migrate inactive default groups without losing inactive custom positions', () => {
  const cleanup = state('Cleanup', {dockOrder:660, floatingX:72, floatingY:132});
  const grain = state('Grain', {side:0, dockOrder:760, floatingX:700, floatingY:150});
  const storage = memoryStorage(encoded([cleanup, grain]));
  saveLayout([state('View')], storage);
  assert.deepEqual(loadLayout([cleanup, grain], storage), [{...cleanup, side:2}, grain]);
});

test('drag bounds use current canvas area and display scale, not fixed desktop width', () => {
  const root = {offsetWidth: 984, offsetHeight: 600,
    getBoundingClientRect: () => ({left: 20, top: 48, width: 1230, height: 750})};
  assert.deepEqual(getBounds(root), {left: 20, top: 48, width: 1230, height: 750, scaleX: 1.25, scaleY: 1.25});
});

test('independent identities, all docks, order, compaction and positions round trip', () => {
  const groups = Array.from({length: 5}, (_, side) => state(`Group${side}`,
    { side, compaction: side % 3, dockOrder: 900 - side * 25, floatingX: 450 + side, floatingY: 200 + side }));
  const storage = memoryStorage();
  assert.equal(saveLayout(groups, storage), true);
  assert.deepEqual(loadLayout(groups.map(s => state(s.groupName)), storage), groups);
  const changed = groups.map(s => ({ ...s }));
  changed[0].floatingX = 700;
  saveLayout(changed, storage);
  assert.deepEqual(loadLayout(groups, storage).slice(1), groups.slice(1));
});

test('missing, corrupt, oversized and unsupported envelopes use defaults without writing', () => {
  for (const value of [null, '', '{', 'null', '[]', '42', '{}', encoded(null),
    JSON.stringify({version: 2, groups: defaults}), JSON.stringify({version: '1', groups: defaults}),
    encoded(Array(129).fill(state('Sketch'))), ' '.repeat(65537)]) {
    const storage = memoryStorage(value);
    assert.deepEqual(loadLayout(defaults, storage), defaults);
    assert.equal(storage.writes, 0);
  }
});

test('invalid entries default individually; valid peers survive', () => {
  for (const bad of [null, [], 'bad', {groupName: 'Sketch'},
    ...[ {side: 5}, {side: '0'}, {compaction: -1}, {floatingX: null},
      {floatingY: '20'}, {floatingY: -1}, {floatingX: 1e100}, {dockOrder: -1} ]
      .map(fields => state('Sketch', fields))]) {
    const view = state('View', {side: 0, floatingX: 800});
    assert.deepEqual(restoreLayout(encoded([bad, view]), defaults), [defaults[0], view, defaults[2]]);
  }
});

test('duplicate identities are rejected; unknown groups ignored; new groups get defaults', () => {
  assert.deepEqual(restoreLayout(encoded([state('Unknown'), state('__proto__'),
    state('Sketch', {side: 0}), state('Sketch', {side: 4})]), defaults), defaults);
});

test('saving a restricted group set retains valid inactive identities without adding controls', () => {
  const sketch = state('Sketch', {side: 0, floatingX: 950});
  const storage = memoryStorage(encoded([sketch, state('Unknown'), null]));
  saveLayout([state('View', {side: 2})], storage);
  assert.deepEqual(loadLayout(defaults, storage), [sketch, state('View', {side: 2}), defaults[2]]);
});

test('storage denial, quota exhaustion and non-finite input fail safely', () => {
  const denied = {getItem() { throw Error('denied'); }, setItem() { throw Error('quota'); }};
  assert.deepEqual(loadLayout(defaults, denied), defaults);
  assert.equal(saveLayout(defaults, denied), false);
  const storage = memoryStorage();
  assert.equal(saveLayout([state('Sketch', {floatingX: Infinity})], storage), false);
  assert.equal(storage.writes, 0);
});

test('floating limits keep the complete header reachable without changing preferred coordinates', () => {
  assert.deepEqual(floatingLimits(1280, 650, 253, 34), {x: 1027, y: 616});
  assert.deepEqual(floatingLimits(800, 400, 253, 34), {x: 547, y: 366});
  assert.deepEqual(floatingLimits(200, 20, 253, 34), {x: 0, y: 0});
});

test('labels persist independently without changing layout or other groups', () => {
  const groups = [state('Sketch', {showLabels: true, side: 0}), state('View')];
  const storage = memoryStorage();
  assert.equal(saveLayout(groups, storage), true);
  assert.deepEqual(loadLayout(defaults.slice(0, 2), storage), groups);
});

test('old default-on labels migrate without changing any dock, collapse, order or position', () => {
  const old = [state('Sketch', {showLabels: true, side: 0, floatingX: 891, floatingY: 235}),
    state('View', {showLabels: true, side: 2, dockOrder: 400, compaction: 1})];
  const storage = memoryStorage(encoded(old, 1));
  const migrated = loadLayout(defaults.slice(0, 2), storage);
  assert.deepEqual(migrated, old.map(s => ({...s, showLabels: false})));
  assert.equal(storage.writes, 0);
  migrated[0].showLabels = true;
  assert.equal(saveLayout(migrated, storage), true);
  assert.equal(JSON.parse(storage.value).densityVersion, 2);
  assert.deepEqual(loadLayout(defaults.slice(0, 2), storage), migrated);
});

test('unmarked legacy layouts migrate on each load until an explicit new choice is saved', () => {
  const old = state('Sketch', {showLabels: true, side: 4, dockOrder: 200});
  const storage = memoryStorage(JSON.stringify({version: 1, groups: [old]}));
  assert.deepEqual(loadLayout(defaults, storage)[0], {...old, showLabels: false});
  assert.deepEqual(loadLayout(defaults, storage)[0], {...old, showLabels: false});
  saveLayout([state('View', {showLabels: true})], storage);
  assert.deepEqual(loadLayout(defaults, storage)[0], {...old, showLabels: false});
  assert.equal(loadLayout(defaults, storage)[1].showLabels, true);
});

test('legacy or malformed label preference preserves valid dock positions', () => {
  for (const showLabels of [undefined, 'true', 1, null]) {
    const legacy = state('Sketch', {side: 0, floatingX: 900, showLabels});
    assert.deepEqual(restoreLayout(encoded([legacy]), defaults)[0],
      state('Sketch', {side: 0, floatingX: 900, showLabels: false}));
  }
  assert.equal(restoreLayout(null, [state('New', {showLabels: true})])[0].showLabels, true);
});

test('observer refreshes measured bounds and releases listeners on disposal', () => {
  const properties = new Map();
  const group = {offsetWidth: 253, querySelector: () => ({offsetHeight: 34}), dataset: {paletteIndex: '0'}};
  let width = 1280;
  const root = {getBoundingClientRect: () => ({left: 0, top: 48, width, height: 700}),
    querySelectorAll: () => [group], style: {setProperty: (key, value) => properties.set(key, value)}};
  let resize, disconnected = false, removed = false;
  const host = {innerWidth: 1280, innerHeight: 720,
    addEventListener: (_, callback) => resize = callback,
    removeEventListener: (_, callback) => removed = callback === resize,
    ResizeObserver: class { observe(value) { assert.equal(value, root); } disconnect() {disconnected = true;} }};
  const observer = observeLayout(root, host);
  assert.equal(properties.get('--dxfer-palette-max-x-0'), '1027px');
  assert.equal(properties.get('--dxfer-palette-max-y-0'), '638px');
  width = 800;
  resize();
  assert.equal(properties.get('--dxfer-palette-max-x-0'), '547px');
  observer.dispose();
  assert.ok(disconnected && removed);
});

test('scaled client bounds convert back to layout pixels before clamping', () => {
  const properties = new Map();
  const root = {offsetWidth: 960, offsetHeight: 500,
    getBoundingClientRect: () => ({left: 0, top: 96, width: 1920, height: 1000}),
    querySelectorAll: () => [{offsetWidth: 253, dataset: {paletteIndex: '0'},
      querySelector: () => ({offsetHeight: 34})}],
    style: {setProperty: (key, value) => properties.set(key, value)}};
  const host = {innerWidth: 1920, innerHeight: 1080,
    addEventListener() {}, removeEventListener() {},
    ResizeObserver: class {observe() {} disconnect() {}}};
  const observer = observeLayout(root, host);
  assert.equal(properties.get('--dxfer-palette-max-x-0'), '707px');
  assert.equal(properties.get('--dxfer-palette-max-y-0'), '458px');
  observer.dispose();
});

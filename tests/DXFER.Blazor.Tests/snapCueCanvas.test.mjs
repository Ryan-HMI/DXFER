import test from 'node:test';
import assert from 'node:assert/strict';
import {getCurrentSnapCue, drawSnapCue, positionSnapCue, getCanvasControlBounds} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const point = {x: 5, y: 0};
const source = {id: 'source', kind: 'line', points: [{x: 0, y: 0}, {x: 10, y: 0}]};
const target = {key: 'source|point|mid|5|0', entityId: 'source', entity: source, kind: 'point', label: 'mid', point};
const state = () => ({activeTool: 'line', pointerScreenPoint: {x: 400, y: 300}, hoveredTarget: target,
  document: {entities: [source]}, toolDraft: {points: [{x: 2, y: 4}], previewPoint: point}, automaticSnapConstraints: true});

test('snap metadata remains accessible without painting an information card', () => {
  const value = state(), attributes = {};
  value.canvas = {dataset:{snapCueBox:'old box'},
    setAttribute:(key, text) => attributes[key] = text, removeAttribute:key => delete attributes[key]};
  value.context = new Proxy({}, {get() {throw new Error('Snap descriptions must not paint a card');}});
  drawSnapCue(value);
  assert.match(attributes['aria-description'], /Midpoint/);
  assert.equal(value.canvas.dataset.snapCueBox, undefined);
  value.hoveredTarget = null;
  drawSnapCue(value);
  assert.equal(attributes['aria-description'], undefined);
});

test('first pick names its snap before a draft preview exists', () => {
  const value = state(); value.toolDraft = {points: []};
  assert.deepEqual(getCurrentSnapCue(value).constraintKinds, ['Midpoint']);
  value.hoveredTarget = null;
  assert.equal(getCurrentSnapCue(value), null);
});

test('canvas cue describes accepted midpoint intent and observes canonical setting', () => {
  const value = state();
  assert.deepEqual(getCurrentSnapCue(value).constraintKinds, ['Midpoint']);
  value.automaticSnapConstraints = false;
  assert.deepEqual(getCurrentSnapCue(value).constraintKinds, []);
  value.automaticSnapConstraints = true;
  assert.deepEqual(getCurrentSnapCue(value).constraintKinds, ['Midpoint']);
});

for (const [name, change] of [
  ['leave', s => s.pointerScreenPoint = null],
  ['cancel', s => s.activeTool = 'select'],
  ['pan', s => s.panning = true],
  ['lost snap', s => s.hoveredTarget = null],
  ['keyed override', s => s.toolDraft.previewPoint = {x: 6, y: 0}],
  ['rejected drag', s => s.geometryDrag = {}]
]) test(`${name} clears the cue`, () => {const value = state(); change(value); assert.equal(getCurrentSnapCue(value), null);});

test('accepted point drag uses only relations that the drag commit supports', () => {
  const value = state();
  value.activeTool = 'select';
  value.geometryDrag = {acceptedSnapTarget: target, acceptedSnapPoint: point};
  assert.deepEqual(getCurrentSnapCue(value).constraintKinds, ['Midpoint']);
  value.geometryDrag.acceptedSnapTarget = {...target, kind: 'entity', label: '', key: 'source'};
  assert.deepEqual(getCurrentSnapCue(value).constraintKinds, []);
});

test('cue placement stays inside canvas and avoids the dimension field', () => {
  const size = {width: 1100, height: 700};
  const obstacle = {x: 510, y: 200, width: 300, height: 150};
  const box = positionSnapCue({x: 500, y: 300}, size, 240, 44, [obstacle]);
  assert.ok(box.y >= 350 || box.x + box.width <= 510);
  for (const anchor of [{x: 0, y: 0}, {x: 1099, y: 699}, {x: -20, y: 750}]) {
    const b = positionSnapCue(anchor, size, 240, 44);
    assert.ok(b.x >= 8 && b.y >= 8 && b.x + b.width <= 1092 && b.y + b.height <= 692);
  }
});

test('floating Grain and other visible controls use canvas-relative bounds', () => {
  const panels = [{left: 724, top: 250, width: 160, height: 82},
    {left: 16, top: 60, width: 150, height: 400}, {left: 0, top: 0, width: 0, height: 0}];
  const value = {canvas: {getBoundingClientRect: () => ({left: 8, top: 48}), closest: () => ({
    querySelectorAll: selector => {
      assert.equal(selector, '.dxfer-tool-group, .dxfer-sync-control-bar');
      return panels.map(rect => ({getBoundingClientRect: () => rect}));
    }
  })}};
  const obstacles = getCanvasControlBounds(value);
  assert.equal(obstacles.length, 2);
  assert.deepEqual(obstacles[0], {x: 716, y: 202, width: 160, height: 82});
  const box = positionSnapCue({x: 600, y: 300}, {width: 890, height: 660}, 246, 44, obstacles);
  assert.ok(box && (box.x + box.width <= 716 || box.y >= 284 || box.y + box.height <= 202));
});

test('blocked quadrants fall back to a clear location, never underneath a palette', () => {
  const obstacles = [{x: 200, y: 160, width: 600, height: 300}];
  const box = positionSnapCue({x: 500, y: 300}, {width: 1000, height: 700}, 240, 44, obstacles);
  assert.ok(box && (box.y + box.height <= 160 || box.y >= 460));
});

test('fully covered or too-small canvas has no visible cue box', () => {
  assert.equal(positionSnapCue({x: 100, y: 100}, {width: 300, height: 200}, 240, 44,
    [{x: 0, y: 0, width: 300, height: 200}]), null);
  assert.equal(positionSnapCue({x: 10, y: 10}, {width: 100, height: 40}, 84, 44), null);
});

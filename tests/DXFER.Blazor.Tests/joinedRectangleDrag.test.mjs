import assert from 'node:assert/strict';
import test from 'node:test';
import {applyGeometryDragPreview} from '../../src/DXFER.Blazor/wwwroot/drawingCanvas.js';

const constraint = (kind, ...referenceKeys) => ({id: kind + referenceKeys.join(), kind, referenceKeys, state: 'satisfied'});
function rectangle(prefix, points) {
  return {
    entities: points.map((p, i) => ({id: prefix + i, kind: 'line', points: [p, points[(i + 1) % 4]]})),
    constraints: [...points.map((_, i) => constraint('coincident', `${prefix}${i}:end`, `${prefix}${(i + 1) % 4}:start`)),
      constraint('parallel', prefix + 0, prefix + 2), constraint('parallel', prefix + 1, prefix + 3),
      constraint('perpendicular', prefix + 0, prefix + 1)]
  };
}

export function joinedRectangles(reverse = false) {
  const a = rectangle('a', [{x: 0, y: 0}, {x: 6, y: 0}, {x: 6, y: 9}, {x: 0, y: 9}]);
  const b = rectangle('b', [{x: 0, y: 0}, {x: 12, y: -2}, {x: 11.5, y: -5}, {x: -0.5, y: -3}]);
  const constraints = [...a.constraints, ...b.constraints, constraint('horizontal', 'a0'), constraint('vertical', 'a1'),
    constraint('coincident', 'a0:start', '$origin:center'), constraint('coincident', 'b0:start', 'a0:start')];
  return {entities: [...a.entities, ...b.entities], dimensions: [], constraints: reverse ? constraints.reverse() : constraints};
}

for (const reverse of [false, true]) for (const key of ['a1', 'a1|point|mid|6|4.5']) {
  test(`joined rectangle edge projects diagonal drag without disturbing its peer: ${key}, reverse=${reverse}`, () => {
    const document = joinedRectangles(reverse), before = structuredClone(document);
    const preview = applyGeometryDragPreview(document, key, {x: 6, y: 4.5}, {x: 8, y: 5.5});
    assert.ok(preview, 'a corner weld must not disable independent rectangle edge resizing');
    assert.deepEqual(preview.entities[1].points, [{x: 8, y: 0}, {x: 8, y: 9}]);
    assert.deepEqual(preview.entities[0].points[0], {x: 0, y: 0});
    assert.deepEqual(preview.entities.slice(4).map(e => e.points), before.entities.slice(4).map(e => e.points));
    assert.deepEqual(document, before);
  });
}

test('joined rectangle fixed edge still refuses unchanged', () => {
  const document = joinedRectangles();
  document.constraints.push(constraint('fix', 'a1'));
  const before = structuredClone(document);
  assert.equal(applyGeometryDragPreview(document, 'a1', {x: 6, y: 4.5}, {x: 8, y: 5.5}), null);
  assert.deepEqual(document, before);
});

test('recognized rectangle does not search unrelated four-line combinations on every pointer move', () => {
  const document = joinedRectangles();
  for (let i = 0; i < 40; i++) document.entities.unshift({id: `unrelated${i}`, kind: 'line', points: [{x: i, y: 30}, {x: i + 1, y: 31}]});
  let reads = 0;
  for (const c of document.constraints) {
    const kind = c.kind;
    Object.defineProperty(c, 'kind', {get() {reads++; return kind;}, enumerable: true});
  }
  assert.ok(applyGeometryDragPreview(document, 'a1', {x: 6, y: 4.5}, {x: 8, y: 5.5}));
  assert.ok(reads < 5000, `constraint visits must stay local, observed ${reads}`);
});

import assert from "node:assert/strict";
import test from "node:test";
import {
  areWorldLinesParallel,
  closestPointOnWorldSegment,
  getPositiveSweepDegrees,
  getWorldLineIntersection,
  midpoint
} from "../../src/DXFER.Blazor/wwwroot/canvas/geometry.js";

test("canvas geometry module exposes pure world helpers", () => {
  assert.deepEqual(
    midpoint({ x: 0, y: 2 }, { x: 10, y: 6 }),
    { x: 5, y: 4 });

  assert.deepEqual(
    getWorldLineIntersection(
      { start: { x: 0, y: 0 }, end: { x: 10, y: 0 } },
      { start: { x: 4, y: -2 }, end: { x: 4, y: 2 } }),
    { x: 4, y: 0 });

  assert.equal(
    areWorldLinesParallel(
      { start: { x: 0, y: 0 }, end: { x: 10, y: 0 } },
      { start: { x: 0, y: 4 }, end: { x: 8, y: 4 } }),
    true);

  assert.equal(getPositiveSweepDegrees(350, 10), 20);

  const projection = closestPointOnWorldSegment(
    { x: 4, y: 3 },
    { x: 0, y: 0 },
    { x: 10, y: 0 });

  assert.deepEqual(projection.point, { x: 4, y: 0 });
  assert.equal(projection.parameter, 0.4);
  assert.equal(projection.distance, 3);
});

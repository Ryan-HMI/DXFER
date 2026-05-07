import assert from "node:assert/strict";
import test from "node:test";
import {
  getLinearDimensionScreenGeometry,
  getRadialDimensionScreenGeometry
} from "../../src/DXFER.Blazor/wwwroot/canvas/dimensions.js";

test("canvas dimensions module exposes linear screen geometry", () => {
  const geometry = getLinearDimensionScreenGeometry(
    { x: 0, y: 20 },
    { x: 100, y: 20 },
    { x: 50, y: 0 },
    24);

  assert.equal(geometry.extensionSegments.length, 2);
  assert.equal(geometry.dimensionSegments.length, 2);
  assert.equal(geometry.arrows.length, 2);
  assert.deepEqual(geometry.arrows[0].point, { x: 0, y: 0 });
  assert.deepEqual(geometry.arrows[1].point, { x: 100, y: 0 });
});

test("canvas dimensions module exposes radial screen geometry", () => {
  const geometry = getRadialDimensionScreenGeometry(
    { x: 100, y: 100 },
    30,
    { x: 200, y: 100 },
    true);

  assert.equal(geometry.segments.length, 1);
  assert.equal(geometry.arrows.length, 1);
  assert.deepEqual(geometry.arrows[0].point, { x: 130, y: 100 });
});

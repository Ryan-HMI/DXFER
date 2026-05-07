import assert from "node:assert/strict";
import test from "node:test";

import {
  CONSTRAINT_KEY_PREFIX,
  POINT_KEY_SEPARATOR,
  SEGMENT_KEY_SEPARATOR,
  parsePointTargetKeyParts,
  parseSegmentTargetKeyParts
} from "../../src/DXFER.Blazor/wwwroot/canvas/targetKeys.js";

test("target key separators stay stable for persisted selection keys", () => {
  assert.equal(POINT_KEY_SEPARATOR, "|point|");
  assert.equal(SEGMENT_KEY_SEPARATOR, "|segment|");
  assert.equal(CONSTRAINT_KEY_PREFIX, "constraint:");
});

test("point target key parser preserves labels and fallback point", () => {
  assert.deepEqual(parsePointTargetKeyParts("line-1|point|midpoint-edge|4.25|-2"), {
    entityId: "line-1",
    label: "midpoint-edge",
    point: { x: 4.25, y: -2 }
  });

  assert.deepEqual(parsePointTargetKeyParts("line-1|point|arc|tangent|3|7"), {
    entityId: "line-1",
    label: "arc|tangent",
    point: { x: 3, y: 7 }
  });

  assert.equal(parsePointTargetKeyParts("line-1|point|bad|x|7"), null);
  assert.equal(parsePointTargetKeyParts("line-1"), null);
});

test("segment target key parser returns entity id and segment index", () => {
  assert.deepEqual(parseSegmentTargetKeyParts("poly-1|segment|3"), {
    entityId: "poly-1",
    segmentIndex: 3
  });

  assert.equal(parseSegmentTargetKeyParts("poly-1|segment|3.5"), null);
  assert.equal(parseSegmentTargetKeyParts("poly-1|point|start|0|0"), null);
});

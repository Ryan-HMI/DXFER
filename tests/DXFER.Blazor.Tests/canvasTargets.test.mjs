import assert from "node:assert/strict";
import test from "node:test";

import {
  getPointTargetMarker,
  isDynamicTargetCurrentToPointer,
  isPanPointerDownForTool,
  isTargetEligibleForConstraintTool,
  shouldDrawSelectionTargetOverlay
} from "../../src/DXFER.Blazor/wwwroot/canvas/targets.js";

test("selection target overlays skip dimensions and constraints", () => {
  assert.equal(shouldDrawSelectionTargetOverlay({ kind: "dimension" }), false);
  assert.equal(shouldDrawSelectionTargetOverlay({ kind: "constraint" }), false);
  assert.equal(shouldDrawSelectionTargetOverlay({ kind: "entity" }), true);
});

test("point target marker classifies tangent and midpoint snap labels", () => {
  assert.equal(getPointTargetMarker({ label: "circle-tangent-edge-0" }), "tangent");
  assert.equal(getPointTargetMarker({ label: "midpoint-edge-0" }), "midpoint");
  assert.equal(getPointTargetMarker({ label: "quadrant-0" }), "point");
});

test("dynamic target visibility follows the current pointer location", () => {
  const state = {
    pointerScreenPoint: { x: 100, y: 100 },
    view: {
      scale: 2,
      offsetX: 10,
      offsetY: 20
    }
  };
  const target = {
    dynamic: true,
    point: { x: 45, y: -40 }
  };

  assert.equal(isDynamicTargetCurrentToPointer(state, target), true);

  state.pointerScreenPoint = { x: 120, y: 100 };
  assert.equal(isDynamicTargetCurrentToPointer(state, target), false);
  assert.equal(isDynamicTargetCurrentToPointer(state, { dynamic: false, point: target.point }), true);
});

test("pan pointer detection is middle button only", () => {
  assert.equal(isPanPointerDownForTool({ button: 1 }, "line"), true);
  assert.equal(isPanPointerDownForTool({ button: 0, shiftKey: true }, "line"), false);
  assert.equal(isPanPointerDownForTool({ button: 2 }, "select"), false);
});

test("constraint tool eligibility filters target categories", () => {
  const lineTarget = { kind: "entity", entity: { Kind: "Line" }, key: "edge" };
  const circleTarget = { kind: "entity", entity: { kind: "circle" }, key: "circle" };
  const arcTarget = { kind: "entity", entity: { kind: "arc" }, key: "arc" };
  const startPoint = { kind: "point", entity: { kind: "line" }, label: "start" };
  const midpoint = { kind: "point", entity: { kind: "line" }, label: "mid" };

  assert.equal(isTargetEligibleForConstraintTool("tangent", lineTarget), true);
  assert.equal(isTargetEligibleForConstraintTool("tangent", circleTarget), true);
  assert.equal(isTargetEligibleForConstraintTool("concentric", arcTarget), true);
  assert.equal(isTargetEligibleForConstraintTool("coincident", startPoint), true);
  assert.equal(isTargetEligibleForConstraintTool("coincident", midpoint), false);
  assert.equal(isTargetEligibleForConstraintTool("horizontal", lineTarget), true);
  assert.equal(isTargetEligibleForConstraintTool("horizontal", circleTarget), false);
  assert.equal(isTargetEligibleForConstraintTool("parallel", startPoint), false);
});

import assert from "node:assert/strict";
import test from "node:test";
import {
  getPowerTrimAlternateEdgeHit,
  resolveNearestTargetHit
} from "../../src/DXFER.Blazor/wwwroot/canvas/targetResolution.js";

const pointTarget = { kind: "point", key: "line-a|point|start|0|0", entityId: "line-a", entity: { kind: "line" } };
const edgeTarget = { kind: "entity", key: "line-b", entityId: "line-b", entity: { kind: "line" } };
const dimensionTarget = { kind: "dimension", key: "dimension:1" };
const dynamicTarget = { kind: "point", key: "__dynamic|point|ortho|1|1", dynamic: true };

test("nearest target resolution prefers point hits inside snap tolerance", () => {
  const target = resolveNearestTargetHit({
    nearestPointHit: { target: pointTarget, distance: 8 },
    nearestEdgeHit: { target: edgeTarget, distance: 1 },
    dimensionHit: { target: dimensionTarget, distance: 1 },
    dynamicPointHit: { target: dynamicTarget, distance: 1 }
  });

  assert.equal(target, pointTarget);
});

test("nearest target resolution keeps constraint mode limited to eligible edge hits", () => {
  assert.equal(
    resolveNearestTargetHit({
      constraintTool: "horizontal",
      nearestPointHit: { target: pointTarget, distance: 9 },
      nearestEdgeHit: { target: edgeTarget, distance: 6 },
      dimensionHit: { target: dimensionTarget, distance: 1 },
      dynamicPointHit: { target: dynamicTarget, distance: 1 }
    }),
    edgeTarget);

  assert.equal(
    resolveNearestTargetHit({
      constraintTool: "horizontal",
      nearestPointHit: { target: pointTarget, distance: 9 },
      nearestEdgeHit: { target: edgeTarget, distance: 10 }
    }),
    null);
});

test("nearest target resolution can defer edge fallback until dynamic hits are known", () => {
  assert.equal(
    resolveNearestTargetHit({
      nearestPointHit: { target: pointTarget, distance: 9 },
      nearestEdgeHit: { target: edgeTarget, distance: 1 },
      edgeFallback: false
    }),
    null);
});

test("nearest target resolution checks dimension, dynamic, then edge hits for normal tools", () => {
  assert.equal(
    resolveNearestTargetHit({
      nearestEdgeHit: { target: edgeTarget, distance: 1 },
      dimensionHit: { target: dimensionTarget, distance: 6 },
      dynamicPointHit: { target: dynamicTarget, distance: 1 }
    }),
    dimensionTarget);

  assert.equal(
    resolveNearestTargetHit({
      nearestEdgeHit: { target: edgeTarget, distance: 1 },
      dynamicPointHit: { target: dynamicTarget, distance: 8 }
    }),
    dynamicTarget);

  assert.equal(
    resolveNearestTargetHit({
      nearestEdgeHit: { target: edgeTarget, distance: 6 },
      dynamicPointHit: { target: dynamicTarget, distance: 9 }
    }),
    edgeTarget);
});

test("nearest target resolution returns null when every hit is outside tolerance", () => {
  assert.equal(
    resolveNearestTargetHit({
      nearestPointHit: { target: pointTarget, distance: 9 },
      nearestEdgeHit: { target: edgeTarget, distance: 10 },
      dimensionHit: { target: dimensionTarget, distance: 10 },
      dynamicPointHit: { target: dynamicTarget, distance: 9 }
    }),
    null);
});

test("power trim alternate edge hit chooses a nearby different line-family edge", () => {
  const farEdge = { target: { ...edgeTarget, entityId: "line-c" }, distance: 5 };
  const nearEdge = { target: edgeTarget, distance: 2 };

  assert.equal(
    getPowerTrimAlternateEdgeHit(
      { target: pointTarget, distance: 7 },
      [farEdge, nearEdge],
      { getEntityKind: entity => String(entity?.kind || "").toLowerCase() }),
    nearEdge);
});

test("power trim alternate edge hit rejects non-line-family and same-entity point hits", () => {
  assert.equal(
    getPowerTrimAlternateEdgeHit(
      { target: { ...pointTarget, entity: { kind: "circle" } }, distance: 7 },
      [{ target: edgeTarget, distance: 2 }],
      { getEntityKind: entity => String(entity?.kind || "").toLowerCase() }),
    null);

  assert.equal(
    getPowerTrimAlternateEdgeHit(
      { target: pointTarget, distance: 7 },
      [{ target: { ...edgeTarget, entityId: "line-a" }, distance: 2 }],
      { getEntityKind: entity => String(entity?.kind || "").toLowerCase() }),
    null);
});

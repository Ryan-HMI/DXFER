import assert from "node:assert/strict";
import test from "node:test";
import { applyGeometryDragPreview, findNearestTarget, finishGeometryDrag, getPersistentSplineTangentHandlesForEntity, updateGeometryDrag } from "../../src/DXFER.Blazor/wwwroot/drawingCanvas.js";
import { sampleControlSpline } from "../../src/DXFER.Blazor/wwwroot/canvas/splineGeometry.js";

function cubic() {
  return {
    id: "spline", kind: "spline",
    controlPoints: [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 3, y: 1 }, { x: 4, y: 1 }],
    points: [{ x: 0, y: 0 }, { x: 2, y: 0.5 }, { x: 4, y: 1 }],
    splineDegree: 3, splineKnots: [0, 0, 0, 0, 1, 1, 1, 1], splineWeights: [1, 1, 1, 1],
    startTangentControlPointIndex: 1, endTangentControlPointIndex: 2
  };
}

for (const start of [true, false]) {
  test(`control spline ${start ? "start" : "end"} tangent preview preserves native curve representation`, () => {
    const spline = cubic();
    const document = { entities: [spline] };
    const before = structuredClone(document);
    const from = start ? { x: 0.25, y: 0 } : { x: 3.75, y: 1 };
    const to = { x: from.x, y: from.y + 0.5 };
    const label = start ? "tangent-start" : "tangent-end";
    const preview = applyGeometryDragPreview(document, `spline|point|${label}|${from.x}|${from.y}`, from, to);
    assert.ok(preview);
    const result = preview.entities[0];
    const expected = structuredClone(spline.controlPoints);
    expected[start ? 1 : 2].y += 2;
    assert.deepEqual(result.controlPoints, expected);
    assert.equal(result.splineDegree, 3);
    assert.deepEqual(result.splineKnots, spline.splineKnots);
    assert.deepEqual(result.splineWeights, spline.splineWeights);
    assert.deepEqual(result.fitPoints, []);
    assert.equal(result.points.length, 25);
    assert.deepEqual(result.points[12], { x: 2, y: 1.25 });
    const handles = getPersistentSplineTangentHandlesForEntity(result);
    assert.deepEqual(start ? handles[0].forward : handles[1].backward, to);
    assert.deepEqual(document, before);
  });

  test(`${start ? "start" : "end"} round control preview moves one point by the unscaled delta`, () => {
    const spline = cubic();
    const index = start ? 1 : 2;
    const from = spline.controlPoints[index];
    const to = { x: from.x, y: from.y + 0.4 };
    const preview = applyGeometryDragPreview({ entities: [spline] }, `spline|point|control-${index}|${from.x}|${from.y}`, from, to);
    const expected = structuredClone(spline.controlPoints);
    expected[index] = to;
    assert.deepEqual(preview.entities[0].controlPoints, expected);
    assert.equal(preview.entities[0].points.length, 25);
    assert.ok(Math.abs(preview.entities[0].points[12].y - 0.65) < 1e-12);
  });

  for (const fit of [false, true]) {
    test(`${fit ? "fit" : "control"} ${start ? "start" : "end"} tangent with Fix has no optimistic mutation`, () => {
      const spline = cubic();
      if (fit) spline.fitPoints = structuredClone(spline.controlPoints);
      const document = { entities: [spline], constraints: [{ id: "fixed", kind: "Fix", referenceKeys: ["spline"], state: "satisfied" }] };
      const before = structuredClone(document);
      const from = start ? { x: 0.25, y: 0 } : { x: 3.75, y: 1 };
      const label = start ? "tangent-start" : "tangent-end";
      assert.equal(applyGeometryDragPreview(document, `spline|point|${label}|${from.x}|${from.y}`, from, { x: from.x, y: from.y + 0.4 }), null);
      assert.deepEqual(document, before);
    });
  }

  test(`fit ${start ? "start" : "end"} tangent preserves every fit point and the opposite handle`, () => {
    const spline = cubic();
    spline.fitPoints = structuredClone(spline.controlPoints);
    spline.startTangentHandle = { x: 0.25, y: 0 };
    spline.endTangentHandle = { x: 3.75, y: 1 };
    const from = start ? spline.startTangentHandle : spline.endTangentHandle;
    const to = { x: from.x, y: from.y + 0.4 };
    const label = start ? "tangent-start" : "tangent-end";
    const result = applyGeometryDragPreview({ entities: [spline] }, `spline|point|${label}|${from.x}|${from.y}`, from, to).entities[0];
    assert.deepEqual(result.fitPoints, spline.fitPoints);
    assert.deepEqual(result.controlPoints, spline.controlPoints);
    assert.deepEqual(result.startTangentHandle, start ? to : spline.startTangentHandle);
    assert.deepEqual(result.endTangentHandle, start ? spline.endTangentHandle : to);
  });
}

test("rational multi-span control preview agrees with native sampling at the internal knot", () => {
  const spline = cubic();
  spline.controlPoints = [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 2, y: 1 }, { x: 3, y: 1 }, { x: 4, y: 0 }];
  spline.splineKnots = [2, 2, 2, 2, 3, 4, 4, 4, 4];
  spline.splineWeights = [1, 2, 3, 2, 1];
  spline.endTangentControlPointIndex = 3;
  const other = { id: "other", kind: "point", points: [{ x: 9, y: 9 }] };
  const preview = applyGeometryDragPreview({ entities: [spline, other] }, "spline|point|tangent-start|0.25|0",
    { x: 0.25, y: 0 }, { x: 0.25, y: 0.4 });
  const result = preview.entities[0];
  assert.equal(result.points.length, 33);
  assert.ok(Math.abs(result.points[16].x - 2) < 1e-12);
  assert.ok(Math.abs(result.points[16].y - 1.12) < 1e-12);
  assert.deepEqual(result.splineKnots, spline.splineKnots);
  assert.deepEqual(result.splineWeights, spline.splineWeights);
  assert.deepEqual(result.controlPoints.slice(2), spline.controlPoints.slice(2));
  assert.deepEqual(preview.entities[1].points, other.points);
});

test("clamped cubic samples follow the exact rational Bernstein curve, not its control polygon", () => {
  const spline = cubic();
  spline.splineWeights = [1, 2, 3, 1];
  const samples = sampleControlSpline(spline.controlPoints, spline.splineDegree, spline.splineKnots, spline.splineWeights);
  for (let index = 0; index < samples.length; index++) {
    const t = index / 24;
    const basis = [(1 - t) ** 3, 3 * t * (1 - t) ** 2, 3 * t * t * (1 - t), t ** 3];
    const weighted = basis.map((value, i) => value * spline.splineWeights[i]);
    const denominator = weighted.reduce((sum, value) => sum + value, 0);
    for (const axis of ["x", "y"]) {
      const expected = weighted.reduce((sum, value, i) => sum + value * spline.controlPoints[i][axis], 0) / denominator;
      assert.ok(Math.abs(samples[index][axis] - expected) < 1e-12);
    }
  }
});

test("repeated interior knots retain both exact Bezier spans", () => {
  const controls = [{ x: 0, y: 0 }, { x: 1, y: 2 }, { x: 2, y: 2 }, { x: 3, y: 0 },
    { x: 4, y: -2 }, { x: 5, y: -2 }, { x: 6, y: 0 }];
  const samples = sampleControlSpline(controls, 3, [0, 0, 0, 0, 0.5, 0.5, 0.5, 1, 1, 1, 1], Array(7).fill(1));
  assert.equal(samples.length, 33);
  assert.deepEqual(samples[8], { x: 1.5, y: 1.5 });
  assert.deepEqual(samples[16], { x: 3, y: 0 });
  assert.deepEqual(samples[24], { x: 4.5, y: -1.5 });
});

test("unsupported endpoint tangents are neither shown nor previewed", () => {
  const spline = cubic();
  spline.startTangentControlPointIndex = null;
  spline.endTangentControlPointIndex = null;
  const handles = getPersistentSplineTangentHandlesForEntity(spline);
  assert.equal(handles[0].forward, null);
  assert.equal(handles[1].backward, null);
  assert.equal(applyGeometryDragPreview({ entities: [spline] }, "spline|point|tangent-start|0.25|0",
    { x: 0.25, y: 0 }, { x: 0.25, y: 0.4 }), null);
});

test("non-fit handles ignore stale explicit metadata before and after dragging", () => {
  const spline = cubic();
  spline.startTangentHandle = { x: 90, y: 90 };
  spline.endTangentHandle = { x: -90, y: -90 };
  const before = getPersistentSplineTangentHandlesForEntity(spline);
  assert.deepEqual(before[0].forward, { x: 0.25, y: 0 });
  assert.deepEqual(before[1].backward, { x: 3.75, y: 1 });
  const result = applyGeometryDragPreview({ entities: [spline] }, "spline|point|tangent-start|0.25|0",
    { x: 0.25, y: 0 }, { x: 0.25, y: 0.4 }).entities[0];
  const after = getPersistentSplineTangentHandlesForEntity(result);
  assert.deepEqual(after[0].forward, { x: 0.25, y: 0.4 });
  assert.deepEqual(after[1].backward, before[1].backward);
  assert.deepEqual(result.startTangentHandle, spline.startTangentHandle);
  assert.deepEqual(result.endTangentHandle, spline.endTangentHandle);
});

for (const start of [true, false]) {
  test(`quadratic ${start ? "start" : "end"} tangent shares its middle control with the other tangent`, () => {
    const spline = cubic();
    spline.controlPoints = [{ x: 0, y: 0 }, { x: 2, y: 1 }, { x: 4, y: 0 }];
    spline.splineDegree = 2;
    spline.splineKnots = [0, 0, 0, 1, 1, 1];
    spline.splineWeights = [1, 1, 1];
    spline.endTangentControlPointIndex = 1;
    const from = start ? { x: 0.5, y: 0.25 } : { x: 3.5, y: 0.25 };
    const result = applyGeometryDragPreview({ entities: [spline] },
      `spline|point|tangent-${start ? "start" : "end"}|${from.x}|${from.y}`, from, { x: from.x, y: 0.75 }).entities[0];
    assert.deepEqual(result.controlPoints, [{ x: 0, y: 0 }, { x: 2, y: 3 }, { x: 4, y: 0 }]);
    const handles = getPersistentSplineTangentHandlesForEntity(result);
    assert.ok(Math.abs(handles[0].forward.x - 0.5) < 1e-12);
    assert.ok(Math.abs(handles[1].backward.x - 3.5) < 1e-12);
    assert.equal(handles[0].forward.y, 0.75);
    assert.equal(handles[1].backward.y, 0.75);
    assert.deepEqual(result.points[12], { x: 2, y: 1.5 });
  });
}

test("missing or invalid native spline parameters refuse preview instead of drawing the control polygon", () => {
  for (const change of [s => delete s.splineKnots, s => s.splineWeights[1] = -1, s => s.splineKnots[4] = -1]) {
    const spline = cubic();
    change(spline);
    const document = { entities: [spline] };
    const before = structuredClone(document);
    assert.equal(applyGeometryDragPreview(document, "spline|point|control-1|1|0", { x: 1, y: 0 }, { x: 1, y: 1 }), null);
    assert.deepEqual(document, before);
  }
});

test("selected square and neighboring round grips remain distinct and release matches the exact preview", async () => {
  const originalDocument = { entities: [cubic()] };
  const key = "spline|point|tangent-start|0.25|0";
  const calls = [];
  const state = {
    activeTool: "select", acquiredSnapPoints: [], document: originalDocument,
    selectedKeys: new Set(["spline|point|start|0|0"]),
    view: { scale: 100, offsetX: 0, offsetY: 0 },
    canvas: { dataset: {}, getBoundingClientRect: () => ({ left: 0, top: 0, width: 800, height: 600 }) },
    dotnetRef: { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(true); } }
  };
  assert.equal(findNearestTarget(state, { x: 25, y: 0 }).label, "tangent-start");
  assert.equal(findNearestTarget(state, { x: 100, y: 0 }).label, "control-1");
  state.selectedKeys = new Set([key]);
  state.activeSelectionKey = key;
  state.geometryDrag = { targetKey: key, startScreenPoint: { x: 25, y: 0 },
    startWorldPoint: { x: 0.25, y: 0 }, originalDocument, moved: true };
  assert.equal(updateGeometryDrag(state, { x: 25, y: -40 }), true);
  assert.deepEqual(state.hoveredTarget.point, { x: 0.25, y: 0.4 });
  assert.deepEqual(state.document.entities[0].controlPoints[1], { x: 1, y: 1.6 });
  const preview = structuredClone(state.document.entities[0]);
  assert.equal(finishGeometryDrag(state, { x: 25, y: -40 }), true);
  await new Promise(resolve => setImmediate(resolve));
  assert.deepEqual(state.document.entities[0], preview);
  assert.deepEqual(calls.find(call => call[0] === "OnGeometryDragRequested"),
    ["OnGeometryDragRequested", key, 0.25, 0, 0.25, 0.4, false, null]);
  assert.deepEqual(state.selectedKeys, new Set([key]));
  assert.deepEqual(originalDocument.entities[0].controlPoints[1], { x: 1, y: 0 });
});

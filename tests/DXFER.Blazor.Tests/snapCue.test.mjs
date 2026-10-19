import assert from "node:assert/strict";
import test from "node:test";
import { getSnapCue, getInferredLineConstraintKinds } from "../../src/DXFER.Blazor/wwwroot/canvas/snapCue.js";
import { getCreationSnapIntents, getCurrentSnapCue, positionSnapCue } from "../../src/DXFER.Blazor/wwwroot/drawingCanvas.js";

const point = { x: 2, y: 3 };
const peer = { id: "peer", kind: "line", points: [point, { x: 8, y: 3 }] };
const endpoint = { key: "peer:start", entityId: "peer", entity: peer, kind: "point", label: "start", point };
const intent = (kind, referenceKey = "peer:start", at = point) => ({ kind, point: at, referenceKeys: [referenceKey] });
const cue = (overrides = {}) => getSnapCue({ target: endpoint, point, tool: "line", pointCount: 1,
  intents: [intent("Coincident")], automaticConstraints: true, ...overrides });

for (const [tool, supported, unsupported] of [
  ["point", [0], [1]],
  ["line", [0, 1], [2]],
  ["midpointline", [1], [0, 2]],
  ["centercircle", [0], [1, 2]],
  ["threepointcircle", [], [0, 1, 2]],
  ["ellipse", [0], [1, 2]],
  ["ellipticalarc", [0], [1, 2, 3]],
  ["twopointrectangle", [0, 1], [2]],
  ["centerrectangle", [1], [0, 2]],
  ["alignedrectangle", [0, 1], [2, 3]],
  ["inscribedpolygon", [0], [1]],
  ["circumscribedpolygon", [0], [1]],
  ["threepointarc", [0, 2], [1, 3]],
  ["tangentarc", [0, 2], [1, 3]],
  ["centerpointarc", [0, 1], [2, 3]],
  ["slot", [0, 1], [2]],
  ["spline", [0], [1, 2]],
  ["splinecontrolpoint", [0], [1, 2]],
  ["bezier", [0, 3], [1, 2, 4]]
]) {
  test(`${tool} only advertises pending point relations at supported stages`, () => {
    for (const pointCount of supported) {
      assert.deepEqual(cue({ tool, pointCount }).constraintKinds, ["Coincident"]);
      assert.equal(cue({ tool, pointCount }).text, "Endpoint - Pending: Coincident");
    }
    for (const pointCount of unsupported) {
      assert.deepEqual(cue({ tool, pointCount }).constraintKinds, []);
      assert.equal(cue({ tool, pointCount }).text, "Endpoint - Snap only");
    }
  });
}

test("missing, stale or nonfinite final coordinates suppress the entire cue", () => {
  assert.equal(getSnapCue(), null);
  for (const target of [null, {}, { label: "start" }]) assert.equal(cue({ target }), null);
  for (const position of [null, { x: NaN, y: 3 }, { x: Infinity, y: 3 }, { x: 2.001, y: 3 }]) {
    assert.equal(cue({ point: position }), null);
  }
  assert.ok(cue({ point: { x: 2 + 1e-7, y: 3 } }));
});

test("automatic constraints off or unspecified never promises persistence", () => {
  for (const automaticConstraints of [false, undefined, null, "true"]) {
    assert.deepEqual(cue({ automaticConstraints }).constraintKinds, []);
  }
  assert.deepEqual(getSnapCue({ target: endpoint, point, tool: "line", pointCount: 0,
    intents: [intent("Coincident")] }).constraintKinds, []);
});

test("tool normalization and invalid stages are conservative", () => {
  assert.deepEqual(cue({ tool: "Center-Circle", pointCount: 0 }).constraintKinds, ["Coincident"]);
  assert.deepEqual(cue({ tool: "two_point_rectangle", pointCount: 1 }).constraintKinds, ["Coincident"]);
  for (const pointCount of [-1, undefined, NaN, 0.5, "1"]) assert.deepEqual(cue({ pointCount }).constraintKinds, []);
  for (const tool of ["select", "offset", "unknown", null]) assert.deepEqual(cue({ tool }).constraintKinds, []);
});

test("origin uses actual commit Coincident intent, not Fix or Concentric", () => {
  const origin = { key: "$origin:center", entityId: "$origin", kind: "point", label: "Origin", point: { x: 0, y: 0 } };
  const intents = getCreationSnapIntents({}, origin, origin.point);
  assert.deepEqual(cue({ target: origin, point: origin.point, intents }), {
    snapType: "Origin", constraintKinds: ["Coincident"], detail: "Pending: Coincident", text: "Origin - Pending: Coincident"
  });
});

for (const [target, expectedType, kinds] of [
  [{ ...endpoint, key: "peer|point|mid|2|3", label: "mid" }, "Midpoint", ["Midpoint"]],
  [{ ...endpoint, key: "peer", kind: "entity", label: "", snapPoint: point, point: undefined }, "On line", ["PointOnLine"]],
  [{ ...endpoint, entity: { id: "peer", kind: "point" }, key: "peer", kind: "entity", label: "", snapPoint: point }, "Point", ["Coincident"]],
  [{ ...endpoint, entity: { id: "peer", kind: "circle" }, key: "peer:center", label: "center" }, "Center", ["Coincident"]]
]) {
  test(`${expectedType} shares actual getCreationSnapIntents mapping`, () => {
    const intents = getCreationSnapIntents({ document: { entities: [peer] } }, target, point);
    const result = cue({ target, intents });
    assert.equal(result.snapType, expectedType);
    assert.deepEqual(result.constraintKinds, kinds);
  });
}

test("line-line intersection deduplicates display kind without losing commit relations", () => {
  const other = { id: "other", kind: "line" };
  const target = { ...endpoint, key: "peer|point|intersection-other-0|2|3", label: "intersection-other-0" };
  const intents = getCreationSnapIntents({ document: { entities: [peer, other] } }, target, point);
  assert.equal(intents.length, 2);
  assert.deepEqual(cue({ target, intents }), {
    snapType: "Intersection", constraintKinds: ["PointOnLine"], detail: "Pending: PointOnLine", text: "Intersection - Pending: PointOnLine"
  });
  assert.equal(intents.length, 2);
});

test("polyline midpoint and segment interior retain their distinct pending kinds", () => {
  const entity = { id: "poly", kind: "polyline", points: [{ x: 0, y: 3 }, { x: 4, y: 3 }] };
  for (const [kind, label, key, expectedType, expectedKind] of [
    ["point", "mid-0", "poly|point|mid-0|2|3", "Midpoint", "Midpoint"],
    ["segment", "", "poly|segment|0", "On line", "PointOnLine"]
  ]) {
    const target = { entity, entityId: "poly", kind, label, key, snapPoint: point };
    const intents = getCreationSnapIntents({ document: { entities: [entity] } }, target, point);
    assert.equal(intents[0].referenceKeys[0], "poly|segment|0");
    assert.equal(cue({ target, intents }).snapType, expectedType);
    assert.deepEqual(cue({ target, intents }).constraintKinds, [expectedKind]);
  }
});

for (const [label, snapType, kinds] of [
  ["project-v-start", "Vertical guide", ["Vertical"]],
  ["project-h-start", "Horizontal guide", ["Horizontal"]],
  ["project-start-end", "Guide intersection", ["Vertical", "Horizontal"]],
  ["project-horizontal-start-peer-0", "Horizontal guide", ["Horizontal", "PointOnLine"]]
]) {
  test(`${label} only describes supplied guide relations`, () => {
    const target = { dynamic: true, kind: "point", point, label,
      creationRelations: kinds.map(kind => ({ kind, referenceKeys: ["peer:start"] })) };
    const intents = getCreationSnapIntents({}, target, point);
    assert.equal(cue({ target, intents }).snapType, snapType);
    assert.deepEqual(cue({ target, intents }).constraintKinds, kinds);
    assert.deepEqual(cue({ target, intents: [] }).constraintKinds, []);
  });
}

test("unsupported source handles do not advertise Coincident", () => {
  for (const kind of ["ellipse", "circle", "polygon"]) {
    for (const label of ["start", "end", "major-end", "quadrant-0", "vertex-0"]) {
      const target = { ...endpoint, entity: { id: "peer", kind }, label };
      assert.deepEqual(cue({ target }).constraintKinds, []);
    }
  }
});

test("explicit center-circle perimeter Tangent is pending only at its matching stage", () => {
  const target = { dynamic: true, kind: "point", label: "circle-tangent-peer-line", point, tangentReferenceKey: "peer|segment|0" };
  const args = { target, tool: "centercircle", pointCount: 1, intents: [] };
  assert.deepEqual(cue(args), { snapType: "Tangent", constraintKinds: ["Tangent"], detail: "Pending: Tangent", text: "Tangent - Pending: Tangent" });
  for (const pointCount of [0, 2]) assert.deepEqual(cue({ ...args, pointCount }).constraintKinds, []);
  assert.deepEqual(cue({ ...args, automaticConstraints: false }).constraintKinds, []);
  assert.deepEqual(cue({ ...args, tool: "threepointcircle", pointCount: 2 }).constraintKinds, []);
  assert.deepEqual(cue({ ...args, target: { ...target, tangentReferenceKey: " " } }).constraintKinds, []);
  assert.equal(cue({ ...args, point: { x: 4, y: 3 } }), null);
});

for (const [label, snapType] of [
  ["normal-peer", "Normal"], ["perpendicular-peer", "Perpendicular"],
  ["tangent-peer-0", "Tangent"], ["tangent-chain", "Tangent"],
  ["circle-tangent-peer-0", "Tangent"], ["midpoint-start-end", "Midpoint"]
]) {
  test(`${label} alone is not evidence of a persisted constraint`, () => {
    const target = { dynamic: true, kind: "point", point, label };
    const intents = getCreationSnapIntents({}, target, point);
    const detail = ["Tangent", "Perpendicular"].includes(snapType) ? "Constraint evaluated on commit" : "Snap only";
    assert.deepEqual(cue({ target, intents }), { snapType, constraintKinds: [], detail, text: `${snapType} - ${detail}` });
  });
}

test("malformed, stale and unsupported intents are ignored without mutating inputs", () => {
  const args = { target: endpoint, point, tool: "line", pointCount: 1, automaticConstraints: true,
    intents: [null, {}, intent("Fix"), intent("Tangent"), intent("Coincident", ""),
      { ...intent("Coincident"), referenceKeys: ["a", "b"] }, intent("Coincident", "peer:start", { x: 4, y: 3 }),
      intent("Coincident"), intent("Coincident")] };
  const before = structuredClone(args);
  assert.deepEqual(getSnapCue(args).constraintKinds, ["Coincident"]);
  assert.deepEqual(args, before);
  assert.deepEqual(cue({ intents: null }).constraintKinds, []);
});

function canvasState(overrides = {}) {
  return { activeTool: "line", pointerScreenPoint: { x: 100, y: 100 },
    document: { entities: [peer] }, hoveredTarget: endpoint, automaticSnapConstraints: true,
    toolDraft: { points: [{ x: 0, y: 0 }], previewPoint: point }, ...overrides };
}

test("integrated cue text matches the rendered type and detail in every mode", () => {
  for (const state of [canvasState(), canvasState({ automaticSnapConstraints: false }),
    canvasState({ activeTool: "centercircle" }),
    canvasState({ hoveredTarget: { dynamic: true, point, label: "project-start-end",
      creationRelations: [intent("Vertical"), intent("Horizontal")] } })]) {
    const result = getCurrentSnapCue(state);
    assert.ok(result);
    const detail = result.constraintKinds.length ? `Pending: ${result.constraintKinds.join(" + ")}`
      : state.automaticSnapConstraints === false ? "Snap only (auto constraints off)" : "Snap only";
    assert.equal(result.detail, detail);
    assert.equal(result.text, `${result.snapType} - ${detail}`);
    assert.deepEqual(result.point, point);
  }
});

test("integrated drag only advertises Coincident or Midpoint from its accepted snap", () => {
  for (const [target, expected] of [
    [endpoint, ["Coincident"]],
    [{ ...endpoint, key: "peer|point|mid|2|3", label: "mid" }, ["Midpoint"]],
    [{ ...endpoint, key: "peer", kind: "entity", label: "", snapPoint: point }, []],
    [{ dynamic: true, point, label: "project-v-start", creationRelations: [intent("Vertical")] }, []],
    [{ dynamic: true, point, label: "circle-tangent-peer-line", tangentReferenceKey: "peer" }, []]
  ]) {
    const geometryDrag = { acceptedSnapTarget: target, acceptedSnapPoint: point };
    const state = canvasState({ activeTool: "select", hoveredTarget: null, geometryDrag });
    assert.deepEqual(getCurrentSnapCue(state).constraintKinds, expected);
    assert.deepEqual(getCurrentSnapCue({ ...state, automaticSnapConstraints: false }).constraintKinds, []);
  }
});

test("integration suppresses stale, unresolved and obstructed interaction cues", () => {
  assert.equal(getCurrentSnapCue(canvasState({ geometryDrag: {} })), null);
  assert.equal(getCurrentSnapCue(canvasState({ geometryDrag: {
    acceptedSnapTarget: endpoint, acceptedSnapPoint: { x: 20, y: 30 }
  } })), null);
  for (const overrides of [{ panning: true }, { selectionBox: {} }, { cornerPreview: {} }, { pointerScreenPoint: null }]) {
    assert.equal(getCurrentSnapCue(canvasState(overrides)), null);
  }
});

test("renderer layout clamps cue boxes inside the viewport", () => {
  for (const anchor of [{ x: 0, y: 0 }, { x: 320, y: 240 }, { x: 160, y: 120 }]) {
    const box = positionSnapCue(anchor, { width: 320, height: 240 }, 140, 44);
    assert.ok(box.x >= 8 && box.y >= 8);
    assert.ok(box.x + box.width <= 312 && box.y + box.height <= 232);
  }
});

function inferredCue(entity, start, end, overrides = {}) {
  return getSnapCue({ tool: "line", pointCount: 1, draftPoints: [start], point: end, intents: [],
    automaticConstraints: true, target: { dynamic: true, kind: "point", point: end, label: "tangent-peer", entity },
    ...overrides });
}

test("exact finite line-circle tangency supports reverse, rotated and midpoint-line geometry", () => {
  const circle = { kind: "circle", center: { x: 0, y: 0 }, radius: 3 };
  for (const [start, end] of [
    [{ x: -4, y: 3 }, { x: 4, y: 3 }], [{ x: 4, y: 3 }, { x: -4, y: 3 }],
    [{ x: 3, y: -4 }, { x: 3, y: 4 }], [{ x: 0, y: 3 }, { x: 4, y: 3 }]
  ]) {
    assert.deepEqual(inferredCue(circle, start, end).constraintKinds, ["Tangent"]);
    assert.equal(inferredCue(circle, start, end).detail, "Pending: Tangent");
  }
  const rotate = p => ({ x: (p.x - p.y) / Math.sqrt(2), y: (p.x + p.y) / Math.sqrt(2) });
  assert.deepEqual(inferredCue(circle, rotate({ x: -4, y: 3 }), rotate({ x: 4, y: 3 })).constraintKinds, ["Tangent"]);
  assert.deepEqual(inferredCue(circle, { x: 0, y: 3 }, { x: 4, y: 3 }, { tool: "midpointline" }).constraintKinds, ["Tangent"]);
});

test("tangent inference rejects infinite-line-only contact, near lines and degenerate geometry", () => {
  const circle = { kind: "circle", center: { x: 0, y: 0 }, radius: 3 };
  for (const [start, end] of [
    [{ x: 1, y: 3 }, { x: 4, y: 3 }],
    [{ x: -4, y: 3.00001 }, { x: 4, y: 3.00001 }],
    [{ x: -4, y: 0 }, { x: 4, y: 0 }],
    [{ x: 0, y: 3 }, { x: 0, y: 3 }],
    [{ x: 0, y: 3 }, { x: 0.0000005, y: 3 }]
  ]) assert.deepEqual(inferredCue(circle, start, end).constraintKinds, []);
  for (const radius of [0, -3, NaN, Infinity]) {
    assert.deepEqual(inferredCue({ ...circle, radius }, { x: -4, y: 3 }, { x: 4, y: 3 }).constraintKinds, []);
  }
  assert.deepEqual(inferredCue(circle, { x: -4, y: 3.0000005 }, { x: 4, y: 3.0000005 }).constraintKinds, ["Tangent"]);
  assert.deepEqual(inferredCue(circle, { x: 0.0000005, y: 3 }, { x: 4, y: 3 }).constraintKinds, ["Tangent"]);
  assert.deepEqual(inferredCue(circle, { x: 0.000002, y: 3 }, { x: 4, y: 3 }).constraintKinds, []);
});

test("arc tangent contact must lie on its actual sweep", () => {
  for (const [startAngleDegrees, endAngleDegrees, expected] of [
    [0, 180, true], [90, 180, true], [350, 100, true],
    [180, 360, false], [0, 0, false], [0, 360, true], [NaN, 180, false]
  ]) {
    const arc = { kind: "arc", center: { x: 0, y: 0 }, radius: 3, startAngleDegrees, endAngleDegrees };
    assert.deepEqual(inferredCue(arc, { x: -4, y: 3 }, { x: 4, y: 3 }).constraintKinds, expected ? ["Tangent"] : []);
  }
});

test("perpendicular inference requires actual endpoint or midpoint contact", () => {
  const line = { kind: "line", points: [{ x: 0, y: 0 }, { x: 10, y: 0 }] };
  for (const x of [0, 5, 10]) {
    for (const reverse of [false, true]) {
      const start = { x, y: 0 }, end = { x, y: 4 };
      assert.deepEqual(inferredCue(line, reverse ? end : start, reverse ? start : end).constraintKinds, ["Perpendicular"]);
    }
  }
  assert.deepEqual(inferredCue(line, { x: 2, y: 0 }, { x: 2, y: 4 }).constraintKinds, []);
  assert.deepEqual(inferredCue(line, { x: 5, y: 0 }, { x: 6, y: 4 }).constraintKinds, []);
  assert.deepEqual(inferredCue(line, { x: 5, y: 0 }, { x: 5, y: 4 }, { tool: "midpointline" }).constraintKinds, []);
  const polyline = { kind: "polyline", points: [{ x: 20, y: 0 }, ...line.points] };
  assert.deepEqual(inferredCue(polyline, { x: 5, y: 0 }, { x: 5, y: 4 }).constraintKinds, ["Perpendicular"]);
  assert.deepEqual(inferredCue({ ...line, kind: "polygon" }, { x: 5, y: 0 }, { x: 5, y: 4 }).constraintKinds, []);
});

test("unknown dynamic sources defer evaluation without parsing hyphenated IDs or accepting caller assertions", () => {
  const target = { dynamic: true, point, label: "tangent-part-with-many-hyphens-0" };
  const result = cue({ target, intents: [], draftPoints: [{ x: 0, y: 0 }], inferredKinds: ["Tangent"] });
  assert.deepEqual(result.constraintKinds, []);
  assert.equal(result.detail, "Constraint evaluated on commit");
  assert.equal(cue({ target, intents: [], automaticConstraints: false }).detail, "Snap only (auto constraints off)");
  assert.equal(cue({ target, intents: [], tool: "threepointcircle", pointCount: 2 }).detail, "Snap only");
  assert.deepEqual(getInferredLineConstraintKinds({ tool: "point", pointCount: 0 }), []);
});

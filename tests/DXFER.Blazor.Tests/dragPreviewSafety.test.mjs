import assert from "node:assert/strict";
import test from "node:test";
import { applyGeometryDragPreview, finishGeometryDrag, updateGeometryDrag } from "../../src/DXFER.Blazor/wwwroot/drawingCanvas.js";

const point = (x, y) => ({ x, y });
const circle = (id, x, y, radius = 3) => ({ id, kind: "circle", center: point(x, y), radius });
const line = (id, start, end) => ({ id, kind: "line", points: [start, end] });
const constraint = (kind, ...referenceKeys) => ({ id: `${kind}-${referenceKeys.join("-")}`, kind, referenceKeys, state: "satisfied" });
const documentOf = (entities, constraints = [], dimensions = []) => ({ entities, constraints, dimensions });
const radiusPick = "moving|point|quadrant-0|3|0";

function assertRejected(document, key, start, end) {
  const before = structuredClone(document);
  assert.equal(applyGeometryDragPreview(document, key, start, end), null);
  assert.deepEqual(document, before, "failed previews must not mutate source geometry, dimensions or intent");
}

test("Equal plus fixed peer rejects circle radius preview", () => {
  const document = documentOf([circle("moving", 0, 0), circle("fixed", 10, 0)],
    [constraint("equal", "moving", "fixed"), constraint("fix", "fixed")]);
  assertRejected(document, radiusPick, point(3, 0), point(5, 0));
});

test("Equal with a free peer still propagates circle radius", () => {
  const document = documentOf([circle("moving", 0, 0), circle("peer", 10, 0)],
    [constraint("equal", "moving", "peer")]);
  const preview = applyGeometryDragPreview(document, radiusPick, point(3, 0), point(5, 0));
  assert.equal(preview.entities[0].radius, 5);
  assert.equal(preview.entities[1].radius, 5);
  assert.equal(document.entities[0].radius, 3);
  assert.equal(document.entities[1].radius, 3);
});

test("Equal propagation cannot break a peer driving radius dimension", () => {
  const document = documentOf([circle("moving", 0, 0), circle("peer", 10, 0)],
    [constraint("equal", "moving", "peer")],
    [{ id: "peer-radius", kind: "radius", referenceKeys: ["peer"], value: 3, isDriving: true, anchor: point(13, 1) }]);
  assertRejected(document, radiusPick, point(3, 0), point(5, 0));
});

test("Equal line length cannot move a fixed peer", () => {
  const document = documentOf([line("moving", point(0, 0), point(3, 0)), line("fixed", point(0, 5), point(3, 5))],
    [constraint("equal", "moving", "fixed"), constraint("fix", "fixed")]);
  assertRejected(document, "moving|point|end|3|0", point(3, 0), point(5, 0));
});

test("Concentric plus fixed center rejects partial circle movement", () => {
  const document = documentOf([circle("moving", 0, 0), circle("fixed", 0, 0, 2)],
    [constraint("concentric", "moving", "fixed"), constraint("fix", "fixed:center")]);
  assertRejected(document, "moving|point|center|0|0", point(0, 0), point(2, 1));
});

test("Midpoint plus fixed point rejects partial line movement", () => {
  const document = documentOf([line("moving", point(0, 0), point(4, 0)), { id: "fixed", kind: "point", points: [point(2, 0)] }],
    [constraint("midpoint", "fixed", "moving"), constraint("fix", "fixed")]);
  assertRejected(document, "moving|point|end|4|0", point(4, 0), point(6, 0));
});

test("Coincident plus fixed endpoint rejects partial welded movement", () => {
  const document = documentOf([line("moving", point(0, 0), point(3, 0)), line("fixed", point(3, 0), point(3, 5))],
    [constraint("coincident", "moving:end", "fixed:start"), constraint("fix", "fixed:start")]);
  assertRejected(document, "moving|point|end|3|0", point(3, 0), point(5, 0));
});

test("Horizontal and Vertical cannot accept a collapsed line", () => {
  const document = documentOf([line("moving", point(0, 0), point(3, 0))],
    [constraint("horizontal", "moving"), constraint("vertical", "moving")]);
  assertRejected(document, "moving|point|end|3|0", point(3, 0), point(5, 2));
});

test("Parallel plus perpendicular cannot accept the last propagated relation only", () => {
  const document = documentOf([line("moving", point(0, 0), point(3, 0)), line("peer", point(0, 5), point(3, 5))],
    [constraint("parallel", "moving", "peer"), constraint("perpendicular", "moving", "peer")]);
  assertRejected(document, "moving|point|end|3|0", point(3, 0), point(4, 2));
});

test("Tangent propagation cannot break a peer Equal relation", () => {
  const document = documentOf([circle("moving", 0, 0), circle("peer", 6, 0), circle("fixed", 20, 0)],
    [constraint("tangent", "moving", "peer"), constraint("equal", "moving", "fixed"), constraint("fix", "fixed")]);
  assertRejected(document, radiusPick, point(3, 0), point(5, 0));
});

for (const kind of ["symmetric", "future-kind"]) {
  test(`unsupported active ${kind} constraint declines optimistic preview`, () => {
    const document = documentOf([circle("moving", 0, 0), circle("peer", 10, 0)],
      [constraint(kind, "moving", "peer")]);
    assertRejected(document, radiusPick, point(3, 0), point(5, 0));
  });
}

test("unsupported or dangling active references fail closed", () => {
  const document = documentOf([circle("moving", 0, 0)], [constraint("equal", "moving", "missing")]);
  assertRejected(document, radiusPick, point(3, 0), point(5, 0));
});

test("suppressed unsupported constraints do not block free previews", () => {
  const document = documentOf([circle("moving", 0, 0)],
    [{ ...constraint("symmetric", "moving", "missing"), state: "suppressed" }]);
  const preview = applyGeometryDragPreview(document, radiusPick, point(3, 0), point(5, 0));
  assert.equal(preview.entities[0].radius, 5);
});

test("unmeasurable active driving dimensions decline optimistic preview", () => {
  const document = documentOf([circle("moving", 0, 0)], [],
    [{ id: "unsupported", kind: "future-dimension", referenceKeys: ["moving"], value: 3, isDriving: true }]);
  assertRejected(document, radiusPick, point(3, 0), point(5, 0));
});

test("failed release discards a prior valid preview but still sends the authoritative core request", () => {
  const originalDocument = documentOf([line("moving", point(0, 0), point(3, 0)), line("fixed", point(10, 10), point(13, 10))],
    [constraint("equal", "moving", "fixed"), constraint("fix", "fixed")]);
  const targetKey = "moving|point|end|3|0";
  const validPreview = applyGeometryDragPreview(originalDocument, targetKey, point(3, 0), point(0, 3));
  assert.ok(validPreview);
  const calls = [];
  const state = {
    activeTool: "select", acquiredSnapPoints: [], document: validPreview,
    hoveredTarget: null, selectedKeys: new Set(), activeSelectionKey: null,
    geometryDrag: {
      pointerId: 1, targetKey, startScreenPoint: point(30, 0),
      startWorldPoint: point(3, 0), originalDocument, moved: true
    },
    view: { scale: 10, offsetX: 0, offsetY: 0 },
    canvas: { dataset: {}, getBoundingClientRect: () => ({ left: 0, top: 0, width: 800, height: 600 }) },
    dotnetRef: { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(); } }
  };
  assert.equal(updateGeometryDrag(state, point(50, 0)), false);
  assert.equal(state.document, originalDocument, "a failed pointer frame must discard the preceding optimistic preview");
  assert.equal(updateGeometryDrag(state, point(0, -30)), true);
  assert.deepEqual(state.document.entities[0].points, [point(0, 0), point(0, 3)]);
  assert.equal(finishGeometryDrag(state, point(50, 0)), true);
  assert.equal(state.document, originalDocument);
  assert.equal(state.geometryDrag, null);
  assert.equal(calls.filter(call => call[0] === "OnGeometryDragRequested").length, 1);
});

function pendingDrag() {
  const originalDocument = documentOf([circle("moving", 0, 0)]);
  let resolve;
  let reject;
  const response = new Promise((yes, no) => { resolve = yes; reject = no; });
  const drawingCalls = [];
  const context = new Proxy({}, {
    get: (target, name) => name in target ? target[name] : (...args) => drawingCalls.push([name, ...args]),
    set: (target, name, value) => { target[name] = value; return true; }
  });
  const state = {
    activeTool: "select", acquiredSnapPoints: [], document: originalDocument,
    hoveredTarget: null, selectedKeys: new Set([radiusPick]), activeSelectionKey: radiusPick,
    geometryDrag: {
      pointerId: 1, targetKey: radiusPick, startScreenPoint: point(30, 0),
      startWorldPoint: point(3, 0), originalDocument, moved: true
    },
    view: { scale: 10, offsetX: 0, offsetY: 0 }, pixelRatio: 2,
    context,
    canvas: {
      dataset: {}, width: 1600, height: 1200,
      getBoundingClientRect: () => ({ left: 0, top: 0, width: 800, height: 600 })
    },
    dotnetRef: { invokeMethodAsync: method => method === "OnGeometryDragRequested" ? response : Promise.resolve() }
  };
  finishGeometryDrag(state, point(50, 0));
  assert.equal(state.document.entities[0].radius, 5);
  return { state, originalDocument, resolve, reject, drawingCalls };
}

for (const outcome of [false, "reject"]) {
  test(`server ${outcome} rolls back optimistic drag and redraws unchanged geometry`, async t => {
    if (outcome === "reject") t.mock.method(console, "warn", () => {});
    const { state, originalDocument, resolve, reject, drawingCalls } = pendingDrag();
    if (outcome === false) resolve(false);
    else reject(new Error("server unavailable"));
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(state.document, originalDocument);
    assert.equal(state.selectedKeys.size, 0, "stale point handles are pruned");
    assert.equal(state.canvas.dataset.selectedCount, "0");
    assert.ok(drawingCalls.some(call => call[0] === "clearRect"), "rollback repaints the canvas");
    assert.deepEqual([state.canvas.width, state.canvas.height], [1600, 1200]);
    assert.deepEqual(state.view, { scale: 10, offsetX: 0, offsetY: 0 });
    assert.ok(drawingCalls.some(call => JSON.stringify(call) === JSON.stringify(["setTransform", 2, 0, 0, 2, 0, 0])));
    assert.ok(drawingCalls.some(call => JSON.stringify(call) === JSON.stringify(["fillRect", 0, 0, 800, 600])));
  });

  test(`late server ${outcome} cannot overwrite a newer document`, async t => {
    if (outcome === "reject") t.mock.method(console, "warn", () => {});
    const { state, resolve, reject, drawingCalls } = pendingDrag();
    const newerDocument = documentOf([circle("moving", 0, 0, 7)]);
    state.document = newerDocument;
    if (outcome === false) resolve(false);
    else reject(new Error("server unavailable"));
    await new Promise(resolve => setImmediate(resolve));
    assert.equal(state.document, newerDocument);
    assert.equal(drawingCalls.length, 0);
  });
}

test("explicit server acceptance retains optimistic geometry", async () => {
  const { state, resolve, drawingCalls } = pendingDrag();
  const optimisticDocument = state.document;
  resolve(true);
  await new Promise(resolve => setImmediate(resolve));
  assert.equal(state.document, optimisticDocument);
  assert.equal(state.document.entities[0].radius, 5);
  assert.equal(drawingCalls.length, 0);
});

test("fixed line start still allows a valid free endpoint and Equal peer", () => {
  const document = documentOf([line("moving", point(0, 0), point(3, 0)), line("peer", point(0, 5), point(3, 5))],
    [constraint("equal", "moving", "peer"), constraint("fix", "moving:start")]);
  const preview = applyGeometryDragPreview(document, "moving|point|end|3|0", point(3, 0), point(5, 0));
  assert.deepEqual(preview.entities[0].points, [point(0, 0), point(5, 0)]);
  assert.deepEqual(preview.entities[1].points, [point(0, 5), point(5, 5)]);
});

test("polyline segment Equal plus fixed peer rejects a partial vertex drag", () => {
  const document = documentOf([
    { id: "moving", kind: "polyline", points: [point(0, 0), point(3, 0), point(3, 3)] },
    line("fixed", point(10, 10), point(13, 10))
  ], [constraint("equal", "moving|segment|0", "fixed"), constraint("fix", "fixed")]);
  assertRejected(document, "moving|point|vertex-1|3|0", point(3, 0), point(5, 0));
});

for (const kind of ["arc", "polygon"]) {
  test(`Equal circle radius propagates to a free ${kind} peer`, () => {
    const peer = { ...circle("peer", 10, 0), kind, startAngleDegrees: 0, endAngleDegrees: 90, sideCount: 6, rotationAngleDegrees: 0 };
    const document = documentOf([circle("moving", 0, 0), peer], [constraint("equal", "moving", "peer")]);
    const preview = applyGeometryDragPreview(document, radiusPick, point(3, 0), point(5, 0));
    assert.equal(preview.entities[0].radius, 5);
    assert.equal(preview.entities[1].radius, 5);
    document.constraints.push(constraint("fix", "peer"));
    assertRejected(document, radiusPick, point(3, 0), point(5, 0));
  });
}

for (const kind of ["ellipse", "spline"]) {
  test(`unsupported ${kind} relation has no misleading optimistic preview`, () => {
    const peer = kind === "ellipse"
      ? { id: "peer", kind, center: point(0, 0), majorAxisEndPoint: point(3, 0), minorRadiusRatio: 0.5 }
      : { id: "peer", kind, points: [point(0, 0), point(3, 2)], controlPoints: [point(0, 0), point(3, 2)] };
    const document = documentOf([circle("moving", 0, 0), peer], [constraint("equal", "moving", "peer")]);
    assertRejected(document, radiusPick, point(3, 0), point(5, 0));
  });
}

test("unknown dimension kind cannot masquerade as a satisfied measured line length", () => {
  const document = documentOf([line("moving", point(0, 0), point(3, 0))], [],
    [{ id: "unknown", kind: "future-dimension", referenceKeys: ["moving:start", "moving:end"], value: 5, isDriving: true }]);
  assertRejected(document, "moving|point|end|3|0", point(3, 0), point(5, 0));
});

for (const [kind, end] of [["horizontaldistance", point(3, 4)], ["verticaldistance", point(4, 3)]]) {
  test(`${kind} checks its axis rather than the diagonal after propagation`, () => {
    const document = documentOf([line("moving", point(0, 0), point(3, 0)), line("peer", point(10, 0), point(13, 0))],
      [constraint("equal", "moving", "peer")],
      [{ id: "axis", kind, referenceKeys: ["moving:start", "moving:end"], value: 3, isDriving: true }]);
    const preview = applyGeometryDragPreview(document, "moving|point|end|3|0", point(3, 0), end);
    assert.ok(preview);
    assert.deepEqual(preview.entities[0].points, [point(0, 0), end]);
    assert.deepEqual(preview.entities[1].points, [point(10, 0), point(15, 0)]);
  });
}

test("suppressed and reference-only dimensions do not block valid Equal propagation", () => {
  const document = documentOf([circle("moving", 0, 0), circle("peer", 10, 0)],
    [constraint("equal", "moving", "peer")], [
      { id: "suppressed", kind: "radius", referenceKeys: ["peer"], value: 3, state: "suppressed", isDriving: true },
      { id: "reference", kind: "radius", referenceKeys: ["peer"], value: 3, isDriving: false }
    ]);
  const preview = applyGeometryDragPreview(document, radiusPick, point(3, 0), point(5, 0));
  assert.equal(preview.entities[1].radius, 5);
});

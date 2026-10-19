import assert from "node:assert/strict";
import test from "node:test";
import {
  canExtendDimensionSelection,
  createDrawingCanvas,
  getDimensionDescriptorFromReferences,
  getDimensionPlacementRequest,
  getPersistentDimensionDescriptors
} from "../../src/DXFER.Blazor/wwwroot/drawingCanvas.js";

function state() {
  return {
    activeTool: "dimension",
    view: { scale: 10, offsetX: 0, offsetY: 0 },
    document: {
      entities: [
        { id: "source", kind: "circle", center: { x: 3, y: 4 }, radius: 5 },
        { id: "dependent", kind: "circle", center: { x: 3, y: 4 }, radius: 8 }
      ],
      constraints: [{ id: "link", kind: "Offset", state: "Satisfied", referenceKeys: ["source", "dependent"] }],
      dimensions: [],
      bounds: { minX: -5, minY: -4, maxX: 11, maxY: 12 }
    }
  };
}

const quadrant = (id, angle, x, y) => `${id}|point|quadrant-${angle}|${x}|${y}`;
const sourcePick = quadrant("source", 0, 8, 4);
const dependentPick = quadrant("dependent", 90, 3, 12);
const descriptor = (s, keys, anchor = { x: 3, y: 16 }) =>
  getDimensionDescriptorFromReferences(s, keys, anchor, false, "preview");
const near = (actual, expected) => assert.ok(Math.abs(actual - expected) < 1e-8, `${actual} != ${expected}`);

for (const keys of [["source", "dependent"], ["dependent", "source"],
  [sourcePick, dependentPick], ["dependent", sourcePick]]) {
  test(`linked circle pair extends and previews a radial gap: ${keys.join(", ")}`, () => {
    const s = state(), before = structuredClone(s.document);
    assert.equal(canExtendDimensionSelection(s, [keys[0]], keys[1]), true);
    const result = descriptor(s, keys);
    assert.equal(result.kind, "offset");
    assert.equal(result.label, "Offset distance");
    assert.equal(result.value, 3);
    assert.equal(result.geometry.type, "linear");
    near(result.geometry.start.x, 3); near(result.geometry.start.y, 9);
    near(result.geometry.end.x, 3); near(result.geometry.end.y, 12);
    s.dimensionDraft = { selectionKeys: keys, complete: true, radialDiameter: true };
    const request = getDimensionPlacementRequest(s, { x: 30, y: -160 });
    assert.deepEqual(request.selectionKeys, ["source", "dependent"]);
    assert.deepEqual(request.anchor, { x: 3, y: 16 });
    assert.equal(request.radialDiameter, false);
    assert.deepEqual(s.document, before);
  });
}

test("inward offsets retain source/dependent order and positive gap", () => {
  const s = state();
  s.document.entities[1].radius = 2;
  const result = descriptor(s, ["dependent", "source"], { x: -10, y: 4 });
  assert.equal(result.value, 3);
  near(result.geometry.start.x, -2); near(result.geometry.end.x, 1);
});

test("center anchor falls back to a finite +X radial witness", () => {
  const result = descriptor(state(), ["source", "dependent"], { x: 3, y: 4 });
  assert.deepEqual(result.geometry.start, { x: 8, y: 4 });
  assert.deepEqual(result.geometry.end, { x: 11, y: 4 });
});

test("circular preview and persistent descriptors use the same anchor angle instead of native +X witnesses", () => {
  const s = state();
  const dimension = { id: "gap", kind: "Offset", referenceKeys: ["source", "dependent"], value: 3,
    anchor: { x: 3, y: 16 }, offsetStart: { x: 8, y: 4 }, offsetEnd: { x: 11, y: 4 } };
  s.document.dimensions.push(dimension);
  const result = descriptor(s, [sourcePick, dependentPick], dimension.anchor);
  assert.deepEqual(result.geometry, getPersistentDimensionDescriptors(s)[0].geometry);
  assert.deepEqual(result.geometry.start, { x: 3, y: 9 });
  assert.deepEqual(result.geometry.end, { x: 3, y: 12 });
  s.dimensionAnchorOverrides = new Map([["gap", { x: -10, y: 4 }]]);
  assert.deepEqual(getPersistentDimensionDescriptors(s)[0].geometry,
    descriptor(s, [sourcePick, dependentPick], { x: -10, y: 4 }).geometry);
});

test("suppressed duplicate does not hide the unique active circle relation", () => {
  const s = state();
  s.document.constraints.push({ ...s.document.constraints[0], id: "old", state: "Suppressed" });
  assert.equal(descriptor(s, ["source", "dependent"]).kind, "offset");
});

for (const [name, change] of [
  ["unlinked", s => { s.document.constraints = []; }],
  ["suppressed", s => { s.document.constraints[0].state = "Suppressed"; }],
  ["duplicate", s => { s.document.constraints.push({ ...s.document.constraints[0], id: "duplicate" }); }],
  ["reverse duplicate", s => { s.document.constraints.push({ kind: "Offset", referenceKeys: ["dependent", "source"] }); }],
  ["multi-source", s => { s.document.constraints[0].referenceKeys = ["source", "other", "dependent"]; }],
  ["source siblings", s => { s.document.constraints[0].referenceKeys = ["source", "dependent", "other"]; }],
  ["nonconcentric", s => { s.document.entities[1].center.x += 0.01; }],
  ["zero gap", s => { s.document.entities[1].radius = 5; }],
  ["nonfinite radius", s => { s.document.entities[1].radius = Infinity; }],
  ["nonfinite center", s => { s.document.entities[1].center.x = NaN; }],
  ["invalid radius", s => { s.document.entities[1].radius = 0; }],
  ["arc", s => { s.document.entities[1].kind = "arc"; }],
  ["duplicate dimensions", s => { s.document.dimensions = [1, 2].map(id => ({ id, kind: "Offset", referenceKeys: ["source", "dependent"] })); }]
]) test(`${name} does not invent a circular offset dimension`, () => {
  const s = state(); change(s);
  assert.equal(canExtendDimensionSelection(s, ["source"], "dependent"), false);
  assert.equal(descriptor(s, ["source", "dependent"]), null);
});

test("same circle and center picks never become offset dimensions", () => {
  const s = state();
  assert.equal(descriptor(s, ["source", "source"]), null);
  assert.equal(descriptor(s, ["source:center", "dependent"]), null);
  const centers = descriptor(s, ["source:center", "dependent:center"]);
  assert.equal(centers.kind, "lineardistance"); assert.equal(centers.value, 0);
  const rawCenters = descriptor(s, ["source|point|center|3|4", "dependent|point|center|3|4"]);
  assert.equal(rawCenters.kind, "lineardistance"); assert.equal(rawCenters.value, 0);
  assert.equal(descriptor(s, ["source"]).value, 10);
  assert.equal(descriptor(s, [sourcePick]).kind, "diameter");
  assert.equal(descriptor(s, ["source:center"]), null);
});

for (const index of [0, 1]) {
  for (const radius of [0, -2, NaN, Infinity, -Infinity]) {
    test(`invalid ${index === 0 ? "source" : "dependent"} radius ${radius} cannot produce an offset model`, () => {
      const s = state(); s.document.entities[index].radius = radius;
      assert.equal(descriptor(s, ["source", "dependent"]), null);
      assert.equal(descriptor(s, [sourcePick, dependentPick]), null);
    });
  }
  for (const coordinate of ["x", "y"]) {
    test(`nonfinite circle ${index} center ${coordinate} cannot produce an offset model`, () => {
      const s = state(); s.document.entities[index].center[coordinate] = Infinity;
      assert.equal(descriptor(s, ["source", "dependent"]), null);
      assert.equal(descriptor(s, [sourcePick, dependentPick]), null);
    });
  }
}

test("unlinked quadrant picks cannot fall through to a point-to-point dimension", () => {
  const s = state(); s.document.constraints = [];
  assert.equal(descriptor(s, [sourcePick, dependentPick]), null);
  assert.equal(canExtendDimensionSelection(s, [sourcePick], dependentPick), false);
});

function harness(t, s) {
  const previous = globalThis.window, handlers = new Map(), calls = [];
  globalThis.window = { devicePixelRatio: 1, addEventListener() {}, removeEventListener() {} };
  const context = new Proxy({ createRadialGradient: () => ({ addColorStop() {} }),
    measureText: text => ({ width: String(text).length * 7 }) }, { get: (o, k) => k in o ? o[k] : () => {} });
  const canvas = { clientWidth: 1000, clientHeight: 800, dataset: {}, style: {}, getContext: () => context,
    getBoundingClientRect: () => ({ left: 0, top: 0, width: 1000, height: 800 }),
    addEventListener: (key, handler) => handlers.set(key, handler), removeEventListener() {}, focus() {},
    setPointerCapture() {}, releasePointerCapture() {} };
  const instance = createDrawingCanvas(canvas, { invokeMethodAsync: (...args) => { calls.push(args); return Promise.resolve(true); } });
  t.after(() => { instance.dispose(); if (previous === undefined) delete globalThis.window; else globalThis.window = previous; });
  instance.setDocument(s.document, true);
  instance.setActiveTool("dimension");
  return {
    requests: () => calls.filter(c => c[0] === "OnSketchDimensionPlacementRequested"),
    click(x, y) {
      const event = { clientX: x * +canvas.dataset.scale + +canvas.dataset.offsetX,
        clientY: -y * +canvas.dataset.scale + +canvas.dataset.offsetY, pointerId: 1, pointerType: "mouse", button: 0, preventDefault() {} };
      handlers.get("pointerdown")(event); handlers.get("pointerup")(event);
    }
  };
}

for (const quadrants of [true, false]) test(`actual Dimension click flow extends ${quadrants ? "quadrants" : "bodies"} then places linked offset`, t => {
  const h = harness(t, state());
  const angle = quadrants ? 0 : Math.PI / 4;
  h.click(3 + 5 * Math.cos(angle), 4 + 5 * Math.sin(angle));
  assert.equal(h.requests().length, 0);
  h.click(3 + 8 * Math.cos(angle), 4 + 8 * Math.sin(angle));
  assert.equal(h.requests().length, 0, "second circle must not place the first diameter");
  h.click(15, 16);
  assert.equal(h.requests().length, 1);
  assert.deepEqual(h.requests()[0].slice(1), [["source", "dependent"], 15, 16, false]);
});

test("actual single-circle click then empty placement still requests diameter", t => {
  const h = harness(t, state());
  h.click(8, 4); h.click(15, 16);
  assert.equal(h.requests().length, 1);
  assert.equal(h.requests()[0][1].length, 1);
  assert.equal(h.requests()[0].at(-1), true);
});

for (const quadrants of [false, true]) test(`unsupported second circle ${quadrants ? "quadrant" : "body"} submits both picks for explicit core rejection`, t => {
  const s = state(); s.document.constraints = [];
  const h = harness(t, s), angle = quadrants ? 0 : Math.PI / 4;
  h.click(3 + 5 * Math.cos(angle), 4 + 5 * Math.sin(angle));
  h.click(3 + 8 * Math.cos(angle), 4 + 8 * Math.sin(angle));
  assert.equal(h.requests().length, 1);
  assert.deepEqual(h.requests()[0][1], quadrants
    ? [sourcePick, quadrant("dependent", 0, 11, 4)] : ["source", "dependent"]);
  assert.equal(h.requests()[0].at(-1), false);
  h.click(15, 16);
  assert.equal(h.requests().length, 1, "failed pair submission resets the draft");
});

for (const center of [false, true]) test(`${center ? "center" : "same circle body"} second pick is excluded from unsupported-pair submission`, t => {
  const h = harness(t, state()), angle = Math.PI / 4;
  h.click(3 + 5 * Math.cos(angle), 4 + 5 * Math.sin(angle));
  h.click(center ? 3 : 3 - 5 * Math.cos(angle), center ? 4 : 4 + 5 * Math.sin(angle));
  assert.equal(h.requests().length, 1);
  assert.deepEqual(h.requests()[0][1], ["source"]);
  assert.equal(h.requests()[0].at(-1), true);
});

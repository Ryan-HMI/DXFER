import assert from "node:assert/strict";
import test from "node:test";
import {
  findNearestTarget,
  getDimensionAnchorUpdateRequest,
  getDimensionDisplayText,
  getDimensionLinearScreenGeometry,
  getDimensionRenderStyle,
  getLinearDimensionScreenGeometry,
  getPersistentDimensionDescriptors
} from "../../src/DXFER.Blazor/wwwroot/drawingCanvas.js";

function createState(kind, anchor, reversed = false) {
  return {
    activeTool: "select",
    acquiredSnapPoints: [],
    dimensionAnchorOverrides: new Map(),
    view: { scale: 100, offsetX: 100, offsetY: 500 },
    document: {
      entities: [
        { id: "a", kind: "point", points: [{ x: 0, y: 0 }] },
        { id: "b", kind: "point", points: [{ x: 3, y: 4 }] }
      ],
      dimensions: [{
        id: "distance",
        kind,
        referenceKeys: reversed ? ["b", "a"] : ["a", "b"],
        value: kind === "HorizontalDistance" ? 3 : kind === "VerticalDistance" ? 4 : 5,
        anchor,
        isDriving: true,
        state: "Satisfied"
      }]
    }
  };
}

function toScreen(state, point) {
  return {
    x: point.x * state.view.scale + state.view.offsetX,
    y: -point.y * state.view.scale + state.view.offsetY
  };
}

function assertAxisProjection(state, axis, textWidth = 20) {
  const before = structuredClone(state.document);
  const [dimension] = getPersistentDimensionDescriptors(state);
  const geometry = getDimensionLinearScreenGeometry(state, dimension.geometry, textWidth);
  const parallel = axis === "horizontal" ? "x" : "y";
  const perpendicular = axis === "horizontal" ? "y" : "x";
  const anchor = toScreen(state, dimension.anchorPoint);
  const references = [dimension.geometry.start, dimension.geometry.end].map(point => toScreen(state, point));
  assert.deepEqual(dimension.point, anchor);
  assert.equal(dimension.value, before.dimensions[0].value);
  assert.equal(getDimensionDisplayText(dimension), dimension.isDriving === false ? `(${dimension.value})` : String(dimension.value));

  for (let index = 0; index < 2; index++) {
    const reference = references[index];
    const extension = geometry.extensionSegments[index];
    const arrow = geometry.arrows[index];
    assert.deepEqual(extension.start, reference);
    assert.equal(extension.end[parallel], reference[parallel]);
    assert.equal(extension.end[perpendicular], anchor[perpendicular]
      + Math.sign(anchor[perpendicular] - reference[perpendicular]) * 6);
    assert.equal(arrow.point[parallel], reference[parallel]);
    assert.equal(arrow.point[perpendicular], anchor[perpendicular]);
    assert.equal(arrow.toward[perpendicular], anchor[perpendicular]);
  }

  assert.equal(Math.abs(geometry.arrows[1].point[parallel] - geometry.arrows[0].point[parallel]),
    dimension.value * state.view.scale);
  for (const segment of geometry.dimensionSegments) {
    assert.equal(segment.start[perpendicular], anchor[perpendicular]);
    assert.equal(segment.end[perpendicular], anchor[perpendicular]);
    const gap = Math.min(dimension.value * state.view.scale / 2, Math.max(14, textWidth / 2 + 5));
    const low = Math.min(segment.start[parallel], segment.end[parallel]);
    const high = Math.max(segment.start[parallel], segment.end[parallel]);
    assert.ok(high <= anchor[parallel] - gap || low >= anchor[parallel] + gap,
      "dimension lines leave a gap at the label anchor, including outside-span leaders");
  }
  assert.deepEqual(state.document, before);
  return { dimension, geometry };
}

for (const [kind, axis, anchors] of [
  ["HorizontalDistance", "horizontal", [{ x: 1.5, y: -1 }, { x: 0.6, y: 2 }, { x: 5, y: 6 }, { x: -2, y: 0 }]],
  ["VerticalDistance", "vertical", [{ x: 4, y: 2 }, { x: 1, y: 0.8 }, { x: -2, y: 6 }, { x: 3, y: -2 }]]
]) {
  for (const reversed of [false, true]) {
    for (const anchor of anchors) {
      test(`${kind} projects real references with anchor ${JSON.stringify(anchor)}, reversed=${reversed}`, () => {
        assertAxisProjection(createState(kind, anchor, reversed), axis);
      });
    }
  }

  test(`${kind} recomputes projection after zoom, pan, viewport resize and anchor override`, () => {
    const state = createState(kind, anchors[0]);
    for (const view of [
      { scale: 2, offsetX: 160, offsetY: 120 },
      { scale: 24, offsetX: 320, offsetY: 240 },
      { scale: 100, offsetX: 600, offsetY: 400 }
    ]) {
      state.view = view;
      assertAxisProjection(state, axis, 35);
      const [dimension] = getPersistentDimensionDescriptors(state);
      const request = getDimensionAnchorUpdateRequest(state, dimension.id, toScreen(state, anchors[1]));
      assert.equal(request.dimensionId, "distance");
      assert.ok(Math.abs(request.anchor.x - anchors[1].x) < 1e-10);
      assert.ok(Math.abs(request.anchor.y - anchors[1].y) < 1e-10);
      state.dimensionAnchorOverrides.set("distance", request.anchor);
      assertAxisProjection(state, axis, 35);
      state.dimensionAnchorOverrides.clear();
    }
  });

  test(`${kind} hit testing follows projected guides away from the label`, () => {
    const state = createState(kind, anchors[0]);
    const point = axis === "horizontal" ? { x: 0.3, y: -1 } : { x: 4, y: 0.3 };
    const target = findNearestTarget(state, toScreen(state, point));
    assert.equal(target?.kind, "dimension");
    assert.equal(target.key, "persistent-distance");
  });

  test(`${kind} keeps read-only/reference values and unsatisfied diagnostics unchanged`, () => {
    const state = createState(kind, anchors[0]);
    state.document.mode = "ReferenceOnly";
    state.document.dimensions[0].isDriving = false;
    state.document.dimensions[0].state = "Unsatisfied";
    state.document.dimensions[0].affectedReferenceKeys = ["a", "b"];
    const { dimension } = assertAxisProjection(state, axis);
    assert.equal(dimension.state, "unsatisfied");
    assert.deepEqual(dimension.affectedReferenceKeys, ["a", "b"]);
    const style = getDimensionRenderStyle(false, { unsatisfied: true });
    assert.equal(style.strokeStyle, "#f87171");
    assert.equal(style.textStyle, "#fecaca");
  });

  test(`${kind} zero axis span does not fall back to a misleading diagonal`, () => {
    const state = createState(kind, anchors[0]);
    state.document.entities[1].points[0][axis === "horizontal" ? "x" : "y"] = 0;
    state.document.dimensions[0].value = 0;
    const [dimension] = getPersistentDimensionDescriptors(state);
    assert.equal(dimension.value, 0);
    assert.equal(getDimensionLinearScreenGeometry(state, dimension.geometry), null);
  });
}

test("aligned linear dimensions retain the existing geometry and anchor semantics exactly", () => {
  for (const reversed of [false, true]) {
    for (const anchor of [{ x: 2, y: 2 }, { x: -4, y: 7 }, { x: 6, y: -3 }]) {
      const state = createState("LinearDistance", anchor, reversed);
      const [dimension] = getPersistentDimensionDescriptors(state);
      assert.equal(dimension.value, 5);
      assert.deepEqual(getDimensionLinearScreenGeometry(state, dimension.geometry, 20),
        getLinearDimensionScreenGeometry(
          toScreen(state, dimension.geometry.start),
          toScreen(state, dimension.geometry.end),
          toScreen(state, anchor), 20));
    }
  }
});

test("axis dimensions without a stored anchor use the reference midpoint without writing it", () => {
  for (const [kind, axis] of [["HorizontalDistance", "horizontal"], ["VerticalDistance", "vertical"]]) {
    const state = createState(kind, null);
    const { dimension } = assertAxisProjection(state, axis);
    assert.deepEqual(dimension.anchorPoint, { x: 1.5, y: 2 });
    assert.equal(state.document.dimensions[0].anchor, null);
  }
});

test("unresolved dimension references retain the existing no-geometry behavior", () => {
  const state = createState("HorizontalDistance", { x: 1.5, y: -1 });
  state.document.dimensions[0].referenceKeys[1] = "missing";
  const before = structuredClone(state.document);
  const [dimension] = getPersistentDimensionDescriptors(state);
  assert.equal(dimension.geometry, null);
  assert.equal(dimension.value, 3);
  assert.deepEqual(state.document, before);
});

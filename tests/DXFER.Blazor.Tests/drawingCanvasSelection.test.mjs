import assert from "node:assert/strict";
import test from "node:test";
import {
  applyDraftDimensionValue,
  applyLockedDraftDimensions,
  applyDirectSelectionClick,
  applyPolarSnapIfRequested,
  clearTransientDimensionInputs,
  clampDimensionInputScreenPoint,
  getAlignedRectangleCorners,
  getCenterRectangleCorners,
  getConstraintGlyphText,
  getDefaultActiveDimensionKey,
  getDynamicSketchSnapHit,
  getCenterPointArc,
  getDimensionPlacementRequest,
  getPersistentDimensionDescriptors,
  getRadialDimensionPreference,
  getSketchToolPointCount,
  getTangentArc,
  getNextDimensionKey,
  getSplitAtPointRequest,
  getThreePointCircle,
  getThreePointArc,
  findNearestTarget,
  isDynamicTargetCurrentToPointer,
  isInferenceGuideWithinScreenDistance,
  isPanPointerDownForTool,
  shouldAutoSelectDimensionInputValue,
  shouldCommitDimensionInputOnChange,
  shouldCommitDimensionInputOnBlur,
  shouldRefreshDimensionInputValue,
  syncActiveSelectionWithSelectedKeys
} from "../../src/DXFER.Blazor/wwwroot/drawingCanvas.js";

test("direct click selects an unselected target and makes it active", () => {
  const state = {
    selectedKeys: new Set(),
    activeSelectionKey: null
  };

  const changed = applyDirectSelectionClick(state, "line-a");

  assert.equal(changed, true);
  assert.deepEqual(Array.from(state.selectedKeys), ["line-a"]);
  assert.equal(state.activeSelectionKey, "line-a");
});

test("direct click on selected inactive target makes it active", () => {
  const state = {
    selectedKeys: new Set(["line-a", "line-b"]),
    activeSelectionKey: "line-a"
  };

  const changed = applyDirectSelectionClick(state, "line-b");

  assert.equal(changed, true);
  assert.deepEqual(Array.from(state.selectedKeys), ["line-a", "line-b"]);
  assert.equal(state.activeSelectionKey, "line-b");
});

test("direct click on active target deselects it and clears active", () => {
  const state = {
    selectedKeys: new Set(["line-a", "line-b"]),
    activeSelectionKey: "line-b"
  };

  const changed = applyDirectSelectionClick(state, "line-b");

  assert.equal(changed, true);
  assert.deepEqual(Array.from(state.selectedKeys), ["line-a"]);
  assert.equal(state.activeSelectionKey, null);
});

test("active selection clears when box deselect removes it", () => {
  const state = {
    selectedKeys: new Set(["line-a"]),
    activeSelectionKey: "line-b"
  };

  const changed = syncActiveSelectionWithSelectedKeys(state);

  assert.equal(changed, true);
  assert.equal(state.activeSelectionKey, null);
});

test("draft line dimension preserves direction when length is typed", () => {
  const state = {
    activeTool: "line",
    toolDraft: {
      points: [{ x: 0, y: 0 }],
      previewPoint: { x: 3, y: 4 }
    }
  };

  const changed = applyDraftDimensionValue(state, "length", 10);

  assert.equal(changed, true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 6, y: 8 });
});

test("draft midpoint line dimension uses full mirrored line length", () => {
  const state = {
    activeTool: "midpointline",
    toolDraft: {
      points: [{ x: 10, y: 10 }],
      previewPoint: { x: 13, y: 14 }
    }
  };

  const changed = applyDraftDimensionValue(state, "length", 10);

  assert.equal(changed, true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 13, y: 14 });
});

test("draft circle dimension updates radius along current direction", () => {
  const state = {
    activeTool: "centercircle",
    toolDraft: {
      points: [{ x: 5, y: 5 }],
      previewPoint: { x: 8, y: 9 }
    }
  };

  const changed = applyDraftDimensionValue(state, "radius", 10);

  assert.equal(changed, true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 11, y: 13 });
});

test("draft rectangle dimensions preserve dragged quadrant", () => {
  const state = {
    activeTool: "twopointrectangle",
    toolDraft: {
      points: [{ x: 10, y: 10 }],
      previewPoint: { x: 8, y: 13 }
    }
  };

  assert.equal(applyDraftDimensionValue(state, "width", 6), true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 4, y: 13 });

  assert.equal(applyDraftDimensionValue(state, "height", 4), true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 4, y: 14 });
});

test("draft aligned rectangle length preserves baseline direction", () => {
  const state = {
    activeTool: "alignedrectangle",
    toolDraft: {
      points: [{ x: 1, y: 2 }],
      previewPoint: { x: 4, y: 6 }
    }
  };

  const changed = applyDraftDimensionValue(state, "length", 10);

  assert.equal(changed, true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 7, y: 10 });
});

test("draft aligned rectangle depth preserves side of baseline", () => {
  const state = {
    activeTool: "alignedrectangle",
    toolDraft: {
      points: [{ x: 0, y: 0 }, { x: 4, y: 0 }],
      previewPoint: { x: 4, y: 3 }
    }
  };

  const changed = applyDraftDimensionValue(state, "depth", 5);

  assert.equal(changed, true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 4, y: 5 });
});

test("aligned rectangle corners project depth perpendicular to baseline", () => {
  const corners = getAlignedRectangleCorners(
    { x: 0, y: 0 },
    { x: 4, y: 0 },
    { x: 3, y: 2 }
  );

  assert.deepEqual(corners, [
    { x: 0, y: 0 },
    { x: 4, y: 0 },
    { x: 4, y: 2 },
    { x: 0, y: 2 }
  ]);
});

test("center rectangle corners mirror the corner across center", () => {
  const corners = getCenterRectangleCorners(
    { x: 10, y: 10 },
    { x: 13, y: 14 }
  );

  assert.deepEqual(corners, [
    { x: 7, y: 6 },
    { x: 13, y: 6 },
    { x: 13, y: 14 },
    { x: 7, y: 14 }
  ]);
});

test("three point circle returns circumcenter and radius", () => {
  const circle = getThreePointCircle(
    { x: 0, y: 1 },
    { x: 1, y: 0 },
    { x: -1, y: 0 }
  );

  assertApproxEqual(circle.center.x, 0);
  assertApproxEqual(circle.center.y, 0);
  assertApproxEqual(circle.radius, 1);
});

test("three point circle rejects collinear points", () => {
  assert.equal(getThreePointCircle(
    { x: 0, y: 0 },
    { x: 1, y: 1 },
    { x: 2, y: 2 }
  ), null);
});

test("three point arc returns arc through the selected through point", () => {
  const arc = getThreePointArc(
    { x: 1, y: 0 },
    { x: 0, y: 1 },
    { x: -1, y: 0 }
  );

  assertApproxEqual(arc.center.x, 0);
  assertApproxEqual(arc.center.y, 0);
  assertApproxEqual(arc.radius, 1);
  assertApproxEqual(arc.startAngleDegrees, 0);
  assertApproxEqual(arc.endAngleDegrees, 180);
});

test("three point arc chooses the sweep containing the through point", () => {
  const arc = getThreePointArc(
    { x: 1, y: 0 },
    { x: 0, y: -1 },
    { x: -1, y: 0 }
  );

  assertApproxEqual(arc.center.x, 0);
  assertApproxEqual(arc.center.y, 0);
  assertApproxEqual(arc.radius, 1);
  assertApproxEqual(arc.startAngleDegrees, 180);
  assertApproxEqual(arc.endAngleDegrees, 360);
});

test("three point arc rejects collinear points", () => {
  assert.equal(getThreePointArc(
    { x: 0, y: 0 },
    { x: 1, y: 1 },
    { x: 2, y: 2 }
  ), null);
});

test("center point arc uses start radius and end angle", () => {
  const arc = getCenterPointArc(
    { x: 0, y: 0 },
    { x: 2, y: 0 },
    { x: 0, y: 5 }
  );

  assertApproxEqual(arc.center.x, 0);
  assertApproxEqual(arc.center.y, 0);
  assertApproxEqual(arc.radius, 2);
  assertApproxEqual(arc.startAngleDegrees, 0);
  assertApproxEqual(arc.endAngleDegrees, 90);
});

test("center point arc chooses clockwise shortest visual sweep", () => {
  const arc = getCenterPointArc(
    { x: 0, y: 0 },
    { x: 2, y: 0 },
    { x: 0, y: -5 }
  );

  assertApproxEqual(arc.center.x, 0);
  assertApproxEqual(arc.center.y, 0);
  assertApproxEqual(arc.radius, 2);
  assertApproxEqual(arc.startAngleDegrees, 270);
  assertApproxEqual(arc.endAngleDegrees, 360);
});

test("center point arc uses clockwise shortest visual sweep past zero", () => {
  const arc = getCenterPointArc(
    { x: 0, y: 0 },
    { x: 0, y: 2 },
    { x: 5, y: 0 }
  );

  assertApproxEqual(arc.startAngleDegrees, 0);
  assertApproxEqual(arc.endAngleDegrees, 90);
});

test("center point arc rejects degenerate radius", () => {
  assert.equal(getCenterPointArc(
    { x: 0, y: 0 },
    { x: 0, y: 0 },
    { x: 0, y: 5 }
  ), null);
});

test("tangent arc uses start tangent and endpoint", () => {
  const arc = getTangentArc(
    { x: 0, y: 0 },
    { x: 1, y: 0 },
    { x: 2, y: 2 }
  );

  assertApproxEqual(arc.center.x, 0);
  assertApproxEqual(arc.center.y, 2);
  assertApproxEqual(arc.radius, 2);
  assertApproxEqual(arc.startAngleDegrees, 270);
  assertApproxEqual(arc.endAngleDegrees, 360);
});

test("tangent arc rejects parallel tangent and chord", () => {
  assert.equal(getTangentArc(
    { x: 0, y: 0 },
    { x: 1, y: 0 },
    { x: 2, y: 0 }
  ), null);
});

test("locked draft dimensions reapply after cursor movement", () => {
  const state = {
    activeTool: "line",
    toolDraft: {
      points: [{ x: 0, y: 0 }],
      previewPoint: { x: 0, y: 2 },
      dimensionValues: { length: 10 }
    }
  };

  const changed = applyLockedDraftDimensions(state);

  assert.equal(changed, true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 0, y: 10 });
});

test("shift is polar snap instead of pan while sketch tools are active", () => {
  assert.equal(isPanPointerDownForTool({ button: 0, shiftKey: true }, "line"), false);
  assert.equal(isPanPointerDownForTool({ button: 0, shiftKey: true }, "select"), false);
  assert.equal(isPanPointerDownForTool({ button: 1, shiftKey: false }, "line"), true);
  assert.equal(isPanPointerDownForTool({ button: 2, shiftKey: false }, "line"), false);
});

test("dynamic inferred target is hidden when cursor moves away from it", () => {
  const state = {
    pointerScreenPoint: { x: 100, y: 100 },
    view: {
      scale: 1,
      offsetX: 0,
      offsetY: 0
    }
  };

  const target = {
    dynamic: true,
    point: { x: 0, y: 0 }
  };

  assert.equal(isDynamicTargetCurrentToPointer(state, target), false);
  state.pointerScreenPoint = { x: 3, y: -4 };
  assert.equal(isDynamicTargetCurrentToPointer(state, target), true);
  assert.equal(isDynamicTargetCurrentToPointer(state, { dynamic: false, point: { x: 0, y: 0 } }), true);
});

test("line polar snap is ortho only near an ortho axis by default", () => {
  const state = {
    polarSnapIncrementDegrees: 15,
    view: {
      scale: 1
    },
    toolDraft: {
      points: [{ x: 0, y: 0 }]
    }
  };
  const nearHorizontal = { x: 10, y: 3 };
  const nearRadius = Math.hypot(nearHorizontal.x, nearHorizontal.y);

  const ortho = applyPolarSnapIfRequested(state, nearHorizontal, "line", { shiftKey: false });
  assertApproxEqual(ortho.x, nearRadius);
  assertApproxEqual(ortho.y, 0);

  const free = applyPolarSnapIfRequested(state, { x: 10, y: 8 }, "line", { shiftKey: false });
  assert.deepEqual(free, { x: 10, y: 8 });

  const fine = applyPolarSnapIfRequested(state, nearHorizontal, "line", { shiftKey: true });
  assertApproxEqual(fine.x, Math.cos(Math.PI / 12) * nearRadius);
  assertApproxEqual(fine.y, Math.sin(Math.PI / 12) * nearRadius);
});

test("dimension active key defaults and cycles through all visible dimensions", () => {
  const dimensions = [{ key: "width" }, { key: "height" }];

  assert.equal(getDefaultActiveDimensionKey(dimensions, null), "width");
  assert.equal(getDefaultActiveDimensionKey(dimensions, "height"), "height");
  assert.equal(getDefaultActiveDimensionKey(dimensions, "radius"), "width");
  assert.equal(getNextDimensionKey(dimensions, "width", false), "height");
  assert.equal(getNextDimensionKey(dimensions, "height", false), "width");
  assert.equal(getNextDimensionKey(dimensions, "width", true), "height");
});

test("focused dimension input keeps typed locks but refreshes live preview when unlocked", () => {
  assert.equal(shouldRefreshDimensionInputValue(true, true), false);
  assert.equal(shouldRefreshDimensionInputValue(true, false), true);
  assert.equal(shouldRefreshDimensionInputValue(false, true), true);
  assert.equal(shouldRefreshDimensionInputValue(true, false, true), false);

  assert.equal(shouldAutoSelectDimensionInputValue(true, false, false, true), true);
  assert.equal(shouldAutoSelectDimensionInputValue(true, true, false, true), false);
  assert.equal(shouldAutoSelectDimensionInputValue(true, false, true, true), false);
  assert.equal(shouldAutoSelectDimensionInputValue(true, false, false, false), false);
  assert.equal(shouldCommitDimensionInputOnBlur(false), true);
  assert.equal(shouldCommitDimensionInputOnBlur(true), false);
  assert.equal(shouldCommitDimensionInputOnBlur(false, true), false);
  assert.equal(shouldCommitDimensionInputOnChange(false), true);
  assert.equal(shouldCommitDimensionInputOnChange(true), false);
});

test("inference guides stop rendering after a maximum screen distance", () => {
  const state = {
    view: {
      scale: 1,
      offsetX: 0,
      offsetY: 0
    }
  };

  assert.equal(isInferenceGuideWithinScreenDistance(
    state,
    { orientation: "vertical", point: { x: 0, y: 0 } },
    { x: 0, y: 100 },
    120
  ), true);
  assert.equal(isInferenceGuideWithinScreenDistance(
    state,
    { orientation: "vertical", point: { x: 0, y: 0 } },
    { x: 0, y: 200 },
    120
  ), false);
  assert.equal(isInferenceGuideWithinScreenDistance(
    state,
    { orientation: "segment", start: { x: 0, y: 0 }, point: { x: 200, y: 0 } },
    { x: 100, y: 0 },
    120
  ), true);
  assert.equal(isInferenceGuideWithinScreenDistance(
    state,
    { orientation: "segment", start: { x: 0, y: 0 }, point: { x: 400, y: 0 } },
    { x: 200, y: 0 },
    120
  ), false);
});

test("two acquired ortho projection keeps both guides when it lands on highlighted geometry", () => {
  const state = {
    activeTool: "line",
    document: {
      entities: [
        {
          id: "horizontal",
          kind: "line",
          points: [{ x: 0, y: 10 }, { x: 30, y: 10 }]
        }
      ]
    },
    acquiredSnapPoints: [
      { label: "lower", point: { x: 20, y: 0 } },
      { label: "left", point: { x: 0, y: 10 } }
    ],
    view: {
      scale: 10,
      offsetX: 0,
      offsetY: 200
    },
    toolDraft: {
      points: [{ x: -5, y: -5 }]
    }
  };
  const highlightedTarget = {
    entity: state.document.entities[0],
    snapPoint: { x: 20, y: 10 }
  };

  const hit = getDynamicSketchSnapHit(state, { x: 200, y: 100 }, highlightedTarget);

  assert.equal(hit.target.point.x, 20);
  assert.equal(hit.target.point.y, 10);
  assert.deepEqual(hit.target.guides.map(guide => guide.orientation).sort(), ["horizontal", "vertical"]);
});

test("dimension input screen position is clamped inside the canvas", () => {
  const state = {
    canvas: {
      getBoundingClientRect: () => ({ width: 300, height: 200 })
    },
    pixelRatio: 1
  };

  assert.deepEqual(clampDimensionInputScreenPoint(state, { x: 150, y: 100 }, 50, 20), { x: 150, y: 100 });
  assert.deepEqual(clampDimensionInputScreenPoint(state, { x: -40, y: 250 }, 50, 20), { x: 50, y: 180 });
  assert.deepEqual(clampDimensionInputScreenPoint(state, { x: 360, y: -25 }, 50, 20), { x: 250, y: 20 });
});

test("transient dimension cleanup removes live draft inputs", () => {
  const removed = [];
  const lengthInput = {
    dataset: {},
    remove: () => removed.push("length")
  };
  const heightInput = {
    dataset: {},
    remove: () => removed.push("height")
  };
  const state = {
    activeDimensionKey: "length",
    dimensionInputs: new Map([
      ["length", lengthInput],
      ["height", heightInput]
    ])
  };

  clearTransientDimensionInputs(state);

  assert.deepEqual(removed, ["length", "height"]);
  assert.equal(state.dimensionInputs.size, 0);
  assert.equal(state.activeDimensionKey, null);
  assert.equal(lengthInput.dataset.skipNextBlurCommit, "true");
  assert.equal(heightInput.dataset.skipNextChangeCommit, "true");
});

test("dimension placement request returns selected references and anchor point", () => {
  const state = {
    activeTool: "dimension",
    dimensionDraft: {
      selectionKeys: ["edge"],
      radialDiameter: false
    },
    view: {
      scale: 2,
      offsetX: 10,
      offsetY: 30
    }
  };

  const request = getDimensionPlacementRequest(state, { x: 14, y: 22 });

  assert.deepEqual(request.selectionKeys, ["edge"]);
  assert.deepEqual(request.anchor, { x: 2, y: 4 });
  assert.equal(request.radialDiameter, false);
});

test("radial dimension preference defaults circles to diameter and arcs to radius", () => {
  assert.equal(getRadialDimensionPreference({ entity: { kind: "circle" } }, {}), true);
  assert.equal(getRadialDimensionPreference({ entity: { kind: "arc" } }, {}), false);
  assert.equal(getRadialDimensionPreference({ entity: { kind: "arc" } }, { shiftKey: true }), true);
});

test("persistent dimension descriptors resolve line references to overlay positions", () => {
  const state = {
    document: {
      entities: [
        {
          id: "edge",
          kind: "line",
          points: [{ x: 0, y: 0 }, { x: 3, y: 4 }]
        }
      ],
      dimensions: [
        {
          id: "dim-1",
          kind: "LinearDistance",
          referenceKeys: ["edge:start", "edge:end"],
          value: 5,
          isDriving: true
        }
      ]
    },
    view: {
      scale: 10,
      offsetX: 100,
      offsetY: 200
    }
  };

  const descriptors = getPersistentDimensionDescriptors(state);

  assert.equal(descriptors.length, 1);
  assert.equal(descriptors[0].id, "dim-1");
  assert.equal(descriptors[0].label, "Distance");
  assert.equal(descriptors[0].value, 5);
  assert.deepEqual(descriptors[0].point, { x: 115, y: 180 });
});

test("persistent dimension descriptors prefer explicit anchors", () => {
  const state = {
    document: {
      entities: [],
      dimensions: [
        {
          id: "radius-1",
          kind: "Radius",
          referenceKeys: ["hole"],
          value: 2,
          anchor: { x: 7, y: 8 },
          isDriving: true
        }
      ]
    },
    view: {
      scale: 2,
      offsetX: 10,
      offsetY: 30
    }
  };

  const descriptors = getPersistentDimensionDescriptors(state);

  assert.equal(descriptors.length, 1);
  assert.equal(descriptors[0].label, "Radius");
  assert.deepEqual(descriptors[0].point, { x: 24, y: 14 });
});

test("constraint glyphs use compact CAD relation markers", () => {
  assert.equal(getConstraintGlyphText("coincident"), "C");
  assert.equal(getConstraintGlyphText("horizontal"), "H");
  assert.equal(getConstraintGlyphText("perpendicular"), "L");
  assert.equal(getConstraintGlyphText("fix"), "F");
});

test("point sketch tool needs one click", () => {
  assert.equal(getSketchToolPointCount("point"), 1);
  assert.equal(getSketchToolPointCount("tangentarc"), 3);
  assert.equal(getSketchToolPointCount("centerrectangle"), 2);
});

test("persistent point entity is selected by id and exposes a snap point", () => {
  const state = createHitTestState([
    {
      id: "point-a",
      kind: "point",
      points: [{ x: 5, y: 2 }]
    }
  ]);

  const target = findNearestTarget(state, { x: 50, y: 80 });

  assert.equal(target.key, "point-a");
  assert.equal(target.kind, "entity");
  assert.deepEqual(target.snapPoint, { x: 5, y: 2 });
});

test("split at point request projects clicked line point", () => {
  const state = createHitTestState([
    {
      id: "edge",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    }
  ]);
  state.activeTool = "splitatpoint";

  const request = getSplitAtPointRequest(state, { x: 40, y: 100 });

  assert.equal(request.targetKey, "edge");
  assertApproxEqual(request.point.x, 4);
  assertApproxEqual(request.point.y, 0);
});

function assertApproxEqual(actual, expected, tolerance = 0.000001) {
  assert.ok(Math.abs(actual - expected) <= tolerance, `expected ${actual} to equal ${expected}`);
}

function createHitTestState(entities) {
  return {
    activeTool: "select",
    document: { entities },
    acquiredSnapPoints: [],
    view: {
      scale: 10,
      offsetX: 0,
      offsetY: 100
    }
  };
}

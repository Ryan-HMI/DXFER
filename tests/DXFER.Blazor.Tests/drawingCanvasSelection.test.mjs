import assert from "node:assert/strict";
import test from "node:test";
import {
  applyDraftDimensionValue,
  adjustPolygonSideCount,
  applyConstraintVisibilityState,
  applyGeometryDragPreview,
  applyLockedDraftDimensions,
  applyDirectSelectionClick,
  applyPolarSnapIfRequested,
  canExtendDimensionSelection,
  clearTransientDimensionInputs,
  clampDimensionInputScreenPoint,
  getAlignedRectangleCorners,
  getAngleDimensionScreenGeometry,
  getArcAngleDimensionScreenGeometry,
  getCenterRectangleCorners,
  getConstraintGlyphGroups,
  getConstraintGlyphIcon,
  getConstraintGlyphLeader,
  getVisibleConstraintGlyphGroups,
  getDefaultActiveDimensionKey,
  getVisibleDimensionDescriptors,
  getConstructionToggleRequest,
  getDynamicSketchSnapHit,
  getAddSplinePointRequest,
  getCenterPointArc,
  getChainedSketchToolDraft,
  getConstraintGlyphGroupHit,
  getDimensionAnchorUpdateRequest,
  getDimensionDisplayText,
  getDimensionInputClassName,
  getDimensionPlacementRequest,
  getDimensionRenderStyle,
  getDimensionSelectionKey,
  isDimensionReferenceSetComplete,
  markDimensionInputsToSkipNextBlurCommit,
  getCenteredAxisDiameterPoints,
  getEllipseAxisDiameterPoints,
  getFitViewForDocument,
  getEllipseFromPoints,
  getSketchDiagnosticEntityIds,
  getSketchDimensionState,
  getPersistentDimensionDescriptors,
  getPersistentDimensionCommitValue,
  getPolygonWorldPoints,
  getPolygonGuideCircleWorldPoints,
  getPolygonGuideCircleWorldPointsForEntity,
  getPendingPersistentDimensionEditId,
  getRadialDimensionScreenGeometry,
  getRadialDimensionPreference,
  getSketchToolDimensionLocks,
  getSketchToolPreviewMarkerPoints,
  getSketchChainContextFromCommittedTool,
  getModifyToolPointCount,
  getSketchToolPointCount,
  getPostCommitSketchToolState,
  getPersistentSplineTangentHandlesForEntity,
  getSplineFitWorldPoints,
  getSplineTangentHandles,
  getTangentArc,
  getTangentArcFallbackPreviewPoints,
  getPointTargetMarker,
  getPowerTrimCrossingRequests,
  getNextDimensionKey,
  getNextSketchToolDimensionFocusKey,
  getPowerTrimRequestMode,
  getPowerTrimRequest,
  getSplitAtPointRequest,
  getThreePointCircle,
  getThreePointArc,
  getThreePointArcConstructionState,
  getThreePointArcPreviewDimensions,
  getLinearDimensionScreenGeometry,
  findNearestTarget,
  finishGeometryDrag,
  releaseDimensionDraftSelection,
  resolveActiveDimensionKey,
  isDynamicTargetCurrentToPointer,
  isInferenceGuideWithinScreenDistance,
  isTargetEligibleForConstraintTool,
  isPanPointerDownForTool,
  prepareSelectionForToolEntry,
  pruneExpiredAcquiredSnapPoints,
  shouldAutoSelectDimensionInputValue,
  shouldCommitDimensionInputOnChange,
  shouldCommitDimensionInputOnBlur,
  shouldDrawSelectionTargetOverlay,
  shouldPreserveDraftDimensionsForNextPoint,
  shouldRefreshDimensionInputValue,
  setHoveredTarget,
  syncActiveSelectionWithSelectedKeys,
  tryToggleSketchChainToolAtPoint
} from "../../src/DXFER.Blazor/wwwroot/drawingCanvas.js";

test("blank document fit uses a sane default sketch scale", () => {
  const view = getFitViewForDocument(
    {
      entities: [],
      bounds: { minX: 0, minY: 0, maxX: 0, maxY: 0 }
    },
    { width: 1200, height: 800 });

  assert.equal(view.scale, 24);
  assert.equal(view.offsetX, 600);
  assert.equal(view.offsetY, 400);
});

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

test("draft ellipse dimensions use major and minor diameters", () => {
  const state = {
    activeTool: "ellipse",
    toolDraft: {
      points: [{ x: 0, y: 0 }, { x: 4, y: 0 }],
      previewPoint: { x: 0, y: 2 }
    }
  };

  assert.equal(applyDraftDimensionValue(state, "minor", 6), true);
  assert.deepEqual(state.toolDraft.previewPoint, { x: 0, y: 3 });

  assert.equal(applyDraftDimensionValue(state, "major", 12), true);
  assert.deepEqual(state.toolDraft.points[1], { x: 6, y: 0 });
  assert.deepEqual(state.toolDraft.dimensionValues, { minor: 6, major: 12 });
});

test("ellipse helper builds ratio and elliptical arc parameter", () => {
  const ellipse = getEllipseFromPoints(
    { x: 0, y: 0 },
    { x: 4, y: 0 },
    { x: 0, y: 2 },
    { x: 0, y: 2 });

  assert.equal(ellipse.majorLength, 4);
  assert.equal(ellipse.minorLength, 2);
  assert.equal(ellipse.minorRadiusRatio, 0.5);
  assert.equal(ellipse.endParameterDegrees, 90);
});

test("ellipse axis dimension helpers use full diameter endpoints", () => {
  const ellipse = getEllipseFromPoints(
    { x: 0, y: 0 },
    { x: 4, y: 0 },
    { x: 0, y: 2 });

  const major = getEllipseAxisDiameterPoints(ellipse, "major");
  const minor = getEllipseAxisDiameterPoints(ellipse, "minor");

  assert.deepEqual(major.start, { x: -4, y: 0 });
  assert.deepEqual(major.end, { x: 4, y: 0 });
  assert.deepEqual(minor.start, { x: 0, y: -2 });
  assert.deepEqual(minor.end, { x: 0, y: 2 });
});

test("ellipse major preview uses centered diameter endpoints before minor point", () => {
  const major = getCenteredAxisDiameterPoints(
    { x: 0, y: 0 },
    { x: 4, y: 0 });

  assert.deepEqual(major.start, { x: -4, y: 0 });
  assert.deepEqual(major.end, { x: 4, y: 0 });
});

test("creation tool point counts cover remaining sketch tools", () => {
  assert.equal(getSketchToolPointCount("ellipse"), 3);
  assert.equal(getSketchToolPointCount("ellipticalarc"), 4);
  assert.equal(getSketchToolPointCount("inscribedpolygon"), 2);
  assert.equal(getSketchToolPointCount("circumscribedpolygon"), 2);
  assert.equal(getSketchToolPointCount("conic"), 3);
  assert.equal(getSketchToolPointCount("spline"), Number.MAX_SAFE_INTEGER);
  assert.equal(getSketchToolPointCount("splinecontrolpoint"), Number.MAX_SAFE_INTEGER);
  assert.equal(getSketchToolPointCount("slot"), 3);
});

test("spline fit preview interpolates every fit point", () => {
  const fitPoints = [
    { x: 0, y: 0 },
    { x: 1, y: 2 },
    { x: 3, y: 1 },
    { x: 5, y: 4 },
    { x: 7, y: 0 },
    { x: 8, y: 1 }
  ];

  const samples = getSplineFitWorldPoints(fitPoints, 12);

  assert.equal(samples.length > fitPoints.length, true);
  for (const point of fitPoints) {
    assert.equal(
      samples.some(sample => distanceBetweenTestPoints(sample, point) <= 0.000001),
      true,
      `expected spline preview to pass through ${JSON.stringify(point)}`);
  }

  assert.deepEqual(samples[0], fitPoints[0]);
  assert.deepEqual(samples.at(-1), fitPoints.at(-1));
});

test("spline tangent handles are generated only from endpoint fit point neighbors", () => {
  const fitPoints = [
    { x: 0, y: 0 },
    { x: 1, y: 2 },
    { x: 3, y: 1 },
    { x: 5, y: 4 },
    { x: 7, y: 0 }
  ];

  const handles = getSplineTangentHandles(fitPoints);

  assert.equal(handles.length, 2);
  assert.deepEqual(handles[0].point, fitPoints[0]);
  assert.equal(handles[0].backward, null);
  assert.ok(handles[0].forward);
  assert.deepEqual(handles[1].point, fitPoints.at(-1));
  assert.ok(handles.at(-1).backward);
  assert.equal(handles.at(-1).forward, null);
  assert.equal(handles.some(handle => handle.point === fitPoints[2]), false);
});

test("created spline entities expose persistent endpoint tangent handles", () => {
  const spline = {
    id: "spline-a",
    kind: "spline",
    points: [
      { x: 0, y: 0 },
      { x: 1, y: 1 },
      { x: 2, y: 0 },
      { x: 3, y: 1 }
    ]
  };

  const handles = getPersistentSplineTangentHandlesForEntity(spline);

  assert.equal(handles.length, 2);
  assert.deepEqual(handles[0].point, { x: 0, y: 0 });
  assert.deepEqual(handles[1].point, { x: 3, y: 1 });
});

test("spline fit points are selectable point targets", () => {
  const spline = {
    id: "spline-a",
    kind: "spline",
    points: [
      { x: 0, y: 0 },
      { x: 2, y: 2 },
      { x: 4, y: -1 },
      { x: 6, y: 0 }
    ],
    fitPoints: [
      { x: 0, y: 0 },
      { x: 2, y: 2 },
      { x: 4, y: -1 },
      { x: 6, y: 0 }
    ]
  };

  const target = findNearestTarget(createHitTestState([spline]), { x: 20, y: 80 });

  assert.equal(target?.kind, "point");
  assert.equal(target?.key, "spline-a|point|fit-1|2|2");
});

test("add spline point request targets clicked fit spline projection", () => {
  const spline = {
    id: "spline-a",
    kind: "spline",
    points: [{ x: 0, y: 0 }, { x: 10, y: 0 }],
    fitPoints: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
  };
  const state = createHitTestState([spline]);

  const request = getAddSplinePointRequest(state, { x: 45, y: 98 });

  assert.equal(request.targetKey, "spline-a");
  assertApproxEqual(request.point.x, 4.5);
  assertApproxEqual(request.point.y, 0);
});

test("selected add spline point request ignores unselected overlapping splines", () => {
  const unselected = {
    id: "unselected",
    kind: "spline",
    points: [{ x: 0, y: 0 }, { x: 10, y: 0 }],
    fitPoints: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
  };
  const selected = {
    id: "selected",
    kind: "spline",
    points: [{ x: 0, y: 4 }, { x: 10, y: 4 }],
    fitPoints: [{ x: 0, y: 4 }, { x: 10, y: 4 }]
  };
  const state = createHitTestState([unselected, selected]);
  state.selectedKeys = new Set(["selected"]);

  const request = getAddSplinePointRequest(state, { x: 45, y: 58 }, true);

  assert.equal(request.targetKey, "selected");
  assertApproxEqual(request.point.x, 4.5);
  assertApproxEqual(request.point.y, 4);
});

test("spline endpoint tangent handles are selectable point targets", () => {
  const spline = {
    id: "spline-a",
    kind: "spline",
    points: [
      { x: 0, y: 0 },
      { x: 4, y: 0 },
      { x: 8, y: 2 }
    ],
    fitPoints: [
      { x: 0, y: 0 },
      { x: 4, y: 0 },
      { x: 8, y: 2 }
    ]
  };

  const target = findNearestTarget(createHitTestState([spline]), { x: 10, y: 100 });

  assert.equal(target?.kind, "point");
  assert.equal(target?.key, "spline-a|point|tangent-start|1|0");
});

test("spline tangent handle target wins over nearby fit point when grabbing handle", () => {
  const spline = {
    id: "spline-a",
    kind: "spline",
    points: [
      { x: 0, y: 0 },
      { x: 4, y: 0 },
      { x: 8, y: 2 }
    ],
    fitPoints: [
      { x: 0, y: 0 },
      { x: 4, y: 0 },
      { x: 8, y: 2 }
    ],
    startTangentHandle: { x: 0.4, y: 0 }
  };

  const target = findNearestTarget(createHitTestState([spline]), { x: 1.5, y: 100 });

  assert.equal(target?.kind, "point");
  assert.equal(target?.key, "spline-a|point|tangent-start|0.4|0");
});

test("spline fit point drag preview moves the fit point", () => {
  const document = {
    entities: [{
      id: "spline-a",
      kind: "spline",
      points: [
        { x: 0, y: 0 },
        { x: 4, y: 0 },
        { x: 8, y: 2 }
      ],
      fitPoints: [
        { x: 0, y: 0 },
        { x: 4, y: 0 },
        { x: 8, y: 2 }
      ]
    }]
  };

  const preview = applyGeometryDragPreview(
    document,
    "spline-a|point|fit-1|4|0",
    { x: 4, y: 0 },
    { x: 4, y: 2 });

  assert.deepEqual(preview.entities[0].fitPoints[1], { x: 4, y: 2 });
  assert.deepEqual(document.entities[0].fitPoints[1], { x: 4, y: 0 });
});

test("spline tangent handle drag preview edits the handle without moving fit points", () => {
  const document = {
    entities: [{
      id: "spline-a",
      kind: "spline",
      points: [
        { x: 0, y: 0 },
        { x: 4, y: 0 },
        { x: 8, y: 2 }
      ],
      fitPoints: [
        { x: 0, y: 0 },
        { x: 4, y: 0 },
        { x: 8, y: 2 }
      ],
      startTangentHandle: { x: 1, y: 0 },
      endTangentHandle: { x: 7, y: 1.5 }
    }]
  };

  const preview = applyGeometryDragPreview(
    document,
    "spline-a|point|tangent-start|1|0",
    { x: 1, y: 0 },
    { x: 1, y: 1 });

  assert.deepEqual(preview.entities[0].startTangentHandle, { x: 1, y: 1 });
  assert.deepEqual(document.entities[0].fitPoints[1], { x: 4, y: 0 });
  assert.deepEqual(preview.entities[0].fitPoints, document.entities[0].fitPoints);
  assert.notDeepEqual(preview.entities[0].points, document.entities[0].points);
});

test("same-key hover refreshes when drag preview swaps spline geometry", () => {
  const originalSpline = {
    id: "spline-a",
    kind: "spline",
    points: [
      { x: 0, y: 0 },
      { x: 4, y: 0 },
      { x: 8, y: 2 }
    ],
    fitPoints: [
      { x: 0, y: 0 },
      { x: 4, y: 0 },
      { x: 8, y: 2 }
    ],
    startTangentHandle: { x: 1, y: 0 }
  };
  const previewSpline = {
    ...originalSpline,
    points: [
      { x: 0, y: 0 },
      { x: 3, y: 1 },
      { x: 8, y: 2 }
    ],
    startTangentHandle: { x: 1, y: 1 }
  };
  const state = createCanvasInteractionState([originalSpline]);
  const selectionKey = "spline-a|point|tangent-start|1|0";
  const originalTarget = {
    kind: "point",
    key: selectionKey,
    entityId: "spline-a",
    entity: originalSpline,
    label: "tangent-start",
    point: { x: 1, y: 0 }
  };
  const previewTarget = {
    ...originalTarget,
    entity: previewSpline,
    point: { x: 1, y: 1 }
  };

  assert.equal(setHoveredTarget(state, originalTarget), true);
  state.document = { entities: [previewSpline] };

  assert.equal(setHoveredTarget(state, previewTarget), true);
  assert.equal(state.hoveredTarget.entity, previewSpline);
  assert.deepEqual(state.hoveredTarget.point, { x: 1, y: 1 });
});

test("spline fit point drag preview preserves independent endpoint tangent handles", () => {
  const document = {
    entities: [{
      id: "spline-a",
      kind: "spline",
      points: [
        { x: 0, y: 0 },
        { x: 4, y: 0 },
        { x: 8, y: 2 }
      ],
      fitPoints: [
        { x: 0, y: 0 },
        { x: 4, y: 0 },
        { x: 8, y: 2 }
      ],
      startTangentHandle: { x: 1, y: 0 },
      endTangentHandle: { x: 7, y: 1.5 }
    }]
  };

  const preview = applyGeometryDragPreview(
    document,
    "spline-a|point|fit-1|4|0",
    { x: 4, y: 0 },
    { x: 4, y: 2 });

  assert.deepEqual(preview.entities[0].fitPoints[1], { x: 4, y: 2 });
  assert.deepEqual(preview.entities[0].startTangentHandle, { x: 1, y: 0 });
  assert.deepEqual(preview.entities[0].endTangentHandle, { x: 7, y: 1.5 });
});

test("polygon helper distinguishes inscribed and circumscribed radii", () => {
  const inscribed = getPolygonWorldPoints({ x: 0, y: 0 }, { x: 10, y: 0 }, false);
  const circumscribed = getPolygonWorldPoints({ x: 0, y: 0 }, { x: 10, y: 0 }, true);

  assert.equal(inscribed.length, 6);
  assert.equal(circumscribed.length, 6);
  assertApproxEqual(Math.hypot(inscribed[0].x, inscribed[0].y), 10);
  assertApproxEqual(Math.hypot(circumscribed[0].x, circumscribed[0].y), 10 / Math.cos(Math.PI / 6));
});

test("polygon preview guide circle follows the picked radius or apothem", () => {
  const guide = getPolygonGuideCircleWorldPoints({ x: 2, y: 3 }, { x: 7, y: 3 });

  assert.equal(guide.length > 16, true);
  for (const point of guide) {
    assertApproxEqual(distanceBetweenTestPoints(point, { x: 2, y: 3 }), 5);
  }
});

test("polygon entity guide circle follows the stored guide radius", () => {
  const guide = getPolygonGuideCircleWorldPointsForEntity({
    id: "polygon",
    kind: "polygon",
    center: { x: 2, y: 3 },
    radius: 5,
    rotationAngleDegrees: 30,
    sideCount: 6,
    circumscribed: true,
    points: []
  });

  assert.equal(guide.length > 16, true);
  for (const point of guide) {
    assertApproxEqual(Math.hypot(point.x - 2, point.y - 3), 5);
  }
});

test("draft polygon side dimension is permanent editable state", () => {
  const state = {
    activeTool: "inscribedpolygon",
    toolDraft: {
      points: [{ x: 0, y: 0 }],
      previewPoint: { x: 10, y: 0 },
      dimensionValues: {}
    }
  };

  const changed = applyDraftDimensionValue(state, "sides", 8);

  assert.equal(changed, true);
  assert.equal(state.toolDraft.polygonSideCount, 8);
  assert.deepEqual(state.toolDraft.dimensionValues, { sides: 8 });
  assert.deepEqual(state.toolDraft.previewPoint, { x: 10, y: 0 });
});

test("polygon wheel changes side count without locking dimension input", () => {
  const state = {
    activeTool: "inscribedpolygon",
    toolDraft: {
      points: [{ x: 0, y: 0 }],
      previewPoint: { x: 10, y: 0 },
      dimensionValues: { radius: 10 },
      polygonSideCount: 6
    }
  };

  const changed = adjustPolygonSideCount(state, -120);

  assert.equal(changed, true);
  assert.equal(state.toolDraft.polygonSideCount, 7);
  assert.deepEqual(state.toolDraft.dimensionValues, { radius: 10 });
});

test("polygon placement supports shifted vertical polar snap", () => {
  const state = {
    polarSnapIncrementDegrees: 15,
    view: { scale: 10 },
    toolDraft: {
      points: [{ x: 0, y: 0 }]
    }
  };

  const snapped = applyPolarSnapIfRequested(
    state,
    { x: 0.3, y: 10 },
    "inscribedpolygon",
    { shiftKey: true });

  assertApproxEqual(snapped.x, 0);
  assertApproxEqual(snapped.y, Math.hypot(0.3, 10));
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

test("three point arc preview prompts only radius", () => {
  const state = createHitTestState([]);
  const dimensions = getThreePointArcPreviewDimensions(
    state,
    { x: 1, y: 0 },
    { x: Math.SQRT1_2, y: Math.SQRT1_2 },
    { x: 0, y: 1 }
  );

  assert.deepEqual(dimensions.map(dimension => dimension.key), ["radius"]);
  assertApproxEqual(dimensions[0].value, 1);
  assert.equal(Number.isFinite(dimensions[0].point.x), true);
  assert.equal(Number.isFinite(dimensions[0].point.y), true);
});

test("three point arc sweep dimension updates the preview endpoint", () => {
  const state = {
    activeTool: "threepointarc",
    toolDraft: {
      points: [{ x: 1, y: 0 }, { x: Math.SQRT1_2, y: Math.SQRT1_2 }],
      previewPoint: { x: 0, y: 1 },
      dimensionValues: {}
    }
  };

  assert.equal(applyDraftDimensionValue(state, "sweep", 120), true);

  const arc = getThreePointArc(
    state.toolDraft.points[0],
    state.toolDraft.points[1],
    state.toolDraft.previewPoint);
  const arcState = getThreePointArcConstructionState(
    state.toolDraft.points[0],
    state.toolDraft.points[1],
    state.toolDraft.previewPoint);

  assertApproxEqual(arc.radius, 1);
  assertApproxEqual(arcState.sweepDegrees, 120);
});

test("three point arc radius dimension updates the preview radius", () => {
  const state = {
    activeTool: "threepointarc",
    toolDraft: {
      points: [{ x: 1, y: 0 }, { x: 0, y: 1 }],
      previewPoint: { x: -1, y: 0 },
      dimensionValues: {}
    }
  };

  assert.equal(applyDraftDimensionValue(state, "radius", 2), true);

  const arc = getThreePointArc(
    state.toolDraft.points[0],
    state.toolDraft.points[1],
    state.toolDraft.previewPoint);

  assertApproxEqual(arc.radius, 2);
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

test("line chain hover toggles to a tangent arc draft and back to a line draft", () => {
  const chainContext = getSketchChainContextFromCommittedTool(
    "line",
    [{ x: 0, y: 0 }, { x: 4, y: 0 }]
  );
  const state = {
    activeTool: "line",
    sketchChainContext: chainContext,
    sketchChainVertexHovering: false,
    toolDraft: {
      points: [{ x: 4, y: 0 }],
      previewPoint: { x: 6, y: 0 }
    },
    dimensionInputs: new Map(),
    acquiredSnapPoints: [{ label: "end", point: { x: 4, y: 0 } }],
    view: {
      scale: 10,
      offsetX: 0,
      offsetY: 100
    }
  };

  assert.equal(tryToggleSketchChainToolAtPoint(state, { x: 70, y: 100 }), false);
  assert.equal(state.activeTool, "line");

  const chainVertexTarget = {
    kind: "point",
    point: { x: 4, y: 0 },
    label: "end"
  };

  assert.equal(tryToggleSketchChainToolAtPoint(state, { x: 40, y: 100 }, chainVertexTarget), true);
  assert.equal(state.activeTool, "tangentarc");
  assert.deepEqual(state.toolDraft.points, [{ x: 4, y: 0 }, { x: 8, y: 0 }]);
  assert.equal(state.toolDraft.previewPoint, null);
  assert.equal(state.sketchChainVertexHovering, true);
  assert.deepEqual(state.acquiredSnapPoints, []);

  assert.equal(tryToggleSketchChainToolAtPoint(state, { x: 40, y: 100 }, chainVertexTarget), false);
  assert.equal(state.activeTool, "tangentarc");
  assert.equal(tryToggleSketchChainToolAtPoint(state, { x: 80, y: 100 }), false);
  assert.equal(state.sketchChainVertexHovering, false);

  assert.equal(tryToggleSketchChainToolAtPoint(state, { x: 40, y: 100 }, chainVertexTarget), true);
  assert.equal(state.activeTool, "line");
  assert.deepEqual(state.toolDraft.points, [{ x: 4, y: 0 }]);
});

test("committed tangent arc chain continues from the arc endpoint", () => {
  const chainContext = getSketchChainContextFromCommittedTool(
    "tangentarc",
    [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 2, y: 2 }]
  );

  const lineDraft = getChainedSketchToolDraft("tangentarc", "line", chainContext);
  assert.deepEqual(lineDraft.points, [{ x: 2, y: 2 }]);

  const arcDraft = getChainedSketchToolDraft("line", "tangentarc", chainContext);
  assert.equal(arcDraft.points.length, 2);
  assert.deepEqual(arcDraft.points[0], { x: 2, y: 2 });
  assert.ok(distanceBetweenTestPoints(arcDraft.points[0], arcDraft.points[1]) > 0);
});

test("tangent arc commit stays in arc mode until the next last vertex hover", () => {
  const state = getPostCommitSketchToolState(
    "tangentarc",
    [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 2, y: 2 }],
    true
  );

  assert.equal(state.activeTool, "tangentarc");
  assert.equal(state.toolDraft.points.length, 2);
  assert.deepEqual(state.toolDraft.points[0], { x: 2, y: 2 });
  assert.equal(state.toolDraft.previewPoint, null);
  assert.equal(state.sketchChainVertexHovering, false);
  assert.equal(state.sketchChainToggleRequiresExit, true);
});

test("tangent arc preview does not mark the tangent control point as a vertex", () => {
  const markers = getSketchToolPreviewMarkerPoints(
    "tangentarc",
    [{ x: 4, y: 0 }, { x: 8, y: 0 }]
  );

  assert.deepEqual(markers, [{ x: 4, y: 0 }]);
});

test("tangent arc invalid fallback does not draw the mirrored tangent-control segment", () => {
  assert.deepEqual(
    getTangentArcFallbackPreviewPoints(
      { x: 4, y: 0 },
      { x: 8, y: 0 },
      { x: 5, y: 0 }
    ),
    [{ x: 4, y: 0 }, { x: 5, y: 0 }]
  );

  assert.deepEqual(
    getTangentArcFallbackPreviewPoints(
      { x: 4, y: 0 },
      { x: 8, y: 0 },
      { x: 4, y: 0 }
    ),
    []
  );
});

test("tangent arc radius key-in updates the preview and creates a radius lock", () => {
  const state = {
    activeTool: "tangentarc",
    toolDraft: {
      points: [{ x: 0, y: 0 }, { x: 1, y: 0 }],
      previewPoint: { x: 2, y: 2 },
      dimensionValues: {}
    }
  };

  assert.equal(applyDraftDimensionValue(state, "radius", 5), true);

  const arc = getTangentArc(
    state.toolDraft.points[0],
    state.toolDraft.points[1],
    state.toolDraft.previewPoint
  );

  assertApproxEqual(arc.radius, 5);
  assert.deepEqual(getSketchToolDimensionLocks(state.toolDraft), {
    keys: ["radius"],
    values: [5]
  });
});

test("center point arc sweep dimension updates the preview endpoint", () => {
  const state = {
    activeTool: "centerpointarc",
    toolDraft: {
      points: [{ x: 0, y: 0 }, { x: 1, y: 0 }],
      previewPoint: { x: 0, y: 1 },
      dimensionValues: {}
    }
  };

  assert.equal(applyDraftDimensionValue(state, "sweep", 120), true);

  const arc = getCenterPointArc(
    state.toolDraft.points[0],
    state.toolDraft.points[1],
    state.toolDraft.previewPoint);

  assertApproxEqual(getPositiveTestSweep(arc.startAngleDegrees, arc.endAngleDegrees), 120);
});

test("chain tool toggle is suppressed until the pointer leaves the committed vertex", () => {
  const state = {
    ...getPostCommitSketchToolState(
      "line",
      [{ x: 0, y: 0 }, { x: 4, y: 0 }]
    ),
    dimensionInputs: new Map(),
    acquiredSnapPoints: [],
    view: {
      scale: 10,
      offsetX: 0,
      offsetY: 100
    }
  };
  const chainVertexTarget = {
    kind: "point",
    point: { x: 4, y: 0 },
    label: "end"
  };

  assert.equal(tryToggleSketchChainToolAtPoint(state, { x: 40, y: 100 }, chainVertexTarget), false);
  assert.equal(state.activeTool, "line");
  assert.equal(state.sketchChainToggleRequiresExit, true);

  assert.equal(tryToggleSketchChainToolAtPoint(state, { x: 70, y: 100 }), false);
  assert.equal(state.sketchChainToggleRequiresExit, false);

  assert.equal(tryToggleSketchChainToolAtPoint(state, { x: 40, y: 100 }, chainVertexTarget), true);
  assert.equal(state.activeTool, "tangentarc");
});

test("line chain hover toggles at the committed vertex even without a point target hit", () => {
  const state = {
    ...getPostCommitSketchToolState(
      "line",
      [{ x: 0, y: 0 }, { x: 4, y: 0 }]
    ),
    dimensionInputs: new Map(),
    acquiredSnapPoints: [],
    view: {
      scale: 10,
      offsetX: 0,
      offsetY: 100
    }
  };

  assert.equal(tryToggleSketchChainToolAtPoint(state, { x: 70, y: 100 }), false);
  assert.equal(state.sketchChainToggleRequiresExit, false);
  assert.equal(tryToggleSketchChainToolAtPoint(state, { x: 40, y: 100 }, null), true);
  assert.equal(state.activeTool, "tangentarc");
});

test("line after tangent arc snaps along the arc endpoint tangent", () => {
  const chainContext = getSketchChainContextFromCommittedTool(
    "tangentarc",
    [{ x: 0, y: 0 }, { x: 1, y: 0 }, { x: 2, y: 2 }]
  );
  const state = {
    activeTool: "line",
    document: { entities: [] },
    acquiredSnapPoints: [],
    sketchChainContext: chainContext,
    toolDraft: {
      points: [{ x: 2, y: 2 }]
    },
    view: {
      scale: 10,
      offsetX: 0,
      offsetY: 100
    }
  };

  const hit = getDynamicSketchSnapHit(state, { x: 20.5, y: 50 });

  assert.equal(hit.target.label, "tangent-chain");
  assert.equal(getPointTargetMarker(hit.target), "tangent");
  assertApproxEqual(hit.target.point.x, 2);
  assertApproxEqual(hit.target.point.y, 5);
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

test("offset draft dimension updates the preview point on the picked side", () => {
  const state = createHitTestState([
    {
      id: "edge",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    }
  ]);
  state.activeTool = "offset";
  state.selectedKeys = new Set(["edge"]);
  state.toolDraft = {
    points: [],
    previewPoint: { x: 2, y: 3 },
    dimensionValues: {}
  };

  const changed = applyDraftDimensionValue(state, "offset", 5);

  assert.equal(changed, true);
  assertApproxEqual(state.toolDraft.previewPoint.x, 2);
  assertApproxEqual(state.toolDraft.previewPoint.y, 5);
  assert.equal(state.toolDraft.dimensionValues.offset, 5);
});

test("sketch tool dimension locks only include finite positive values", () => {
  const locks = getSketchToolDimensionLocks({
    dimensionValues: {
      length: 12.5,
      height: 0,
      ignored: Number.NaN,
      width: "7"
    }
  });

  assert.deepEqual(locks.keys, ["length", "width"]);
  assert.deepEqual(locks.values, [12.5, 7]);
});

test("polygon side count draft exports as side lock without mutating dimension input", () => {
  const locks = getSketchToolDimensionLocks({
    polygonSideCount: 8,
    dimensionValues: {
      radius: 10
    }
  });

  assert.deepEqual(locks.keys, ["radius", "sides"]);
  assert.deepEqual(locks.values, [10, 8]);
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

test("three point circle snaps the third point to line tangency", () => {
  const state = createHitTestState([
    {
      id: "edge",
      kind: "line",
      points: [{ x: -10, y: 2 }, { x: 10, y: 2 }]
    }
  ]);
  state.activeTool = "threepointcircle";
  state.toolDraft = {
    points: [{ x: 0, y: 0 }, { x: 4, y: 0 }],
    previewPoint: null
  };

  const target = findNearestTarget(state, { x: 20, y: 80 });

  assert.equal(target.dynamic, true);
  assert.equal(target.label.startsWith("circle-tangent-edge"), true);
  assertApproxEqual(target.point.x, 2);
  assertApproxEqual(target.point.y, 2);
});

test("three point circle snaps the third point to curve tangency", () => {
  const tangentPoint = { x: Math.SQRT1_2, y: Math.SQRT1_2 };
  const newCircleCenter = { x: 3 * Math.SQRT1_2, y: 3 * Math.SQRT1_2 };
  const perpendicular = { x: -Math.SQRT1_2, y: Math.SQRT1_2 };
  const state = createHitTestState([
    {
      id: "hole",
      kind: "circle",
      center: { x: 0, y: 0 },
      radius: 1
    }
  ]);
  state.view.scale = 20;
  state.activeTool = "threepointcircle";
  state.toolDraft = {
    points: [
      {
        x: newCircleCenter.x + (2 * perpendicular.x),
        y: newCircleCenter.y + (2 * perpendicular.y)
      },
      {
        x: newCircleCenter.x - (2 * perpendicular.x),
        y: newCircleCenter.y - (2 * perpendicular.y)
      }
    ],
    previewPoint: null
  };

  const target = findNearestTarget(state, {
    x: tangentPoint.x * state.view.scale,
    y: state.view.offsetY - (tangentPoint.y * state.view.scale)
  });

  assert.equal(target.dynamic, true);
  assert.equal(target.label.startsWith("circle-tangent-hole"), true);
  assertApproxEqual(target.point.x, tangentPoint.x);
  assertApproxEqual(target.point.y, tangentPoint.y);
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

test("ellipse polar snap uses the center while placing the major axis", () => {
  const state = {
    view: {
      scale: 1
    },
    toolDraft: {
      points: [{ x: 0, y: 0 }]
    }
  };
  const nearHorizontal = { x: 10, y: 3 };
  const nearRadius = Math.hypot(nearHorizontal.x, nearHorizontal.y);

  const snapped = applyPolarSnapIfRequested(state, nearHorizontal, "ellipse", { shiftKey: false });

  assertApproxEqual(snapped.x, nearRadius);
  assertApproxEqual(snapped.y, 0);
});

test("aligned rectangle polar snap uses the last placed point for depth", () => {
  const state = {
    view: {
      scale: 1
    },
    toolDraft: {
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    }
  };
  const nearVerticalFromBaselineEnd = { x: 13, y: 10 };
  const radius = Math.hypot(3, 10);

  const snapped = applyPolarSnapIfRequested(state, nearVerticalFromBaselineEnd, "alignedrectangle", { shiftKey: false });

  assertApproxEqual(snapped.x, 10);
  assertApproxEqual(snapped.y, radius);
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

test("pending dimension focus waits until requested key becomes visible", () => {
  const pending = resolveActiveDimensionKey([], null, "depth");

  assert.deepEqual(pending, {
    activeKey: null,
    pendingKey: "depth"
  });

  assert.deepEqual(
    resolveActiveDimensionKey([{ key: "length" }, { key: "depth" }], null, pending.pendingKey),
    {
      activeKey: "depth",
      pendingKey: null
    });
});

test("focused dimension key stays active when no pending focus is waiting", () => {
  assert.deepEqual(
    resolveActiveDimensionKey([{ key: "length" }, { key: "depth" }], "length", null, "depth"),
    {
      activeKey: "depth",
      pendingKey: null
    });
});

test("pending dimension focus wins over stale focused key during command phase change", () => {
  assert.deepEqual(
    resolveActiveDimensionKey([{ key: "length" }, { key: "depth" }], "length", "depth", "length"),
    {
      activeKey: "depth",
      pendingKey: null
    });
});

test("sequential sketch tools preserve first keyed dimension and request the next input on click", () => {
  const ellipseNextPoints = [{ x: 0, y: 0 }, { x: 5, y: 0 }];
  const alignedRectangleNextPoints = [{ x: 0, y: 0 }, { x: 5, y: 0 }];

  assert.equal(shouldPreserveDraftDimensionsForNextPoint("ellipse", ellipseNextPoints), true);
  assert.equal(getNextSketchToolDimensionFocusKey("ellipse", ellipseNextPoints), "minor");
  assert.equal(shouldPreserveDraftDimensionsForNextPoint("alignedrectangle", alignedRectangleNextPoints), true);
  assert.equal(getNextSketchToolDimensionFocusKey("alignedrectangle", alignedRectangleNextPoints), "depth");
});

test("visible dimension descriptors preserve drawn order instead of map insertion order", () => {
  const state = {
    visibleDimensionKeys: ["length", "depth"],
    dimensionInputs: new Map([
      ["depth", {}],
      ["length", {}]
    ])
  };

  assert.deepEqual(getVisibleDimensionDescriptors(state), [{ key: "length" }, { key: "depth" }]);
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
  assert.equal(shouldCommitDimensionInputOnBlur(false, false, false), false);
  assert.equal(shouldCommitDimensionInputOnBlur(false, false, true), true);
  assert.equal(shouldCommitDimensionInputOnBlur(true, false, true), false);
  assert.equal(shouldCommitDimensionInputOnBlur(false, true, true), false);
  assert.equal(shouldCommitDimensionInputOnChange(false, false), false);
  assert.equal(shouldCommitDimensionInputOnChange(false, true), true);
  assert.equal(shouldCommitDimensionInputOnChange(true, true), false);
});

test("dimension drag marks persistent inputs to skip synthetic blur commits", () => {
  const draftInput = { dataset: {} };
  const persistentInput = { dataset: {} };
  const state = {
    dimensionInputs: new Map([["draft", draftInput]]),
    persistentDimensionInputs: new Map([["persistent", persistentInput]])
  };

  markDimensionInputsToSkipNextBlurCommit(state);

  assert.equal(draftInput.dataset.skipNextBlurCommit, "true");
  assert.equal(draftInput.dataset.skipNextChangeCommit, "true");
  assert.equal(persistentInput.dataset.skipNextBlurCommit, "true");
  assert.equal(persistentInput.dataset.skipNextChangeCommit, "true");
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

test("used acquired ortho projection refreshes its inactivity timer", () => {
  const now = performance.now();
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
      { key: "lower", label: "lower", point: { x: 20, y: 0 }, acquiredAt: now - 2500 }
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
  const previousTimestamp = state.acquiredSnapPoints[0].acquiredAt;

  const hit = getDynamicSketchSnapHit(state, { x: 200, y: 100 }, highlightedTarget);

  assert.equal(hit.target.point.x, 20);
  assert.equal(hit.target.point.y, 10);
  assert.ok(state.acquiredSnapPoints[0].acquiredAt > previousTimestamp);
  assert.equal(pruneExpiredAcquiredSnapPoints(state, state.acquiredSnapPoints[0].acquiredAt + 2500), false);
  assert.deepEqual(state.acquiredSnapPoints.map(point => point.key), ["lower"]);
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

test("empty persistent dimension edit reverts instead of committing zero", () => {
  assert.deepEqual(getPersistentDimensionCommitValue("", 20), { shouldCommit: false, value: 20 });
  assert.deepEqual(getPersistentDimensionCommitValue("0", 20), { shouldCommit: false, value: 20 });
  assert.deepEqual(getPersistentDimensionCommitValue("12.5", 20), { shouldCommit: true, value: 12.5 });
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

test("dimension tool entry clears existing canvas selection", () => {
  const state = {
    selectedKeys: new Set(["edge-a", "edge-b"]),
    activeSelectionKey: "edge-b"
  };

  const changed = prepareSelectionForToolEntry(state, "dimension", "select");

  assert.equal(changed, true);
  assert.equal(state.selectedKeys.size, 0);
  assert.equal(state.activeSelectionKey, null);
});

test("dimension tool entry does not clear when already dimensioning", () => {
  const state = {
    selectedKeys: new Set(["edge-a"]),
    activeSelectionKey: "edge-a"
  };

  const changed = prepareSelectionForToolEntry(state, "dimension", "dimension");

  assert.equal(changed, false);
  assert.deepEqual(Array.from(state.selectedKeys), ["edge-a"]);
  assert.equal(state.activeSelectionKey, "edge-a");
});

test("right mouse dimension release backs out the latest picked reference", () => {
  const state = {
    dimensionDraft: {
      selectionKeys: ["edge", "marker|point|mid|2|3"],
      complete: true,
      radialDiameter: false,
      anchorPoint: { x: 4, y: 5 }
    },
    dimensionInputs: new Map()
  };

  const changed = releaseDimensionDraftSelection(state);

  assert.equal(changed, true);
  assert.deepEqual(state.dimensionDraft.selectionKeys, ["edge"]);
  assert.equal(state.dimensionDraft.complete, false);
  assert.equal(state.dimensionDraft.anchorPoint, null);
});

test("dimension tool can extend a selected line to an angle dimension", () => {
  const state = createHitTestState([
    {
      id: "horizontal",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    },
    {
      id: "vertical",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 0, y: 10 }]
    }
  ]);

  assert.equal(canExtendDimensionSelection(state, ["horizontal"], "vertical"), true);
});

test("dimension selection promotes line midpoint snap targets to line references", () => {
  const lineState = createHitTestState([
    {
      id: "base",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    }
  ]);
  const lineMidpointTarget = findNearestTarget(lineState, { x: 50, y: 100 });

  assert.equal(lineMidpointTarget.kind, "point");
  assert.equal(lineMidpointTarget.label, "mid");
  assert.equal(getDimensionSelectionKey(lineMidpointTarget), "base");
  assert.equal(isDimensionReferenceSetComplete(lineState, ["base"], lineMidpointTarget), true);

  const polylineState = createHitTestState([
    {
      id: "path",
      kind: "polyline",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }, { x: 10, y: 5 }]
    }
  ]);
  const segmentMidpointTarget = findNearestTarget(polylineState, { x: 50, y: 100 });

  assert.equal(segmentMidpointTarget.kind, "point");
  assert.equal(segmentMidpointTarget.label, "mid-0");
  assert.equal(getDimensionSelectionKey(segmentMidpointTarget), "path|segment|0");
  assert.equal(isDimensionReferenceSetComplete(polylineState, ["path|segment|0"], segmentMidpointTarget), true);
});

test("dimension tool can extend a selected line to a point-line dimension", () => {
  const state = createHitTestState([
    {
      id: "edge",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    },
    {
      id: "marker",
      kind: "point",
      points: [{ x: 3, y: 4 }]
    }
  ]);

  assert.equal(canExtendDimensionSelection(state, ["edge"], "marker"), true);
});

test("dimension tool does not extend a selected circle with unrelated geometry", () => {
  const state = createHitTestState([
    {
      id: "hole",
      kind: "circle",
      center: { x: 0, y: 0 },
      radius: 3
    },
    {
      id: "edge",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    }
  ]);

  assert.equal(canExtendDimensionSelection(state, ["hole"], "edge"), false);
});

test("radial dimension preference defaults circles to diameter and arcs to radius", () => {
  assert.equal(getRadialDimensionPreference({ entity: { kind: "circle" } }, {}), true);
  assert.equal(getRadialDimensionPreference({ entity: { kind: "arc" } }, {}), false);
  assert.equal(getRadialDimensionPreference({ entity: { kind: "arc" } }, { shiftKey: true }), true);
});

test("dimension display text prefixes diameter and radius dimensions", () => {
  assert.equal(getDimensionDisplayText({ kind: "diameter", value: 6 }), "\u23006");
  assert.equal(getDimensionDisplayText({ kind: "radius", value: 3 }), "R3");
  assert.equal(getDimensionDisplayText({ kind: "angle", value: 72.077 }), "72.077");
});

test("angle dimension graphics use the line vertex and anchor-selected sweep", () => {
  const state = {
    view: {
      scale: 10,
      offsetX: 0,
      offsetY: 100
    }
  };
  const geometry = getAngleDimensionScreenGeometry(
    state,
    { start: { x: 0, y: 0 }, end: { x: 10, y: 0 } },
    { start: { x: 0, y: 0 }, end: { x: 0, y: 10 } },
    { x: 0, y: 0 },
    { x: 3, y: 3 });

  assert.deepEqual(geometry.vertex, { x: 0, y: 100 });
  assertApproxEqual(geometry.radius, Math.hypot(30, 30));
  assert.equal(geometry.extensionSegments.length, 2);
  assert.equal(geometry.arrows.length, 2);
  assert.ok(geometry.sweep.start < geometry.sweep.end);
});

test("diameter dimension uses an outside leader with a text gap", () => {
  const geometry = getRadialDimensionScreenGeometry(
    { x: 100, y: 100 },
    30,
    { x: 200, y: 100 },
    true
  );

  assert.deepEqual(geometry.segments, [
    { start: { x: 130, y: 100 }, end: { x: 188, y: 100 } }
  ]);
  assert.deepEqual(geometry.arrows, [
    { point: { x: 130, y: 100 }, toward: { x: 100, y: 100 } }
  ]);
});

test("radial dimension arrow flips when the text anchor is inside", () => {
  const geometry = getRadialDimensionScreenGeometry(
    { x: 100, y: 100 },
    30,
    { x: 115, y: 100 },
    false
  );

  assert.deepEqual(geometry.arrows, [
    { point: { x: 130, y: 100 }, toward: { x: 131, y: 100 } }
  ]);
});

test("arc radial dimension leader starts at the arc edge instead of crossing the center", () => {
  const geometry = getRadialDimensionScreenGeometry(
    { x: 100, y: 100 },
    30,
    { x: 105, y: 80 },
    false,
    0,
    { x: 100, y: 70 }
  );

  assert.deepEqual(geometry.arrows, [
    { point: { x: 100, y: 70 }, toward: { x: 100, y: 100 } }
  ]);
  assert.equal(geometry.segments.length, 1);
  assert.deepEqual(geometry.segments[0].start, { x: 100, y: 70 });
  assert.notDeepEqual(geometry.segments[0].start, { x: 100, y: 100 });
  assert.notDeepEqual(geometry.segments[0].end, { x: 100, y: 100 });
});

test("arc angle dimension extension lines start at arc endpoints", () => {
  const state = {
    view: {
      scale: 10,
      offsetX: 0,
      offsetY: 100
    }
  };

  const geometry = getArcAngleDimensionScreenGeometry(
    state,
    { x: 0, y: 0 },
    5,
    0,
    90,
    { x: 7, y: 7 });

  assert.deepEqual(geometry.vertex, { x: 0, y: 100 });
  assert.deepEqual(geometry.extensionSegments[0].start, { x: 50, y: 100 });
  assertApproxPoint(geometry.extensionSegments[1].start, { x: 0, y: 50 });
  assert.notDeepEqual(geometry.extensionSegments[0].start, geometry.vertex);
  assert.notDeepEqual(geometry.extensionSegments[1].start, geometry.vertex);
  assert.equal(geometry.arrows.length, 2);
});

test("acquired snap points expire after a short inactivity window", () => {
  const state = {
    acquiredSnapPoints: [
      { key: "old", label: "old", point: { x: 0, y: 0 }, acquiredAt: 0 },
      { key: "fresh", label: "fresh", point: { x: 1, y: 1 }, acquiredAt: 2500 }
    ]
  };

  const changed = pruneExpiredAcquiredSnapPoints(state, 4001);

  assert.equal(changed, true);
  assert.deepEqual(state.acquiredSnapPoints.map(point => point.key), ["fresh"]);
});

test("linear dimension graphics leave a text gap and extend past the dimension line", () => {
  const geometry = getLinearDimensionScreenGeometry(
    { x: 0, y: 20 },
    { x: 100, y: 20 },
    { x: 50, y: 0 },
    24);

  assert.equal(geometry.dimensionSegments.length, 2);
  assert.deepEqual(geometry.dimensionSegments[0], { start: { x: 0, y: 0 }, end: { x: 33, y: 0 } });
  assert.deepEqual(geometry.dimensionSegments[1], { start: { x: 67, y: 0 }, end: { x: 100, y: 0 } });
  assert.deepEqual(geometry.extensionSegments[0].end, { x: 0, y: -6 });
  assert.deepEqual(geometry.extensionSegments[1].end, { x: 100, y: -6 });
  assert.deepEqual(geometry.arrows[0], { point: { x: 0, y: 0 }, toward: { x: -1, y: 0 } });
  assert.deepEqual(geometry.arrows[1], { point: { x: 100, y: 0 }, toward: { x: 101, y: 0 } });
});

test("linear dimension graphics center the gap on the dragged text anchor", () => {
  const geometry = getLinearDimensionScreenGeometry(
    { x: 0, y: 20 },
    { x: 100, y: 20 },
    { x: 70, y: 0 },
    24);

  assert.deepEqual(geometry.dimensionSegments[0], { start: { x: 0, y: 0 }, end: { x: 53, y: 0 } });
  assert.deepEqual(geometry.dimensionSegments[1], { start: { x: 87, y: 0 }, end: { x: 100, y: 0 } });
});

test("linear dimension arrows flip outside when text cannot fit inside", () => {
  const geometry = getLinearDimensionScreenGeometry(
    { x: 0, y: 20 },
    { x: 24, y: 20 },
    { x: 12, y: 0 },
    28);

  assert.deepEqual(geometry.arrows[0], { point: { x: 0, y: 0 }, toward: { x: 1, y: 0 } });
  assert.deepEqual(geometry.arrows[1], { point: { x: 24, y: 0 }, toward: { x: 23, y: 0 } });
});

test("persistent dimension style is visually distinct from normal geometry", () => {
  const geometryStroke = "#94a3b8";
  const style = getDimensionRenderStyle(false);
  const previewStyle = getDimensionRenderStyle(true);

  assert.notEqual(style.strokeStyle.toLowerCase(), geometryStroke);
  assert.notEqual(style.strokeStyle, previewStyle.strokeStyle);
  assert.equal(Math.abs(getHexBrightness(style.strokeStyle) - getHexBrightness(geometryStroke)) >= 30, true);
});

test("selected persistent dimension style paints the full dimension differently", () => {
  const normalStyle = getDimensionRenderStyle(false);
  const selectedStyle = getDimensionRenderStyle(false, { selected: true });

  assert.notEqual(selectedStyle.strokeStyle, normalStyle.strokeStyle);
  assert.notEqual(selectedStyle.textStyle, normalStyle.textStyle);
  assert.equal(selectedStyle.lineWidth > normalStyle.lineWidth, true);
  assert.equal(selectedStyle.glow, true);
});

test("unsatisfied persistent dimension style paints red", () => {
  const normalStyle = getDimensionRenderStyle(false);
  const unsatisfiedStyle = getDimensionRenderStyle(false, { unsatisfied: true });

  assert.equal(unsatisfiedStyle.strokeStyle, "#f87171");
  assert.equal(unsatisfiedStyle.textStyle, "#fecaca");
  assert.notEqual(unsatisfiedStyle.strokeStyle, normalStyle.strokeStyle);
});

test("persistent dimension descriptors carry solver state", () => {
  const state = {
    document: {
      entities: [
        { id: "edge", kind: "line", points: [{ x: 0, y: 0 }, { x: 4, y: 0 }] }
      ],
      dimensions: [
        {
          id: "distance",
          kind: "LinearDistance",
          referenceKeys: ["edge:start", "edge:end"],
          value: 10,
          state: "Unsatisfied",
          affectedReferenceKeys: ["edge:start", "edge:end"]
        }
      ]
    },
    view: {
      scale: 10,
      offsetX: 0,
      offsetY: 0
    },
    dimensionAnchorOverrides: new Map()
  };

  const descriptors = getPersistentDimensionDescriptors(state);

  assert.equal(descriptors.length, 1);
  assert.equal(descriptors[0].state, "unsatisfied");
  assert.deepEqual(descriptors[0].affectedReferenceKeys, ["edge:start", "edge:end"]);
  assert.equal(getSketchDimensionState(state.document.dimensions[0]), "unsatisfied");
});

test("unsatisfied sketch diagnostics identify affected entity ids", () => {
  const document = {
    entities: [
      { id: "edge", kind: "line", points: [{ x: 0, y: 0 }, { x: 4, y: 0 }] },
      { id: "other", kind: "line", points: [{ x: 0, y: 5 }, { x: 4, y: 5 }] }
    ],
    dimensions: [
      {
        id: "distance",
        kind: "LinearDistance",
        state: "Unsatisfied",
        affectedReferenceKeys: ["edge:start", "edge:end"]
      }
    ],
    constraints: [
      {
        id: "parallel",
        kind: "Parallel",
        state: "Unsatisfied",
        affectedReferenceKeys: ["edge", "other"]
      }
    ]
  };

  assert.deepEqual(getSketchDiagnosticEntityIds(document), new Set(["edge", "other"]));
});

test("dimension target selection uses full-dimension painting instead of a text rectangle", () => {
  assert.equal(shouldDrawSelectionTargetOverlay({ kind: "dimension" }), false);
  assert.equal(shouldDrawSelectionTargetOverlay({ kind: "constraint" }), false);
  assert.equal(shouldDrawSelectionTargetOverlay({ kind: "entity" }), true);
});

test("persistent dimension input classes mark selected text without edit chrome", () => {
  const className = getDimensionInputClassName({
    persistent: true,
    selected: true,
    active: true,
    editing: false
  });

  assert.equal(className.includes("drawing-persistent-dimension-input-selected"), true);
  assert.equal(className.includes("drawing-persistent-dimension-input-active"), true);
  assert.equal(className.includes("drawing-dimension-input-active"), false);
});

test("persistent dimension input classes mark unsatisfied dimensions", () => {
  const className = getDimensionInputClassName({
    persistent: true,
    unsatisfied: true
  });

  assert.equal(className.includes("drawing-persistent-dimension-input-unsatisfied"), true);
});

test("new persistent dimensions are detected for immediate edit focus", () => {
  const pendingId = getPendingPersistentDimensionEditId(
    [
      { id: "existing-dim" },
      { id: "new-dim" }
    ],
    new Set(["existing-dim"]));

  assert.equal(pendingId, "new-dim");
  assert.equal(getPendingPersistentDimensionEditId([{ id: "existing-dim" }], new Set(["existing-dim"])), null);
});

test("persistent dimension commit accepts displayed prefixes", () => {
  assert.deepEqual(getPersistentDimensionCommitValue("R3.5", 1), {
    shouldCommit: true,
    value: 3.5
  });
  assert.deepEqual(getPersistentDimensionCommitValue("\u23006", 1), {
    shouldCommit: true,
    value: 6
  });
});

test("dimension anchor drag request converts screen point to world anchor", () => {
  const state = {
    view: {
      scale: 2,
      offsetX: 10,
      offsetY: 30
    }
  };

  const request = getDimensionAnchorUpdateRequest(state, "dim-a", { x: 14, y: 22 });

  assert.deepEqual(request, {
    dimensionId: "dim-a",
    anchor: { x: 2, y: 4 }
  });
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

test("persistent dimension descriptors resolve parallel line distance references", () => {
  const state = {
    document: {
      entities: [
        {
          id: "base",
          kind: "line",
          points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
        },
        {
          id: "offset",
          kind: "line",
          points: [{ x: 2, y: 4 }, { x: 8, y: 4 }]
        }
      ],
      dimensions: [
        {
          id: "dim-offset",
          kind: "PointToLineDistance",
          referenceKeys: ["base", "offset"],
          value: 4,
          isDriving: true
        }
      ]
    },
    view: {
      scale: 10,
      offsetX: 0,
      offsetY: 100
    }
  };

  const descriptors = getPersistentDimensionDescriptors(state);

  assert.equal(descriptors.length, 1);
  assert.equal(descriptors[0].id, "dim-offset");
  assert.equal(descriptors[0].geometry.type, "linear");
  assert.deepEqual(descriptors[0].geometry.start, { x: 5, y: 4 });
  assert.deepEqual(descriptors[0].geometry.end, { x: 5, y: 0 });
  assert.equal(descriptors[0].value, 4);
});

test("persistent dimension descriptors resolve arc angle references to sweep geometry", () => {
  const state = {
    document: {
      entities: [
        {
          id: "arc-a",
          kind: "arc",
          center: { x: 0, y: 0 },
          radius: 2,
          startAngleDegrees: 0,
          endAngleDegrees: 120
        }
      ],
      dimensions: [
        {
          id: "sweep-a",
          kind: "Angle",
          referenceKeys: ["arc-a"],
          value: 120,
          anchor: { x: 1, y: 1 },
          isDriving: true
        }
      ]
    },
    dimensionAnchorOverrides: new Map(),
    view: {
      scale: 10,
      offsetX: 100,
      offsetY: 100
    }
  };

  const descriptors = getPersistentDimensionDescriptors(state);

  assert.equal(descriptors.length, 1);
  assert.equal(descriptors[0].geometry.type, "arcangle");
  assert.deepEqual(descriptors[0].geometry.center, { x: 0, y: 0 });
  assert.equal(descriptors[0].geometry.radius, 2);
  assert.equal(descriptors[0].geometry.startAngleDegrees, 0);
  assert.equal(descriptors[0].geometry.endAngleDegrees, 120);
});

test("persistent dimension descriptors resolve polyline segment endpoints", () => {
  const state = {
    document: {
      entities: [
        {
          id: "poly-a",
          kind: "polyline",
          points: [{ x: 0, y: 0 }, { x: 3, y: 0 }, { x: 3, y: 4 }]
        }
      ],
      dimensions: [
        {
          id: "dim-segment",
          kind: "LinearDistance",
          referenceKeys: ["poly-a|segment|1:start", "poly-a|segment|1:end"],
          value: 4,
          anchor: { x: 4, y: 2 }
        }
      ]
    },
    dimensionAnchorOverrides: new Map(),
    view: {
      scale: 10,
      offsetX: 0,
      offsetY: 100
    }
  };

  const descriptors = getPersistentDimensionDescriptors(state);

  assert.equal(descriptors.length, 1);
  assert.deepEqual(descriptors[0].geometry.start, { x: 3, y: 0 });
  assert.deepEqual(descriptors[0].geometry.end, { x: 3, y: 4 });
});

test("persistent dimensions are selectable canvas targets", () => {
  const state = {
    activeTool: "select",
    document: {
      entities: [
        {
          id: "edge",
          kind: "line",
          points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
        }
      ],
      dimensions: [
        {
          id: "dim-edge",
          kind: "LinearDistance",
          referenceKeys: ["edge:start", "edge:end"],
          value: 10,
          anchor: { x: 5, y: 2 }
        }
      ]
    },
    dimensionAnchorOverrides: new Map(),
    acquiredSnapPoints: [],
    view: {
      scale: 10,
      offsetX: 0,
      offsetY: 100
    }
  };

  const target = findNearestTarget(state, { x: 50, y: 80 });

  assert.equal(target.kind, "dimension");
  assert.equal(target.key, "persistent-dim-edge");
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

test("constraint glyphs use symbolic CAD relation icons instead of letters", () => {
  assert.equal(getConstraintGlyphIcon("coincident"), "coincident");
  assert.equal(getConstraintGlyphIcon("horizontal"), "horizontal");
  assert.equal(getConstraintGlyphIcon("perpendicular"), "perpendicular");
  assert.equal(getConstraintGlyphIcon("tangent"), "tangent");
  assert.equal(getConstraintGlyphIcon("fix"), "fix");
  assert.equal(getConstraintGlyphIcon("unknown"), "");
});

test("constraint glyph groups hide until hover when show all is off", () => {
  const state = createConstraintGlyphState(false);

  assert.equal(getVisibleConstraintGlyphGroups(state).length, 0);

  state.hoveredTarget = {
    kind: "entity",
    key: "edge-a",
    entityId: "edge-a"
  };

  const groups = getVisibleConstraintGlyphGroups(state);

  assert.equal(groups.length, 1);
  assert.equal(groups[0].constraints.length, 2);
  assert.deepEqual(groups[0].constraints.map(constraint => constraint.id), ["fix-a", "horizontal-a"]);
});

test("constraint glyph groups show all constraints and keep dragged screen offsets", () => {
  const state = createConstraintGlyphState(true);
  state.constraintGroupOffsets.set("constraint-group:edge-a", { x: 24, y: -12 });

  const groups = getConstraintGlyphGroups(state);
  const visibleGroups = getVisibleConstraintGlyphGroups(state);
  const movedGroup = visibleGroups.find(group => group.key === "constraint-group:edge-a");

  assert.equal(groups.length, 2);
  assert.equal(visibleGroups.length, 2);
  assert.ok(movedGroup);
  assert.deepEqual(movedGroup.anchorScreenPoint, { x: 60, y: 90 });
  assert.deepEqual(movedGroup.point, { x: 84, y: 78 });
});

test("dragged constraint glyph groups expose leader geometry to the reference anchor", () => {
  const state = createConstraintGlyphState(true);
  state.constraintGroupOffsets.set("constraint-group:edge-a", { x: 40, y: -30 });

  const group = getConstraintGlyphGroups(state)
    .find(candidate => candidate.key === "constraint-group:edge-a");
  const leader = getConstraintGlyphLeader(group);

  assert.ok(leader);
  assert.deepEqual(leader.start, { x: 60, y: 90 });
  assert.ok(leader.end.x > leader.start.x);
  assert.ok(leader.end.y < leader.start.y);
});

test("default constraint glyph groups do not cover their anchor point", () => {
  const state = createConstraintGlyphState(true);
  const group = getConstraintGlyphGroups(state)
    .find(candidate => candidate.key === "constraint-group:edge-a");

  assert.ok(group);
  assert.equal(
    group.anchorScreenPoint.x >= group.rect.minX
      && group.anchorScreenPoint.x <= group.rect.maxX
      && group.anchorScreenPoint.y >= group.rect.minY
      && group.anchorScreenPoint.y <= group.rect.maxY,
    false);
});

test("default-position constraint glyph groups do not expose leader geometry", () => {
  const state = createConstraintGlyphState(true);
  const group = getConstraintGlyphGroups(state)
    .find(candidate => candidate.key === "constraint-group:edge-a");

  assert.equal(getConstraintGlyphLeader(group), null);
});

test("visible hover constraint glyphs are selectable when show all is off", () => {
  const state = createConstraintGlyphState(false);
  state.hoveredTarget = {
    kind: "entity",
    key: "edge-a",
    entityId: "edge-a"
  };

  const group = getVisibleConstraintGlyphGroups(state)
    .find(candidate => candidate.key === "constraint-group:edge-a");
  const hit = getConstraintGlyphGroupHit(state, {
    x: group.rect.minX + 8,
    y: group.point.y
  });

  assert.ok(hit);
  assert.equal(hit.target.kind, "constraint");
  assert.equal(hit.target.key, "constraint:fix-a");
});

test("hovered constraint targets keep their glyph group visible", () => {
  const state = createConstraintGlyphState(false);
  state.hoveredTarget = {
    kind: "constraint",
    key: "constraint:fix-a",
    constraintId: "fix-a"
  };

  const groups = getVisibleConstraintGlyphGroups(state);

  assert.equal(groups.length, 1);
  assert.equal(groups[0].key, "constraint-group:edge-a");
});

test("changing constraint visibility clears dragged glyph offsets", () => {
  const state = createConstraintGlyphState(true);
  state.constraintGroupOffsets.set("constraint-group:edge-a", { x: 24, y: -12 });
  state.constraintGroupDrag = { groupKey: "constraint-group:edge-a" };

  const changed = applyConstraintVisibilityState(state, false);

  assert.equal(changed, true);
  assert.equal(state.showAllConstraints, false);
  assert.equal(state.constraintGroupOffsets.size, 0);
  assert.equal(state.constraintGroupDrag, null);
});

test("constraint tool hover filtering matches eligible reference types", () => {
  const lineTarget = { kind: "entity", entity: { kind: "line" }, key: "edge" };
  const circleTarget = { kind: "entity", entity: { kind: "circle" }, key: "circle" };
  const arcTarget = { kind: "entity", entity: { kind: "arc" }, key: "arc" };
  const pointTarget = { kind: "point", entity: { kind: "line" }, label: "start", key: "edge|point|start|0|0" };
  const midpointTarget = { kind: "point", entity: { kind: "line" }, label: "mid", key: "edge|point|mid|5|0" };

  assert.equal(isTargetEligibleForConstraintTool("tangent", lineTarget), true);
  assert.equal(isTargetEligibleForConstraintTool("tangent", circleTarget), true);
  assert.equal(isTargetEligibleForConstraintTool("tangent", pointTarget), false);
  assert.equal(isTargetEligibleForConstraintTool("concentric", circleTarget), true);
  assert.equal(isTargetEligibleForConstraintTool("concentric", arcTarget), true);
  assert.equal(isTargetEligibleForConstraintTool("concentric", lineTarget), false);
  assert.equal(isTargetEligibleForConstraintTool("coincident", pointTarget), true);
  assert.equal(isTargetEligibleForConstraintTool("coincident", midpointTarget), false);
  assert.equal(isTargetEligibleForConstraintTool("horizontal", lineTarget), true);
  assert.equal(isTargetEligibleForConstraintTool("horizontal", pointTarget), true);
  assert.equal(isTargetEligibleForConstraintTool("horizontal", circleTarget), false);
});

test("point target marker uses a dedicated tangent snap symbol", () => {
  assert.equal(getPointTargetMarker({ label: "tangent-hole-0" }), "tangent");
  assert.equal(getPointTargetMarker({ label: "circle-tangent-edge-0" }), "tangent");
  assert.equal(getPointTargetMarker({ label: "mid" }), "midpoint");
  assert.equal(getPointTargetMarker({ label: "quadrant-0" }), "point");
});

test("geometry drag preview moves one line endpoint", () => {
  const document = {
    entities: [
      {
        id: "edge",
        kind: "line",
        points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
      }
    ],
    dimensions: [],
    constraints: []
  };

  const preview = applyGeometryDragPreview(
    document,
    "edge|point|start|0|0",
    { x: 0, y: 0 },
    { x: 2, y: 3 });

  assert.deepEqual(preview.entities[0].points, [{ x: 2, y: 3 }, { x: 10, y: 0 }]);
  assert.deepEqual(document.entities[0].points, [{ x: 0, y: 0 }, { x: 10, y: 0 }]);
});

test("geometry drag preview keeps coincident endpoints welded", () => {
  const document = {
    entities: [
      {
        id: "base",
        kind: "line",
        points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
      },
      {
        id: "upright",
        kind: "line",
        points: [{ x: 10, y: 0 }, { x: 10, y: 5 }]
      }
    ],
    dimensions: [],
    constraints: [
      {
        id: "join",
        kind: "coincident",
        referenceKeys: ["base:end", "upright:start"],
        state: "satisfied"
      }
    ]
  };

  const preview = applyGeometryDragPreview(
    document,
    "base|point|end|10|0",
    { x: 10, y: 0 },
    { x: 12, y: 3 });

  assert.deepEqual(preview.entities[0].points, [{ x: 0, y: 0 }, { x: 12, y: 3 }]);
  assert.deepEqual(preview.entities[1].points, [{ x: 12, y: 3 }, { x: 10, y: 5 }]);
});

test("finishing geometry drag keeps the final preview until server confirmation", () => {
  const originalDocument = {
    entities: [{
      id: "edge",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    }],
    dimensions: [],
    constraints: []
  };
  const previewDocument = applyGeometryDragPreview(
    originalDocument,
    "edge|point|end|10|0",
    { x: 10, y: 0 },
    { x: 14, y: 0 });
  const calls = [];
  const state = {
    activeTool: "select",
    acquiredSnapPoints: [],
    document: previewDocument,
    hoveredTarget: null,
    selectedKeys: new Set(),
    activeSelectionKey: null,
    geometryDrag: {
      pointerId: 1,
      targetKey: "edge|point|end|10|0",
      startScreenPoint: { x: 10, y: 0 },
      currentScreenPoint: { x: 20, y: 0 },
      startWorldPoint: { x: 10, y: 0 },
      currentWorldPoint: { x: 14, y: 0 },
      originalDocument,
      constrainToCurrentVector: false,
      moved: true
    },
    view: {
      scale: 1,
      offsetX: 0,
      offsetY: 0
    },
    canvas: {
      dataset: {},
      getBoundingClientRect: () => ({ left: 0, top: 0, width: 800, height: 600 })
    },
    dotnetRef: {
      invokeMethodAsync: (...args) => {
        calls.push(args);
        return Promise.resolve();
      }
    }
  };

  assert.equal(finishGeometryDrag(state, { x: 20, y: 0 }, { pointerId: 1 }), true);
  assert.notEqual(state.document, originalDocument);
  assert.deepEqual(state.document.entities[0].points, [{ x: 0, y: 0 }, { x: 20, y: 0 }]);
  assert.equal(state.geometryDrag, null);
  assert.equal(calls.some(call => call[0] === "OnGeometryDragRequested"), true);
});

test("geometry drag preview translates a line midpoint", () => {
  const document = {
    entities: [
      {
        id: "edge",
        kind: "line",
        points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
      }
    ],
    dimensions: [],
    constraints: []
  };

  const preview = applyGeometryDragPreview(
    document,
    "edge|point|mid|5|0",
    { x: 5, y: 0 },
    { x: 7, y: 3 });

  assert.deepEqual(preview.entities[0].points, [{ x: 2, y: 3 }, { x: 12, y: 3 }]);
});

test("geometry drag preview translates dimension anchors for moved geometry", () => {
  const document = {
    entities: [
      {
        id: "edge",
        kind: "line",
        points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
      }
    ],
    dimensions: [
      {
        id: "dim-edge",
        kind: "lineardistance",
        referenceKeys: ["edge:start", "edge:end"],
        value: 10,
        anchor: { x: 5, y: 2 }
      }
    ],
    constraints: []
  };

  const preview = applyGeometryDragPreview(
    document,
    "edge|point|mid|5|0",
    { x: 5, y: 0 },
    { x: 7, y: 3 });

  assert.deepEqual(preview.dimensions[0].anchor, { x: 7, y: 5 });
  assert.deepEqual(document.dimensions[0].anchor, { x: 5, y: 2 });
});

test("geometry drag preview translates a dimensioned line when endpoint edit would break length", () => {
  const document = {
    entities: [
      {
        id: "edge",
        kind: "line",
        points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
      }
    ],
    dimensions: [
      {
        id: "dim-edge",
        kind: "lineardistance",
        referenceKeys: ["edge:start", "edge:end"],
        value: 10,
        anchor: { x: 5, y: 2 },
        isDriving: true
      }
    ],
    constraints: []
  };

  const preview = applyGeometryDragPreview(
    document,
    "edge|point|end|10|0",
    { x: 10, y: 0 },
    { x: 14, y: 0 });

  assert.deepEqual(preview.entities[0].points, [{ x: 4, y: 0 }, { x: 14, y: 0 }]);
  assert.deepEqual(preview.dimensions[0].anchor, { x: 9, y: 2 });
  assert.deepEqual(document.entities[0].points, [{ x: 0, y: 0 }, { x: 10, y: 0 }]);
});

test("geometry drag preview resizes an undimensioned rectangle edge using only perpendicular motion", () => {
  const document = createUndimensionedRectanglePreviewDocument();

  const preview = applyGeometryDragPreview(
    document,
    "rect-4|point|mid|0|2.5",
    { x: 0, y: 2.5 },
    { x: 2, y: 1.5 });

  assert.deepEqual(preview.entities.map(entity => entity.points), [
    [{ x: 2, y: 0 }, { x: 10, y: 0 }],
    [{ x: 10, y: 0 }, { x: 10, y: 5 }],
    [{ x: 10, y: 5 }, { x: 2, y: 5 }],
    [{ x: 2, y: 5 }, { x: 2, y: 0 }]
  ]);
});

test("geometry drag preview resizes an undimensioned rectangle corner as a rectangle", () => {
  const document = createUndimensionedRectanglePreviewDocument();

  const preview = applyGeometryDragPreview(
    document,
    "rect-1|point|start|0|0",
    { x: 0, y: 0 },
    { x: 2, y: -1 });

  assert.deepEqual(preview.entities.map(entity => entity.points), [
    [{ x: 2, y: -1 }, { x: 10, y: -1 }],
    [{ x: 10, y: -1 }, { x: 10, y: 5 }],
    [{ x: 10, y: 5 }, { x: 2, y: 5 }],
    [{ x: 2, y: 5 }, { x: 2, y: -1 }]
  ]);
});

test("geometry drag preview translates dimensioned rectangle edge as a rigid rectangle", () => {
  const document = createDimensionedRectanglePreviewDocument();

  const preview = applyGeometryDragPreview(
    document,
    "rect-3",
    { x: 5, y: 5 },
    { x: 7, y: 8 });

  assert.deepEqual(preview.entities.map(entity => entity.points), [
    [{ x: 2, y: 3 }, { x: 12, y: 3 }],
    [{ x: 12, y: 3 }, { x: 12, y: 8 }],
    [{ x: 12, y: 8 }, { x: 2, y: 8 }],
    [{ x: 2, y: 8 }, { x: 2, y: 3 }]
  ]);
  assert.deepEqual(preview.dimensions.map(dimension => dimension.anchor), [
    { x: 7, y: 4.2 },
    { x: 11.4, y: 5.5 }
  ]);
});

test("geometry drag preview translates dimensioned rectangle vertex as a rigid rectangle", () => {
  const document = createDimensionedRectanglePreviewDocument();

  const preview = applyGeometryDragPreview(
    document,
    "rect-2|point|end|10|5",
    { x: 10, y: 5 },
    { x: 12, y: 8 });

  assert.deepEqual(preview.entities.map(entity => entity.points), [
    [{ x: 2, y: 3 }, { x: 12, y: 3 }],
    [{ x: 12, y: 3 }, { x: 12, y: 8 }],
    [{ x: 12, y: 8 }, { x: 2, y: 8 }],
    [{ x: 2, y: 8 }, { x: 2, y: 3 }]
  ]);
  assert.deepEqual(preview.dimensions.map(dimension => dimension.anchor), [
    { x: 7, y: 4.2 },
    { x: 11.4, y: 5.5 }
  ]);
});

test("geometry drag preview keeps partially dimensioned rectangle coherent in free direction", () => {
  const document = createPartiallyDimensionedRectanglePreviewDocument();

  const preview = applyGeometryDragPreview(
    document,
    "rect-3",
    { x: 5, y: 5 },
    { x: 2, y: 8 });

  assert.deepEqual(preview.entities.map(entity => entity.points), [
    [{ x: -3, y: 0 }, { x: 7, y: 0 }],
    [{ x: 7, y: 0 }, { x: 7, y: 8 }],
    [{ x: 7, y: 8 }, { x: -3, y: 8 }],
    [{ x: -3, y: 8 }, { x: -3, y: 0 }]
  ]);
  assert.deepEqual(preview.dimensions.map(dimension => dimension.anchor), [
    { x: 2, y: 1.2 }
  ]);
});

test("geometry drag preview translates partially dimensioned rectangle in constrained direction", () => {
  const document = createPartiallyDimensionedRectanglePreviewDocument();

  const preview = applyGeometryDragPreview(
    document,
    "rect-4|point|mid|0|2.5",
    { x: 0, y: 2.5 },
    { x: -3, y: 2.5 });

  assert.deepEqual(preview.entities.map(entity => entity.points), [
    [{ x: -3, y: 0 }, { x: 7, y: 0 }],
    [{ x: 7, y: 0 }, { x: 7, y: 5 }],
    [{ x: 7, y: 5 }, { x: -3, y: 5 }],
    [{ x: -3, y: 5 }, { x: -3, y: 0 }]
  ]);
  assert.deepEqual(preview.dimensions.map(dimension => dimension.anchor), [
    { x: 2, y: 1.2 }
  ]);
});

test("geometry drag preview decomposes partially dimensioned rectangle vertex drag", () => {
  const document = createPartiallyDimensionedRectanglePreviewDocument();

  const preview = applyGeometryDragPreview(
    document,
    "rect-3|point|end|0|5",
    { x: 0, y: 5 },
    { x: -3, y: 8 });

  assert.deepEqual(preview.entities.map(entity => entity.points), [
    [{ x: -3, y: 0 }, { x: 7, y: 0 }],
    [{ x: 7, y: 0 }, { x: 7, y: 8 }],
    [{ x: 7, y: 8 }, { x: -3, y: 8 }],
    [{ x: -3, y: 8 }, { x: -3, y: 0 }]
  ]);
  assert.deepEqual(preview.dimensions.map(dimension => dimension.anchor), [
    { x: 2, y: 1.2 }
  ]);
});

test("geometry drag preview updates ellipse major and minor handles", () => {
  const document = {
    entities: [
      {
        id: "ellipse",
        kind: "ellipse",
        center: { x: 0, y: 0 },
        majorAxisEndPoint: { x: 4, y: 0 },
        minorRadiusRatio: 0.5,
        startAngleDegrees: 0,
        endAngleDegrees: 360
      }
    ],
    dimensions: [],
    constraints: []
  };

  const majorPreview = applyGeometryDragPreview(
    document,
    "ellipse|point|quadrant-0|4|0",
    { x: 4, y: 0 },
    { x: 6, y: 0 });

  assert.deepEqual(majorPreview.entities[0].majorAxisEndPoint, { x: 6, y: 0 });
  assert.equal(majorPreview.entities[0].minorRadiusRatio, 0.5);

  const minorPreview = applyGeometryDragPreview(
    document,
    "ellipse|point|quadrant-90|0|2",
    { x: 0, y: 2 },
    { x: 0, y: 3 });

  assert.equal(minorPreview.entities[0].minorRadiusRatio, 0.75);
});

test("geometry drag preview translates whole ellipse and dimension anchors", () => {
  const document = {
    entities: [
      {
        id: "ellipse",
        kind: "ellipse",
        center: { x: 1, y: 2 },
        majorAxisEndPoint: { x: 4, y: 0 },
        minorRadiusRatio: 0.5,
        startAngleDegrees: 0,
        endAngleDegrees: 360
      }
    ],
    dimensions: [
      {
        id: "ellipse-major",
        kind: "lineardistance",
        referenceKeys: ["ellipse:start", "ellipse:end"],
        value: 8,
        anchor: { x: 1, y: 4 }
      }
    ],
    constraints: []
  };

  const preview = applyGeometryDragPreview(
    document,
    "ellipse",
    { x: 1, y: 2 },
    { x: 4, y: 6 });

  assert.deepEqual(preview.entities[0].center, { x: 4, y: 6 });
  assert.deepEqual(preview.entities[0].majorAxisEndPoint, { x: 4, y: 0 });
  assert.deepEqual(preview.dimensions[0].anchor, { x: 4, y: 8 });
});

test("geometry drag preview translates polygon center and dimension anchors", () => {
  const document = {
    entities: [
      {
        id: "polygon",
        kind: "polygon",
        center: { x: 0, y: 0 },
        radius: 5,
        rotationAngleDegrees: 30,
        sideCount: 6,
        points: [{ x: 5, y: 0 }, { x: 2.5, y: 4.33 }, { x: -2.5, y: 4.33 }]
      }
    ],
    dimensions: [
      {
        id: "polygon-radius",
        kind: "radius",
        referenceKeys: ["polygon"],
        value: 5,
        anchor: { x: 5, y: 0 }
      }
    ],
    constraints: []
  };

  const preview = applyGeometryDragPreview(
    document,
    "polygon|point|center|0|0",
    { x: 0, y: 0 },
    { x: 3, y: 4 });

  assert.deepEqual(preview.entities[0].center, { x: 3, y: 4 });
  assert.equal(preview.entities[0].radius, 5);
  assert.equal(preview.entities[0].rotationAngleDegrees, 30);
  assert.deepEqual(preview.dimensions[0].anchor, { x: 8, y: 4 });
});

test("geometry drag preview translates dimensioned polygon midpoint and dimension anchors", () => {
  const document = {
    entities: [
      {
        id: "polygon",
        kind: "polygon",
        center: { x: 0, y: 0 },
        radius: 5,
        rotationAngleDegrees: 30,
        sideCount: 6,
        points: [{ x: 5, y: 0 }, { x: 2.5, y: 4.33 }, { x: -2.5, y: 4.33 }]
      }
    ],
    dimensions: [
      {
        id: "polygon-radius",
        kind: "radius",
        referenceKeys: ["polygon"],
        value: 5,
        anchor: { x: 5, y: 0 }
      }
    ],
    constraints: []
  };

  const preview = applyGeometryDragPreview(
    document,
    "polygon|point|mid-0|3.75|2.165064",
    { x: 3.75, y: 2.165064 },
    { x: 6.75, y: 6.165064 });

  assert.deepEqual(preview.entities[0].center, { x: 3, y: 4 });
  assert.deepEqual(preview.entities[0].points[0], { x: 8, y: 4 });
  assert.equal(preview.entities[0].radius, 5);
  assert.equal(preview.entities[0].rotationAngleDegrees, 30);
  assert.deepEqual(preview.dimensions[0].anchor, { x: 8, y: 4 });
});

test("geometry drag preview translates dimensioned polygon from edge selection", () => {
  const document = {
    entities: [
      {
        id: "polygon",
        kind: "polygon",
        center: { x: 0, y: 0 },
        radius: 5,
        rotationAngleDegrees: 30,
        sideCount: 6,
        points: [{ x: 5, y: 0 }, { x: 2.5, y: 4.33 }, { x: -2.5, y: 4.33 }]
      }
    ],
    dimensions: [
      {
        id: "polygon-count",
        kind: "count",
        referenceKeys: ["polygon"],
        value: 6,
        anchor: { x: 0, y: -3 }
      },
      {
        id: "polygon-radius",
        kind: "radius",
        referenceKeys: ["polygon"],
        value: 5,
        anchor: { x: 5, y: 0 }
      }
    ],
    constraints: []
  };

  const preview = applyGeometryDragPreview(
    document,
    "polygon",
    { x: 5, y: 0 },
    { x: 8, y: 4 });

  assert.deepEqual(preview.entities[0].center, { x: 3, y: 4 });
  assert.equal(preview.entities[0].radius, 5);
  assert.equal(preview.entities[0].rotationAngleDegrees, 30);
  assert.deepEqual(preview.dimensions[0].anchor, { x: 3, y: 1 });
  assert.deepEqual(preview.dimensions[1].anchor, { x: 8, y: 4 });
});

test("geometry drag preview scales unconstrained polygon midpoint", () => {
  const document = {
    entities: [
      {
        id: "polygon",
        kind: "polygon",
        center: { x: 0, y: 0 },
        radius: 5,
        rotationAngleDegrees: 0,
        sideCount: 4,
        points: [{ x: 5, y: 0 }, { x: 0, y: 5 }, { x: -5, y: 0 }, { x: 0, y: -5 }]
      }
    ],
    dimensions: [],
    constraints: []
  };

  const preview = applyGeometryDragPreview(
    document,
    "polygon|point|mid-0|2.5|2.5",
    { x: 2.5, y: 2.5 },
    { x: 4, y: 4 });

  assert.deepEqual(preview.entities[0].center, { x: 0, y: 0 });
  assert.equal(preview.entities[0].radius, 8);
  assert.deepEqual(preview.entities[0].points[0], { x: 8, y: 0 });
  assert.equal(preview.entities[0].rotationAngleDegrees, 0);
  assert.equal(preview.dimensions.length, 0);
});

test("geometry drag preview scales unconstrained polygon from edge selection", () => {
  const document = {
    entities: [
      {
        id: "polygon",
        kind: "polygon",
        center: { x: 0, y: 0 },
        radius: 5,
        rotationAngleDegrees: 30,
        sideCount: 6,
        points: [{ x: 5, y: 0 }, { x: 2.5, y: 4.33 }, { x: -2.5, y: 4.33 }]
      }
    ],
    dimensions: [],
    constraints: []
  };

  const preview = applyGeometryDragPreview(
    document,
    "polygon",
    { x: 5, y: 0 },
    { x: 8, y: 4 });

  assert.deepEqual(preview.entities[0].center, { x: 0, y: 0 });
  assertApproxEqual(preview.entities[0].radius, 8.944272);
  assert.equal(preview.entities[0].rotationAngleDegrees, 30);
  assert.equal(preview.dimensions.length, 0);
});

test("geometry drag preview scales unconstrained polygon vertex", () => {
  const document = {
    entities: [
      {
        id: "polygon",
        kind: "polygon",
        center: { x: 0, y: 0 },
        radius: 5,
        rotationAngleDegrees: 0,
        sideCount: 4,
        circumscribed: false,
        points: [{ x: 5, y: 0 }, { x: 0, y: 5 }, { x: -5, y: 0 }, { x: 0, y: -5 }]
      }
    ],
    dimensions: [],
    constraints: []
  };

  const preview = applyGeometryDragPreview(
    document,
    "polygon|point|vertex-0|5|0",
    { x: 5, y: 0 },
    { x: 8, y: 0 });

  assert.deepEqual(preview.entities[0].center, { x: 0, y: 0 });
  assert.equal(preview.entities[0].radius, 8);
  assert.deepEqual(preview.entities[0].points[0], { x: 8, y: 0 });
});

test("point sketch tool needs one click", () => {
  assert.equal(getSketchToolPointCount("point"), 1);
  assert.equal(getSketchToolPointCount("tangentarc"), 3);
  assert.equal(getSketchToolPointCount("centerrectangle"), 2);
});

test("modify tools expose expected point counts", () => {
  assert.equal(getModifyToolPointCount("addsplinepoint"), 1);
  assert.equal(getModifyToolPointCount("offset"), 1);
  assert.equal(getModifyToolPointCount("translate"), 2);
  assert.equal(getModifyToolPointCount("mirror"), 2);
  assert.equal(getModifyToolPointCount("rotate"), 3);
  assert.equal(getModifyToolPointCount("scale"), 3);
  assert.equal(getModifyToolPointCount("circularpattern"), 3);
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

test("power trim request targets clicked entity with projected point", () => {
  const state = createHitTestState([
    {
      id: "edge",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    }
  ]);
  state.activeTool = "powertrim";

  const request = getPowerTrimRequest(state, { x: 40, y: 100 });

  assert.equal(request.targetKey, "edge");
  assertApproxEqual(request.point.x, 4);
  assertApproxEqual(request.point.y, 0);
});

test("power trim request preserves off-end line edge pick for extend", () => {
  const state = createHitTestState([
    {
      id: "edge",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    }
  ]);
  state.activeTool = "powertrim";

  const request = getPowerTrimRequest(state, { x: 109, y: 100 });

  assert.equal(request.targetKey, "edge");
  assertApproxEqual(request.point.x, 10.9);
  assertApproxEqual(request.point.y, 0);
});

test("power trim request uses extended off-end tolerance for line extend picks", () => {
  const state = createHitTestState([
    {
      id: "edge",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    }
  ]);
  state.activeTool = "powertrim";

  const request = getPowerTrimRequest(state, { x: 114, y: 100 });
  const mode = getPowerTrimRequestMode(state, { x: 114, y: 100 });

  assert.equal(request.targetKey, "edge");
  assert.equal(mode, "extend");
  assertApproxEqual(request.point.x, 11.4);
  assertApproxEqual(request.point.y, 0);
});

test("power trim request mode classifies on-edge picks as trim", () => {
  const state = createHitTestState([
    {
      id: "edge",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    }
  ]);
  state.activeTool = "powertrim";

  const mode = getPowerTrimRequestMode(state, { x: 40, y: 100 });

  assert.equal(mode, "trim");
});

test("power trim request preserves off-end polyline edge pick for extend", () => {
  const state = createHitTestState([
    {
      id: "edge",
      kind: "polyline",
      points: [{ x: 0, y: 0 }, { x: 5, y: 0 }, { x: 10, y: 0 }]
    }
  ]);
  state.activeTool = "powertrim";

  const request = getPowerTrimRequest(state, { x: 109, y: 100 });

  assert.equal(request.targetKey, "edge");
  assertApproxEqual(request.point.x, 10.9);
  assertApproxEqual(request.point.y, 0);
});

test("power trim request preserves off-end spline edge pick for extend", () => {
  const state = createHitTestState([
    {
      id: "edge",
      kind: "spline",
      points: [{ x: 0, y: 0 }, { x: 5, y: 0 }, { x: 10, y: 0 }]
    }
  ]);
  state.activeTool = "powertrim";

  const request = getPowerTrimRequest(state, { x: 109, y: 100 });

  assert.equal(request.targetKey, "edge");
  assertApproxEqual(request.point.x, 10.9);
  assertApproxEqual(request.point.y, 0);
});

test("power trim request preserves off-end arc edge pick for extend", () => {
  const state = createHitTestState([
    {
      id: "arc",
      kind: "arc",
      center: { x: 0, y: 0 },
      radius: 10,
      startAngleDegrees: 0,
      endAngleDegrees: 90
    }
  ]);
  state.activeTool = "powertrim";
  const raw = {
    x: 10 * Math.cos(95 * Math.PI / 180),
    y: 10 * Math.sin(95 * Math.PI / 180)
  };

  const request = getPowerTrimRequest(state, { x: raw.x * 10, y: 100 - raw.y * 10 });

  assert.equal(request.targetKey, "arc");
  assertApproxEqual(request.point.x, raw.x);
  assertApproxEqual(request.point.y, raw.y);
});

test("power trim request preserves off-end elliptical arc edge pick for extend", () => {
  const state = createHitTestState([
    {
      id: "ellipse",
      kind: "ellipse",
      center: { x: 0, y: 0 },
      majorAxisEndPoint: { x: 10, y: 0 },
      minorRadiusRatio: 0.5,
      startParameterDegrees: 0,
      endParameterDegrees: 90
    }
  ]);
  state.activeTool = "powertrim";
  const raw = {
    x: 10 * Math.cos(95 * Math.PI / 180),
    y: 5 * Math.sin(95 * Math.PI / 180)
  };

  const request = getPowerTrimRequest(state, { x: raw.x * 10, y: 100 - raw.y * 10 });

  assert.equal(request.targetKey, "ellipse");
  assertApproxEqual(request.point.x, raw.x);
  assertApproxEqual(request.point.y, raw.y);
});

test("power trim request projects closed circle edge picks instead of preserving raw extend picks", () => {
  const state = createHitTestState([
    {
      id: "circle",
      kind: "circle",
      center: { x: 0, y: 0 },
      radius: 5
    }
  ]);
  state.activeTool = "powertrim";

  const request = getPowerTrimRequest(state, { x: 54, y: 96 });

  assert.equal(request.targetKey, "circle");
  assertApproxEqual(Math.hypot(request.point.x, request.point.y), 5);
  assert.notEqual(request.point.x, 5.4);
  assert.notEqual(request.point.y, 0.4);
});

test("power trim request projects closed polygon edge picks instead of preserving raw extend picks", () => {
  const state = createHitTestState([
    {
      id: "polygon",
      kind: "polygon",
      points: [
        { x: -5, y: -5 },
        { x: 5, y: -5 },
        { x: 5, y: 5 },
        { x: -5, y: 5 }
      ]
    }
  ]);
  state.activeTool = "powertrim";

  const request = getPowerTrimRequest(state, { x: 20, y: 46 });

  assert.equal(request.targetKey, "polygon");
  assertApproxEqual(request.point.x, 2);
  assertApproxEqual(request.point.y, 5);
});

test("power trim request uses projected ellipse edge point for near-edge clicks", () => {
  const state = createHitTestState([
    {
      id: "ellipse",
      kind: "ellipse",
      center: { x: 0, y: 0 },
      majorAxisEndPoint: { x: 4, y: 0 },
      minorRadiusRatio: 0.5,
      startAngleDegrees: 0,
      endAngleDegrees: 360
    }
  ]);
  state.activeTool = "powertrim";

  const request = getPowerTrimRequest(state, { x: 28, y: 80 });

  assert.equal(request.targetKey, "ellipse");
  assert.equal(request.point.y < 2, true);
  assert.notEqual(request.point.y, 2);
});

test("power trim request prefers ellipse edge over coincident cutter endpoint", () => {
  const state = createHitTestState([
    {
      id: "ellipse",
      kind: "ellipse",
      center: { x: 0, y: 0 },
      majorAxisEndPoint: { x: 5, y: 0 },
      minorRadiusRatio: 0.5,
      startAngleDegrees: 0,
      endAngleDegrees: 360
    },
    {
      id: "cutter",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 2.5, y: 2.165064 }]
    }
  ]);
  state.activeTool = "powertrim";

  const request = getPowerTrimRequest(state, { x: 25, y: 78.34936 });

  assert.equal(request.targetKey, "ellipse");
  assertApproxEqual(request.point.x, 2.5, 0.01);
  assertApproxEqual(request.point.y, 2.165064, 0.01);
});

test("power trim drag request captures crossed line targets", () => {
  const state = createHitTestState([
    {
      id: "first",
      kind: "line",
      points: [{ x: 2, y: -2 }, { x: 2, y: 2 }]
    },
    {
      id: "second",
      kind: "line",
      points: [{ x: 6, y: -2 }, { x: 6, y: 2 }]
    },
    {
      id: "arc",
      kind: "arc",
      center: { x: 8, y: 0 },
      radius: 1,
      startAngleDegrees: 0,
      endAngleDegrees: 90
    }
  ]);
  state.activeTool = "powertrim";

  const requests = getPowerTrimCrossingRequests(state, [
    { x: 0, y: 100 },
    { x: 80, y: 100 }
  ]);

  assert.deepEqual(requests.map(request => request.targetKey), ["first", "second"]);
  assertApproxEqual(requests[0].point.x, 2);
  assertApproxEqual(requests[0].point.y, 0);
  assertApproxEqual(requests[1].point.x, 6);
  assertApproxEqual(requests[1].point.y, 0);
});

test("ellipse entity edge is hoverable from sampled geometry", () => {
  const state = createHitTestState([
    {
      id: "ellipse",
      kind: "ellipse",
      center: { x: 0, y: 0 },
      majorAxisEndPoint: { x: 4, y: 0 },
      minorRadiusRatio: 0.5,
      startAngleDegrees: 0,
      endAngleDegrees: 360
    }
  ]);

  const target = findNearestTarget(state, { x: 28, y: 86 });

  assert.equal(target.key, "ellipse");
  assert.equal(target.kind, "entity");
  assertApproxEqual(target.snapPoint.x, 2.8, 0.2);
  assertApproxEqual(target.snapPoint.y, 1.4, 0.2);
});

test("power trim drag request captures crossed curve targets", () => {
  const state = createHitTestState([
    {
      id: "hole",
      kind: "circle",
      center: { x: 4, y: 0 },
      radius: 1
    }
  ]);
  state.activeTool = "powertrim";

  const requests = getPowerTrimCrossingRequests(state, [
    { x: 0, y: 100 },
    { x: 80, y: 100 }
  ]);

  assert.deepEqual(requests.map(request => request.targetKey), ["hole"]);
});

test("construction toggle request targets the hovered entity while construction tool is active", () => {
  const state = createHitTestState([
    {
      id: "edge",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    }
  ]);
  state.activeTool = "construction";

  const request = getConstructionToggleRequest(state, { x: 40, y: 100 });

  assert.deepEqual(request, { targetKey: "edge" });
});

test("construction toggle request is inactive outside the construction tool", () => {
  const state = createHitTestState([
    {
      id: "edge",
      kind: "line",
      points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
    }
  ]);

  assert.equal(getConstructionToggleRequest(state, { x: 40, y: 100 }), null);
});

function createConstraintGlyphState(showAllConstraints) {
  return {
    showAllConstraints,
    hoveredTarget: null,
    constraintGroupOffsets: new Map(),
    document: {
      entities: [
        {
          id: "edge-a",
          kind: "line",
          points: [{ x: 0, y: 0 }, { x: 4, y: 0 }]
        },
        {
          id: "edge-b",
          kind: "line",
          points: [{ x: 0, y: 2 }, { x: 4, y: 2 }]
        }
      ],
      constraints: [
        {
          id: "horizontal-a",
          kind: "Horizontal",
          referenceKeys: ["edge-a"],
          state: "Satisfied"
        },
        {
          id: "fix-a",
          kind: "Fix",
          referenceKeys: ["edge-a"],
          state: "Satisfied"
        },
        {
          id: "vertical-b",
          kind: "Vertical",
          referenceKeys: ["edge-b"],
          state: "Satisfied"
        }
      ]
    },
    view: {
      scale: 10,
      offsetX: 40,
      offsetY: 90
    }
  };
}

function createDimensionedRectanglePreviewDocument() {
  return {
    entities: [
      {
        id: "rect-1",
        kind: "line",
        points: [{ x: 0, y: 0 }, { x: 10, y: 0 }]
      },
      {
        id: "rect-2",
        kind: "line",
        points: [{ x: 10, y: 0 }, { x: 10, y: 5 }]
      },
      {
        id: "rect-3",
        kind: "line",
        points: [{ x: 10, y: 5 }, { x: 0, y: 5 }]
      },
      {
        id: "rect-4",
        kind: "line",
        points: [{ x: 0, y: 5 }, { x: 0, y: 0 }]
      }
    ],
    dimensions: [
      {
        id: "width",
        kind: "lineardistance",
        referenceKeys: ["rect-1:start", "rect-1:end"],
        value: 10,
        anchor: { x: 5, y: 1.2 },
        isDriving: true
      },
      {
        id: "height",
        kind: "lineardistance",
        referenceKeys: ["rect-2:start", "rect-2:end"],
        value: 5,
        anchor: { x: 9.4, y: 2.5 },
        isDriving: true
      }
    ],
    constraints: [
      { id: "coincident-1", kind: "coincident", referenceKeys: ["rect-1:end", "rect-2:start"], state: "satisfied" },
      { id: "coincident-2", kind: "coincident", referenceKeys: ["rect-2:end", "rect-3:start"], state: "satisfied" },
      { id: "coincident-3", kind: "coincident", referenceKeys: ["rect-3:end", "rect-4:start"], state: "satisfied" },
      { id: "coincident-4", kind: "coincident", referenceKeys: ["rect-4:end", "rect-1:start"], state: "satisfied" },
      { id: "parallel-1", kind: "parallel", referenceKeys: ["rect-1", "rect-3"], state: "satisfied" },
      { id: "parallel-2", kind: "parallel", referenceKeys: ["rect-2", "rect-4"], state: "satisfied" },
      { id: "perpendicular", kind: "perpendicular", referenceKeys: ["rect-1", "rect-2"], state: "satisfied" },
      { id: "horizontal", kind: "horizontal", referenceKeys: ["rect-1"], state: "satisfied" },
      { id: "vertical", kind: "vertical", referenceKeys: ["rect-2"], state: "satisfied" }
    ]
  };
}

function createUndimensionedRectanglePreviewDocument() {
  const document = createDimensionedRectanglePreviewDocument();
  return {
    ...document,
    dimensions: []
  };
}

function createPartiallyDimensionedRectanglePreviewDocument() {
  const document = createDimensionedRectanglePreviewDocument();
  return {
    ...document,
    dimensions: document.dimensions.slice(0, 1)
  };
}

function assertApproxEqual(actual, expected, tolerance = 0.000001) {
  assert.ok(Math.abs(actual - expected) <= tolerance, `expected ${actual} to equal ${expected}`);
}

function assertApproxPoint(actual, expected, tolerance = 0.000001) {
  assertApproxEqual(actual.x, expected.x, tolerance);
  assertApproxEqual(actual.y, expected.y, tolerance);
}

function distanceBetweenTestPoints(first, second) {
  return Math.hypot(first.x - second.x, first.y - second.y);
}

function getHexBrightness(color) {
  const value = color.startsWith("#") ? color.slice(1) : color;
  const red = Number.parseInt(value.slice(0, 2), 16);
  const green = Number.parseInt(value.slice(2, 4), 16);
  const blue = Number.parseInt(value.slice(4, 6), 16);
  return (red * 0.299) + (green * 0.587) + (blue * 0.114);
}

function getPositiveTestSweep(startAngleDegrees, endAngleDegrees) {
  let sweep = (endAngleDegrees - startAngleDegrees) % 360;
  if (sweep <= 0) {
    sweep += 360;
  }

  return sweep;
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

function createCanvasInteractionState(entities) {
  return {
    activeTool: "select",
    activeSelectionKey: null,
    acquiredSnapPoints: [],
    canvas: {
      dataset: {},
      getBoundingClientRect: () => ({ left: 0, top: 0, width: 800, height: 600 })
    },
    document: { entities },
    hoveredTarget: null,
    selectedKeys: new Set(),
    selectionBox: null,
    showAllConstraints: false,
    showOriginAxes: false,
    view: {
      scale: 10,
      offsetX: 0,
      offsetY: 100
    }
  };
}

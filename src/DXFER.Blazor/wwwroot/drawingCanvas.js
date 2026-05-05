const DEFAULT_FIT_MARGIN = 32;
const HIT_TEST_TOLERANCE = 9;
const SNAP_POINT_TOLERANCE = 8;
const CLICK_MOVE_TOLERANCE = 5;
const MIN_VIEW_SCALE = 0.000001;
const MAX_VIEW_SCALE = 1000000;
const FULL_CIRCLE_DEGREES = 360;
const MAX_ACQUIRED_SNAP_POINTS = 2;
const SEGMENT_KEY_SEPARATOR = "|segment|";
const POINT_KEY_SEPARATOR = "|point|";
const DYNAMIC_POINT_KEY_PREFIX = "__dynamic";
const WORLD_GEOMETRY_TOLERANCE = 0.000001;
const ORTHO_POLAR_SNAP_TOLERANCE = 6;
const MAX_INFERENCE_GUIDE_SCREEN_DISTANCE = 360;
const DIMENSION_INPUT_SCREEN_MARGIN_X = 52;
const DIMENSION_INPUT_SCREEN_MARGIN_Y = 18;
const DIMENSION_LAYER_STROKE_STYLE = "#8fa1b6";
const DIMENSION_LAYER_TEXT_STYLE = "#a7b6c8";
const DIMENSION_PREVIEW_STROKE_STYLE = "#facc15";
const DIMENSION_ARROWHEAD_SIZE = 15;
const DIMENSION_TEXT_GAP_PADDING = 5;
const DIMENSION_INPUT_LEADER_GAP = 12;
const SKETCH_CHAIN_TOGGLE_REARM_TOLERANCE = SNAP_POINT_TOLERANCE + 5;
const SKETCH_CHAIN_DIMENSION_SUPPRESS_TOLERANCE = SNAP_POINT_TOLERANCE * 3;
const CONSTRAINT_GLYPH_SIZE = 16;
const CONSTRAINT_GLYPH_GAP = 4;
const CONSTRAINT_GROUP_HIT_PADDING = 4;

export function createDrawingCanvas(canvas, dotnetRef, dimensionOverlay = null) {
  const context = canvas.getContext("2d");
  if (!context) {
    throw new Error("DXFER drawing canvas requires a 2D rendering context.");
  }

  const state = {
    canvas,
    context,
    dotnetRef,
    dimensionOverlay,
    dimensionInputs: new Map(),
    persistentDimensionInputs: new Map(),
    dimensionAnchorOverrides: new Map(),
    activeDimensionKey: null,
    pendingDimensionFocusKey: null,
    visibleDimensionKeys: [],
    dimensionDrag: null,
    geometryDrag: null,
    suppressDimensionInputCommit: false,
    document: null,
    showOriginAxes: false,
    showAllConstraints: false,
    constraintGroupOffsets: new Map(),
    constraintGroupDrag: null,
    grainDirection: "none",
    constructionMode: false,
    polarSnapIncrementDegrees: 15,
    activeTool: "select",
    toolDraft: createEmptyToolDraft(),
    sketchChainContext: null,
    sketchChainVertexHovering: false,
    sketchChainAutoTool: false,
    sketchChainToggleRequiresExit: false,
    dimensionDraft: createEmptyDimensionDraft(),
    acquiredSnapPoints: [],
    hoveredTarget: null,
    selectedKeys: new Set(),
    activeSelectionKey: null,
    view: {
      scale: 1,
      offsetX: 0,
      offsetY: 0
    },
    pixelRatio: 1,
    disposed: false,
    panning: false,
    lastPointerScreen: null,
    pointerScreenPoint: null,
    clickCandidate: null,
    selectionBox: null,
    previousTouchAction: canvas.style.touchAction
  };

  const resize = () => resizeCanvas(state);
  const onPointerMove = event => handlePointerMove(state, event);
  const onPointerDown = event => handlePointerDown(state, event);
  const onPointerUp = event => handlePointerUp(state, event);
  const onPointerCancel = event => handlePointerCancel(state, event);
  const onPointerLeave = () => handlePointerLeave(state);
  const onWheel = event => handleWheel(state, event);
  const onDoubleClick = event => handleDoubleClick(state, event);
  const onContextMenu = event => event.preventDefault();
  const onKeyDown = event => handleKeyDown(state, event);

  state.canvas.style.touchAction = "none";
  state.canvas.addEventListener("pointermove", onPointerMove);
  state.canvas.addEventListener("pointerdown", onPointerDown);
  state.canvas.addEventListener("pointerup", onPointerUp);
  state.canvas.addEventListener("pointercancel", onPointerCancel);
  state.canvas.addEventListener("pointerleave", onPointerLeave);
  state.canvas.addEventListener("wheel", onWheel, { passive: false });
  state.canvas.addEventListener("dblclick", onDoubleClick);
  state.canvas.addEventListener("contextmenu", onContextMenu);
  window.addEventListener("resize", resize);
  window.addEventListener("keydown", onKeyDown);

  const resizeObserver = typeof ResizeObserver === "undefined"
    ? null
    : new ResizeObserver(resize);

  if (resizeObserver) {
    resizeObserver.observe(state.canvas);
  }

  resizeCanvas(state);

  return {
    setDocument(document, fitToDocument = false) {
      state.document = document || null;
      state.acquiredSnapPoints = [];
      if (fitToDocument) {
        state.toolDraft = createEmptyToolDraft();
        state.sketchChainContext = null;
        state.sketchChainVertexHovering = false;
        state.sketchChainAutoTool = false;
        state.sketchChainToggleRequiresExit = false;
        state.constraintGroupDrag = null;
        state.constraintGroupOffsets.clear();
      }
      pruneInteractionState(state, true);
      pruneConstraintGroupOffsets(state);
      if (fitToDocument) {
        fitToExtents(state);
      }
      updateDebugAttributes(state);
      draw(state);
    },

    fitToExtents() {
      fitToExtents(state);
      updateDebugAttributes(state);
      draw(state);
    },

    setGrainDirection(direction) {
      state.grainDirection = normalizeGrainDirection(direction);
      updateDebugAttributes(state);
      draw(state);
    },

    setConstructionMode(enabled) {
      state.constructionMode = Boolean(enabled);
      state.canvas.dataset.constructionMode = state.constructionMode ? "true" : "false";
      updateDebugAttributes(state);
      draw(state);
    },

    setPolarSnapIncrementDegrees(incrementDegrees) {
      state.polarSnapIncrementDegrees = normalizePolarSnapIncrement(incrementDegrees);
      updateDebugAttributes(state);
      draw(state);
    },

    clearSelection() {
      clearInteractionState(state);
      updateDebugAttributes(state);
      draw(state);
    },

    getSelectedKeys() {
      return Array.from(state.selectedKeys);
    },

    getActiveSelectionKey() {
      return state.activeSelectionKey || null;
    },

    setOriginAxesVisible(visible) {
      state.showOriginAxes = Boolean(visible);
      updateDebugAttributes(state);
      draw(state);
    },

    setShowAllConstraints(visible) {
      state.showAllConstraints = Boolean(visible);
      state.canvas.dataset.showAllConstraints = state.showAllConstraints ? "true" : "false";
      updateDebugAttributes(state);
      draw(state);
    },

    setActiveTool(toolName) {
      clearTransientDimensionInputs(state);
      const previousTool = normalizeToolName(state.activeTool);
      const nextTool = normalizeToolName(toolName);
      const selectionCleared = prepareSelectionForToolEntry(state, nextTool, previousTool);
      const draft = getChainedSketchToolDraft(previousTool, nextTool, state.sketchChainContext);
      if (!isChainableSketchTool(previousTool) || !isChainableSketchTool(nextTool)) {
        state.sketchChainContext = null;
        state.sketchChainAutoTool = false;
        state.sketchChainToggleRequiresExit = false;
      }

      state.activeTool = nextTool;
      state.toolDraft = draft;
      state.sketchChainAutoTool = false;
      state.sketchChainVertexHovering = previousTool === nextTool && state.sketchChainContext
        ? state.sketchChainVertexHovering
        : false;
      state.sketchChainToggleRequiresExit = previousTool === nextTool && state.sketchChainContext
        ? state.sketchChainToggleRequiresExit
        : false;
      state.dimensionDraft = createEmptyDimensionDraft();
      state.acquiredSnapPoints = [];
      state.canvas.dataset.activeTool = state.activeTool;
      if (selectionCleared) {
        invokeDotNet(state, "OnSelectionCleared");
      }

      updateDebugAttributes(state);
      draw(state);
    },

    dispose() {
      state.disposed = true;
      state.canvas.removeEventListener("pointermove", onPointerMove);
      state.canvas.removeEventListener("pointerdown", onPointerDown);
      state.canvas.removeEventListener("pointerup", onPointerUp);
      state.canvas.removeEventListener("pointercancel", onPointerCancel);
      state.canvas.removeEventListener("pointerleave", onPointerLeave);
      state.canvas.removeEventListener("wheel", onWheel);
      state.canvas.removeEventListener("dblclick", onDoubleClick);
      state.canvas.removeEventListener("contextmenu", onContextMenu);
      window.removeEventListener("resize", resize);
      window.removeEventListener("keydown", onKeyDown);

      if (resizeObserver) {
        resizeObserver.disconnect();
      }

      state.canvas.style.touchAction = state.previousTouchAction;
      clearDimensionInputs(state);
      clearPersistentDimensionInputs(state);
      setHoveredTarget(state, null);
      updateDebugAttributes(state);
    }
  };
}

function resizeCanvas(state) {
  if (state.disposed) {
    return;
  }

  const size = getCanvasCssSize(state);
  const pixelRatio = Math.max(1, window.devicePixelRatio || 1);
  const pixelWidth = Math.max(1, Math.floor(size.width * pixelRatio));
  const pixelHeight = Math.max(1, Math.floor(size.height * pixelRatio));

  if (state.canvas.width !== pixelWidth || state.canvas.height !== pixelHeight) {
    state.canvas.width = pixelWidth;
    state.canvas.height = pixelHeight;
  }

  state.pixelRatio = pixelRatio;
  draw(state);
}

function createEmptyToolDraft() {
  return {
    points: [],
    previewPoint: null,
    dimensionValues: {}
  };
}

export function getChainedSketchToolDraft(previousTool, nextTool, chainContext) {
  if (!isChainableSketchTool(previousTool)
    || !isChainableSketchTool(nextTool)
    || !chainContext
    || !chainContext.point) {
    return createEmptyToolDraft();
  }

  if (nextTool === "line") {
    return {
      points: [copyPoint(chainContext.point)],
      previewPoint: null,
      dimensionValues: {}
    };
  }

  if (nextTool === "tangentarc" && chainContext.tangentPoint) {
    return {
      points: [copyPoint(chainContext.point), copyPoint(chainContext.tangentPoint)],
      previewPoint: null,
      dimensionValues: {}
    };
  }

  return createEmptyToolDraft();
}

export function getSketchChainContextFromCommittedTool(tool, points) {
  if (!Array.isArray(points)) {
    return null;
  }

  if (tool === "line" && points.length >= 2) {
    const start = points[0];
    const end = points[1];
    return createSketchChainContext(
      end,
      subtractPoints(end, start),
      distanceBetweenWorldPoints(start, end),
      false);
  }

  if (tool === "tangentarc" && points.length >= 3) {
    return getTangentArcChainContext(points[0], points[1], points[2]);
  }

  return null;
}

export function getPostCommitSketchToolState(tool, points, wasAutoChained = false) {
  const normalizedTool = normalizeToolName(tool);
  const chainContext = getSketchChainContextFromCommittedTool(normalizedTool, points);

  return {
    activeTool: normalizedTool,
    toolDraft: getChainedSketchToolDraft(normalizedTool, normalizedTool, chainContext),
    sketchChainContext: chainContext,
    sketchChainVertexHovering: false,
    sketchChainToggleRequiresExit: Boolean(chainContext)
  };
}

function getTangentArcChainContext(start, tangentPoint, end) {
  const arc = getTangentArc(start, tangentPoint, end);
  if (!arc) {
    return null;
  }

  const startTangent = normalizeWorldVector(subtractPoints(tangentPoint, start));
  const startRadius = subtractPoints(start, arc.center);
  const endRadius = subtractPoints(end, arc.center);
  if (!startTangent || distanceBetweenWorldPoints(arc.center, end) <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const startRadiusLength = Math.hypot(startRadius.x, startRadius.y);
  const endRadiusLength = Math.hypot(endRadius.x, endRadius.y);
  if (startRadiusLength <= WORLD_GEOMETRY_TOLERANCE || endRadiusLength <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const counterClockwiseStartTangent = {
    x: -startRadius.y / startRadiusLength,
    y: startRadius.x / startRadiusLength
  };
  const counterClockwiseEndTangent = {
    x: -endRadius.y / endRadiusLength,
    y: endRadius.x / endRadiusLength
  };
  const endTangent = dotPoints(counterClockwiseStartTangent, startTangent) >= 0
    ? counterClockwiseEndTangent
    : { x: -counterClockwiseEndTangent.x, y: -counterClockwiseEndTangent.y };

  return createSketchChainContext(
    end,
    endTangent,
    distanceBetweenWorldPoints(start, tangentPoint),
    true);
}

function createSketchChainContext(point, tangentVector, tangentLength = 1, isTangentContinuation = false) {
  const tangent = normalizeWorldVector(tangentVector);
  if (!point || !tangent) {
    return null;
  }

  const controlLength = Number.isFinite(tangentLength) && tangentLength > WORLD_GEOMETRY_TOLERANCE
    ? tangentLength
    : 1;

  return {
    point: copyPoint(point),
    tangentPoint: {
      x: point.x + tangent.x * controlLength,
      y: point.y + tangent.y * controlLength
    },
    isTangentContinuation: Boolean(isTangentContinuation)
  };
}

function isChainableSketchTool(tool) {
  return tool === "line" || tool === "tangentarc";
}

function getSketchChainToggleTool(tool) {
  const normalized = normalizeToolName(tool);
  if (normalized === "line") {
    return "tangentarc";
  }

  return normalized === "tangentarc" ? "line" : null;
}

export function tryToggleSketchChainToolAtPoint(state, screenPoint, target = null) {
  const previousTool = normalizeToolName(state && state.activeTool);
  const nextTool = getSketchChainToggleTool(previousTool);
  if (!nextTool || !state.sketchChainContext || !state.sketchChainContext.point) {
    if (state) {
      state.sketchChainVertexHovering = false;
      state.sketchChainToggleRequiresExit = false;
    }
    return false;
  }

  const chainPoint = state.sketchChainContext.point;
  const chainScreenPoint = worldToScreen(state, chainPoint);
  const chainDistance = distanceBetweenScreenPoints(screenPoint, chainScreenPoint);
  if (chainDistance > SKETCH_CHAIN_TOGGLE_REARM_TOLERANCE) {
    state.sketchChainVertexHovering = false;
    state.sketchChainToggleRequiresExit = false;
    return false;
  }

  if (state.sketchChainToggleRequiresExit) {
    state.sketchChainVertexHovering = true;
    return false;
  }

  if (!isSketchChainVertexTarget(target, chainPoint)) {
    return false;
  }

  const firstDraftPoint = state.toolDraft && Array.isArray(state.toolDraft.points)
    ? state.toolDraft.points[0]
    : null;
  if (!sameOptionalWorldPoint(firstDraftPoint, chainPoint)) {
    state.sketchChainVertexHovering = false;
    return false;
  }

  if (chainDistance > SNAP_POINT_TOLERANCE) {
    state.sketchChainVertexHovering = false;
    return false;
  }

  if (state.sketchChainVertexHovering) {
    return false;
  }

  const draft = getChainedSketchToolDraft(previousTool, nextTool, state.sketchChainContext);
  if (!draft.points.length) {
    return false;
  }

  clearTransientDimensionInputs(state);
  state.activeTool = nextTool;
  state.toolDraft = draft;
  state.sketchChainAutoTool = nextTool === "tangentarc";
  state.sketchChainVertexHovering = true;
  state.pendingDimensionFocusKey = null;
  state.acquiredSnapPoints = [];
  if (state.canvas && state.canvas.dataset) {
    state.canvas.dataset.activeTool = state.activeTool;
  }
  if (state.dotnetRef) {
    invokeDotNet(state, "OnSketchToolModeChanged", nextTool);
  }
  return true;
}

function isSketchChainVertexTarget(target, chainPoint) {
  return Boolean(target
    && target.kind === "point"
    && target.point
    && sameOptionalWorldPoint(target.point, chainPoint));
}

function copyPoint(point) {
  return {
    x: point.x,
    y: point.y
  };
}

function normalizeWorldVector(vector) {
  if (!vector) {
    return null;
  }

  const length = Math.hypot(vector.x, vector.y);
  return length <= WORLD_GEOMETRY_TOLERANCE
    ? null
    : {
      x: vector.x / length,
      y: vector.y / length
    };
}

function createEmptyDimensionDraft() {
  return {
    selectionKeys: [],
    complete: false,
    radialDiameter: false,
    anchorPoint: null
  };
}

function draw(state) {
  const { context, canvas } = state;
  const size = getCanvasCssSize(state);

  context.save();
  context.setTransform(1, 0, 0, 1, 0, 0);
  context.clearRect(0, 0, canvas.width, canvas.height);
  context.restore();

  context.save();
  context.setTransform(state.pixelRatio, 0, 0, state.pixelRatio, 0, 0);
  context.fillStyle = "#0f172a";
  context.fillRect(0, 0, size.width, size.height);

  if (state.showOriginAxes) {
    drawOriginAxes(state, size);
  }

  const entities = getDocumentEntities(state.document);
  for (const entity of entities) {
    drawEntity(state, entity, {
      strokeStyle: entity.isConstruction ? "#64748b" : "#94a3b8",
      lineWidth: entity.isConstruction ? 1.1 : 1.5,
      lineDash: entity.isConstruction ? [8, 5] : []
    });
  }

  for (const selectedKey of state.selectedKeys) {
    const target = resolveSelectionTarget(state, selectedKey);
    if (target) {
      const isActive = selectedKey === state.activeSelectionKey;
      drawTarget(state, target, {
        strokeStyle: isActive ? "#7dd3fc" : "#2d7898",
        lineWidth: target.kind === "point" ? isActive ? 3 : 1.65 : isActive ? 4.5 : 2.4,
        lineDash: getTargetSelectionLineDash(target),
        glow: isActive
      });
    }
  }

  drawAcquiredSnapPoints(state);
  drawInferenceGuides(state, size);

  if (state.hoveredTarget) {
    const isSelected = state.selectedKeys.has(state.hoveredTarget.key);
    const isActive = state.hoveredTarget.key === state.activeSelectionKey;
    drawTarget(state, state.hoveredTarget, {
      strokeStyle: isActive ? "#bae6fd" : isSelected ? "#38bdf8" : "#f59e0b",
      lineWidth: state.hoveredTarget.kind === "point" ? isActive ? 3 : 1.8 : isActive ? 4.5 : isSelected ? 2.5 : 3.5,
      lineDash: getTargetSelectionLineDash(state.hoveredTarget, isSelected ? [6, 4] : []),
      glow: isActive
    });
  }

  if (state.selectionBox) {
    drawSelectionBox(state);
  }

  const constraintGlyphGroups = getVisibleConstraintGlyphGroups(state);
  drawConstraintGlyphGroups(state, constraintGlyphGroups);
  const persistentDimensions = getPersistentDimensionDescriptors(state);
  drawPersistentDimensions(state, persistentDimensions);
  updatePersistentDimensionInputs(state, persistentDimensions);
  if (!state.dimensionOverlay) {
    drawPreviewDimensionFallbackLabels(state, persistentDimensions);
  }

  const previewDimensions = drawToolPreview(state);
  updateDimensionInputs(state, previewDimensions);
  if (!state.dimensionOverlay) {
    drawPreviewDimensionFallbackLabels(state, previewDimensions);
  }
  drawDimensionToolPreview(state);
  drawGrainDirection(state, size);

  context.restore();
}

function drawEntity(state, entity, style) {
  const { context } = state;

  context.save();
  context.strokeStyle = style.strokeStyle;
  context.lineWidth = style.lineWidth;
  context.lineCap = "round";
  context.lineJoin = "round";
  context.setLineDash(style.lineDash || []);
  if (style.glow) {
    context.shadowColor = "rgba(125, 211, 252, 0.55)";
    context.shadowBlur = 9;
  }

  if (getEntityKind(entity) === "point") {
    const point = getPointEntityLocation(entity);
    if (point) {
      drawPointTarget(state, point, style);
    }

    context.restore();
    return;
  }

  const hasPath = buildEntityPath(state, entity);
  if (hasPath) {
    context.stroke();
  }

  context.restore();
}

function drawOriginAxes(state, size) {
  const { context } = state;
  const origin = worldToScreen(state, { x: 0, y: 0 });

  context.save();
  context.lineWidth = 1.25;
  context.setLineDash([4, 5]);

  context.strokeStyle = "#ef4444";
  context.beginPath();
  context.moveTo(0, origin.y);
  context.lineTo(size.width, origin.y);
  context.stroke();

  context.strokeStyle = "#22c55e";
  context.beginPath();
  context.moveTo(origin.x, 0);
  context.lineTo(origin.x, size.height);
  context.stroke();

  context.setLineDash([]);
  context.strokeStyle = "#e5e7eb";
  context.fillStyle = "#0f172a";
  context.lineWidth = 1.5;
  context.beginPath();
  context.arc(origin.x, origin.y, 4, 0, Math.PI * 2);
  context.fill();
  context.stroke();
  context.restore();
}

function drawGrainDirection(state, size) {
  const direction = normalizeGrainDirection(state.grainDirection);
  if (direction === "none") {
    return;
  }

  const { context } = state;
  const origin = { x: 20, y: size.height - 28 };
  const end = direction === "globaly"
    ? { x: origin.x, y: origin.y - 48 }
    : { x: origin.x + 58, y: origin.y };
  const label = direction === "globaly" ? "GRAIN Y" : "GRAIN X";

  context.save();
  context.lineWidth = 2.5;
  context.strokeStyle = "#facc15";
  context.fillStyle = "#facc15";
  context.setLineDash([]);
  context.beginPath();
  context.moveTo(origin.x, origin.y);
  context.lineTo(end.x, end.y);
  context.stroke();

  const arrowAngle = Math.atan2(end.y - origin.y, end.x - origin.x);
  drawArrowHead(context, end, arrowAngle, 9);

  context.font = "600 12px Segoe UI, system-ui, sans-serif";
  context.textBaseline = "middle";
  context.fillText(label, end.x + 10, end.y);
  context.restore();
}

function drawArrowHead(context, point, angle, size) {
  const wingAngle = Math.PI / 12;
  context.beginPath();
  context.moveTo(point.x, point.y);
  context.lineTo(
    point.x - size * Math.cos(angle - wingAngle),
    point.y - size * Math.sin(angle - wingAngle));
  context.lineTo(
    point.x - size * Math.cos(angle + wingAngle),
    point.y - size * Math.sin(angle + wingAngle));
  context.closePath();
  context.fill();
}

function drawScreenLine(context, start, end) {
  context.beginPath();
  context.moveTo(start.x, start.y);
  context.lineTo(end.x, end.y);
  context.stroke();
}

function drawArrowhead(context, point, toward, size = 9) {
  drawArrowHead(context, point, Math.atan2(toward.y - point.y, toward.x - point.x), size);
}

function drawTarget(state, target, style) {
  if (!isDynamicTargetCurrentToPointer(state, target)) {
    return;
  }

  const { context } = state;

  if (target.kind === "point") {
    drawPointTarget(state, target.point, style, getPointTargetMarker(target));
    return;
  }

  if (target.kind === "entity" && getEntityKind(target.entity) === "point") {
    const point = target.snapPoint || getPointEntityLocation(target.entity);
    if (point) {
      drawPointTarget(state, point, style);
    }

    return;
  }

  if (target.kind === "dimension") {
    drawDimensionTarget(state, target.dimension, style);
    return;
  }

  context.save();
  context.strokeStyle = style.strokeStyle;
  context.lineWidth = style.lineWidth;
  context.lineCap = "round";
  context.lineJoin = "round";
  context.setLineDash(style.lineDash || []);

  const hasPath = target.kind === "segment"
    ? buildPolylineSegmentPath(state, target.entity, target.segmentIndex)
    : buildEntityPath(state, target.entity);
  if (hasPath) {
    context.stroke();
  }

  context.restore();

  if (getSketchCreationTool(state) && target.snapPoint) {
    drawPointTarget(state, target.snapPoint, {
      strokeStyle: style.strokeStyle,
      lineWidth: 2
    });
  }
}

function drawAcquiredSnapPoints(state) {
  if (!getSketchCreationTool(state) || state.acquiredSnapPoints.length === 0) {
    return;
  }

  const { context } = state;
  context.save();
  context.strokeStyle = "#67e8f9";
  context.fillStyle = "#0f172a";
  context.lineWidth = 1.25;
  context.setLineDash([]);

  for (const acquired of state.acquiredSnapPoints) {
    const point = worldToScreen(state, acquired.point);
    context.beginPath();
    context.rect(point.x - 3, point.y - 3, 6, 6);
    context.fill();
    context.stroke();
  }

  context.restore();
}

function drawInferenceGuides(state, size) {
  const guides = state.hoveredTarget
    && isDynamicTargetCurrentToPointer(state, state.hoveredTarget)
    && Array.isArray(state.hoveredTarget.guides)
    ? state.hoveredTarget.guides
    : [];
  if (guides.length === 0) {
    return;
  }

  const { context } = state;
  context.save();
  context.strokeStyle = "#67e8f9";
  context.lineWidth = 1;
  context.setLineDash([4, 4]);

  for (const guide of guides) {
    if (!isInferenceGuideWithinScreenDistance(state, guide, state.hoveredTarget.point)) {
      continue;
    }

    const point = worldToScreen(state, guide.point);
    context.beginPath();
    if (guide.orientation === "segment" && guide.start) {
      const start = worldToScreen(state, guide.start);
      context.moveTo(start.x, start.y);
      context.lineTo(point.x, point.y);
    } else if (guide.orientation === "vertical") {
      context.moveTo(point.x, 0);
      context.lineTo(point.x, size.height);
    } else {
      context.moveTo(0, point.y);
      context.lineTo(size.width, point.y);
    }

    context.stroke();
  }

  context.restore();
}

export function isInferenceGuideWithinScreenDistance(state, guide, targetPoint, maxDistance = MAX_INFERENCE_GUIDE_SCREEN_DISTANCE) {
  if (!state || !guide || !targetPoint || !Number.isFinite(maxDistance)) {
    return false;
  }

  if (guide.orientation === "segment" && guide.start && guide.point) {
    return isWorldPointWithinScreenDistance(state, guide.start, targetPoint, maxDistance)
      && isWorldPointWithinScreenDistance(state, guide.point, targetPoint, maxDistance);
  }

  return guide.point
    ? isWorldPointWithinScreenDistance(state, guide.point, targetPoint, maxDistance)
    : true;
}

function isWorldPointWithinScreenDistance(state, first, second, maxDistance = MAX_INFERENCE_GUIDE_SCREEN_DISTANCE) {
  if (!first || !second) {
    return false;
  }

  return distanceBetweenScreenPoints(worldToScreen(state, first), worldToScreen(state, second)) <= maxDistance;
}

function drawPointTarget(state, point, style, marker = "point") {
  const { context } = state;
  const screenPoint = worldToScreen(state, point);

  context.save();
  context.strokeStyle = "#0f172a";
  context.fillStyle = style.strokeStyle;
  context.lineWidth = 1;
  context.setLineDash([]);
  if (style.glow) {
    context.shadowColor = "rgba(125, 211, 252, 0.55)";
    context.shadowBlur = 8;
  }
  if (marker === "midpoint") {
    drawMidpointTargetPath(context, screenPoint);
  } else if (marker === "tangent") {
    drawTangentTargetPath(context, screenPoint);
  } else {
    context.beginPath();
    context.arc(screenPoint.x, screenPoint.y, 3.5, 0, Math.PI * 2);
  }

  context.fill();
  context.stroke();
  context.restore();
}

function drawMidpointTargetPath(context, screenPoint) {
  const size = 5.6;
  context.beginPath();
  context.moveTo(screenPoint.x, screenPoint.y - size);
  context.lineTo(screenPoint.x + size, screenPoint.y + size * 0.72);
  context.lineTo(screenPoint.x - size, screenPoint.y + size * 0.72);
  context.closePath();
}

function drawTangentTargetPath(context, screenPoint) {
  context.beginPath();
  context.arc(screenPoint.x, screenPoint.y, 4.2, 0, Math.PI * 2);
  context.moveTo(screenPoint.x - 5.2, screenPoint.y + 5.2);
  context.lineTo(screenPoint.x + 5.2, screenPoint.y - 5.2);
}

function drawSelectionBox(state) {
  const { context, selectionBox } = state;
  if (!selectionBox) {
    return;
  }

  const rect = normalizeScreenRect(selectionBox.start, selectionBox.end);
  const isCrossing = isCrossingSelection(selectionBox);
  const isDeselect = selectionBox.operation === "deselect";
  const strokeStyle = isDeselect ? "#f97316" : isCrossing ? "#22c55e" : "#38bdf8";
  const fillStyle = isDeselect
    ? "rgba(249, 115, 22, 0.14)"
    : isCrossing
      ? "rgba(34, 197, 94, 0.14)"
      : "rgba(56, 189, 248, 0.14)";

  context.save();
  context.strokeStyle = strokeStyle;
  context.fillStyle = fillStyle;
  context.lineWidth = 1.5;
  context.setLineDash(isCrossing ? [6, 4] : []);
  context.fillRect(rect.minX, rect.minY, rect.maxX - rect.minX, rect.maxY - rect.minY);
  context.strokeRect(rect.minX, rect.minY, rect.maxX - rect.minX, rect.maxY - rect.minY);
  context.restore();
}

function drawToolPreview(state) {
  const tool = getSketchCreationTool(state);
  const modifyTool = getModifyTool(state);
  if (!tool && modifyTool) {
    drawModifyToolPreview(state, modifyTool);
    return [];
  }

  if (!tool || !state.toolDraft || state.toolDraft.points.length === 0 || !state.toolDraft.previewPoint) {
    return [];
  }

  const points = state.toolDraft.points;
  const first = points[0];
  const second = points.length > 1 ? points[1] : state.toolDraft.previewPoint;
  const third = state.toolDraft.previewPoint;
  const { context } = state;
  const dimensions = [];
  const suppressChainDimensions = shouldSuppressSketchChainPreviewDimensions(state);

  context.save();
  context.strokeStyle = state.constructionMode ? "#67e8f9" : "#facc15";
  context.fillStyle = state.constructionMode ? "#67e8f9" : "#facc15";
  context.lineWidth = 1.5;
  context.setLineDash(state.constructionMode ? [3, 5] : [6, 4]);

  if (tool === "line") {
    const start = worldToScreen(state, first);
    const end = worldToScreen(state, second);
    context.beginPath();
    context.moveTo(start.x, start.y);
    context.lineTo(end.x, end.y);
    context.stroke();
    if (!suppressChainDimensions) {
      addLinearPreviewDimension(dimensions, state, "length", "Length", first, second);
    }
  } else if (tool === "midpointline") {
    const mirrored = mirrorPoint(first, second);
    const start = worldToScreen(state, mirrored);
    const end = worldToScreen(state, second);
    context.beginPath();
    context.moveTo(start.x, start.y);
    context.lineTo(end.x, end.y);
    context.stroke();
    addLinearPreviewDimension(dimensions, state, "length", "Length", mirrored, second);
  } else if (tool === "twopointrectangle") {
    const a = worldToScreen(state, first);
    const b = worldToScreen(state, second);
    const rect = normalizeScreenRect(a, b);
    context.strokeRect(rect.minX, rect.minY, rect.maxX - rect.minX, rect.maxY - rect.minY);
    addRectanglePreviewDimensions(dimensions, state, first, second);
  } else if (tool === "centerrectangle") {
    const corners = getCenterRectangleCorners(first, second);
    drawWorldPolyline(state, corners.concat(corners[0]));
    addRectanglePreviewDimensions(dimensions, state, corners[0], corners[2]);
  } else if (tool === "alignedrectangle") {
    if (points.length === 1) {
      const start = worldToScreen(state, first);
      const end = worldToScreen(state, second);
      context.beginPath();
      context.moveTo(start.x, start.y);
      context.lineTo(end.x, end.y);
      context.stroke();
      if (!suppressChainDimensions) {
        addLinearPreviewDimension(dimensions, state, "length", "Length", first, second);
      }
    } else {
      const corners = getAlignedRectangleCorners(first, second, third);
      drawWorldPolyline(state, corners.concat(corners[0]));
      addLinearPreviewDimension(dimensions, state, "length", "Length", first, second);
      addAlignedRectangleDepthDimension(dimensions, state, corners);
    }
  } else if (tool === "centercircle") {
    const center = worldToScreen(state, first);
    const radius = distanceBetweenWorldPoints(first, second) * state.view.scale;
    if (radius > 0) {
      context.beginPath();
      context.arc(center.x, center.y, radius, 0, Math.PI * 2);
      context.stroke();
      addRadiusPreviewDimension(dimensions, state, first, second);
    }
  } else if (tool === "threepointcircle") {
    if (points.length === 1) {
      drawWorldPolyline(state, [first, third]);
    } else {
      const circle = getThreePointCircle(first, second, third);
      if (circle) {
        const center = worldToScreen(state, circle.center);
        const radius = circle.radius * state.view.scale;
        context.beginPath();
        context.arc(center.x, center.y, radius, 0, Math.PI * 2);
        context.stroke();
      }
      drawWorldPolyline(state, [second, third]);
    }
  } else if (tool === "threepointarc") {
    if (points.length === 1) {
      drawWorldPolyline(state, [first, third]);
    } else {
      const arc = getThreePointArc(first, second, third);
      if (arc) {
        drawWorldArcPreview(state, arc);
        dimensions.push(...getThreePointArcPreviewDimensions(state, first, second, third));
      } else {
        drawWorldPolyline(state, [second, third]);
      }
    }
  } else if (tool === "tangentarc") {
    if (points.length === 1) {
      drawWorldPolyline(state, [first, third]);
    } else {
      const arc = getTangentArc(first, second, third);
      if (arc) {
        drawWorldArcPreview(state, arc);
        if (!suppressChainDimensions) {
          addRadiusPreviewDimension(dimensions, state, arc.center, first);
        }
      } else {
        drawWorldPolyline(state, [second, third]);
      }
    }
  } else if (tool === "centerpointarc") {
    if (points.length === 1) {
      drawWorldPolyline(state, [first, second]);
      addRadiusPreviewDimension(dimensions, state, first, second);
    } else {
      const arc = getCenterPointArc(first, second, third);
      if (arc) {
        drawWorldArcPreview(state, arc);
        addCenterPointArcSweepPreviewDimension(dimensions, state, first, second, third);
      }
    }
  }

  context.setLineDash([]);
  for (const point of points) {
    const marker = worldToScreen(state, point);
    context.beginPath();
    context.arc(marker.x, marker.y, 3.5, 0, Math.PI * 2);
    context.fill();
  }
  context.restore();
  return dimensions;
}

function getTargetSelectionLineDash(target, fallback = []) {
  return target && target.entity && target.entity.isConstruction
    ? [8, 5]
    : fallback;
}

function drawModifyToolPreview(state, tool) {
  if (!tool || !state.toolDraft || state.toolDraft.points.length === 0) {
    return false;
  }

  const points = state.toolDraft.previewPoint
    ? state.toolDraft.points.concat(state.toolDraft.previewPoint)
    : state.toolDraft.points;
  const { context } = state;

  context.save();
  context.strokeStyle = "#facc15";
  context.fillStyle = "#facc15";
  context.lineWidth = 1.5;
  context.setLineDash([6, 4]);

  if (tool === "translate" || tool === "linearpattern" || tool === "mirror") {
    drawWorldPolyline(state, points.slice(0, 2));
  } else if (tool === "rotate" || tool === "scale" || tool === "circularpattern") {
    const center = points[0];
    if (points.length >= 2) {
      drawWorldPolyline(state, [center, points[1]]);
    }
    if (points.length >= 3) {
      drawWorldPolyline(state, [center, points[2]]);
      const centerScreen = worldToScreen(state, center);
      const radius = distanceBetweenWorldPoints(center, points[1]) * state.view.scale;
      if (radius > 0) {
        context.beginPath();
        context.arc(centerScreen.x, centerScreen.y, radius, 0, Math.PI * 2);
        context.stroke();
      }
    }
  } else if (tool === "offset") {
    const marker = worldToScreen(state, points[0]);
    context.beginPath();
    context.arc(marker.x, marker.y, 5, 0, Math.PI * 2);
    context.stroke();
  }

  for (const point of points) {
    const marker = worldToScreen(state, point);
    context.beginPath();
    context.arc(marker.x, marker.y, 3.5, 0, Math.PI * 2);
    context.fill();
  }

  context.restore();
  return true;
}

function shouldSuppressSketchChainPreviewDimensions(state) {
  if (!state
    || !state.sketchChainContext
    || !state.sketchChainContext.point
    || !state.pointerScreenPoint
    || !state.toolDraft
    || !Array.isArray(state.toolDraft.points)
    || state.toolDraft.points.length === 0
    || !sameOptionalWorldPoint(state.toolDraft.points[0], state.sketchChainContext.point)) {
    return false;
  }

  return distanceBetweenScreenPoints(
    state.pointerScreenPoint,
    worldToScreen(state, state.sketchChainContext.point)) <= SKETCH_CHAIN_DIMENSION_SUPPRESS_TOLERANCE;
}

function drawWorldPolyline(state, points) {
  if (!points || points.length < 2) {
    return;
  }

  const first = worldToScreen(state, points[0]);
  state.context.beginPath();
  state.context.moveTo(first.x, first.y);
  for (let index = 1; index < points.length; index += 1) {
    const point = worldToScreen(state, points[index]);
    state.context.lineTo(point.x, point.y);
  }
  state.context.stroke();
}

function drawWorldArcPreview(state, arc) {
  drawWorldPolyline(state, getArcWorldPoints({
    kind: "arc",
    center: arc.center,
    radius: arc.radius,
    startAngleDegrees: arc.startAngleDegrees,
    endAngleDegrees: arc.endAngleDegrees
  }));
}

function addLinearPreviewDimension(dimensions, state, key, label, first, second) {
  const value = distanceBetweenWorldPoints(first, second);
  if (!formatDimensionValue(value)) {
    return;
  }

  const start = worldToScreen(state, first);
  const end = worldToScreen(state, second);
  const midpointScreen = midpointScreenPoint(start, end);
  const normal = getScreenNormal(start, end);
  dimensions.push({
    key,
    label,
    value,
    point: {
      x: midpointScreen.x + normal.x * 18,
      y: midpointScreen.y + normal.y * 18
    }
  });
}

function addRectanglePreviewDimensions(dimensions, state, first, second) {
  const minX = Math.min(first.x, second.x);
  const maxX = Math.max(first.x, second.x);
  const minY = Math.min(first.y, second.y);
  const maxY = Math.max(first.y, second.y);
  const topLeft = worldToScreen(state, { x: minX, y: maxY });
  const topRight = worldToScreen(state, { x: maxX, y: maxY });
  const bottomLeft = worldToScreen(state, { x: minX, y: minY });
  const width = maxX - minX;
  const height = maxY - minY;

  if (formatDimensionValue(width)) {
    dimensions.push({
      key: "width",
      label: "Width",
      value: width,
      point: {
        x: (topLeft.x + topRight.x) / 2,
        y: topLeft.y - 20
      }
    });
  }

  if (formatDimensionValue(height)) {
    dimensions.push({
      key: "height",
      label: "Height",
      value: height,
      point: {
        x: bottomLeft.x - 28,
        y: (topLeft.y + bottomLeft.y) / 2
      }
    });
  }
}

function addAlignedRectangleDepthDimension(dimensions, state, corners) {
  if (!corners || corners.length < 4) {
    return;
  }

  const value = distanceBetweenWorldPoints(corners[1], corners[2]);
  if (!formatDimensionValue(value)) {
    return;
  }

  const start = worldToScreen(state, corners[1]);
  const end = worldToScreen(state, corners[2]);
  const midpointScreen = midpointScreenPoint(start, end);
  const normal = getScreenNormal(start, end);
  dimensions.push({
    key: "depth",
    label: "Depth",
    value,
    point: {
      x: midpointScreen.x + normal.x * 18,
      y: midpointScreen.y + normal.y * 18
    }
  });
}

function addRadiusPreviewDimension(dimensions, state, center, edge) {
  const value = distanceBetweenWorldPoints(center, edge);
  if (!formatDimensionValue(value)) {
    return;
  }

  const centerScreen = worldToScreen(state, center);
  const edgeScreen = worldToScreen(state, edge);
  const midpointScreen = midpointScreenPoint(centerScreen, edgeScreen);
  const normal = getScreenNormal(centerScreen, edgeScreen);
  dimensions.push({
    key: "radius",
    label: "Radius",
    value,
    point: {
      x: midpointScreen.x + normal.x * 18,
      y: midpointScreen.y + normal.y * 18
    }
  });
}

export function getThreePointArcPreviewDimensions(state, first, through, end) {
  const arcState = getThreePointArcConstructionState(first, through, end);
  if (!arcState) {
    return [];
  }

  const dimensions = [];
  addRadiusPreviewDimension(dimensions, state, arcState.center, first);
  return dimensions;
}

function addCenterPointArcSweepPreviewDimension(dimensions, state, center, startRadiusPoint, endAnglePoint) {
  const arc = getCenterPointArc(center, startRadiusPoint, endAnglePoint);
  if (!arc) {
    return;
  }

  dimensions.push({
    key: "sweep",
    label: "Sweep",
    value: getPositiveSweepDegrees(arc.startAngleDegrees, arc.endAngleDegrees),
    point: getArcSweepDimensionPoint(
      state,
      arc.center,
      arc.radius,
      arc.startAngleDegrees,
      arc.endAngleDegrees)
  });
}

function getArcSweepDimensionPoint(state, center, radius, startAngleDegrees, endAngleDegrees) {
  const startScreen = worldToScreen(state, pointOnCircle(center, radius, startAngleDegrees));
  const endScreen = worldToScreen(state, pointOnCircle(center, radius, endAngleDegrees));
  const midpointScreen = midpointScreenPoint(startScreen, endScreen);
  const centerScreen = worldToScreen(state, center);
  const direction = normalizeScreenVector(subtractScreenPoints(midpointScreen, centerScreen)) || { x: 0, y: -1 };
  return {
    x: midpointScreen.x + direction.x * 24,
    y: midpointScreen.y + direction.y * 24
  };
}

function drawPreviewDimensionFallbackLabels(state, dimensions) {
  for (const dimension of dimensions) {
    drawFloatingDimensionLabel(state, formatDimensionValue(dimension.value), dimension.point);
  }
}

export function getPersistentDimensionDescriptors(state) {
  return getDocumentDimensions(state.document)
    .map(dimension => getPersistentDimensionDescriptor(state, dimension))
    .filter(dimension => dimension !== null);
}

function getPersistentDimensionDescriptor(state, dimension) {
  const id = getSketchItemId(dimension);
  const kind = getSketchItemKind(dimension);
  const referenceKeys = getSketchReferenceKeys(dimension);
  const value = Number(readProperty(dimension, "value", "Value"));
  const anchor = readPoint(readProperty(dimension, "anchor", "Anchor"));
  if (!id || !kind || !Number.isFinite(value)) {
    return null;
  }

  const anchorPoint = getDimensionAnchorOverride(state, id) || anchor || getDimensionAnchorPoint(state, kind, referenceKeys);
  if (!anchorPoint) {
    return null;
  }

  return {
    key: `persistent-${id}`,
    id,
    kind,
    label: getDimensionLabel(kind),
    value,
    anchorPoint,
    point: worldToScreen(state, anchorPoint),
    geometry: getDimensionGeometry(state, kind, referenceKeys, anchorPoint)
  };
}

function drawPersistentDimensions(state, dimensions) {
  state.context.save();
  state.context.globalCompositeOperation = "source-over";
  for (const dimension of dimensions) {
    drawDimensionGraphics(state, dimension, false);
  }
  state.context.restore();
}

function getDimensionAnchorOverride(state, id) {
  return state.dimensionAnchorOverrides && state.dimensionAnchorOverrides.has(id)
    ? state.dimensionAnchorOverrides.get(id)
    : null;
}

function drawDimensionToolPreview(state) {
  if (!isDimensionTool(state) || !state.dimensionDraft || !state.dimensionDraft.complete) {
    return;
  }

  const anchorPoint = state.dimensionDraft.anchorPoint
    || (state.pointerScreenPoint ? screenToWorld(state, state.pointerScreenPoint) : null);
  if (!anchorPoint) {
    return;
  }

  const descriptor = getDimensionDescriptorFromReferences(
    state,
    state.dimensionDraft.selectionKeys,
    anchorPoint,
    state.dimensionDraft.radialDiameter,
    "dimension-preview");
  if (!descriptor) {
    return;
  }

  drawDimensionGraphics(state, descriptor, true);
}

function getDimensionDescriptorFromReferences(state, referenceKeys, anchorPoint, radialDiameter, id) {
  const model = getDimensionModelFromReferences(state, referenceKeys, radialDiameter);
  if (!model) {
    return null;
  }

  return {
    key: `preview-${id}`,
    id,
    kind: model.kind,
    label: getDimensionLabel(model.kind),
    value: model.value,
    anchorPoint,
    point: worldToScreen(state, anchorPoint),
    geometry: getDimensionGeometry(state, model.kind, model.referenceKeys, anchorPoint)
  };
}

function getDimensionModelFromReferences(state, referenceKeys, radialDiameter = false) {
  if (!Array.isArray(referenceKeys) || referenceKeys.length === 0) {
    return null;
  }

  if (referenceKeys.length === 1) {
    const reference = parseSketchReference(referenceKeys[0]);
    const entity = reference ? findDocumentEntity(state, reference.entityId) : null;
    const kind = entity ? getEntityKind(entity) : "";
    const line = resolveSketchLineReference(state, referenceKeys[0]);

    if (line) {
      const baseKey = getSketchReferenceBaseKey(reference);
      return {
        kind: "lineardistance",
        referenceKeys: [`${baseKey}:start`, `${baseKey}:end`],
        value: distanceBetweenWorldPoints(line.start, line.end)
      };
    }

    if (kind === "circle" || kind === "arc") {
      const circleLike = resolveSketchCircleLikeReference(state, referenceKeys[0]);
      if (!circleLike) {
        return null;
      }

      const dimensionKind = kind === "circle" || radialDiameter ? "diameter" : "radius";
      return {
        kind: dimensionKind,
        referenceKeys,
        value: dimensionKind === "diameter" ? circleLike.radius * 2 : circleLike.radius
      };
    }
  }

  if (referenceKeys.length >= 2) {
    const firstPoint = resolveSketchReferencePoint(state, referenceKeys[0]);
    const secondPoint = resolveSketchReferencePoint(state, referenceKeys[1]);
    if (firstPoint && secondPoint) {
      return {
        kind: "lineardistance",
        referenceKeys: referenceKeys.slice(0, 2),
        value: distanceBetweenWorldPoints(firstPoint, secondPoint)
      };
    }

    const firstLine = resolveSketchLineReference(state, referenceKeys[0]);
    const secondLine = resolveSketchLineReference(state, referenceKeys[1]);
    if (firstLine && secondLine) {
      return {
        kind: "angle",
        referenceKeys: referenceKeys.slice(0, 2),
        value: angleBetweenWorldLines(firstLine, secondLine)
      };
    }

    const line = firstLine || secondLine;
    const point = firstPoint || secondPoint;
    if (line && point) {
      return {
        kind: "pointtolinedistance",
        referenceKeys: referenceKeys.slice(0, 2),
        value: distanceBetweenWorldPoints(point, projectPointToWorldLine(point, line))
      };
    }
  }

  return null;
}

function getDimensionAnchorPoint(state, kind, referenceKeys) {
  if (kind === "radius" || kind === "diameter") {
    const circleLike = resolveSketchCircleLikeReference(state, referenceKeys[0]);
    if (!circleLike) {
      return null;
    }

    return {
      x: circleLike.center.x + circleLike.radius,
      y: circleLike.center.y
    };
  }

  const points = referenceKeys
    .map(key => resolveSketchReferencePoint(state, key))
    .filter(point => point !== null);
  if (points.length >= 2) {
    return midpoint(points[0], points[1]);
  }

  const entityAnchors = referenceKeys
    .map(key => resolveSketchEntityAnchorPoint(state, key))
    .filter(point => point !== null);
  if (entityAnchors.length >= 2) {
    return midpoint(entityAnchors[0], entityAnchors[1]);
  }

  return points[0] || entityAnchors[0] || null;
}

function getDimensionGeometry(state, kind, referenceKeys, anchorPoint) {
  if (kind === "radius" || kind === "diameter") {
    const circleLike = resolveSketchCircleLikeReference(state, referenceKeys[0]);
    return circleLike
      ? {
        type: "radial",
        center: circleLike.center,
        radius: circleLike.radius,
        anchorPoint,
        diameter: kind === "diameter"
      }
      : null;
  }

  if (kind === "pointtolinedistance") {
    const firstPoint = resolveSketchReferencePoint(state, referenceKeys[0]);
    const secondPoint = resolveSketchReferencePoint(state, referenceKeys[1]);
    const firstLine = resolveSketchLineReference(state, referenceKeys[0]);
    const secondLine = resolveSketchLineReference(state, referenceKeys[1]);
    const point = firstPoint || secondPoint;
    const line = firstLine || secondLine;
    if (!point || !line) {
      return null;
    }

    return {
      type: "linear",
      start: point,
      end: projectPointToWorldLine(point, line),
      anchorPoint
    };
  }

  if (kind === "angle") {
    const firstLine = resolveSketchLineReference(state, referenceKeys[0]);
    const secondLine = resolveSketchLineReference(state, referenceKeys[1]);
    return firstLine && secondLine
      ? {
        type: "angle",
        firstLine,
        secondLine,
        vertex: getWorldLineIntersection(firstLine, secondLine)
          || midpoint(
            midpoint(firstLine.start, firstLine.end),
            midpoint(secondLine.start, secondLine.end)),
        anchorPoint
      }
      : null;
  }

  const points = referenceKeys
    .map(key => resolveSketchReferencePoint(state, key))
    .filter(point => point !== null);
  if (points.length >= 2) {
    return {
      type: "linear",
      start: points[0],
      end: points[1],
      anchorPoint
    };
  }

  return null;
}

function drawDimensionGraphics(state, dimension, isPreview) {
  if (!dimension || !dimension.geometry) {
    return;
  }

  if (dimension.geometry.type === "linear") {
    drawLinearDimensionGraphics(state, dimension, isPreview);
  } else if (dimension.geometry.type === "radial") {
    drawRadialDimensionGraphics(state, dimension, isPreview);
  } else if (dimension.geometry.type === "angle") {
    drawAngleDimensionGraphics(state, dimension, isPreview);
  }
}

function drawLinearDimensionGraphics(state, dimension, isPreview) {
  const geometry = dimension.geometry;
  const start = worldToScreen(state, geometry.start);
  const end = worldToScreen(state, geometry.end);
  const anchor = worldToScreen(state, geometry.anchorPoint);
  const text = getDimensionDisplayText(dimension);
  const textWidth = text ? measureDimensionCanvasText(state, text) : 0;
  const screenGeometry = getLinearDimensionScreenGeometry(start, end, anchor, textWidth);
  if (!screenGeometry) {
    return;
  }

  const { context } = state;
  const style = getDimensionRenderStyle(isPreview);
  context.save();
  context.strokeStyle = style.strokeStyle;
  context.fillStyle = style.strokeStyle;
  context.lineWidth = 1.15;
  context.setLineDash(style.lineDash);

  for (const segment of screenGeometry.extensionSegments) {
    drawScreenLine(context, segment.start, segment.end);
  }

  for (const segment of screenGeometry.dimensionSegments) {
    drawScreenLine(context, segment.start, segment.end);
  }

  for (const arrow of screenGeometry.arrows) {
    drawArrowhead(context, arrow.point, arrow.toward, DIMENSION_ARROWHEAD_SIZE);
  }

  context.restore();

  drawDimensionCanvasText(state, dimension, isPreview);
}

export function getLinearDimensionScreenGeometry(start, end, anchor, textWidth = 0) {
  const vector = normalizeScreenVector({ x: end.x - start.x, y: end.y - start.y });
  if (!vector) {
    return null;
  }

  const startOffset = dotScreenPoints(subtractScreenPoints(start, anchor), vector);
  const endOffset = dotScreenPoints(subtractScreenPoints(end, anchor), vector);
  const dimensionStart = { x: anchor.x + vector.x * startOffset, y: anchor.y + vector.y * startOffset };
  const dimensionEnd = { x: anchor.x + vector.x * endOffset, y: anchor.y + vector.y * endOffset };
  const dimensionLength = distanceBetweenScreenPoints(dimensionStart, dimensionEnd);
  const textGapHalfWidth = Math.min(dimensionLength / 2, Math.max(14, textWidth / 2 + DIMENSION_TEXT_GAP_PADDING));
  const firstExtension = getExtensionSegmentPastDimensionLine(start, dimensionStart);
  const secondExtension = getExtensionSegmentPastDimensionLine(end, dimensionEnd);
  const useInsideArrows = dimensionLength >= Math.max(36, textWidth + (DIMENSION_ARROWHEAD_SIZE * 4));
  const arrows = useInsideArrows
    ? [
      {
        point: dimensionStart,
        toward: { x: dimensionStart.x - vector.x, y: dimensionStart.y - vector.y }
      },
      {
        point: dimensionEnd,
        toward: { x: dimensionEnd.x + vector.x, y: dimensionEnd.y + vector.y }
      }
    ]
    : [
      {
        point: dimensionStart,
        toward: { x: dimensionStart.x + vector.x, y: dimensionStart.y + vector.y }
      },
      {
        point: dimensionEnd,
        toward: { x: dimensionEnd.x - vector.x, y: dimensionEnd.y - vector.y }
      }
    ];

  return {
    extensionSegments: [
      { start, end: firstExtension },
      { start: end, end: secondExtension }
    ],
    dimensionSegments: getLinearDimensionSegmentsAroundText(dimensionStart, dimensionEnd, anchor, textGapHalfWidth),
    arrows
  };
}

function getLinearDimensionSegmentsAroundText(start, end, anchor, textGap) {
  const direction = normalizeScreenVector({
    x: end.x - start.x,
    y: end.y - start.y
  });
  if (!direction) {
    return [];
  }

  const totalLength = distanceBetweenScreenPoints(start, end);
  const centerOffset = dotScreenPoints(subtractScreenPoints(anchor, start), direction);
  const gapStartOffset = centerOffset - textGap;
  const gapEndOffset = centerOffset + textGap;
  if (gapStartOffset > totalLength) {
    return [
      { start, end },
      {
        start: end,
        end: {
          x: start.x + direction.x * gapStartOffset,
          y: start.y + direction.y * gapStartOffset
        }
      }
    ];
  }

  if (gapEndOffset < 0) {
    return [
      {
        start: {
          x: start.x + direction.x * gapEndOffset,
          y: start.y + direction.y * gapEndOffset
        },
        end: start
      },
      { start, end }
    ];
  }

  return getSegmentPartsAroundPoint(start, end, anchor, textGap);
}

function getExtensionSegmentPastDimensionLine(referencePoint, dimensionPoint) {
  const direction = normalizeScreenVector(subtractScreenPoints(dimensionPoint, referencePoint));
  if (!direction) {
    return dimensionPoint;
  }

  return {
    x: dimensionPoint.x + direction.x * 6,
    y: dimensionPoint.y + direction.y * 6
  };
}

function drawRadialDimensionGraphics(state, dimension, isPreview) {
  const geometry = dimension.geometry;
  const center = worldToScreen(state, geometry.center);
  const anchor = worldToScreen(state, geometry.anchorPoint);
  const radius = geometry.radius * state.view.scale;
  const text = getDimensionDisplayText(dimension);
  const textWidth = text ? measureDimensionCanvasText(state, text) : 0;
  const radialGeometry = getRadialDimensionScreenGeometry(center, radius, anchor, geometry.diameter, textWidth);
  const { context } = state;
  const style = getDimensionRenderStyle(isPreview);

  context.save();
  context.strokeStyle = style.strokeStyle;
  context.fillStyle = style.strokeStyle;
  context.lineWidth = 1.15;
  context.setLineDash(style.lineDash);

  for (const segment of radialGeometry.segments) {
    drawScreenLine(context, segment.start, segment.end);
  }

  for (const arrow of radialGeometry.arrows) {
    drawArrowhead(context, arrow.point, arrow.toward, DIMENSION_ARROWHEAD_SIZE);
  }

  context.restore();
  drawDimensionCanvasText(state, dimension, isPreview);
}

export function getRadialDimensionScreenGeometry(center, radius, anchor, diameter = false, textWidth = 0) {
  const direction = normalizeScreenVector({
    x: anchor.x - center.x,
    y: anchor.y - center.y
  }) || { x: 1, y: 0 };
  const anchorDistance = distanceBetweenScreenPoints(center, anchor);
  const isInside = anchorDistance < radius;
  const textGap = Math.max(DIMENSION_INPUT_LEADER_GAP, textWidth / 2 + DIMENSION_TEXT_GAP_PADDING);
  const edge = {
    x: center.x + direction.x * radius,
    y: center.y + direction.y * radius
  };

  if (diameter) {
    if (isInside) {
      const oppositeEdge = {
        x: center.x - direction.x * radius,
        y: center.y - direction.y * radius
      };
      return {
        segments: getSegmentPartsAroundPoint(oppositeEdge, edge, anchor, textGap),
        arrows: [
          { point: oppositeEdge, toward: { x: oppositeEdge.x - direction.x, y: oppositeEdge.y - direction.y } },
          { point: edge, toward: { x: edge.x + direction.x, y: edge.y + direction.y } }
        ]
      };
    }

    return {
      segments: getLeaderSegmentsToAnchor(edge, anchor, textGap),
      arrows: [{ point: edge, toward: center }]
    };
  }

  if (isInside) {
    return {
      segments: getSegmentPartsAroundPoint(center, edge, anchor, textGap),
      arrows: [{ point: edge, toward: { x: edge.x + direction.x, y: edge.y + direction.y } }]
    };
  }

  return {
    segments: getLeaderSegmentsToAnchor(edge, anchor, textGap),
    arrows: [{ point: edge, toward: center }]
  };
}

function getLeaderSegmentsToAnchor(start, anchor, textGap) {
  const directionToStart = normalizeScreenVector({
    x: start.x - anchor.x,
    y: start.y - anchor.y
  });
  if (!directionToStart) {
    return [];
  }

  const distance = distanceBetweenScreenPoints(start, anchor);
  const gap = Math.min(Math.max(0, distance - 2), textGap);
  const leaderEnd = {
    x: anchor.x + directionToStart.x * gap,
    y: anchor.y + directionToStart.y * gap
  };
  return distance > gap + WORLD_GEOMETRY_TOLERANCE
    ? [{ start, end: leaderEnd }]
    : [];
}

function getSegmentPartsAroundPoint(start, end, gapCenter, textGap) {
  const direction = normalizeScreenVector({
    x: end.x - start.x,
    y: end.y - start.y
  });
  if (!direction) {
    return [];
  }

  const totalLength = distanceBetweenScreenPoints(start, end);
  const centerOffset = clamp(
    dotScreenPoints(subtractScreenPoints(gapCenter, start), direction),
    0,
    totalLength);
  const gap = Math.min(textGap, totalLength / 2);
  const gapStartOffset = clamp(centerOffset - gap, 0, totalLength);
  const gapEndOffset = clamp(centerOffset + gap, 0, totalLength);
  const gapStart = {
    x: start.x + direction.x * gapStartOffset,
    y: start.y + direction.y * gapStartOffset
  };
  const gapEnd = {
    x: start.x + direction.x * gapEndOffset,
    y: start.y + direction.y * gapEndOffset
  };
  const segments = [];
  if (gapStartOffset > WORLD_GEOMETRY_TOLERANCE) {
    segments.push({ start, end: gapStart });
  }

  if (totalLength - gapEndOffset > WORLD_GEOMETRY_TOLERANCE) {
    segments.push({ start: gapEnd, end });
  }

  return segments;
}

function drawAngleDimensionGraphics(state, dimension, isPreview) {
  const geometry = getAngleDimensionScreenGeometry(
    state,
    dimension.geometry.firstLine,
    dimension.geometry.secondLine,
    dimension.geometry.vertex,
    dimension.geometry.anchorPoint);
  if (!geometry) {
    return;
  }

  const { context } = state;
  const style = getDimensionRenderStyle(isPreview);
  context.save();
  context.strokeStyle = style.strokeStyle;
  context.fillStyle = style.strokeStyle;
  context.lineWidth = 1.15;
  context.setLineDash(style.lineDash);
  for (const segment of geometry.extensionSegments) {
    drawScreenLine(context, segment.start, segment.end);
  }

  context.beginPath();
  context.arc(geometry.vertex.x, geometry.vertex.y, geometry.radius, geometry.sweep.start, geometry.sweep.end);
  context.stroke();
  for (const arrow of geometry.arrows) {
    drawArrowhead(context, arrow.point, arrow.toward, DIMENSION_ARROWHEAD_SIZE);
  }

  context.restore();
  drawDimensionCanvasText(state, dimension, isPreview);
}

export function getAngleDimensionScreenGeometry(state, firstLine, secondLine, vertex, anchor) {
  if (!vertex || !anchor) {
    return null;
  }

  const screenVertex = worldToScreen(state, vertex);
  const screenAnchor = worldToScreen(state, anchor);
  const firstAxis = getScreenLineDirection(state, firstLine);
  const secondAxis = getScreenLineDirection(state, secondLine);
  if (!firstAxis || !secondAxis) {
    return null;
  }

  const anchorDirection = normalizeScreenVector(subtractScreenPoints(screenAnchor, screenVertex));
  const sweep = getAngleSweepForAnchor(firstAxis, secondAxis, anchorDirection);
  if (!sweep) {
    return null;
  }

  const radius = Math.max(18, distanceBetweenScreenPoints(screenVertex, screenAnchor));
  const extensionLength = radius + 12;
  const firstExtensionEnd = pointFromScreenAngle(screenVertex, sweep.start, extensionLength);
  const secondExtensionEnd = pointFromScreenAngle(screenVertex, sweep.end, extensionLength);
  const arcStart = pointFromScreenAngle(screenVertex, sweep.start, radius);
  const arcEnd = pointFromScreenAngle(screenVertex, sweep.end, radius);
  const arrowSweep = Math.max(0.05, Math.min(0.18, Math.abs(sweep.end - sweep.start) / 5));

  return {
    vertex: screenVertex,
    radius,
    sweep,
    extensionSegments: [
      { start: screenVertex, end: firstExtensionEnd },
      { start: screenVertex, end: secondExtensionEnd }
    ],
    arrows: [
      {
        point: arcStart,
        toward: pointFromScreenAngle(screenVertex, sweep.start + arrowSweep, radius)
      },
      {
        point: arcEnd,
        toward: pointFromScreenAngle(screenVertex, sweep.end - arrowSweep, radius)
      }
    ]
  };
}

function getScreenLineDirection(state, line) {
  if (!line || !line.start || !line.end) {
    return null;
  }

  const start = worldToScreen(state, line.start);
  const end = worldToScreen(state, line.end);
  return normalizeScreenVector({
    x: end.x - start.x,
    y: end.y - start.y
  });
}

function getAngleSweepForAnchor(firstAxis, secondAxis, anchorDirection) {
  const firstAngles = [
    Math.atan2(firstAxis.y, firstAxis.x),
    Math.atan2(-firstAxis.y, -firstAxis.x)
  ];
  const secondAngles = [
    Math.atan2(secondAxis.y, secondAxis.x),
    Math.atan2(-secondAxis.y, -secondAxis.x)
  ];
  const anchorAngle = anchorDirection
    ? Math.atan2(anchorDirection.y, anchorDirection.x)
    : null;
  let best = null;

  for (const firstAngle of firstAngles) {
    for (const secondAngle of secondAngles) {
      const sweep = getShortestScreenAngleSweep(firstAngle, secondAngle);
      const containsAnchor = anchorAngle !== null && isScreenAngleWithinSweep(anchorAngle, sweep);
      const score = (containsAnchor ? 0 : 1000)
        + getScreenAngleDistance(anchorAngle ?? getSweepMidAngle(sweep), getSweepMidAngle(sweep));
      if (!best || score < best.score) {
        best = { ...sweep, score };
      }
    }
  }

  return best ? { start: best.start, end: best.end } : null;
}

function getShortestScreenAngleSweep(startAngle, endAngle) {
  const fullCircle = Math.PI * 2;
  let delta = (endAngle - startAngle) % fullCircle;
  if (delta < 0) {
    delta += fullCircle;
  }

  if (delta <= Math.PI) {
    return {
      start: startAngle,
      end: startAngle + delta
    };
  }

  return {
    start: endAngle,
    end: endAngle + (fullCircle - delta)
  };
}

function isScreenAngleWithinSweep(angle, sweep) {
  const fullCircle = Math.PI * 2;
  const delta = ((angle - sweep.start) % fullCircle + fullCircle) % fullCircle;
  return delta <= Math.abs(sweep.end - sweep.start) + 0.000001;
}

function getSweepMidAngle(sweep) {
  return sweep.start + (sweep.end - sweep.start) / 2;
}

function getScreenAngleDistance(first, second) {
  const fullCircle = Math.PI * 2;
  const delta = Math.abs(((first - second) % fullCircle + fullCircle) % fullCircle);
  return Math.min(delta, fullCircle - delta);
}

function pointFromScreenAngle(origin, angle, distance) {
  return {
    x: origin.x + Math.cos(angle) * distance,
    y: origin.y + Math.sin(angle) * distance
  };
}

function drawDimensionCanvasText(state, dimension, isPreview) {
  const text = getDimensionDisplayText(dimension);
  if (!text || state.dimensionOverlay && !isPreview) {
    return;
  }

  drawFloatingDimensionLabel(state, text, dimension.point);
}

function measureDimensionCanvasText(state, text) {
  if (!text || !state || !state.context) {
    return 0;
  }

  const { context } = state;
  context.save();
  context.font = "600 12px Segoe UI, system-ui, sans-serif";
  const width = context.measureText(text).width;
  context.restore();
  return width;
}

function getDimensionRenderStyle(isPreview) {
  return {
    strokeStyle: isPreview ? DIMENSION_PREVIEW_STROKE_STYLE : DIMENSION_LAYER_STROKE_STYLE,
    textStyle: isPreview ? DIMENSION_PREVIEW_STROKE_STYLE : DIMENSION_LAYER_TEXT_STYLE,
    lineDash: isPreview ? [5, 4] : []
  };
}

export function getDimensionDisplayText(dimension) {
  const value = formatDimensionValue(dimension && dimension.value);
  if (!value) {
    return "";
  }

  switch (String(dimension && dimension.kind || "").toLowerCase()) {
    case "radius":
      return `R${value}`;
    case "diameter":
      return `\u2300${value}`;
    case "angle":
      return value;
    default:
      return value;
  }
}

function getDimensionLabel(kind) {
  switch (kind) {
    case "radius":
      return "Radius";
    case "diameter":
      return "Diameter";
    case "angle":
      return "Angle";
    case "horizontaldistance":
      return "Horizontal distance";
    case "verticaldistance":
      return "Vertical distance";
    case "pointtolinedistance":
      return "Point-to-line distance";
    default:
      return "Distance";
  }
}

function updatePersistentDimensionInputs(state, dimensions) {
  const overlay = state.dimensionOverlay;
  if (!overlay) {
    return;
  }

  const activeIds = new Set(dimensions.map(dimension => dimension.id));
  for (const id of Array.from(state.persistentDimensionInputs.keys())) {
    if (!activeIds.has(id)) {
      const input = state.persistentDimensionInputs.get(id);
      input.remove();
      state.persistentDimensionInputs.delete(id);
    }
  }

  if (state.dimensionAnchorOverrides) {
    for (const id of Array.from(state.dimensionAnchorOverrides.keys())) {
      if (!activeIds.has(id)) {
        state.dimensionAnchorOverrides.delete(id);
      }
    }
  }

  for (const dimension of dimensions) {
    const input = getOrCreatePersistentDimensionInput(state, dimension);
    const isFocused = document.activeElement === input;
    const isEditing = input.dataset.dimensionEditing === "true";
    if (!isFocused && !isEditing) {
      input.value = getDimensionDisplayText(dimension);
    }

    input.dataset.dimensionId = dimension.id;
    input.dataset.dimensionLabel = dimension.label;
    input.dataset.dimensionKind = dimension.kind;
    input.dataset.dimensionValue = String(dimension.value);
    input.dataset.dimensionEditing = isEditing ? "true" : "false";
    const inputPoint = clampDimensionInputScreenPoint(state, dimension.point);
    input.style.left = `${inputPoint.x}px`;
    input.style.top = `${inputPoint.y}px`;
  }
}

function getOrCreatePersistentDimensionInput(state, dimension) {
  let input = state.persistentDimensionInputs.get(dimension.id);
  if (input) {
    return input;
  }

  input = document.createElement("input");
  input.type = "text";
  input.step = "0.001";
  input.inputMode = "decimal";
  input.className = "drawing-dimension-input drawing-persistent-dimension-input";
  input.style.pointerEvents = "auto";
  input.style.zIndex = "6";
  input.title = dimension.label;
  input.setAttribute("aria-label", dimension.label);
  input.dataset.dimensionId = dimension.id;
  input.addEventListener("pointerdown", event => handlePersistentDimensionPointerDown(state, input, event));
  input.addEventListener("pointermove", event => handlePersistentDimensionPointerMove(state, input, event));
  input.addEventListener("pointerup", event => handlePersistentDimensionPointerUp(state, input, event));
  input.addEventListener("pointercancel", event => handlePersistentDimensionPointerCancel(state, input, event));
  input.addEventListener("click", event => event.stopPropagation());
  input.addEventListener("dblclick", event => handlePersistentDimensionDoubleClick(state, input, event));
  input.addEventListener("focus", () => {
    input.dataset.dimensionEditing = "true";
    input.dataset.dimensionDragging = "false";
    input.classList.add("drawing-dimension-input-active");
    selectInputText(input);
  });
  input.addEventListener("blur", () => {
    input.dataset.dimensionEditing = "false";
    input.classList.remove("drawing-dimension-input-active");
    commitPersistentDimensionInputValue(state, input);
  });
  input.addEventListener("keydown", event => handlePersistentDimensionInputKeyDown(state, input, event));
  input.addEventListener("change", () => commitPersistentDimensionInputValue(state, input));

  state.dimensionOverlay.appendChild(input);
  state.persistentDimensionInputs.set(dimension.id, input);
  return input;
}

function handlePersistentDimensionPointerDown(state, input, event) {
  event.stopPropagation();
  if (!isPrimaryPointerButton(event)) {
    return;
  }

  const screenPoint = getPointerScreenPoint(state, event);
  state.dimensionDrag = {
    dimensionId: input.dataset.dimensionId,
    pointerId: event.pointerId,
    startScreenPoint: screenPoint,
    currentScreenPoint: screenPoint,
    moved: false
  };
  input.dataset.dimensionDragging = "false";
  capturePointer(input, event.pointerId);
  event.preventDefault();
}

function handlePersistentDimensionPointerMove(state, input, event) {
  const drag = state.dimensionDrag;
  if (!drag || drag.pointerId !== event.pointerId || drag.dimensionId !== input.dataset.dimensionId) {
    return;
  }

  const screenPoint = getPointerScreenPoint(state, event);
  drag.currentScreenPoint = screenPoint;
  if (drag.moved || distanceBetweenScreenPoints(screenPoint, drag.startScreenPoint) > CLICK_MOVE_TOLERANCE) {
    drag.moved = true;
    input.dataset.dimensionDragging = "true";
    const request = getDimensionAnchorUpdateRequest(state, drag.dimensionId, screenPoint);
    if (request && state.dimensionAnchorOverrides) {
      state.dimensionAnchorOverrides.set(request.dimensionId, request.anchor);
      draw(state);
    }
  }

  event.preventDefault();
  event.stopPropagation();
}

function handlePersistentDimensionPointerUp(state, input, event) {
  const drag = state.dimensionDrag;
  if (!drag || drag.pointerId !== event.pointerId || drag.dimensionId !== input.dataset.dimensionId) {
    event.stopPropagation();
    return;
  }

  releasePointer(input, event.pointerId);
  state.dimensionDrag = null;
  input.dataset.dimensionDragging = "false";

  if (drag.moved) {
    const request = getDimensionAnchorUpdateRequest(state, drag.dimensionId, drag.currentScreenPoint);
    if (request) {
      invokeDotNet(state, "OnSketchDimensionAnchorChanged", request.dimensionId, request.anchor.x, request.anchor.y);
    }

    focusCanvasWithoutDimensionCommit(state);
  } else {
    selectPersistentDimension(state, input.dataset.dimensionId);
    focusCanvasWithoutDimensionCommit(state);
  }

  event.preventDefault();
  event.stopPropagation();
}

function handlePersistentDimensionPointerCancel(state, input, event) {
  const drag = state.dimensionDrag;
  if (drag && drag.pointerId === event.pointerId) {
    state.dimensionDrag = null;
  }

  input.dataset.dimensionDragging = "false";
  releasePointer(input, event.pointerId);
  event.stopPropagation();
}

export function getDimensionAnchorUpdateRequest(state, dimensionId, screenPoint) {
  const id = String(dimensionId || "");
  if (!id || !screenPoint) {
    return null;
  }

  return {
    dimensionId: id,
    anchor: screenToWorld(state, screenPoint)
  };
}

function handlePersistentDimensionInputKeyDown(state, input, event) {
  event.stopPropagation();

  if (event.key === "Enter") {
    commitPersistentDimensionInputValue(state, input);
    focusCanvas(state.canvas);
    event.preventDefault();
  } else if (event.key === "Escape") {
    input.dataset.dimensionEditing = "false";
    focusCanvas(state.canvas);
    event.preventDefault();
  } else if (event.key === "Tab") {
    focusNextPersistentDimensionInput(state, input.dataset.dimensionId, event.shiftKey);
    event.preventDefault();
  }
}

function focusNextPersistentDimensionInput(state, currentId, reverse) {
  const ids = Array.from(state.persistentDimensionInputs.keys());
  if (ids.length === 0) {
    return;
  }

  const index = Math.max(0, ids.indexOf(currentId));
  const nextId = ids[(index + (reverse ? -1 : 1) + ids.length) % ids.length];
  const nextInput = state.persistentDimensionInputs.get(nextId);
  if (nextInput) {
    focusElement(nextInput);
    selectInputText(nextInput);
  }
}

function commitPersistentDimensionInputValue(state, input) {
  const id = input.dataset.dimensionId;
  const commit = getPersistentDimensionCommitValue(input.value, input.dataset.dimensionValue);
  input.dataset.dimensionEditing = "false";
  if (!id || !commit.shouldCommit) {
    input.value = getDimensionDisplayText({
      kind: input.dataset.dimensionKind,
      value: commit.value
    });
    return false;
  }

  invokeDotNet(state, "OnSketchDimensionValueChanged", id, commit.value);
  return true;
}

function handlePersistentDimensionDoubleClick(state, input, event) {
  event.preventDefault();
  event.stopPropagation();
  selectPersistentDimension(state, input.dataset.dimensionId);
  focusElement(input);
  selectInputText(input);
}

function selectPersistentDimension(state, dimensionId) {
  const id = String(dimensionId || "");
  if (!id) {
    return false;
  }

  const selectionKey = `persistent-${id}`;
  const changed = state.activeSelectionKey !== selectionKey
    || state.selectedKeys.size !== 1
    || !state.selectedKeys.has(selectionKey);
  state.selectedKeys.clear();
  state.selectedKeys.add(selectionKey);
  state.activeSelectionKey = selectionKey;
  if (changed) {
    notifySelectionChanged(state);
    updateDebugAttributes(state);
    draw(state);
  }

  return changed;
}

export function getPersistentDimensionCommitValue(text, fallbackValue) {
  const fallback = Number(fallbackValue);
  const safeFallback = Number.isFinite(fallback) ? fallback : 0;
  const value = parseDimensionInputNumber(text);
  if (!Number.isFinite(value) || value <= WORLD_GEOMETRY_TOLERANCE) {
    return {
      shouldCommit: false,
      value: safeFallback
    };
  }

  return {
    shouldCommit: true,
    value
  };
}

function parseDimensionInputNumber(text) {
  const match = String(text || "")
    .trim()
    .match(/[+-]?(?:\d+(?:\.\d*)?|\.\d+)/);
  return match ? Number(match[0]) : Number.NaN;
}

function clearPersistentDimensionInputs(state) {
  if (!state.persistentDimensionInputs) {
    return;
  }

  for (const input of state.persistentDimensionInputs.values()) {
    input.remove();
  }

  state.persistentDimensionInputs.clear();
}

function drawConstraintGlyphGroups(state, groups) {
  for (const group of groups) {
    drawConstraintGlyphGroup(state, group);
  }
}

function drawConstraintGlyphGroup(state, group) {
  if (!group || !Array.isArray(group.constraints) || group.constraints.length === 0 || !group.point) {
    return;
  }

  const { context } = state;
  const dragging = state.constraintGroupDrag && state.constraintGroupDrag.groupKey === group.key;
  context.save();
  if (dragging) {
    context.shadowColor = "rgba(14, 165, 233, 0.45)";
    context.shadowBlur = 10;
  }

  for (let index = 0; index < group.constraints.length; index++) {
    const constraint = group.constraints[index];
    const text = getConstraintGlyphText(getSketchItemKind(constraint));
    const point = getConstraintGlyphItemPoint(group, index);
    drawConstraintGlyph(state, text, point, constraint);
  }

  context.restore();
}

function getConstraintGlyphItemPoint(group, index) {
  const count = Math.max(1, group.constraints.length);
  const width = count * CONSTRAINT_GLYPH_SIZE + (count - 1) * CONSTRAINT_GLYPH_GAP;
  return {
    x: group.point.x - width / 2 + CONSTRAINT_GLYPH_SIZE / 2 + index * (CONSTRAINT_GLYPH_SIZE + CONSTRAINT_GLYPH_GAP),
    y: group.point.y
  };
}

export function getVisibleConstraintGlyphGroups(state) {
  const groups = getConstraintGlyphGroups(state);
  if (state.showAllConstraints) {
    return groups;
  }

  return groups.filter(group => isConstraintGroupRelatedToTarget(group, state.hoveredTarget));
}

export function getConstraintGlyphGroups(state) {
  const constraints = getDocumentConstraints(state.document);
  const groupsByKey = new Map();
  for (const constraint of constraints) {
    if (!getConstraintGlyphText(getSketchItemKind(constraint))) {
      continue;
    }

    const anchor = getConstraintAnchorPoint(state, constraint);
    if (!anchor) {
      continue;
    }

    const key = getConstraintGroupKey(constraint);
    if (!key) {
      continue;
    }

    let group = groupsByKey.get(key);
    if (!group) {
      group = {
        key,
        constraints: [],
        referenceKeys: new Set(),
        anchor: { x: 0, y: 0 },
        anchorCount: 0
      };
      groupsByKey.set(key, group);
    }

    group.constraints.push(constraint);
    group.anchor.x += anchor.x;
    group.anchor.y += anchor.y;
    group.anchorCount += 1;
    for (const relationKey of getConstraintRelationKeys(constraint)) {
      group.referenceKeys.add(relationKey);
    }
  }

  return Array.from(groupsByKey.values())
    .map(group => finalizeConstraintGlyphGroup(state, group))
    .filter(group => group !== null);
}

function finalizeConstraintGlyphGroup(state, group) {
  if (!group || group.anchorCount <= 0 || group.constraints.length === 0) {
    return null;
  }

  const anchor = {
    x: group.anchor.x / group.anchorCount,
    y: group.anchor.y / group.anchorCount
  };
  const anchorScreenPoint = worldToScreen(state, anchor);
  const offset = getConstraintGroupOffset(state, group.key);
  const point = {
    x: anchorScreenPoint.x + offset.x,
    y: anchorScreenPoint.y + offset.y
  };
  const constraints = group.constraints.slice().sort(compareConstraintsById);
  const result = {
    key: group.key,
    constraints,
    referenceKeys: Array.from(group.referenceKeys),
    anchor,
    anchorScreenPoint,
    point
  };
  result.rect = getConstraintGlyphGroupRect(result);
  return result;
}

function compareConstraintsById(first, second) {
  return getSketchItemId(first).localeCompare(getSketchItemId(second), undefined, { sensitivity: "base" });
}

function getConstraintGlyphGroupRect(group) {
  const count = Math.max(1, group.constraints.length);
  const width = count * CONSTRAINT_GLYPH_SIZE + (count - 1) * CONSTRAINT_GLYPH_GAP;
  return {
    minX: group.point.x - width / 2,
    minY: group.point.y - CONSTRAINT_GLYPH_SIZE / 2,
    maxX: group.point.x + width / 2,
    maxY: group.point.y + CONSTRAINT_GLYPH_SIZE / 2
  };
}

function getConstraintGroupKey(constraint) {
  const referenceKeys = getSketchReferenceKeys(constraint)
    .map(getConstraintReferenceGroupKey)
    .filter(key => key.length > 0)
    .sort();
  if (referenceKeys.length > 0) {
    return `constraint-group:${referenceKeys.join("+")}`;
  }

  const id = getSketchItemId(constraint);
  return id ? `constraint:${id}` : "";
}

function getConstraintReferenceGroupKey(referenceKey) {
  const reference = parseSketchReference(referenceKey);
  if (!reference) {
    return String(referenceKey || "").trim();
  }

  const baseKey = getSketchReferenceBaseKey(reference);
  return reference.target && reference.target !== "entity"
    ? `${baseKey}:${reference.target}`
    : baseKey;
}

function getConstraintRelationKeys(constraint) {
  const keys = new Set();
  for (const referenceKey of getSketchReferenceKeys(constraint)) {
    const rawKey = String(referenceKey || "").trim();
    if (rawKey) {
      keys.add(rawKey);
    }

    const reference = parseSketchReference(rawKey);
    if (!reference) {
      continue;
    }

    const baseKey = getSketchReferenceBaseKey(reference);
    if (reference.entityId) {
      keys.add(reference.entityId);
    }

    if (baseKey) {
      keys.add(baseKey);
    }

    if (baseKey && reference.target && reference.target !== "entity") {
      keys.add(`${baseKey}:${reference.target}`);
    }
  }

  return keys;
}

function isConstraintGroupRelatedToTarget(group, target) {
  if (!group || !target) {
    return false;
  }

  const relationKeys = new Set(group.referenceKeys || []);
  for (const targetKey of getConstraintTargetRelationKeys(target)) {
    if (relationKeys.has(targetKey)) {
      return true;
    }
  }

  return false;
}

function getConstraintTargetRelationKeys(target) {
  const keys = new Set();
  if (!target) {
    return keys;
  }

  if (target.key) {
    keys.add(target.key);
  }

  if (target.entityId) {
    keys.add(target.entityId);
  }

  if (target.kind === "point" && target.entityId) {
    const referenceTarget = normalizeSketchReferenceTarget(target.label);
    if (referenceTarget !== "entity") {
      keys.add(`${target.entityId}:${referenceTarget}`);
    }
  }

  if (target.kind === "segment" && target.entityId && Number.isInteger(target.segmentIndex)) {
    keys.add(`${target.entityId}${SEGMENT_KEY_SEPARATOR}${target.segmentIndex}`);
  }

  return keys;
}

function getConstraintGroupOffset(state, groupKey) {
  const offset = state.constraintGroupOffsets && state.constraintGroupOffsets.get(groupKey);
  return offset && Number.isFinite(offset.x) && Number.isFinite(offset.y)
    ? offset
    : { x: 0, y: 0 };
}

function setConstraintGroupOffset(state, groupKey, offset) {
  if (!state.constraintGroupOffsets || !groupKey || !offset) {
    return false;
  }

  const x = Number(offset.x);
  const y = Number(offset.y);
  if (!Number.isFinite(x) || !Number.isFinite(y)) {
    return false;
  }

  state.constraintGroupOffsets.set(groupKey, { x, y });
  return true;
}

function pruneConstraintGroupOffsets(state) {
  if (!state.constraintGroupOffsets || state.constraintGroupOffsets.size === 0) {
    return;
  }

  const activeGroupKeys = new Set(getConstraintGlyphGroups(state).map(group => group.key));
  for (const groupKey of Array.from(state.constraintGroupOffsets.keys())) {
    if (!activeGroupKeys.has(groupKey)) {
      state.constraintGroupOffsets.delete(groupKey);
    }
  }
}

function getConstraintGlyphGroupHit(state, screenPoint) {
  if (!state.showAllConstraints || !screenPoint) {
    return null;
  }

  let nearestHit = null;
  for (const group of getVisibleConstraintGlyphGroups(state)) {
    const distance = getConstraintGlyphGroupScreenDistance(group, screenPoint);
    if (distance > CONSTRAINT_GROUP_HIT_PADDING) {
      continue;
    }

    if (!nearestHit || distance < nearestHit.distance) {
      nearestHit = {
        group,
        distance
      };
    }
  }

  return nearestHit;
}

function getConstraintGlyphGroupScreenDistance(group, screenPoint) {
  if (!group || !group.rect || !screenPoint) {
    return Number.POSITIVE_INFINITY;
  }

  const dx = Math.max(0, group.rect.minX - screenPoint.x, screenPoint.x - group.rect.maxX);
  const dy = Math.max(0, group.rect.minY - screenPoint.y, screenPoint.y - group.rect.maxY);
  return Math.hypot(dx, dy);
}

function startConstraintGroupDrag(state, candidate, screenPoint) {
  if (!candidate || !candidate.constraintGroupKey || !state.showAllConstraints) {
    return false;
  }

  const anchorScreenPoint = candidate.constraintGroupAnchorScreenPoint;
  if (!anchorScreenPoint) {
    return false;
  }

  state.constraintGroupDrag = {
    pointerId: candidate.pointerId,
    groupKey: candidate.constraintGroupKey,
    anchorScreenPoint,
    startScreenPoint: candidate.screenPoint,
    currentScreenPoint: screenPoint
  };
  updateConstraintGroupDrag(state, screenPoint);
  return true;
}

function updateConstraintGroupDrag(state, screenPoint) {
  const drag = state.constraintGroupDrag;
  if (!drag || !screenPoint) {
    return false;
  }

  drag.currentScreenPoint = screenPoint;
  return setConstraintGroupOffset(state, drag.groupKey, {
    x: screenPoint.x - drag.anchorScreenPoint.x,
    y: screenPoint.y - drag.anchorScreenPoint.y
  });
}

function getConstraintAnchorPoint(state, constraint) {
  const referenceKeys = getSketchReferenceKeys(constraint);
  const points = referenceKeys
    .map(key => resolveSketchReferencePoint(state, key) || resolveSketchEntityAnchorPoint(state, key))
    .filter(point => point !== null);
  if (points.length === 0) {
    return null;
  }

  const sum = points.reduce((total, point) => ({
    x: total.x + point.x,
    y: total.y + point.y
  }), { x: 0, y: 0 });
  return {
    x: sum.x / points.length,
    y: sum.y / points.length
  };
}

function drawConstraintGlyph(state, text, point, constraint) {
  if (!text) {
    return;
  }

  const { context } = state;
  const stateText = getSketchConstraintState(constraint);
  context.save();
  context.font = "700 9px Segoe UI, system-ui, sans-serif";
  context.textAlign = "center";
  context.textBaseline = "middle";
  context.fillStyle = stateText === "unsatisfied"
    ? "rgba(127, 29, 29, 0.78)"
    : "rgba(51, 65, 85, 0.82)";
  context.strokeStyle = stateText === "unsatisfied" ? "#f87171" : "#64748b";
  context.lineWidth = 1;
  context.setLineDash([]);
  context.beginPath();
  context.rect(point.x - 8, point.y - 8, 16, 16);
  context.fill();
  context.stroke();
  context.fillStyle = stateText === "unsatisfied" ? "#fecaca" : "#e2e8f0";
  context.fillText(text, point.x, point.y + 0.5);
  context.restore();
}

export function getConstraintGlyphText(kind) {
  switch (kind) {
    case "coincident":
      return "C";
    case "concentric":
      return "O";
    case "parallel":
      return "//";
    case "horizontal":
      return "H";
    case "vertical":
      return "V";
    case "perpendicular":
      return "L";
    case "equal":
      return "=";
    case "midpoint":
      return "M";
    case "fix":
      return "F";
    default:
      return "";
  }
}

function updateDimensionInputs(state, dimensions) {
  const overlay = state.dimensionOverlay;
  if (!overlay) {
    return;
  }

  const activeKeys = new Set(dimensions.map(dimension => dimension.key));
  for (const key of Array.from(state.dimensionInputs.keys())) {
    if (!activeKeys.has(key)) {
      const input = state.dimensionInputs.get(key);
      input.remove();
      state.dimensionInputs.delete(key);
    }
  }

  state.visibleDimensionKeys = getDimensionKeys(dimensions);
  const focusedInput = getFocusedDimensionInput(state);
  const focusedDimensionKey = focusedInput && state.dimensionInputs.has(focusedInput.dataset.dimensionKey)
    ? focusedInput.dataset.dimensionKey
    : null;
  const activeResolution = resolveActiveDimensionKey(
    dimensions,
    state.activeDimensionKey,
    state.pendingDimensionFocusKey,
    focusedDimensionKey);
  state.activeDimensionKey = activeResolution.activeKey;
  state.pendingDimensionFocusKey = activeResolution.pendingKey;

  for (const dimension of dimensions) {
    const input = getOrCreateDimensionInput(state, dimension);
    const lockedValue = getLockedDraftDimensionValue(state, dimension.key);
    const hasLockedValue = Number.isFinite(lockedValue);
    const displayValue = hasLockedValue
      ? formatDimensionValue(lockedValue)
      : formatDimensionValue(dimension.value);

    const isFocused = document.activeElement === input;
    const isEditing = input.dataset.dimensionEditing === "true";
    const isActiveDimension = dimension.key === state.activeDimensionKey || dimension.key === focusedDimensionKey;
    if (shouldRefreshDimensionInputValue(isFocused, hasLockedValue, isEditing)) {
      input.value = displayValue;
      if (shouldAutoSelectDimensionInputValue(isFocused, hasLockedValue, isEditing, isActiveDimension)) {
        selectInputText(input);
      }
    }

    input.dataset.dimensionKey = dimension.key;
    input.dataset.dimensionLabel = dimension.label;
    const inputPoint = clampDimensionInputScreenPoint(state, dimension.point);
    input.style.left = `${inputPoint.x}px`;
    input.style.top = `${inputPoint.y}px`;
    input.classList.toggle("drawing-dimension-input-active", isActiveDimension);
  }

  focusActiveDimensionInputIfNeeded(state, dimensions);
}

export function getDefaultActiveDimensionKey(dimensions, requestedKey) {
  const keys = getDimensionKeys(dimensions);
  if (keys.length === 0) {
    return null;
  }

  return keys.includes(requestedKey)
    ? requestedKey
    : keys[0];
}

export function resolveActiveDimensionKey(dimensions, requestedKey, pendingKey = null, focusedKey = null) {
  const keys = getDimensionKeys(dimensions);
  const requested = requestedKey || null;
  const pending = pendingKey || null;
  const focused = focusedKey || null;

  if (keys.length === 0) {
    return {
      activeKey: null,
      pendingKey: pending
    };
  }

  if (pending && keys.includes(pending)) {
    return {
      activeKey: pending,
      pendingKey: null
    };
  }

  return {
    activeKey: keys.includes(focused) ? focused : keys.includes(requested) ? requested : keys[0],
    pendingKey: null
  };
}

export function getNextDimensionKey(dimensions, currentKey, reverse = false) {
  const keys = getDimensionKeys(dimensions);
  if (keys.length === 0) {
    return null;
  }

  const currentIndex = Math.max(0, keys.indexOf(currentKey));
  const offset = reverse ? -1 : 1;
  const nextIndex = (currentIndex + offset + keys.length) % keys.length;
  return keys[nextIndex];
}

function getDimensionKeys(dimensions) {
  if (!Array.isArray(dimensions) || dimensions.length === 0) {
    return [];
  }

  return dimensions
    .map(dimension => dimension && dimension.key)
    .filter(key => Boolean(key));
}

export function shouldRefreshDimensionInputValue(isFocused, hasLockedValue, hasTransientEdit = false) {
  return !hasTransientEdit && (!isFocused || !hasLockedValue);
}

export function shouldAutoSelectDimensionInputValue(isFocused, hasLockedValue, hasTransientEdit = false, isActiveDimension = false) {
  return isFocused && isActiveDimension && !hasLockedValue && !hasTransientEdit;
}

export function shouldCommitDimensionInputOnBlur(
  suppressDimensionInputCommit = false,
  skipNextBlurCommit = false,
  hasTransientEdit = false) {
  return hasTransientEdit && !suppressDimensionInputCommit && !skipNextBlurCommit;
}

export function shouldCommitDimensionInputOnChange(skipNextChangeCommit = false, hasTransientEdit = false) {
  return hasTransientEdit && !skipNextChangeCommit;
}

export function clampDimensionInputScreenPoint(
  state,
  point,
  marginX = DIMENSION_INPUT_SCREEN_MARGIN_X,
  marginY = DIMENSION_INPUT_SCREEN_MARGIN_Y) {
  const size = getCanvasCssSize(state);
  const maxX = Math.max(marginX, size.width - marginX);
  const maxY = Math.max(marginY, size.height - marginY);

  return {
    x: clamp(point.x, marginX, maxX),
    y: clamp(point.y, marginY, maxY)
  };
}

function focusActiveDimensionInputIfNeeded(state, dimensions) {
  const focusedInput = getFocusedDimensionInput(state);
  if (focusedInput && state.dimensionInputs.has(focusedInput.dataset.dimensionKey)) {
    state.activeDimensionKey = focusedInput.dataset.dimensionKey || null;
    markActiveDimensionInput(state, focusedInput);
    return;
  }

  const activeKey = getDefaultActiveDimensionKey(dimensions, state.activeDimensionKey);
  if (!activeKey) {
    return;
  }

  const activeInput = state.dimensionInputs.get(activeKey);
  if (!activeInput || document.activeElement === activeInput) {
    return;
  }

  focusDimensionInput(state, activeInput, true);
}

function getOrCreateDimensionInput(state, dimension) {
  let input = state.dimensionInputs.get(dimension.key);
  if (input) {
    return input;
  }

  input = document.createElement("input");
  input.type = "number";
  input.step = "0.001";
  input.inputMode = "decimal";
  input.className = "drawing-dimension-input";
  input.style.pointerEvents = "auto";
  input.style.zIndex = "5";
  input.title = dimension.label;
  input.setAttribute("aria-label", dimension.label);
  input.addEventListener("pointerdown", event => {
    event.stopPropagation();
    focusDimensionInput(state, input, false);
  });
  input.addEventListener("pointerup", event => {
    event.stopPropagation();
  });
  input.addEventListener("click", event => {
    event.stopPropagation();
  });
  input.addEventListener("focus", () => setActiveDimensionInput(state, input));
  input.addEventListener("blur", () => {
    const skipNextBlurCommit = input.dataset.skipNextBlurCommit === "true";
    input.dataset.skipNextBlurCommit = "false";
    if (shouldCommitDimensionInputOnBlur(
      state.suppressDimensionInputCommit,
      skipNextBlurCommit,
      input.dataset.dimensionEditing === "true")) {
      commitDimensionInputValue(state, input);
    }
    if (state.activeDimensionKey === input.dataset.dimensionKey) {
      state.activeDimensionKey = null;
    }
    input.classList.remove("drawing-dimension-input-active");
  });
  input.addEventListener("input", () => {
    input.dataset.dimensionEditing = "true";
    commitDimensionInputValue(state, input);
  });
  input.addEventListener("keydown", event => handleDimensionInputKeyDown(state, input, event));
  input.addEventListener("change", () => {
    const skipNextChangeCommit = input.dataset.skipNextChangeCommit === "true";
    input.dataset.skipNextChangeCommit = "false";
    if (shouldCommitDimensionInputOnChange(skipNextChangeCommit, input.dataset.dimensionEditing === "true")) {
      commitDimensionInputValue(state, input);
    }
  });

  state.dimensionOverlay.appendChild(input);
  state.dimensionInputs.set(dimension.key, input);
  return input;
}

function handleDimensionInputKeyDown(state, input, event) {
  event.stopPropagation();

  if (isDimensionTypingKey(event)
    && shouldAutoSelectDimensionInputValue(
      document.activeElement === input,
      Number.isFinite(getLockedDraftDimensionValue(state, input.dataset.dimensionKey)),
      input.dataset.dimensionEditing === "true",
      state.activeDimensionKey === input.dataset.dimensionKey || document.activeElement === input)) {
    selectInputText(input);
    input.dataset.dimensionEditing = "true";
    return;
  }

  if (event.key === "Enter") {
    input.dataset.dimensionEditing = "false";
    commitDimensionInputValue(state, input);
    const dimensions = getVisibleDimensionDescriptors(state);
    if (isLastDimensionInput(dimensions, input)) {
      commitCurrentSketchTool(state);
      focusCanvasWithoutDimensionCommit(state);
      updateDebugAttributes(state);
      draw(state);
    } else {
      focusDimensionInputByKey(state, getNextDimensionKey(dimensions, input.dataset.dimensionKey, false), true);
    }
    event.preventDefault();
  } else if (event.key === "Escape") {
    cancelActiveTool(state);
    focusCanvasWithoutDimensionCommit(state);
    event.preventDefault();
  } else if (event.key === "Tab") {
    input.dataset.dimensionEditing = "false";
    commitDimensionInputValue(state, input);
    focusDimensionInputByKey(
      state,
      getNextDimensionKey(getVisibleDimensionDescriptors(state), input.dataset.dimensionKey, event.shiftKey),
      true);
    event.preventDefault();
  }
}

function commitDimensionInputValue(state, input) {
  const text = String(input.value || "").trim();
  if (!text) {
    input.dataset.dimensionEditing = "false";
    if (clearDraftDimensionValue(state, input.dataset.dimensionKey)) {
      updateDebugAttributes(state);
      draw(state);
      return true;
    }

    return false;
  }

  if (text === "-" || text === "." || text === "-.") {
    input.dataset.dimensionEditing = "true";
    return false;
  }

  const value = Number(text);
  if (!applyDraftDimensionValue(state, input.dataset.dimensionKey, value)) {
    input.dataset.dimensionEditing = "true";
    return false;
  }

  input.dataset.dimensionEditing = "false";
  updateDebugAttributes(state);
  draw(state);
  return true;
}

function setActiveDimensionInput(state, input) {
  state.activeDimensionKey = input.dataset.dimensionKey || null;
  markActiveDimensionInput(state, input);
  selectInputText(input);
}

function markActiveDimensionInput(state, input) {
  for (const candidate of state.dimensionInputs.values()) {
    candidate.classList.toggle("drawing-dimension-input-active", candidate === input);
  }
}

function getFocusedDimensionInput(state) {
  const activeElement = document.activeElement;
  if (!activeElement || !activeElement.dataset) {
    return null;
  }

  return state.dimensionInputs.get(activeElement.dataset.dimensionKey) === activeElement
    ? activeElement
    : null;
}

function focusDimensionInputByKey(state, key, selectContents) {
  const input = key ? state.dimensionInputs.get(key) : null;
  if (input) {
    focusDimensionInput(state, input, selectContents);
  }
}

function focusDimensionInput(state, input, selectContents) {
  if (!input) {
    return;
  }

  state.activeDimensionKey = input.dataset.dimensionKey || null;
  focusElement(input);
  if (selectContents) {
    selectInputText(input);
  }
  setActiveDimensionInput(state, input);
}

function focusCanvasWithoutDimensionCommit(state) {
  markDimensionInputsToSkipNextBlurCommit(state);
  state.suppressDimensionInputCommit = true;
  try {
    focusCanvas(state.canvas);
  } finally {
    state.suppressDimensionInputCommit = false;
  }
}

function markDimensionInputsToSkipNextBlurCommit(state) {
  if (!state.dimensionInputs) {
    return;
  }

  for (const input of state.dimensionInputs.values()) {
    input.dataset.skipNextBlurCommit = "true";
    input.dataset.skipNextChangeCommit = "true";
  }
}

function selectInputText(input) {
  if (!input || typeof input.select !== "function") {
    return;
  }

  try {
    input.select();
  } catch {
  }
}

function isDimensionTypingKey(event) {
  if (!event || event.ctrlKey || event.metaKey || event.altKey || event.key.length !== 1) {
    return false;
  }

  return /[0-9.+-]/.test(event.key);
}

export function getVisibleDimensionDescriptors(state) {
  const keys = Array.isArray(state.visibleDimensionKeys) && state.visibleDimensionKeys.length > 0
    ? state.visibleDimensionKeys
    : Array.from(state.dimensionInputs.keys());

  return keys
    .filter(key => state.dimensionInputs.has(key))
    .map(key => ({ key }));
}

function isLastDimensionInput(dimensions, input) {
  if (!input || !Array.isArray(dimensions) || dimensions.length === 0) {
    return false;
  }

  const key = input.dataset.dimensionKey;
  return dimensions[dimensions.length - 1].key === key;
}

function clearDimensionInputs(state) {
  if (!state.dimensionInputs) {
    return;
  }

  for (const input of state.dimensionInputs.values()) {
    input.remove();
  }

  state.dimensionInputs.clear();
  state.activeDimensionKey = null;
  state.pendingDimensionFocusKey = null;
  state.visibleDimensionKeys = [];
}

export function clearTransientDimensionInputs(state) {
  markDimensionInputsToSkipNextBlurCommit(state);
  clearDimensionInputEditState(state);
  clearDimensionInputs(state);
}

function clearDimensionInputEditState(state) {
  if (!state.dimensionInputs) {
    return;
  }

  for (const input of state.dimensionInputs.values()) {
    input.dataset.dimensionEditing = "false";
  }
}

function drawFloatingDimensionLabel(state, label, point) {
  if (!label) {
    return;
  }

  const { context } = state;
  context.save();
  context.font = "650 12px Segoe UI, system-ui, sans-serif";
  context.textBaseline = "middle";
  context.textAlign = "center";

  const metrics = context.measureText(label);
  const width = Math.ceil(metrics.width) + 10;
  const height = 20;
  const x = point.x - width / 2;
  const y = point.y - height / 2;

  context.fillStyle = "rgba(15, 23, 42, 0.88)";
  context.strokeStyle = "#38bdf8";
  context.lineWidth = 1;
  context.setLineDash([]);
  context.beginPath();
  context.roundRect(x, y, width, height, 3);
  context.fill();
  context.stroke();

  context.fillStyle = "#7dd3fc";
  context.fillText(label, point.x, point.y + 0.5);
  context.restore();
}

function buildEntityPath(state, entity) {
  const kind = getEntityKind(entity);

  switch (kind) {
    case "line":
      return buildLinePath(state, entity);
    case "polyline":
      return buildPolylinePath(state, entity);
    case "spline":
      return buildPolylinePath(state, entity);
    case "circle":
      return buildCirclePath(state, entity);
    case "arc":
      return buildArcPath(state, entity);
    default:
      return false;
  }
}

function buildLinePath(state, entity) {
  const points = getEntityPoints(entity);
  if (points.length < 2) {
    return false;
  }

  const start = worldToScreen(state, points[0]);
  const end = worldToScreen(state, points[1]);

  state.context.beginPath();
  state.context.moveTo(start.x, start.y);
  state.context.lineTo(end.x, end.y);
  return true;
}

function buildPolylinePath(state, entity) {
  const points = getEntityPoints(entity);
  if (points.length < 2) {
    return false;
  }

  const first = worldToScreen(state, points[0]);
  state.context.beginPath();
  state.context.moveTo(first.x, first.y);

  for (let index = 1; index < points.length; index += 1) {
    const screenPoint = worldToScreen(state, points[index]);
    state.context.lineTo(screenPoint.x, screenPoint.y);
  }

  return true;
}

function buildPolylineSegmentPath(state, entity, segmentIndex) {
  const points = getEntityPoints(entity);
  if (segmentIndex < 0 || segmentIndex >= points.length - 1) {
    return false;
  }

  const start = worldToScreen(state, points[segmentIndex]);
  const end = worldToScreen(state, points[segmentIndex + 1]);

  state.context.beginPath();
  state.context.moveTo(start.x, start.y);
  state.context.lineTo(end.x, end.y);
  return true;
}

function buildCirclePath(state, entity) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);

  if (!center || !isFinitePositive(radius)) {
    return false;
  }

  const screenCenter = worldToScreen(state, center);
  const screenRadius = radius * state.view.scale;

  state.context.beginPath();
  state.context.arc(screenCenter.x, screenCenter.y, screenRadius, 0, Math.PI * 2);
  return true;
}

function buildArcPath(state, entity) {
  const points = getArcWorldPoints(entity);
  if (points.length < 2) {
    return false;
  }

  const first = worldToScreen(state, points[0]);
  state.context.beginPath();
  state.context.moveTo(first.x, first.y);

  for (let index = 1; index < points.length; index += 1) {
    const screenPoint = worldToScreen(state, points[index]);
    state.context.lineTo(screenPoint.x, screenPoint.y);
  }

  return true;
}

function handlePointerMove(state, event) {
  if (state.disposed) {
    return;
  }

  const screenPoint = getPointerScreenPoint(state, event);
  state.pointerScreenPoint = screenPoint;

  if (state.panning && state.lastPointerScreen) {
    state.view.offsetX += screenPoint.x - state.lastPointerScreen.x;
    state.view.offsetY += screenPoint.y - state.lastPointerScreen.y;
    state.lastPointerScreen = screenPoint;
    event.preventDefault();
    updateDebugAttributes(state);
    draw(state);
    return;
  }

  if (state.geometryDrag && state.geometryDrag.pointerId === event.pointerId) {
    updateGeometryDrag(state, screenPoint, event);
    event.preventDefault();
    updateDebugAttributes(state);
    draw(state);
    return;
  }

  if (state.constraintGroupDrag && state.constraintGroupDrag.pointerId === event.pointerId) {
    updateConstraintGroupDrag(state, screenPoint);
    event.preventDefault();
    updateDebugAttributes(state);
    draw(state);
    return;
  }

  if (isDimensionTool(state)) {
    const nearestTarget = findNearestTarget(state, screenPoint);
    setHoveredTarget(state, nearestTarget);
    if (state.dimensionDraft && state.dimensionDraft.complete) {
      state.dimensionDraft.anchorPoint = screenToWorld(state, screenPoint);
    }

    if (state.clickCandidate) {
      const moveDistance = distanceBetweenScreenPoints(screenPoint, state.clickCandidate.screenPoint);
      if (moveDistance > CLICK_MOVE_TOLERANCE) {
        state.clickCandidate.cancelled = true;
      }
    }

    updateDebugAttributes(state);
    draw(state);
    return;
  }

  if (isPowerTrimTool(state) || isSplitAtPointTool(state) || isConstructionTool(state)) {
    const nearestTarget = findNearestTarget(state, screenPoint);
    if (setHoveredTarget(state, nearestTarget)) {
      draw(state);
    }

    if (state.clickCandidate) {
      const moveDistance = distanceBetweenScreenPoints(screenPoint, state.clickCandidate.screenPoint);
      if (moveDistance > CLICK_MOVE_TOLERANCE) {
        state.clickCandidate.cancelled = true;
      }
    }

    return;
  }

  if (getSketchCreationTool(state) || getModifyTool(state)) {
    const nearestTarget = findNearestTarget(state, screenPoint);
    setHoveredTarget(state, nearestTarget);

    if (getSketchCreationTool(state) && tryToggleSketchChainToolAtPoint(state, screenPoint, nearestTarget)) {
      updateDebugAttributes(state);
      draw(state);
      return;
    }

    if (state.toolDraft && state.toolDraft.points.length > 0) {
      state.toolDraft.previewPoint = getSketchWorldPoint(state, screenPoint, nearestTarget, event);
      if (getSketchCreationTool(state)) {
        applyLockedDraftDimensions(state);
      }
      updateDebugAttributes(state);
      draw(state);
    } else if (nearestTarget) {
      draw(state);
    }

    if (state.clickCandidate) {
      const moveDistance = distanceBetweenScreenPoints(screenPoint, state.clickCandidate.screenPoint);
      if (moveDistance > CLICK_MOVE_TOLERANCE) {
        state.clickCandidate.cancelled = true;
      }
    }

    return;
  }

  if (state.clickCandidate) {
    const moveDistance = distanceBetweenScreenPoints(screenPoint, state.clickCandidate.screenPoint);
    if (moveDistance > CLICK_MOVE_TOLERANCE) {
      state.clickCandidate.cancelled = true;
      if (state.clickCandidate.pointerId === event.pointerId
        && state.clickCandidate.constraintGroupKey) {
        startConstraintGroupDrag(state, state.clickCandidate, screenPoint);
        event.preventDefault();
        updateDebugAttributes(state);
        draw(state);
        return;
      }

      if (state.clickCandidate.pointerId === event.pointerId
        && state.clickCandidate.target
        && canStartGeometryDrag(state, state.clickCandidate.target)) {
        startGeometryDrag(state, state.clickCandidate, screenPoint, event);
        event.preventDefault();
        updateDebugAttributes(state);
        draw(state);
        return;
      }

      if (state.clickCandidate.pointerId === event.pointerId && !state.selectionBox) {
        state.selectionBox = {
          start: state.clickCandidate.screenPoint,
          end: screenPoint,
          pointerId: event.pointerId,
          operation: getSelectionBoxOperation(event)
        };
      }
    }
  }

  if (state.selectionBox && state.selectionBox.pointerId === event.pointerId) {
    state.selectionBox.end = screenPoint;
    state.selectionBox.operation = getSelectionBoxOperation(event);
    event.preventDefault();
    updateDebugAttributes(state);
    draw(state);
    return;
  }

  const nearestTarget = findNearestTarget(state, screenPoint);
  if (setHoveredTarget(state, nearestTarget)) {
    draw(state);
  }
}

function handlePointerDown(state, event) {
  if (state.disposed) {
    return;
  }

  const screenPoint = getPointerScreenPoint(state, event);
  state.pointerScreenPoint = screenPoint;
  focusCanvas(state.canvas);
  capturePointer(state.canvas, event.pointerId);

  if (isPanPointerDownForTool(event, state.activeTool)) {
    state.panning = true;
    state.lastPointerScreen = screenPoint;
    state.clickCandidate = null;
    state.selectionBox = null;
    event.preventDefault();
    return;
  }

  if (isSecondaryPointerButton(event) && isDimensionTool(state)) {
    if (releaseDimensionDraftSelection(state)) {
      setHoveredTarget(state, findNearestTarget(state, screenPoint));
      updateDebugAttributes(state);
      draw(state);
    }

    releasePointer(state.canvas, event.pointerId);
    event.preventDefault();
    return;
  }

  if (isPrimaryPointerButton(event)) {
    const constraintGroupHit = state.activeTool === "select"
      ? getConstraintGlyphGroupHit(state, screenPoint)
      : null;
    if (constraintGroupHit) {
      state.clickCandidate = {
        screenPoint,
        pointerId: event.pointerId,
        cancelled: false,
        constraintGroupKey: constraintGroupHit.group.key,
        constraintGroupAnchorScreenPoint: constraintGroupHit.group.anchorScreenPoint
      };
      event.preventDefault();
      return;
    }

    const dragTarget = state.activeTool === "select"
      ? findNearestTarget(state, screenPoint)
      : null;
    state.clickCandidate = {
      screenPoint,
      pointerId: event.pointerId,
      cancelled: false,
      target: dragTarget,
      startWorldPoint: screenToWorld(state, screenPoint)
    };

    if (getSketchCreationTool(state) || getModifyTool(state) || isPowerTrimTool(state) || isSplitAtPointTool(state) || isConstructionTool(state)) {
      event.preventDefault();
    }
  }
}

function handlePointerUp(state, event) {
  if (state.disposed) {
    return;
  }

  const screenPoint = getPointerScreenPoint(state, event);
  state.pointerScreenPoint = screenPoint;

  if (state.panning) {
    state.panning = false;
    state.lastPointerScreen = null;
    releasePointer(state.canvas, event.pointerId);
    event.preventDefault();

    const nearestTarget = findNearestTarget(state, screenPoint);
    if (setHoveredTarget(state, nearestTarget)) {
      draw(state);
    }

    return;
  }

  if (state.geometryDrag && state.geometryDrag.pointerId === event.pointerId) {
    finishGeometryDrag(state, screenPoint, event);
    state.clickCandidate = null;
    const nearestTarget = findNearestTarget(state, screenPoint);
    setHoveredTarget(state, nearestTarget);
    updateDebugAttributes(state);
    draw(state);
    releasePointer(state.canvas, event.pointerId);
    event.preventDefault();
    return;
  }

  if (state.constraintGroupDrag && state.constraintGroupDrag.pointerId === event.pointerId) {
    updateConstraintGroupDrag(state, screenPoint);
    state.constraintGroupDrag = null;
    state.clickCandidate = null;
    updateDebugAttributes(state);
    draw(state);
    releasePointer(state.canvas, event.pointerId);
    event.preventDefault();
    return;
  }

  if (state.selectionBox && state.selectionBox.pointerId === event.pointerId) {
    state.selectionBox.end = screenPoint;
    state.selectionBox.operation = getSelectionBoxOperation(event);
    const changed = applyBoxSelection(state, state.selectionBox);
    state.selectionBox = null;
    state.clickCandidate = null;
    if (changed) {
      notifySelectionChanged(state);
    }

    const nearestTarget = findNearestTarget(state, screenPoint);
    setHoveredTarget(state, nearestTarget);
    updateDebugAttributes(state);
    draw(state);
    releasePointer(state.canvas, event.pointerId);
    event.preventDefault();
    return;
  }

  const candidate = state.clickCandidate;
  state.clickCandidate = null;

  if (candidate && candidate.pointerId === event.pointerId && !candidate.cancelled) {
    const moveDistance = distanceBetweenScreenPoints(screenPoint, candidate.screenPoint);
    if (moveDistance <= CLICK_MOVE_TOLERANCE) {
      if (isSplitAtPointTool(state)) {
        const request = getSplitAtPointRequest(state, screenPoint);
        if (request) {
          invokeDotNet(state, "OnSplitAtPointRequested", request.targetKey, request.point.x, request.point.y);
        }
        updateDebugAttributes(state);
        draw(state);
        releasePointer(state.canvas, event.pointerId);
        event.preventDefault();
        return;
      }

      if (isPowerTrimTool(state)) {
        const request = getPowerTrimRequest(state, screenPoint);
        if (request) {
          invokeDotNet(state, "OnPowerTrimRequested", request.targetKey, request.point.x, request.point.y);
        }
        updateDebugAttributes(state);
        draw(state);
        releasePointer(state.canvas, event.pointerId);
        event.preventDefault();
        return;
      }

      if (isConstructionTool(state)) {
        const request = getConstructionToggleRequest(state, screenPoint);
        if (request) {
          invokeDotNet(state, "OnConstructionToggleRequested", request.targetKey);
        }
        updateDebugAttributes(state);
        draw(state);
        releasePointer(state.canvas, event.pointerId);
        event.preventDefault();
        return;
      }

      if (isDimensionTool(state)) {
        handleDimensionToolClick(state, screenPoint, event);
        updateDebugAttributes(state);
        draw(state);
        releasePointer(state.canvas, event.pointerId);
        event.preventDefault();
        return;
      }

      if (getSketchCreationTool(state)) {
        handleSketchToolClick(state, screenPoint, event);
        updateDebugAttributes(state);
        draw(state);
        releasePointer(state.canvas, event.pointerId);
        event.preventDefault();
        return;
      }

      if (getModifyTool(state)) {
        handleModifyToolClick(state, screenPoint, event);
        updateDebugAttributes(state);
        draw(state);
        releasePointer(state.canvas, event.pointerId);
        event.preventDefault();
        return;
      }

      if (candidate.constraintGroupKey) {
        updateDebugAttributes(state);
        draw(state);
        releasePointer(state.canvas, event.pointerId);
        event.preventDefault();
        return;
      }

      const clickedTarget = findNearestTarget(state, screenPoint);
      if (clickedTarget) {
        if (applyDirectSelectionClick(state, clickedTarget.key)) {
          notifySelectionChanged(state);
        }
        updateDebugAttributes(state);
        draw(state);
      } else if (clearSelectedTargets(state)) {
        invokeDotNet(state, "OnSelectionCleared");
        updateDebugAttributes(state);
        draw(state);
      }
    }
  }

  releasePointer(state.canvas, event.pointerId);
}

function canStartGeometryDrag(state, target) {
  if (!target || target.dynamic || state.activeTool !== "select") {
    return false;
  }

  return target.kind === "point"
    || target.kind === "segment"
    || (target.kind === "entity" && getEntityKind(target.entity) !== "spline");
}

function startGeometryDrag(state, candidate, screenPoint, event = {}) {
  const target = candidate.target;
  if (!target || !canStartGeometryDrag(state, target)) {
    return false;
  }

  state.geometryDrag = {
    pointerId: candidate.pointerId,
    targetKey: target.key,
    startScreenPoint: candidate.screenPoint,
    currentScreenPoint: screenPoint,
    startWorldPoint: candidate.startWorldPoint || screenToWorld(state, candidate.screenPoint),
    currentWorldPoint: screenToWorld(state, screenPoint),
    originalDocument: state.document,
    constrainToCurrentVector: Boolean(event.shiftKey),
    moved: true
  };

  if (!state.selectedKeys.has(target.key) || state.activeSelectionKey !== target.key) {
    state.selectedKeys.clear();
    state.selectedKeys.add(target.key);
    state.activeSelectionKey = target.key;
    notifySelectionChanged(state);
  }

  updateGeometryDrag(state, screenPoint, event);
  return true;
}

function updateGeometryDrag(state, screenPoint, event = {}) {
  const drag = state.geometryDrag;
  if (!drag) {
    return false;
  }

  drag.currentScreenPoint = screenPoint;
  drag.currentWorldPoint = screenToWorld(state, screenPoint);
  drag.constrainToCurrentVector = Boolean(event.shiftKey);
  drag.moved = distanceBetweenScreenPoints(drag.startScreenPoint, screenPoint) > CLICK_MOVE_TOLERANCE;
  const previewDocument = applyGeometryDragPreview(
    drag.originalDocument,
    drag.targetKey,
    drag.startWorldPoint,
    drag.currentWorldPoint,
    drag.constrainToCurrentVector);
  if (!previewDocument) {
    return false;
  }

  state.document = previewDocument;
  setHoveredTarget(state, resolveSelectionTarget(state, drag.targetKey));
  return true;
}

function finishGeometryDrag(state, screenPoint, event = {}) {
  const drag = state.geometryDrag;
  if (!drag) {
    return false;
  }

  updateGeometryDrag(state, screenPoint, event);
  const endWorldPoint = drag.currentWorldPoint || screenToWorld(state, screenPoint);
  state.document = drag.originalDocument;
  if (drag.moved) {
    invokeDotNet(
      state,
      "OnGeometryDragRequested",
      drag.targetKey,
      drag.startWorldPoint.x,
      drag.startWorldPoint.y,
      endWorldPoint.x,
      endWorldPoint.y,
      Boolean(drag.constrainToCurrentVector));
  }

  state.geometryDrag = null;
  return true;
}

function cancelGeometryDrag(state) {
  if (!state.geometryDrag) {
    return false;
  }

  state.document = state.geometryDrag.originalDocument;
  state.geometryDrag = null;
  return true;
}

function handleSketchToolClick(state, screenPoint, event) {
  const tool = getSketchCreationTool(state);
  if (!tool) {
    return;
  }

  const clickTarget = findNearestTarget(state, screenPoint);
  if (clickTarget) {
    setHoveredTarget(state, clickTarget);
  }

  let worldPoint = getSketchWorldPoint(state, screenPoint, clickTarget, event);
  if (getSketchToolPointCount(tool) === 1) {
    commitSketchToolPoints(state, tool, [worldPoint]);
    return;
  }

  if (!state.toolDraft || state.toolDraft.points.length === 0) {
    state.toolDraft = {
      points: [worldPoint],
      previewPoint: null,
      dimensionValues: {}
    };
    setHoveredTarget(state, null);
    return;
  }

  state.toolDraft.previewPoint = worldPoint;
  applyLockedDraftDimensions(state);
  worldPoint = state.toolDraft.previewPoint;

  const previousPoint = state.toolDraft.points[state.toolDraft.points.length - 1];
  if (distanceBetweenWorldPoints(previousPoint, worldPoint) <= WORLD_GEOMETRY_TOLERANCE) {
    state.toolDraft.previewPoint = null;
    return;
  }

  const nextPoints = state.toolDraft.points.concat(worldPoint);
  if (nextPoints.length < getSketchToolPointCount(tool)) {
    const dimensionValues = shouldPreserveDraftDimensionsForNextPoint(tool, nextPoints)
      ? { ...(state.toolDraft.dimensionValues || {}) }
      : {};

    state.toolDraft = {
      points: nextPoints,
      previewPoint: null,
      dimensionValues
    };
    state.pendingDimensionFocusKey = getNextSketchToolDimensionFocusKey(tool, nextPoints);
    setHoveredTarget(state, null);
    return;
  }

  commitSketchToolPoints(state, tool, nextPoints);
}

function handleModifyToolClick(state, screenPoint, event) {
  const tool = getModifyTool(state);
  if (!tool) {
    return false;
  }

  const clickTarget = findNearestTarget(state, screenPoint);
  if (clickTarget) {
    setHoveredTarget(state, clickTarget);
  }

  const worldPoint = getSketchWorldPoint(state, screenPoint, clickTarget, event);
  if (getModifyToolPointCount(tool) === 1) {
    commitModifyToolPoints(state, tool, [worldPoint]);
    return true;
  }

  if (!state.toolDraft || state.toolDraft.points.length === 0) {
    state.toolDraft = {
      points: [worldPoint],
      previewPoint: null,
      dimensionValues: {}
    };
    setHoveredTarget(state, null);
    return true;
  }

  const previousPoint = state.toolDraft.points[state.toolDraft.points.length - 1];
  if (distanceBetweenWorldPoints(previousPoint, worldPoint) <= WORLD_GEOMETRY_TOLERANCE) {
    state.toolDraft.previewPoint = null;
    return false;
  }

  const nextPoints = state.toolDraft.points.concat(worldPoint);
  if (nextPoints.length < getModifyToolPointCount(tool)) {
    state.toolDraft = {
      points: nextPoints,
      previewPoint: null,
      dimensionValues: {}
    };
    setHoveredTarget(state, null);
    return true;
  }

  commitModifyToolPoints(state, tool, nextPoints);
  return true;
}

function commitModifyToolPoints(state, tool, points) {
  if (!tool || !Array.isArray(points) || points.length < getModifyToolPointCount(tool)) {
    return false;
  }

  state.activeTool = "select";
  if (state.canvas && state.canvas.dataset) {
    state.canvas.dataset.activeTool = state.activeTool;
  }

  state.toolDraft = createEmptyToolDraft();
  clearTransientDimensionInputs(state);
  setHoveredTarget(state, null);
  invokeDotNet(state, "OnModifyToolCommitted", tool, flattenPointCoordinates(points));
  return true;
}

function shouldPreserveDraftDimensionsForNextPoint(tool, nextPoints) {
  return Array.isArray(nextPoints)
    && nextPoints.length === 2
    && (tool === "alignedrectangle" || tool === "centerpointarc");
}

function getNextSketchToolDimensionFocusKey(tool, nextPoints) {
  if (!Array.isArray(nextPoints) || nextPoints.length !== 2) {
    return null;
  }

  if (tool === "alignedrectangle") {
    return "depth";
  }

  return tool === "centerpointarc" ? "sweep" : null;
}

function handleDimensionToolClick(state, screenPoint, event) {
  if (!isDimensionTool(state)) {
    return false;
  }

  const draft = state.dimensionDraft || createEmptyDimensionDraft();
  const target = findNearestTarget(state, screenPoint);
  const selectionKey = getDimensionSelectionKey(target);
  if (draft.complete && draft.selectionKeys.length > 0) {
    if (canExtendDimensionSelection(state, draft.selectionKeys, selectionKey)) {
      return addDimensionDraftSelection(state, draft, target, selectionKey, screenPoint, event);
    }

    const request = getDimensionPlacementRequest(state, screenPoint, event);
    if (!request) {
      return false;
    }

    clearTransientDimensionInputs(state);
    state.dimensionDraft = createEmptyDimensionDraft();
    setHoveredTarget(state, null);
    invokeDotNet(
      state,
      "OnSketchDimensionPlacementRequested",
      request.selectionKeys,
      request.anchor.x,
      request.anchor.y,
      request.radialDiameter);
    return true;
  }

  return addDimensionDraftSelection(state, draft, target, selectionKey, screenPoint, event);
}

function addDimensionDraftSelection(state, draft, target, selectionKey, screenPoint, event) {
  const existingKeys = Array.isArray(draft.selectionKeys) ? draft.selectionKeys.filter(key => Boolean(key)) : [];
  if (!canAddDimensionSelection(state, existingKeys, selectionKey)) {
    return false;
  }

  const selectionKeys = existingKeys.concat(selectionKey);
  const complete = isDimensionReferenceSetComplete(state, selectionKeys, target);
  state.dimensionDraft = {
    selectionKeys,
    complete,
    radialDiameter: draft.radialDiameter || getRadialDimensionPreference(target, event),
    anchorPoint: complete ? screenToWorld(state, screenPoint) : null
  };
  setHoveredTarget(state, target);
  return true;
}

function canAddDimensionSelection(state, selectionKeys, selectionKey) {
  if (!selectionKey) {
    return false;
  }

  const existingKeys = Array.isArray(selectionKeys) ? selectionKeys.filter(key => Boolean(key)) : [];
  if (existingKeys.includes(selectionKey) || existingKeys.length >= 2) {
    return false;
  }

  const nextKeys = existingKeys.concat(selectionKey);
  return nextKeys.length < 2 || getDimensionModelFromReferences(state, nextKeys) !== null;
}

export function releaseDimensionDraftSelection(state) {
  const draft = state.dimensionDraft || createEmptyDimensionDraft();
  const selectionKeys = Array.isArray(draft.selectionKeys)
    ? draft.selectionKeys.filter(key => Boolean(key))
    : [];
  if (selectionKeys.length === 0) {
    return false;
  }

  const nextSelectionKeys = selectionKeys.slice(0, -1);
  clearTransientDimensionInputs(state);
  state.dimensionDraft = {
    selectionKeys: nextSelectionKeys,
    complete: false,
    radialDiameter: nextSelectionKeys.length > 0 && Boolean(draft.radialDiameter),
    anchorPoint: null
  };
  return true;
}

export function canExtendDimensionSelection(state, selectionKeys, selectionKey) {
  const existingKeys = Array.isArray(selectionKeys) ? selectionKeys.filter(key => Boolean(key)) : [];
  return existingKeys.length === 1 && canAddDimensionSelection(state, existingKeys, selectionKey);
}

export function getDimensionPlacementRequest(state, screenPoint, event = {}) {
  if (!isDimensionTool(state) || !state.dimensionDraft) {
    return null;
  }

  const selectionKeys = Array.isArray(state.dimensionDraft.selectionKeys)
    ? state.dimensionDraft.selectionKeys.filter(key => Boolean(key))
    : [];
  if (selectionKeys.length === 0) {
    return null;
  }

  return {
    selectionKeys,
    anchor: screenToWorld(state, screenPoint),
    radialDiameter: Boolean(state.dimensionDraft.radialDiameter || event.shiftKey)
  };
}

function getDimensionSelectionKey(target) {
  if (!target || target.dynamic) {
    return null;
  }

  if (target.kind === "point" && target.entityId) {
    return target.key;
  }

  if (target.kind === "entity") {
    const kind = getEntityKind(target.entity);
    return kind === "line" || kind === "circle" || kind === "arc" || kind === "point"
      ? target.key
      : null;
  }

  if (target.kind === "segment" && target.entityId && Number.isInteger(target.segmentIndex)) {
    return target.key;
  }

  return null;
}

function isDimensionReferenceSetComplete(state, selectionKeys, lastTarget) {
  if (!Array.isArray(selectionKeys) || selectionKeys.length === 0) {
    return false;
  }

  if (selectionKeys.length >= 2) {
    return getDimensionModelFromReferences(state, selectionKeys) !== null;
  }

  if (!lastTarget || (lastTarget.kind !== "entity" && lastTarget.kind !== "segment")) {
    return Boolean(lastTarget
      && lastTarget.kind === "point"
      && isCurvePerimeterPointTarget(lastTarget));
  }

  const kind = getEntityKind(lastTarget.entity);
  return lastTarget.kind === "segment" || kind === "line" || kind === "circle" || kind === "arc";
}

function isCurvePerimeterPointTarget(target) {
  if (!target || target.kind !== "point" || !target.entity) {
    return false;
  }

  const kind = getEntityKind(target.entity);
  const label = String(target.label || "").split("|")[0].toLowerCase();
  return (kind === "circle" || kind === "arc") && label !== "center";
}

export function getRadialDimensionPreference(target, event = {}) {
  const kind = target && target.entity ? getEntityKind(target.entity) : "";
  if (kind === "circle") {
    return true;
  }

  return kind === "arc" && Boolean(event.shiftKey);
}

function commitCurrentSketchTool(state) {
  const tool = getSketchCreationTool(state);
  if (!tool || !state.toolDraft || state.toolDraft.points.length === 0 || !state.toolDraft.previewPoint) {
    return false;
  }

  const points = state.toolDraft.points.concat(state.toolDraft.previewPoint);
  if (points.length < getSketchToolPointCount(tool)) {
    return false;
  }

  const previousPoint = points[points.length - 2];
  const lastPoint = points[points.length - 1];
  if (distanceBetweenWorldPoints(previousPoint, lastPoint) <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

  if (!isSketchToolPointSetValid(tool, points)) {
    return false;
  }

  commitSketchToolPoints(state, tool, points);
  return true;
}

function commitSketchToolPoints(state, tool, points) {
  if (!isSketchToolPointSetValid(tool, points)) {
    return false;
  }

  const dimensionLocks = getSketchToolDimensionLocks(state.toolDraft);
  const postCommitState = getPostCommitSketchToolState(tool, points, state.sketchChainAutoTool);
  const nextTool = postCommitState.activeTool;
  const toolModeChanged = normalizeToolName(state.activeTool) !== nextTool;
  state.activeTool = nextTool;
  if (state.canvas && state.canvas.dataset) {
    state.canvas.dataset.activeTool = state.activeTool;
  }
  state.sketchChainContext = postCommitState.sketchChainContext;
  state.toolDraft = postCommitState.toolDraft;
  state.sketchChainAutoTool = false;
  state.sketchChainVertexHovering = postCommitState.sketchChainVertexHovering;
  state.sketchChainToggleRequiresExit = postCommitState.sketchChainToggleRequiresExit;
  clearTransientDimensionInputs(state);
  setHoveredTarget(state, null);
  invokeDotNet(
    state,
    "OnSketchToolCommitted",
    tool,
    flattenPointCoordinates(points),
    dimensionLocks.keys,
    dimensionLocks.values);
  if (toolModeChanged) {
    invokeDotNet(state, "OnSketchToolModeChanged", nextTool);
  }

  return true;
}

function handlePointerCancel(state, event) {
  cancelGeometryDrag(state);
  state.constraintGroupDrag = null;
  state.panning = false;
  state.lastPointerScreen = null;
  state.pointerScreenPoint = null;
  state.clickCandidate = null;
  state.selectionBox = null;
  state.sketchChainVertexHovering = false;
  state.sketchChainToggleRequiresExit = false;
  releasePointer(state.canvas, event.pointerId);

  if (setHoveredTarget(state, null)) {
    draw(state);
  }
}

function handlePointerLeave(state) {
  if (state.panning || state.geometryDrag || state.constraintGroupDrag) {
    return;
  }

  state.pointerScreenPoint = null;
  state.sketchChainVertexHovering = false;
  state.sketchChainToggleRequiresExit = false;

  if (state.selectionBox) {
    state.selectionBox = null;
    updateDebugAttributes(state);
    draw(state);
    return;
  }

  if (setHoveredTarget(state, null)) {
    draw(state);
  }
}

function handleKeyDown(state, event) {
  if (state.disposed || isEditableKeyTarget(event.target)) {
    return;
  }

  const activeTool = normalizeToolName(state.activeTool);
  if (event.key === "Escape" && activeTool !== "select") {
    cancelActiveTool(state);
    event.preventDefault();
    return;
  }

  if (event.key === "Delete" && activeTool === "select" && state.selectedKeys.size > 0) {
    invokeDotNet(state, "OnDeleteSelectionRequested", Array.from(state.selectedKeys));
    event.preventDefault();
  }
}

function cancelActiveTool(state) {
  const activeTool = normalizeToolName(state.activeTool);
  if (activeTool === "select") {
    return false;
  }

  state.activeTool = "select";
  state.toolDraft = createEmptyToolDraft();
  state.sketchChainContext = null;
  state.sketchChainVertexHovering = false;
  state.sketchChainAutoTool = false;
  state.sketchChainToggleRequiresExit = false;
  state.acquiredSnapPoints = [];
  state.dimensionDraft = createEmptyDimensionDraft();
  clearTransientDimensionInputs(state);
  setHoveredTarget(state, null);
  invokeDotNet(state, "OnSketchToolCanceled");
  updateDebugAttributes(state);
  draw(state);
  return true;
}

function handleWheel(state, event) {
  if (state.disposed) {
    return;
  }

  event.preventDefault();

  const screenPoint = getPointerScreenPoint(state, event);
  state.pointerScreenPoint = screenPoint;
  const worldPoint = screenToWorld(state, screenPoint);
  const normalizedDelta = normalizeWheelDelta(event);
  const zoomFactor = clamp(Math.exp(-normalizedDelta * 0.001), 0.2, 5);
  const nextScale = clamp(state.view.scale * zoomFactor, MIN_VIEW_SCALE, MAX_VIEW_SCALE);

  if (nextScale === state.view.scale) {
    return;
  }

  state.view.scale = nextScale;
  state.view.offsetX = screenPoint.x - worldPoint.x * state.view.scale;
  state.view.offsetY = screenPoint.y + worldPoint.y * state.view.scale;

  const nearestTarget = findNearestTarget(state, screenPoint);
  setHoveredTarget(state, nearestTarget);
  updateDebugAttributes(state);
  draw(state);
}

function handleDoubleClick(state, event) {
  if (state.disposed || state.panning || state.selectionBox) {
    return;
  }

  if (getSketchCreationTool(state)) {
    state.toolDraft = createEmptyToolDraft();
    state.sketchChainContext = null;
    state.sketchChainVertexHovering = false;
    state.sketchChainAutoTool = false;
    state.sketchChainToggleRequiresExit = false;
    setHoveredTarget(state, null);
    updateDebugAttributes(state);
    draw(state);
    event.preventDefault();
    return;
  }

  const screenPoint = getPointerScreenPoint(state, event);
  const target = findNearestWholeEntityTarget(state, screenPoint);
  if (!target) {
    return;
  }

  state.selectedKeys.clear();
  state.selectedKeys.add(target.key);
  state.activeSelectionKey = target.key;
  setHoveredTarget(state, target);
  notifySelectionChanged(state);
  updateDebugAttributes(state);
  draw(state);
  event.preventDefault();
}

function fitToExtents(state) {
  const size = getCanvasCssSize(state);
  const bounds = getDocumentBounds(state.document);

  if (!bounds) {
    state.view.scale = 1;
    state.view.offsetX = size.width / 2;
    state.view.offsetY = size.height / 2;
    return;
  }

  const worldWidth = Math.max(bounds.maxX - bounds.minX, 1);
  const worldHeight = Math.max(bounds.maxY - bounds.minY, 1);
  const availableWidth = Math.max(1, size.width - DEFAULT_FIT_MARGIN * 2);
  const availableHeight = Math.max(1, size.height - DEFAULT_FIT_MARGIN * 2);
  const scale = clamp(
    Math.min(availableWidth / worldWidth, availableHeight / worldHeight),
    MIN_VIEW_SCALE,
    MAX_VIEW_SCALE);

  const centerX = (bounds.minX + bounds.maxX) / 2;
  const centerY = (bounds.minY + bounds.maxY) / 2;

  state.view.scale = scale;
  state.view.offsetX = size.width / 2 - centerX * scale;
  state.view.offsetY = size.height / 2 + centerY * scale;
  updateDebugAttributes(state);
}

function worldToScreen(state, point) {
  return {
    x: point.x * state.view.scale + state.view.offsetX,
    y: -point.y * state.view.scale + state.view.offsetY
  };
}

function screenToWorld(state, point) {
  return {
    x: (point.x - state.view.offsetX) / state.view.scale,
    y: (state.view.offsetY - point.y) / state.view.scale
  };
}

function midpointScreenPoint(first, second) {
  return {
    x: (first.x + second.x) / 2,
    y: (first.y + second.y) / 2
  };
}

function getScreenNormal(first, second) {
  const dx = second.x - first.x;
  const dy = second.y - first.y;
  const length = Math.hypot(dx, dy);
  if (length <= 0.000001) {
    return { x: 0, y: -1 };
  }

  return {
    x: -dy / length,
    y: dx / length
  };
}

function getSketchWorldPoint(state, screenPoint, target = state.hoveredTarget, event = null) {
  const tool = getSketchCreationTool(state);
  if (target && target.kind === "point" && target.point) {
    return applyPolarSnapIfRequested(state, target.point, tool, event);
  }

  if (target && target.snapPoint) {
    return applyPolarSnapIfRequested(state, target.snapPoint, tool, event);
  }

  return applyPolarSnapIfRequested(state, screenToWorld(state, screenPoint), tool, event);
}

export function findNearestTarget(state, screenPoint) {
  let nearestPointHit = null;
  let nearestEdgeHit = null;

  for (const entity of getDocumentEntities(state.document)) {
    const pointHit = getEntityPointHit(state, entity, screenPoint);
    if (pointHit && (!nearestPointHit || pointHit.distance < nearestPointHit.distance)) {
      nearestPointHit = pointHit;
    }

    const edgeHit = getEntityEdgeHit(state, entity, screenPoint);
    if (edgeHit && (!nearestEdgeHit || edgeHit.distance < nearestEdgeHit.distance)) {
      nearestEdgeHit = edgeHit;
    }
  }

  if (nearestPointHit && nearestPointHit.distance <= SNAP_POINT_TOLERANCE) {
    return nearestPointHit.target;
  }

  const dimensionHit = getPersistentDimensionHit(state, screenPoint);
  if (dimensionHit && dimensionHit.distance <= HIT_TEST_TOLERANCE) {
    return dimensionHit.target;
  }

  const dynamicPointHit = getDynamicSketchSnapHit(state, screenPoint, nearestEdgeHit ? nearestEdgeHit.target : null);
  if (dynamicPointHit && dynamicPointHit.distance <= SNAP_POINT_TOLERANCE) {
    return dynamicPointHit.target;
  }

  if (nearestEdgeHit && nearestEdgeHit.distance <= HIT_TEST_TOLERANCE) {
    return nearestEdgeHit.target;
  }

  return null;
}

function findNearestWholeEntityTarget(state, screenPoint) {
  let nearestTarget = null;
  let nearestDistance = Number.POSITIVE_INFINITY;

  for (const entity of getDocumentEntities(state.document)) {
    const target = createEntityTarget(entity);
    if (!target) {
      continue;
    }

    const distance = getEntityScreenDistance(state, entity, screenPoint);
    if (distance < nearestDistance) {
      nearestDistance = distance;
      nearestTarget = target;
    }
  }

  return nearestDistance <= HIT_TEST_TOLERANCE ? nearestTarget : null;
}

function getPersistentDimensionHit(state, screenPoint) {
  let nearestHit = null;
  for (const dimension of getPersistentDimensionDescriptors(state)) {
    const distance = getDimensionScreenDistance(state, dimension, screenPoint);
    if (!Number.isFinite(distance)) {
      continue;
    }

    if (!nearestHit || distance < nearestHit.distance) {
      nearestHit = {
        target: createDimensionTarget(dimension),
        distance
      };
    }
  }

  return nearestHit;
}

function getDimensionScreenDistance(state, dimension, screenPoint) {
  if (!dimension || !dimension.geometry) {
    return Number.POSITIVE_INFINITY;
  }

  const labelDistance = dimension.point
    ? getDimensionLabelScreenDistance(state, dimension, screenPoint)
    : Number.POSITIVE_INFINITY;
  const geometryDistance = getDimensionGeometryScreenDistance(state, dimension, screenPoint);
  return Math.min(labelDistance, geometryDistance);
}

function getDimensionLabelScreenDistance(state, dimension, screenPoint) {
  const text = getDimensionDisplayText(dimension);
  const width = Math.max(44, measureDimensionCanvasText(state, text) + 16);
  const height = 24;
  const dx = Math.max(0, Math.abs(screenPoint.x - dimension.point.x) - width / 2);
  const dy = Math.max(0, Math.abs(screenPoint.y - dimension.point.y) - height / 2);
  return Math.hypot(dx, dy);
}

function getDimensionGeometryScreenDistance(state, dimension, screenPoint) {
  const geometry = dimension.geometry;
  if (geometry.type === "linear") {
    const start = worldToScreen(state, geometry.start);
    const end = worldToScreen(state, geometry.end);
    const anchor = worldToScreen(state, geometry.anchorPoint);
    const textWidth = measureDimensionCanvasText(state, getDimensionDisplayText(dimension));
    const screenGeometry = getLinearDimensionScreenGeometry(start, end, anchor, textWidth);
    const segments = screenGeometry
      ? screenGeometry.extensionSegments.concat(screenGeometry.dimensionSegments)
      : [];
    return getScreenSegmentsDistance(screenPoint, segments);
  }

  if (geometry.type === "radial") {
    const center = worldToScreen(state, geometry.center);
    const anchor = worldToScreen(state, geometry.anchorPoint);
    const radius = geometry.radius * state.view.scale;
    const textWidth = measureDimensionCanvasText(state, getDimensionDisplayText(dimension));
    const screenGeometry = getRadialDimensionScreenGeometry(center, radius, anchor, geometry.diameter, textWidth);
    return getScreenSegmentsDistance(screenPoint, screenGeometry.segments);
  }

  if (geometry.type === "angle") {
    const angleGeometry = getAngleDimensionScreenGeometry(
      state,
      geometry.firstLine,
      geometry.secondLine,
      geometry.vertex,
      geometry.anchorPoint);
    if (!angleGeometry) {
      return Number.POSITIVE_INFINITY;
    }

    const extensionDistance = getScreenSegmentsDistance(screenPoint, angleGeometry.extensionSegments);
    const radialDistance = Math.abs(distanceBetweenScreenPoints(screenPoint, angleGeometry.vertex) - angleGeometry.radius);
    return Math.min(extensionDistance, radialDistance);
  }

  return Number.POSITIVE_INFINITY;
}

function getScreenSegmentsDistance(screenPoint, segments) {
  if (!Array.isArray(segments) || segments.length === 0) {
    return Number.POSITIVE_INFINITY;
  }

  return segments.reduce((nearest, segment) => {
    const projection = closestPointOnScreenSegment(screenPoint, segment.start, segment.end);
    return Math.min(nearest, projection.distance);
  }, Number.POSITIVE_INFINITY);
}

function getEntityPointHit(state, entity, screenPoint) {
  if (getEntityKind(entity) === "point") {
    return getPointEntityHit(state, entity, screenPoint);
  }

  let nearestHit = null;
  const entities = getDocumentEntities(state.document);

  for (const snapPoint of getSnapPoints(entity, entities)) {
    const screenSnapPoint = worldToScreen(state, snapPoint.point);
    const distance = distanceBetweenScreenPoints(screenPoint, screenSnapPoint);
    const priority = Number(snapPoint.priority || 0);
    const nearestPriority = nearestHit ? Number(nearestHit.priority || 0) : 0;
    if (!nearestHit
      || distance < nearestHit.distance
      || (Math.abs(distance - nearestHit.distance) <= 0.000001 && priority > nearestPriority)) {
      nearestHit = {
        target: createPointTarget(entity, snapPoint.label, snapPoint.point),
        distance,
        priority
      };
    }
  }

  return nearestHit;
}

function getEntityEdgeHit(state, entity, screenPoint) {
  const kind = getEntityKind(entity);

  switch (kind) {
    case "point":
      return getPointEntityHit(state, entity, screenPoint);
    case "line":
      return getLineEdgeHit(state, entity, screenPoint);
    case "polyline":
      return getPolylineSegmentScreenHit(state, entity, screenPoint);
    case "spline":
      return getSampledCurveEdgeHit(state, entity, screenPoint);
    case "circle":
      return getCircleEdgeHit(state, entity, screenPoint);
    case "arc":
      return getArcEdgeHit(state, entity, screenPoint);
    default:
      return null;
  }
}

export function getDynamicSketchSnapHit(state, screenPoint, highlightedTarget = null) {
  const tool = getSketchCreationTool(state);
  if (!tool) {
    return null;
  }

  const worldPoint = screenToWorld(state, screenPoint);
  const candidates = [];
  addAcquiredProjectionSnapCandidates(candidates, state, worldPoint);
  addHighlightedGeometryOrthoSnapCandidates(candidates, state, highlightedTarget);
  addDynamicTangentSnapCandidates(candidates, state, tool, highlightedTarget, worldPoint);

  const hits = [];
  for (const candidate of candidates) {
    const screenCandidate = worldToScreen(state, candidate.point);
    const distance = distanceBetweenScreenPoints(screenPoint, screenCandidate);
    const priority = Number(candidate.priority || 0);
    if (distance <= SNAP_POINT_TOLERANCE) {
      hits.push({
        target: createDynamicPointTarget(candidate.label, candidate.point, candidate.guides),
        distance,
        priority
      });
    }
  }

  if (hits.length === 0) {
    return null;
  }

  hits.sort((first, second) =>
    second.priority - first.priority
    || first.distance - second.distance);
  return hits[0];
}

function addAcquiredProjectionSnapCandidates(candidates, state, worldPoint) {
  if (state.acquiredSnapPoints.length === 0) {
    return;
  }

  if (state.acquiredSnapPoints.length === 2) {
    const first = state.acquiredSnapPoints[0];
    const second = state.acquiredSnapPoints[1];
    addAcquiredIntersectionCandidate(candidates, state, first, second);
    addAcquiredIntersectionCandidate(candidates, state, second, first);
    addAcquiredMidpointCandidate(candidates, state, first, second);
  }

  for (const acquired of state.acquiredSnapPoints) {
    const verticalPoint = { x: acquired.point.x, y: worldPoint.y };
    if (isWorldPointWithinScreenDistance(state, acquired.point, verticalPoint)) {
      candidates.push({
        label: `project-v-${acquired.label}`,
        point: verticalPoint,
        guides: [{ orientation: "vertical", point: acquired.point }],
        priority: 2
      });
    }

    const horizontalPoint = { x: worldPoint.x, y: acquired.point.y };
    if (isWorldPointWithinScreenDistance(state, acquired.point, horizontalPoint)) {
      candidates.push({
        label: `project-h-${acquired.label}`,
        point: horizontalPoint,
        guides: [{ orientation: "horizontal", point: acquired.point }],
        priority: 2
      });
    }
  }
}

function addAcquiredMidpointCandidate(candidates, state, first, second) {
  if (distanceBetweenWorldPoints(first.point, second.point) <= WORLD_GEOMETRY_TOLERANCE) {
    return;
  }

  const point = midpoint(first.point, second.point);
  if (!isWorldPointWithinScreenDistance(state, first.point, point)
    || !isWorldPointWithinScreenDistance(state, second.point, point)) {
    return;
  }

  candidates.push({
    label: `midpoint-${first.label}-${second.label}`,
    point,
    guides: [{ orientation: "segment", start: first.point, point: second.point }],
    priority: 6
  });
}

function addAcquiredIntersectionCandidate(candidates, state, verticalSource, horizontalSource) {
  if (Math.abs(verticalSource.point.x - horizontalSource.point.x) <= WORLD_GEOMETRY_TOLERANCE
    || Math.abs(verticalSource.point.y - horizontalSource.point.y) <= WORLD_GEOMETRY_TOLERANCE) {
    return;
  }

  const point = {
    x: verticalSource.point.x,
    y: horizontalSource.point.y
  };
  if (!isWorldPointWithinScreenDistance(state, verticalSource.point, point)
    || !isWorldPointWithinScreenDistance(state, horizontalSource.point, point)) {
    return;
  }

  candidates.push({
    label: `project-${verticalSource.label}-${horizontalSource.label}`,
    point,
    guides: [
      { orientation: "vertical", point: verticalSource.point },
      { orientation: "horizontal", point: horizontalSource.point }
    ],
    priority: 9
  });
}

function addHighlightedGeometryOrthoSnapCandidates(candidates, state, highlightedTarget) {
  if (state.acquiredSnapPoints.length === 0
    || !highlightedTarget
    || !highlightedTarget.entity
    || !highlightedTarget.snapPoint) {
    return;
  }

  const entityId = getEntityId(highlightedTarget.entity) || "entity";
  for (const acquired of state.acquiredSnapPoints) {
    const points = getEntityOrthographicSnapPoints(highlightedTarget.entity, acquired.point, highlightedTarget.snapPoint);
    for (const snap of points) {
      if (!isWorldPointWithinScreenDistance(state, acquired.point, snap.point)) {
        continue;
      }

      candidates.push({
        label: `project-${snap.orientation}-${acquired.label}-${entityId}-${snap.index}`,
        point: snap.point,
        guides: [{ orientation: snap.orientation === "vertical" ? "vertical" : "horizontal", point: acquired.point }],
        priority: 7
      });
    }
  }
}

function addDynamicTangentSnapCandidates(candidates, state, tool, highlightedTarget = null, worldPoint = null) {
  if (tool === "threepointcircle") {
    addThreePointCircleTangentSnapCandidates(candidates, state, highlightedTarget);
    return;
  }

  if (tool !== "line" && tool !== "midpointline") {
    return;
  }

  if (!state.toolDraft || state.toolDraft.points.length === 0) {
    return;
  }

  const anchor = state.toolDraft.points[0];
  addSketchChainTangentSnapCandidate(candidates, state, anchor, worldPoint);

  for (const entity of getDocumentEntities(state.document)) {
    if (!isCurveEntity(entity)) {
      continue;
    }

    const entityId = getEntityId(entity) || "curve";
    const tangentPoints = getPointCurveTangentPoints(anchor, entity);
    for (let index = 0; index < tangentPoints.length; index += 1) {
      candidates.push({
        label: `tangent-${entityId}-${index}`,
        point: tangentPoints[index],
        guides: [{ orientation: "segment", start: anchor, point: tangentPoints[index] }],
        priority: 8
      });
    }
  }
}

function drawDimensionTarget(state, dimension, style) {
  if (!dimension || !dimension.point) {
    return;
  }

  const { context } = state;
  const width = Math.max(44, measureDimensionCanvasText(state, getDimensionDisplayText(dimension)) + 16);
  const height = 24;
  context.save();
  context.strokeStyle = style.strokeStyle;
  context.lineWidth = style.lineWidth;
  context.setLineDash(style.lineDash || []);
  if (style.glow) {
    context.shadowColor = "rgba(125, 211, 252, 0.55)";
    context.shadowBlur = 8;
  }

  context.strokeRect(dimension.point.x - width / 2, dimension.point.y - height / 2, width, height);
  context.restore();
}

function addSketchChainTangentSnapCandidate(candidates, state, anchor, worldPoint) {
  const chainContext = state && state.sketchChainContext;
  if (!chainContext
    || !chainContext.isTangentContinuation
    || !chainContext.point
    || !chainContext.tangentPoint
    || !worldPoint
    || !sameOptionalWorldPoint(anchor, chainContext.point)) {
    return;
  }

  const tangent = normalizeWorldVector(subtractPoints(chainContext.tangentPoint, chainContext.point));
  if (!tangent) {
    return;
  }

  const along = dotPoints(subtractPoints(worldPoint, chainContext.point), tangent);
  if (along <= WORLD_GEOMETRY_TOLERANCE) {
    return;
  }

  const point = {
    x: chainContext.point.x + tangent.x * along,
    y: chainContext.point.y + tangent.y * along
  };
  candidates.push({
    label: "tangent-chain",
    point,
    guides: [{ orientation: "segment", start: chainContext.point, point }],
    priority: 10
  });
}

function addThreePointCircleTangentSnapCandidates(candidates, state, highlightedTarget) {
  if (!highlightedTarget
    || !highlightedTarget.entity
    || !state.toolDraft
    || state.toolDraft.points.length !== 2) {
    return;
  }

  const first = state.toolDraft.points[0];
  const second = state.toolDraft.points[1];
  const entity = highlightedTarget.entity;
  const entityId = getEntityId(entity) || "entity";
  const kind = getEntityKind(entity);
  let tangentPoints = [];

  if (kind === "line" || kind === "polyline" || kind === "spline") {
    const segments = getHighlightedLinearSegments(entity, highlightedTarget);
    for (const segment of segments) {
      tangentPoints = tangentPoints.concat(getThreePointCircleLinearTangentPoints(first, second, segment));
    }
  } else if (kind === "circle" || kind === "arc") {
    tangentPoints = getThreePointCircleCurveTangentPoints(first, second, entity);
  }

  for (let index = 0; index < tangentPoints.length; index += 1) {
    const point = tangentPoints[index];
    candidates.push({
      label: `circle-tangent-${entityId}-${index}`,
      point,
      guides: [{ orientation: "segment", start: first, point }],
      priority: 8
    });
  }
}

function getHighlightedLinearSegments(entity, highlightedTarget) {
  const segments = getLinearSegments(entity);
  if (highlightedTarget
    && highlightedTarget.kind === "segment"
    && Number.isInteger(highlightedTarget.segmentIndex)) {
    return segments.filter(segment => segment.segmentIndex === highlightedTarget.segmentIndex);
  }

  return segments;
}

function getThreePointCircleLinearTangentPoints(first, second, segment) {
  if (!segment || !segment.start || !segment.end) {
    return [];
  }

  const dx = segment.end.x - segment.start.x;
  const dy = segment.end.y - segment.start.y;
  const length = Math.hypot(dx, dy);
  if (length <= WORLD_GEOMETRY_TOLERANCE) {
    return [];
  }

  const tangent = { x: dx / length, y: dy / length };
  const normal = { x: -tangent.y, y: tangent.x };
  const firstOffset = subtractPoints(first, segment.start);
  const secondOffset = subtractPoints(second, segment.start);
  const firstAlong = dotPoints(firstOffset, tangent);
  const secondAlong = dotPoints(secondOffset, tangent);
  const firstHeight = dotPoints(firstOffset, normal);
  const secondHeight = dotPoints(secondOffset, normal);
  const roots = solveQuadratic(
    secondHeight - firstHeight,
    2 * ((secondAlong * firstHeight) - (firstAlong * secondHeight)),
    (secondHeight * ((firstAlong * firstAlong) + (firstHeight * firstHeight)))
      - (firstHeight * ((secondAlong * secondAlong) + (secondHeight * secondHeight))));
  const points = [];

  for (const along of roots) {
    const parameter = along / length;
    if (!isUnitParameter(parameter)) {
      continue;
    }

    const point = {
      x: segment.start.x + tangent.x * along,
      y: segment.start.y + tangent.y * along
    };
    const circle = getThreePointCircle(first, second, point);
    if (!circle || !isCircleTangentToLinearSegmentAtPoint(circle, segment, point)) {
      continue;
    }

    addUniquePoint(points, point);
  }

  return points;
}

function getThreePointCircleCurveTangentPoints(first, second, curveEntity) {
  const center = getEntityCenter(curveEntity);
  const radius = getEntityRadius(curveEntity);
  if (!center || !isFinitePositive(radius)) {
    return [];
  }

  const kind = getEntityKind(curveEntity);
  const startAngle = kind === "arc" ? getEntityStartAngle(curveEntity) : 0;
  const sweep = kind === "arc"
    ? getPositiveSweepDegrees(startAngle, getEntityEndAngle(curveEntity))
    : FULL_CIRCLE_DEGREES;
  const sampleCount = Math.max(32, Math.ceil(sweep / 2.5));
  const roots = [];
  let previous = null;

  for (let index = 0; index <= sampleCount; index += 1) {
    const angle = startAngle + (sweep * index / sampleCount);
    const value = getThreePointCircleCurveTangentResidual(first, second, center, radius, angle);
    if (value === null) {
      previous = null;
      continue;
    }

    if (Math.abs(value) <= 0.0001) {
      addUniqueNumber(roots, angle);
    }

    if (previous && previous.value * value < 0) {
      addUniqueNumber(roots, refineThreePointCircleCurveTangentAngle(
        first,
        second,
        center,
        radius,
        previous.angle,
        angle));
    }

    previous = { angle, value };
  }

  const points = [];
  for (const angle of roots) {
    const point = pointOnCircle(center, radius, angle);
    if (kind === "arc" && !isAngleOnArc(angle, startAngle, getEntityEndAngle(curveEntity))) {
      continue;
    }

    const circle = getThreePointCircle(first, second, point);
    if (!circle || !isCircleTangentToCurveAtPoint(circle, center, point)) {
      continue;
    }

    addUniquePoint(points, point);
  }

  return points;
}

function getThreePointCircleCurveTangentResidual(first, second, center, radius, angleDegrees) {
  const point = pointOnCircle(center, radius, angleDegrees);
  const circle = getThreePointCircle(first, second, point);
  if (!circle || !isFinitePositive(circle.radius)) {
    return null;
  }

  const candidateRadius = subtractPoints(point, circle.center);
  const targetRadius = subtractPoints(point, center);
  const candidateLength = Math.hypot(candidateRadius.x, candidateRadius.y);
  const targetLength = Math.hypot(targetRadius.x, targetRadius.y);
  if (candidateLength <= WORLD_GEOMETRY_TOLERANCE || targetLength <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  return crossPoints(candidateRadius, targetRadius) / (candidateLength * targetLength);
}

function refineThreePointCircleCurveTangentAngle(first, second, center, radius, startAngle, endAngle) {
  let low = startAngle;
  let high = endAngle;
  let lowValue = getThreePointCircleCurveTangentResidual(first, second, center, radius, low);

  for (let index = 0; index < 32; index += 1) {
    const mid = (low + high) / 2;
    const midValue = getThreePointCircleCurveTangentResidual(first, second, center, radius, mid);
    if (lowValue === null || midValue === null) {
      return mid;
    }

    if (Math.abs(midValue) <= 0.0000001) {
      return mid;
    }

    if (lowValue * midValue <= 0) {
      high = mid;
    } else {
      low = mid;
      lowValue = midValue;
    }
  }

  return (low + high) / 2;
}

function getEntityScreenDistance(state, entity, screenPoint) {
  const hit = getEntityEdgeHit(state, entity, screenPoint);
  return hit ? hit.distance : Number.POSITIVE_INFINITY;
}

function getPointEntityHit(state, entity, screenPoint) {
  const point = getPointEntityLocation(entity);
  const target = createEntityTarget(entity);
  if (!point || !target) {
    return null;
  }

  return {
    target: withSnapPoint(target, point),
    distance: distanceBetweenScreenPoints(screenPoint, worldToScreen(state, point)),
    priority: 10
  };
}

function getLineEdgeHit(state, entity, screenPoint) {
  const target = createEntityTarget(entity);
  const points = getEntityPoints(entity);
  if (!target || points.length < 2) {
    return null;
  }

  const start = worldToScreen(state, points[0]);
  const end = worldToScreen(state, points[1]);
  const projection = closestPointOnScreenSegment(screenPoint, start, end);

  return {
    target: withSnapPoint(target, screenToWorld(state, projection.point)),
    distance: projection.distance
  };
}

function getPolylineSegmentScreenHit(state, entity, screenPoint) {
  const id = getEntityId(entity);
  const points = getEntityPoints(entity);
  if (!id || points.length < 2) {
    return null;
  }

  let closestDistance = Number.POSITIVE_INFINITY;
  let closestSegmentIndex = -1;
  let closestProjectionPoint = null;

  for (let index = 1; index < points.length; index += 1) {
    const projection = closestPointOnScreenSegment(
      screenPoint,
      worldToScreen(state, points[index - 1]),
      worldToScreen(state, points[index]));
    if (projection.distance < closestDistance) {
      closestDistance = projection.distance;
      closestSegmentIndex = index - 1;
      closestProjectionPoint = projection.point;
    }
  }

  return closestSegmentIndex < 0 || !closestProjectionPoint
    ? null
    : {
      target: withSnapPoint(
        createPolylineSegmentTarget(entity, closestSegmentIndex),
        screenToWorld(state, closestProjectionPoint)),
      distance: closestDistance
    };
}

function getSampledCurveEdgeHit(state, entity, screenPoint) {
  const target = createEntityTarget(entity);
  const points = getEntityPoints(entity);
  if (!target || points.length < 2) {
    return null;
  }

  let closestDistance = Number.POSITIVE_INFINITY;
  let closestProjectionPoint = null;

  for (let index = 1; index < points.length; index += 1) {
    const projection = closestPointOnScreenSegment(
      screenPoint,
      worldToScreen(state, points[index - 1]),
      worldToScreen(state, points[index]));
    if (projection.distance < closestDistance) {
      closestDistance = projection.distance;
      closestProjectionPoint = projection.point;
    }
  }

  return closestProjectionPoint
    ? {
      target: withSnapPoint(target, screenToWorld(state, closestProjectionPoint)),
      distance: closestDistance
    }
    : null;
}

function getCircleEdgeHit(state, entity, screenPoint) {
  const target = createEntityTarget(entity);
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);

  if (!target || !center || !isFinitePositive(radius)) {
    return null;
  }

  const screenCenter = worldToScreen(state, center);
  const screenRadius = radius * state.view.scale;
  const vectorX = screenPoint.x - screenCenter.x;
  const vectorY = screenPoint.y - screenCenter.y;
  const vectorLength = Math.hypot(vectorX, vectorY);
  const snapPoint = vectorLength > 0
    ? screenToWorld(state, {
      x: screenCenter.x + vectorX * screenRadius / vectorLength,
      y: screenCenter.y + vectorY * screenRadius / vectorLength
    })
    : null;

  return {
    target: snapPoint ? withSnapPoint(target, snapPoint) : target,
    distance: Math.abs(vectorLength - screenRadius)
  };
}

function getArcEdgeHit(state, entity, screenPoint) {
  const target = createEntityTarget(entity);
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);

  if (!target || !center || !isFinitePositive(radius)) {
    return null;
  }

  const worldPoint = screenToWorld(state, screenPoint);
  const angleDegrees = radiansToDegrees(Math.atan2(worldPoint.y - center.y, worldPoint.x - center.x));
  const screenCenter = worldToScreen(state, center);
  const screenRadius = radius * state.view.scale;

  if (isAngleOnArc(angleDegrees, getEntityStartAngle(entity), getEntityEndAngle(entity))) {
    return {
      target: withSnapPoint(target, pointOnCircle(center, radius, angleDegrees)),
      distance: Math.abs(distanceBetweenScreenPoints(screenPoint, screenCenter) - screenRadius)
    };
  }

  const endpointHits = getArcEndpointWorldPoints(entity).map(point => {
    const screenEndpoint = worldToScreen(state, point);
    return {
      point,
      distance: distanceBetweenScreenPoints(screenPoint, screenEndpoint)
    };
  });

  const nearestEndpoint = endpointHits.reduce((nearest, candidate) =>
    !nearest || candidate.distance < nearest.distance ? candidate : nearest, null);
  if (!nearestEndpoint) {
    return null;
  }

  return {
    target: withSnapPoint(target, nearestEndpoint.point),
    distance: nearestEndpoint.distance
  };
}

function applyBoxSelection(state, selectionBox) {
  const rect = normalizeScreenRect(selectionBox.start, selectionBox.end);
  const crossing = isCrossingSelection(selectionBox);
  const operation = selectionBox.operation;
  const targets = getBoxSelectionTargets(state, rect, crossing);
  let changed = false;

  for (const target of targets) {
    if (operation === "deselect") {
      if (state.selectedKeys.delete(target.key)) {
        changed = true;
      }
    } else if (!state.selectedKeys.has(target.key)) {
      state.selectedKeys.add(target.key);
      changed = true;
    }
  }

  if (syncActiveSelectionWithSelectedKeys(state)) {
    changed = true;
  }

  return changed;
}

function getBoxSelectionTargets(state, rect, crossing) {
  const targets = [];

  for (const entity of getDocumentEntities(state.document)) {
    const target = createEntityTarget(entity);
    if (!target) {
      continue;
    }

    const hit = crossing
      ? entityIntersectsScreenRect(state, entity, rect)
      : entityInsideScreenRect(state, entity, rect);
    if (hit) {
      targets.push(target);
    }
  }

  return targets;
}

function entityInsideScreenRect(state, entity, rect) {
  const bounds = getEntityScreenBounds(state, entity);
  return bounds
    && bounds.minX >= rect.minX
    && bounds.maxX <= rect.maxX
    && bounds.minY >= rect.minY
    && bounds.maxY <= rect.maxY;
}

function entityIntersectsScreenRect(state, entity, rect) {
  const bounds = getEntityScreenBounds(state, entity);
  if (!bounds || !screenRectsOverlap(bounds, rect)) {
    return false;
  }

  const segments = getEntityScreenSegments(state, entity);
  if (segments.length === 0) {
    const center = getEntityCenter(entity);
    return center ? pointInScreenRect(worldToScreen(state, center), rect) : false;
  }

  return segments.some(segment =>
    pointInScreenRect(segment.start, rect)
    || pointInScreenRect(segment.end, rect)
    || screenSegmentIntersectsRect(segment.start, segment.end, rect));
}

function getEntityScreenBounds(state, entity) {
  const points = getEntityScreenSamplePoints(state, entity);
  if (points.length === 0) {
    return null;
  }

  let bounds = null;
  for (const point of points) {
    bounds = includeScreenBoundsPoint(bounds, point);
  }

  return bounds;
}

function getEntityScreenSegments(state, entity) {
  const points = getEntityScreenSamplePoints(state, entity);
  const segments = [];

  for (let index = 1; index < points.length; index += 1) {
    segments.push({
      start: points[index - 1],
      end: points[index]
    });
  }

  if (getEntityKind(entity) === "circle" && points.length > 2) {
    segments.push({
      start: points[points.length - 1],
      end: points[0]
    });
  }

  return segments;
}

function getEntityScreenSamplePoints(state, entity) {
  const kind = getEntityKind(entity);

  if (kind === "point") {
    const point = getPointEntityLocation(entity);
    return point ? [worldToScreen(state, point)] : [];
  }

  if (kind === "line" || kind === "polyline" || kind === "spline") {
    return getEntityPoints(entity).map(point => worldToScreen(state, point));
  }

  if (kind === "circle") {
    const center = getEntityCenter(entity);
    const radius = getEntityRadius(entity);
    if (!center || !isFinitePositive(radius)) {
      return [];
    }

    const points = [];
    for (let index = 0; index < 32; index += 1) {
      points.push(worldToScreen(state, pointOnCircle(center, radius, index * FULL_CIRCLE_DEGREES / 32)));
    }

    return points;
  }

  if (kind === "arc") {
    return getArcWorldPoints(entity).map(point => worldToScreen(state, point));
  }

  return [];
}

function getSnapPoints(entity, entities) {
  const kind = getEntityKind(entity);

  switch (kind) {
    case "point":
      return getPointSnapPoints(entity);
    case "line":
      return getLineSnapPoints(entity, entities);
    case "polyline":
      return getPolylineSnapPoints(entity, entities);
    case "spline":
      return getSplineSnapPoints(entity, entities);
    case "circle":
      return getCircleSnapPoints(entity, entities);
    case "arc":
      return getArcSnapPoints(entity, entities);
    default:
      return [];
  }
}

function getPointSnapPoints(entity) {
  const point = getPointEntityLocation(entity);
  return point ? [{ label: "point", point, priority: 10 }] : [];
}

function getLineSnapPoints(entity, entities) {
  const points = getEntityPoints(entity);
  if (points.length < 2) {
    return [];
  }

  const snapPoints = [
    { label: "start", point: points[0] },
    { label: "end", point: points[1] },
    { label: "mid", point: midpoint(points[0], points[1]) }
  ];

  addIntersectionSnapPoints(snapPoints, entity, entities);
  addTangentSnapPointsForLinearEntity(snapPoints, entity, getLinearSegments(entity), entities);
  return snapPoints;
}

function getPolylineSnapPoints(entity, entities) {
  const points = getEntityPoints(entity);
  const snapPoints = [];

  for (let index = 0; index < points.length; index += 1) {
    snapPoints.push({ label: `vertex-${index}`, point: points[index] });
  }

  for (let index = 1; index < points.length; index += 1) {
    snapPoints.push({ label: `mid-${index - 1}`, point: midpoint(points[index - 1], points[index]) });
  }

  addIntersectionSnapPoints(snapPoints, entity, entities);
  addTangentSnapPointsForLinearEntity(snapPoints, entity, getLinearSegments(entity), entities);
  return snapPoints;
}

function getSplineSnapPoints(entity, entities) {
  const points = getEntityPoints(entity);
  if (points.length < 2) {
    return [];
  }

  const snapPoints = [
    { label: "start", point: points[0] },
    { label: "end", point: points[points.length - 1] },
    { label: "mid", point: points[Math.floor(points.length / 2)] }
  ];

  addIntersectionSnapPoints(snapPoints, entity, entities);
  return snapPoints;
}

function getCircleSnapPoints(entity, entities) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);
  if (!center || !isFinitePositive(radius)) {
    return [];
  }

  const snapPoints = [
    { label: "center", point: center },
    { label: "quadrant-0", point: pointOnCircle(center, radius, 0) },
    { label: "quadrant-90", point: pointOnCircle(center, radius, 90) },
    { label: "quadrant-180", point: pointOnCircle(center, radius, 180) },
    { label: "quadrant-270", point: pointOnCircle(center, radius, 270) }
  ];

  addIntersectionSnapPoints(snapPoints, entity, entities);
  addTangentSnapPointsForCurveEntity(snapPoints, entity, entities);
  return snapPoints;
}

function getArcSnapPoints(entity, entities) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);
  if (!center || !isFinitePositive(radius)) {
    return [];
  }

  const startAngle = getEntityStartAngle(entity);
  const endAngle = getEntityEndAngle(entity);
  const sweep = getPositiveSweepDegrees(startAngle, endAngle);
  const snapPoints = [
    { label: "center", point: center },
    { label: "start", point: pointOnCircle(center, radius, startAngle) },
    { label: "end", point: pointOnCircle(center, radius, endAngle) },
    { label: "mid", point: pointOnCircle(center, radius, startAngle + sweep / 2) }
  ];

  for (const quadrantAngle of [0, 90, 180, 270]) {
    if (isAngleOnArc(quadrantAngle, startAngle, endAngle)) {
      snapPoints.push({
        label: `quadrant-${quadrantAngle}`,
        point: pointOnCircle(center, radius, quadrantAngle)
      });
    }
  }

  addIntersectionSnapPoints(snapPoints, entity, entities);
  addTangentSnapPointsForCurveEntity(snapPoints, entity, entities);
  return snapPoints;
}

function addIntersectionSnapPoints(snapPoints, entity, entities) {
  const entityId = getEntityId(entity);
  if (!entityId || !Array.isArray(entities)) {
    return;
  }

  for (const otherEntity of entities) {
    const otherId = getEntityId(otherEntity);
    if (!otherId || otherId === entityId) {
      continue;
    }

    const intersections = getEntityIntersections(entity, otherEntity);
    for (let index = 0; index < intersections.length; index += 1) {
      addUniqueSnapPoint(snapPoints, `intersection-${otherId}-${index}`, intersections[index], 9);
    }
  }
}

function addTangentSnapPointsForLinearEntity(snapPoints, entity, segments, entities) {
  const entityId = getEntityId(entity);
  if (!entityId || !Array.isArray(entities)) {
    return;
  }

  for (const curveEntity of entities) {
    const curveId = getEntityId(curveEntity);
    if (!curveId || curveId === entityId || !isCurveEntity(curveEntity)) {
      continue;
    }

    for (const segment of segments) {
      const tangentPoint = getSegmentCurveTangentPoint(segment.start, segment.end, curveEntity);
      if (tangentPoint) {
        addUniqueSnapPoint(snapPoints, `tangent-${curveId}`, tangentPoint);
      }
    }
  }
}

function addTangentSnapPointsForCurveEntity(snapPoints, entity, entities) {
  const entityId = getEntityId(entity);
  if (!entityId || !Array.isArray(entities)) {
    return;
  }

  for (const linearEntity of entities) {
    const linearId = getEntityId(linearEntity);
    if (!linearId || linearId === entityId || !isLinearEntity(linearEntity)) {
      continue;
    }

    for (const segment of getLinearSegments(linearEntity)) {
      const tangentPoint = getSegmentCurveTangentPoint(segment.start, segment.end, entity);
      if (tangentPoint) {
        const segmentLabel = segment.segmentIndex === null ? "" : `-${segment.segmentIndex}`;
        addUniqueSnapPoint(snapPoints, `tangent-${linearId}${segmentLabel}`, tangentPoint);
      }
    }
  }
}

function getSegmentCurveTangentPoint(segmentStart, segmentEnd, curveEntity) {
  const center = getEntityCenter(curveEntity);
  const radius = getEntityRadius(curveEntity);
  if (!center || !isFinitePositive(radius)) {
    return null;
  }

  const projection = closestPointOnWorldSegment(center, segmentStart, segmentEnd);
  if (!projection || projection.parameter < -WORLD_GEOMETRY_TOLERANCE || projection.parameter > 1 + WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const tolerance = Math.max(WORLD_GEOMETRY_TOLERANCE, radius * 0.0001);
  if (Math.abs(projection.distance - radius) > tolerance) {
    return null;
  }

  if (getEntityKind(curveEntity) === "arc") {
    const angleDegrees = radiansToDegrees(Math.atan2(projection.point.y - center.y, projection.point.x - center.x));
    if (!isAngleOnArc(angleDegrees, getEntityStartAngle(curveEntity), getEntityEndAngle(curveEntity))) {
      return null;
    }
  }

  return projection.point;
}

function getPointCurveTangentPoints(point, curveEntity) {
  const center = getEntityCenter(curveEntity);
  const radius = getEntityRadius(curveEntity);
  if (!center || !isFinitePositive(radius)) {
    return [];
  }

  const deltaX = point.x - center.x;
  const deltaY = point.y - center.y;
  const distance = Math.hypot(deltaX, deltaY);
  if (distance <= radius + WORLD_GEOMETRY_TOLERANCE) {
    return [];
  }

  const baseAngle = Math.atan2(deltaY, deltaX);
  const offsetAngle = Math.acos(clamp(radius / distance, -1, 1));
  const tangentAngles = [baseAngle + offsetAngle, baseAngle - offsetAngle];
  const points = [];

  for (const tangentAngle of tangentAngles) {
    const tangentPoint = {
      x: center.x + radius * Math.cos(tangentAngle),
      y: center.y + radius * Math.sin(tangentAngle)
    };

    if (getEntityKind(curveEntity) === "arc") {
      const tangentDegrees = radiansToDegrees(tangentAngle);
      if (!isAngleOnArc(tangentDegrees, getEntityStartAngle(curveEntity), getEntityEndAngle(curveEntity))) {
        continue;
      }
    }

    points.push(tangentPoint);
  }

  return points;
}

function getEntityOrthographicSnapPoints(entity, sourcePoint, highlightedPoint) {
  const kind = getEntityKind(entity);
  const points = [];

  if (kind === "line" || kind === "polyline" || kind === "spline") {
    for (const segment of getLinearSegments(entity)) {
      addSegmentOrthographicSnapPoints(points, segment, sourcePoint, highlightedPoint);
    }
  } else if (kind === "circle" || kind === "arc") {
    const center = getEntityCenter(entity);
    const radius = getEntityRadius(entity);
    if (center && isFinitePositive(radius)) {
      const curve = {
        kind: "curve",
        center,
        radius,
        startAngle: kind === "arc" ? getEntityStartAngle(entity) : null,
        endAngle: kind === "arc" ? getEntityEndAngle(entity) : null
      };
      addCurveOrthographicSnapPoints(points, curve, sourcePoint, highlightedPoint);
    }
  }

  return points.map((snap, index) => ({ ...snap, index }));
}

function addSegmentOrthographicSnapPoints(points, segment, sourcePoint, highlightedPoint) {
  const dx = segment.end.x - segment.start.x;
  const dy = segment.end.y - segment.start.y;

  if (Math.abs(dx) <= WORLD_GEOMETRY_TOLERANCE) {
    if (Math.abs(sourcePoint.x - segment.start.x) <= WORLD_GEOMETRY_TOLERANCE
      && isPointOnSegmentPrimitive(highlightedPoint, segment)) {
      addUniqueOrthoSnapPoint(points, "vertical", highlightedPoint);
    }
  } else {
    const parameter = (sourcePoint.x - segment.start.x) / dx;
    if (isUnitParameter(parameter)) {
      addUniqueOrthoSnapPoint(points, "vertical", {
        x: sourcePoint.x,
        y: segment.start.y + dy * parameter
      });
    }
  }

  if (Math.abs(dy) <= WORLD_GEOMETRY_TOLERANCE) {
    if (Math.abs(sourcePoint.y - segment.start.y) <= WORLD_GEOMETRY_TOLERANCE
      && isPointOnSegmentPrimitive(highlightedPoint, segment)) {
      addUniqueOrthoSnapPoint(points, "horizontal", highlightedPoint);
    }
  } else {
    const parameter = (sourcePoint.y - segment.start.y) / dy;
    if (isUnitParameter(parameter)) {
      addUniqueOrthoSnapPoint(points, "horizontal", {
        x: segment.start.x + dx * parameter,
        y: sourcePoint.y
      });
    }
  }
}

function addCurveOrthographicSnapPoints(points, curve, sourcePoint, highlightedPoint) {
  const dx = sourcePoint.x - curve.center.x;
  const verticalRemainder = curve.radius * curve.radius - dx * dx;
  if (verticalRemainder >= -WORLD_GEOMETRY_TOLERANCE) {
    const offset = Math.sqrt(Math.max(0, verticalRemainder));
    for (const y of [curve.center.y + offset, curve.center.y - offset]) {
      const point = { x: sourcePoint.x, y };
      if (isPointOnCurvePrimitive(point, curve)) {
        addUniqueOrthoSnapPoint(points, "vertical", point);
      }
    }
  } else if (Math.abs(highlightedPoint.x - sourcePoint.x) <= WORLD_GEOMETRY_TOLERANCE
    && isPointOnCurvePrimitive(highlightedPoint, curve)) {
    addUniqueOrthoSnapPoint(points, "vertical", highlightedPoint);
  }

  const dy = sourcePoint.y - curve.center.y;
  const horizontalRemainder = curve.radius * curve.radius - dy * dy;
  if (horizontalRemainder >= -WORLD_GEOMETRY_TOLERANCE) {
    const offset = Math.sqrt(Math.max(0, horizontalRemainder));
    for (const x of [curve.center.x + offset, curve.center.x - offset]) {
      const point = { x, y: sourcePoint.y };
      if (isPointOnCurvePrimitive(point, curve)) {
        addUniqueOrthoSnapPoint(points, "horizontal", point);
      }
    }
  } else if (Math.abs(highlightedPoint.y - sourcePoint.y) <= WORLD_GEOMETRY_TOLERANCE
    && isPointOnCurvePrimitive(highlightedPoint, curve)) {
    addUniqueOrthoSnapPoint(points, "horizontal", highlightedPoint);
  }
}

function addUniqueOrthoSnapPoint(points, orientation, point) {
  const duplicate = points.some(existing =>
    existing.orientation === orientation
    && distanceBetweenWorldPoints(existing.point, point) <= WORLD_GEOMETRY_TOLERANCE);

  if (!duplicate) {
    points.push({ orientation, point });
  }
}

function getLinearSegments(entity) {
  const kind = getEntityKind(entity);
  const points = getEntityPoints(entity);

  if (kind === "line" && points.length >= 2) {
    return [{
      start: points[0],
      end: points[1],
      segmentIndex: null
    }];
  }

  if ((kind !== "polyline" && kind !== "spline") || points.length < 2) {
    return [];
  }

  const segments = [];
  for (let index = 1; index < points.length; index += 1) {
    segments.push({
      start: points[index - 1],
      end: points[index],
      segmentIndex: index - 1
    });
  }

  return segments;
}

function getEntityIntersections(firstEntity, secondEntity) {
  const intersections = [];
  const firstPrimitives = getIntersectionPrimitives(firstEntity);
  const secondPrimitives = getIntersectionPrimitives(secondEntity);

  for (const first of firstPrimitives) {
    for (const second of secondPrimitives) {
      for (const point of getPrimitiveIntersections(first, second)) {
        addUniquePoint(intersections, point);
      }
    }
  }

  return intersections;
}

function getIntersectionPrimitives(entity) {
  const kind = getEntityKind(entity);

  if (kind === "line" || kind === "polyline" || kind === "spline") {
    return getLinearSegments(entity).map(segment => ({
      kind: "segment",
      start: segment.start,
      end: segment.end
    }));
  }

  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);
  if (!center || !isFinitePositive(radius)) {
    return [];
  }

  if (kind === "circle") {
    return [{
      kind: "curve",
      center,
      radius,
      startAngle: null,
      endAngle: null
    }];
  }

  if (kind === "arc") {
    return [{
      kind: "curve",
      center,
      radius,
      startAngle: getEntityStartAngle(entity),
      endAngle: getEntityEndAngle(entity)
    }];
  }

  return [];
}

function getPrimitiveIntersections(first, second) {
  if (first.kind === "segment" && second.kind === "segment") {
    return getSegmentSegmentIntersections(first, second);
  }

  if (first.kind === "segment" && second.kind === "curve") {
    return getSegmentCurveIntersections(first, second);
  }

  if (first.kind === "curve" && second.kind === "segment") {
    return getSegmentCurveIntersections(second, first);
  }

  if (first.kind === "curve" && second.kind === "curve") {
    return getCurveCurveIntersections(first, second);
  }

  return [];
}

function getSegmentSegmentIntersections(first, second) {
  const r = subtractPoints(first.end, first.start);
  const s = subtractPoints(second.end, second.start);
  const denominator = crossPoints(r, s);
  const delta = subtractPoints(second.start, first.start);

  if (Math.abs(denominator) <= WORLD_GEOMETRY_TOLERANCE) {
    if (Math.abs(crossPoints(delta, r)) > WORLD_GEOMETRY_TOLERANCE) {
      return [];
    }

    const points = [];
    for (const point of [first.start, first.end]) {
      if (isPointOnSegmentPrimitive(point, second)) {
        addUniquePoint(points, point);
      }
    }

    for (const point of [second.start, second.end]) {
      if (isPointOnSegmentPrimitive(point, first)) {
        addUniquePoint(points, point);
      }
    }

    return points;
  }

  const t = crossPoints(delta, s) / denominator;
  const u = crossPoints(delta, r) / denominator;
  if (!isUnitParameter(t) || !isUnitParameter(u)) {
    return [];
  }

  return [{
    x: first.start.x + r.x * t,
    y: first.start.y + r.y * t
  }];
}

function getSegmentCurveIntersections(segment, curve) {
  const direction = subtractPoints(segment.end, segment.start);
  const offset = subtractPoints(segment.start, curve.center);
  const a = dotPoints(direction, direction);
  if (a <= WORLD_GEOMETRY_TOLERANCE * WORLD_GEOMETRY_TOLERANCE) {
    return [];
  }

  const b = 2 * dotPoints(offset, direction);
  const c = dotPoints(offset, offset) - curve.radius * curve.radius;
  const discriminant = b * b - 4 * a * c;
  if (discriminant < -WORLD_GEOMETRY_TOLERANCE) {
    return [];
  }

  const parameters = [];
  if (Math.abs(discriminant) <= WORLD_GEOMETRY_TOLERANCE) {
    parameters.push(-b / (2 * a));
  } else {
    const root = Math.sqrt(Math.max(0, discriminant));
    parameters.push((-b - root) / (2 * a));
    parameters.push((-b + root) / (2 * a));
  }

  const points = [];
  for (const parameter of parameters) {
    if (!isUnitParameter(parameter)) {
      continue;
    }

    const point = {
      x: segment.start.x + direction.x * parameter,
      y: segment.start.y + direction.y * parameter
    };
    if (isPointOnCurvePrimitive(point, curve)) {
      addUniquePoint(points, point);
    }
  }

  return points;
}

function getCurveCurveIntersections(first, second) {
  const distance = distanceBetweenWorldPoints(first.center, second.center);
  if (distance <= WORLD_GEOMETRY_TOLERANCE) {
    return [];
  }

  if (distance > first.radius + second.radius + WORLD_GEOMETRY_TOLERANCE
    || distance < Math.abs(first.radius - second.radius) - WORLD_GEOMETRY_TOLERANCE) {
    return [];
  }

  const a = (first.radius * first.radius - second.radius * second.radius + distance * distance) / (2 * distance);
  const hSquared = first.radius * first.radius - a * a;
  if (hSquared < -WORLD_GEOMETRY_TOLERANCE) {
    return [];
  }

  const direction = {
    x: (second.center.x - first.center.x) / distance,
    y: (second.center.y - first.center.y) / distance
  };
  const base = {
    x: first.center.x + direction.x * a,
    y: first.center.y + direction.y * a
  };
  const h = Math.sqrt(Math.max(0, hSquared));
  const candidates = Math.abs(h) <= WORLD_GEOMETRY_TOLERANCE
    ? [base]
    : [
      { x: base.x - direction.y * h, y: base.y + direction.x * h },
      { x: base.x + direction.y * h, y: base.y - direction.x * h }
    ];

  const points = [];
  for (const point of candidates) {
    if (isPointOnCurvePrimitive(point, first) && isPointOnCurvePrimitive(point, second)) {
      addUniquePoint(points, point);
    }
  }

  return points;
}

function addUniqueSnapPoint(snapPoints, label, point, priority = null) {
  const isTangent = String(label).startsWith("tangent-");
  const duplicate = snapPoints.some(existing =>
    Math.abs(existing.point.x - point.x) <= WORLD_GEOMETRY_TOLERANCE
    && Math.abs(existing.point.y - point.y) <= WORLD_GEOMETRY_TOLERANCE);

  if (!duplicate || isTangent) {
    snapPoints.push({ label, point, priority: priority === null ? (isTangent ? 10 : 0) : priority });
  }
}

function isLinearEntity(entity) {
  const kind = getEntityKind(entity);
  return kind === "line" || kind === "polyline";
}

function isCurveEntity(entity) {
  const kind = getEntityKind(entity);
  return kind === "circle" || kind === "arc";
}

function createEntityTarget(entity) {
  const id = getEntityId(entity);
  if (!id) {
    return null;
  }

  return {
    kind: "entity",
    key: id,
    entityId: id,
    entity
  };
}

function createPolylineSegmentTarget(entity, segmentIndex) {
  const id = getEntityId(entity);
  if (!id) {
    return null;
  }

  return {
    kind: "segment",
    key: `${id}${SEGMENT_KEY_SEPARATOR}${segmentIndex}`,
    entityId: id,
    entity,
    segmentIndex
  };
}

function createPointTarget(entity, label, point) {
  const id = getEntityId(entity);
  if (!id) {
    return null;
  }

  return {
    kind: "point",
    key: `${id}${POINT_KEY_SEPARATOR}${sanitizeKeyPart(label)}|${formatKeyNumber(point.x)}|${formatKeyNumber(point.y)}`,
    entityId: id,
    entity,
    label,
    point
  };
}

function createDimensionTarget(dimension) {
  if (!dimension || !dimension.id) {
    return null;
  }

  return {
    kind: "dimension",
    key: dimension.key,
    dimensionId: dimension.id,
    dimension
  };
}

function createDynamicPointTarget(label, point, guides = []) {
  return {
    kind: "point",
    key: `${DYNAMIC_POINT_KEY_PREFIX}${POINT_KEY_SEPARATOR}${sanitizeKeyPart(label)}|${formatKeyNumber(point.x)}|${formatKeyNumber(point.y)}`,
    entityId: null,
    entity: null,
    label,
    point,
    dynamic: true,
    guides
  };
}

export function getPointTargetMarker(target) {
  const label = String(target && target.label ? target.label : "").toLowerCase();
  if (label.includes("tangent")) {
    return "tangent";
  }

  if (label === "mid" || label.startsWith("mid-") || label.startsWith("midpoint-")) {
    return "midpoint";
  }

  return "point";
}

function withSnapPoint(target, snapPoint) {
  return target
    ? { ...target, snapPoint }
    : null;
}

function resolveSelectionTarget(state, key) {
  const pointTarget = parsePointTargetKey(state, key);
  if (pointTarget) {
    return pointTarget;
  }

  const segmentTarget = parseSegmentTargetKey(state, key);
  if (segmentTarget) {
    return segmentTarget;
  }

  const dimensionTarget = parseDimensionTargetKey(state, key);
  if (dimensionTarget) {
    return dimensionTarget;
  }

  const entity = getDocumentEntities(state.document)
    .find(candidate => StringComparer(getEntityId(candidate), key));
  return entity ? createEntityTarget(entity) : null;
}

function parsePointTargetKey(state, key) {
  const separatorIndex = key.indexOf(POINT_KEY_SEPARATOR);
  if (separatorIndex < 0) {
    return null;
  }

  const entityId = key.slice(0, separatorIndex);
  const parts = key.slice(separatorIndex + POINT_KEY_SEPARATOR.length).split("|");
  if (parts.length < 3) {
    return null;
  }

  const x = Number(parts[parts.length - 2]);
  const y = Number(parts[parts.length - 1]);
  if (!Number.isFinite(x) || !Number.isFinite(y)) {
    return null;
  }

  const entity = getDocumentEntities(state.document)
    .find(candidate => StringComparer(getEntityId(candidate), entityId));
  if (!entity) {
    return null;
  }

  const label = parts.slice(0, parts.length - 2).join("|");
  return {
    kind: "point",
    key,
    entityId,
    entity,
    label,
    point: resolveCurrentPointTargetPoint(entity, label, { x, y })
  };
}

function resolveCurrentPointTargetPoint(entity, label, fallbackPoint) {
  if (!entity) {
    return fallbackPoint;
  }

  const normalizedLabel = String(label || "").split("|")[0].toLowerCase();
  const kind = getEntityKind(entity);
  const points = getEntityPoints(entity);
  if (kind === "line" && points.length >= 2) {
    if (normalizedLabel === "start") {
      return points[0];
    }

    if (normalizedLabel === "end") {
      return points[1];
    }

    if (normalizedLabel === "mid") {
      return midpoint(points[0], points[1]);
    }
  }

  if (kind === "polyline" && points.length >= 2) {
    const vertexIndex = parseIndexedPointLabel(normalizedLabel, "vertex-");
    if (vertexIndex !== null && vertexIndex >= 0 && vertexIndex < points.length) {
      return points[vertexIndex];
    }

    const segmentIndex = parseIndexedPointLabel(normalizedLabel, "mid-");
    if (segmentIndex !== null && segmentIndex >= 0 && segmentIndex < points.length - 1) {
      return midpoint(points[segmentIndex], points[segmentIndex + 1]);
    }
  }

  if (kind === "point" && normalizedLabel === "point") {
    return getPointEntityLocation(entity) || fallbackPoint;
  }

  if (kind === "circle" || kind === "arc") {
    const center = getEntityCenter(entity);
    const radius = getEntityRadius(entity);
    if (!center || !isFinitePositive(radius)) {
      return fallbackPoint;
    }

    if (normalizedLabel === "center") {
      return center;
    }

    if (kind === "arc" && normalizedLabel === "start") {
      return pointOnCircle(center, radius, getEntityStartAngle(entity));
    }

    if (kind === "arc" && normalizedLabel === "end") {
      return pointOnCircle(center, radius, getEntityEndAngle(entity));
    }

    if (kind === "arc" && normalizedLabel === "mid") {
      const startAngle = getEntityStartAngle(entity);
      const sweep = getPositiveSweepDegrees(startAngle, getEntityEndAngle(entity));
      return pointOnCircle(center, radius, startAngle + sweep / 2);
    }

    const quadrantAngle = parseIndexedPointLabel(normalizedLabel, "quadrant-");
    if (quadrantAngle !== null) {
      return pointOnCircle(center, radius, quadrantAngle);
    }
  }

  return fallbackPoint;
}

function parseIndexedPointLabel(label, prefix) {
  if (!label.startsWith(prefix)) {
    return null;
  }

  const value = Number(label.slice(prefix.length));
  return Number.isFinite(value) ? value : null;
}

export function applyGeometryDragPreview(document, selectionKey, dragStart, dragEnd, constrainToCurrentVector = false) {
  if (!document || !selectionKey || !dragStart || !dragEnd) {
    return null;
  }

  const delta = {
    x: Number(dragEnd.x) - Number(dragStart.x),
    y: Number(dragEnd.y) - Number(dragStart.y)
  };
  if (!Number.isFinite(delta.x) || !Number.isFinite(delta.y)) {
    return null;
  }

  const previewDocument = cloneCanvasDocument(document);
  if (!applyGeometryDragPreviewMutation(previewDocument, selectionKey, delta, dragEnd, constrainToCurrentVector)) {
    return null;
  }

  previewDocument.bounds = computeEntityBounds(getDocumentEntities(previewDocument));
  return previewDocument;
}

function applyGeometryDragPreviewMutation(document, selectionKey, delta, dragEnd, constrainToCurrentVector = false) {
  const segmentReference = parseSegmentSelectionKey(selectionKey);
  if (segmentReference) {
    return translatePreviewPolylineSegment(document, segmentReference.entityId, segmentReference.segmentIndex, delta);
  }

  const pointReference = parsePointSelectionKey(selectionKey);
  if (pointReference) {
    const entity = findCanvasDocumentEntity(document, pointReference.entityId);
    return entity
      ? applyPreviewPointDrag(document, entity, pointReference.label, delta, dragEnd, constrainToCurrentVector)
      : false;
  }

  const entity = findCanvasDocumentEntity(document, selectionKey);
  return entity ? applyPreviewEntityDrag(document, entity, delta, dragEnd) : false;
}

function applyPreviewPointDrag(document, entity, label, delta, dragEnd, constrainToCurrentVector = false) {
  const normalizedLabel = String(label || "").split("|")[0].toLowerCase();
  const kind = getEntityKind(entity);
  if (kind === "line") {
    const points = getEntityPoints(entity);
    if (points.length < 2) {
      return false;
    }

    if (normalizedLabel === "start") {
      entity.points = [
        constrainToCurrentVector ? projectWorldPointToLine(dragEnd, points[0], points[1]) : addWorldPoints(points[0], delta),
        points[1]
      ];
      return true;
    }

    if (normalizedLabel === "end") {
      entity.points = [
        points[0],
        constrainToCurrentVector ? projectWorldPointToLine(dragEnd, points[0], points[1]) : addWorldPoints(points[1], delta)
      ];
      return true;
    }

    if (normalizedLabel === "mid") {
      entity.points = points.map(point => addWorldPoints(point, delta));
      return true;
    }

    return false;
  }

  if (kind === "polyline") {
    const vertexIndex = parseIndexedPointLabel(normalizedLabel, "vertex-");
    if (vertexIndex !== null) {
      const points = getEntityPoints(entity);
      if (vertexIndex < 0 || vertexIndex >= points.length) {
        return false;
      }

      points[vertexIndex] = addWorldPoints(points[vertexIndex], delta);
      entity.points = points;
      return true;
    }

    const segmentIndex = parseIndexedPointLabel(normalizedLabel, "mid-");
    return segmentIndex !== null
      ? translatePreviewPolylineSegment(document, getEntityId(entity), segmentIndex, delta)
      : false;
  }

  if (kind === "point") {
    const point = getPointEntityLocation(entity);
    if (!point) {
      return false;
    }

    entity.points = [addWorldPoints(point, delta)];
    return true;
  }

  if (kind === "circle" || kind === "arc") {
    const center = getEntityCenter(entity);
    if (!center) {
      return false;
    }

    if (normalizedLabel === "center") {
      entity.center = addWorldPoints(center, delta);
      return true;
    }

    if (kind === "arc" && (normalizedLabel === "start" || normalizedLabel === "end")) {
      const radius = constrainToCurrentVector
        ? getEntityRadius(entity)
        : distanceBetweenWorldPoints(center, dragEnd);
      if (radius <= WORLD_GEOMETRY_TOLERANCE) {
        return false;
      }

      const angle = radiansToDegrees(Math.atan2(dragEnd.y - center.y, dragEnd.x - center.x));
      entity.radius = radius;
      if (normalizedLabel === "start") {
        entity.startAngleDegrees = angle;
      } else {
        entity.endAngleDegrees = angle;
      }

      return true;
    }

    const radius = distanceBetweenWorldPoints(center, dragEnd);
    if (radius <= WORLD_GEOMETRY_TOLERANCE) {
      return false;
    }

    entity.radius = radius;
    return true;
  }

  return false;
}

function applyPreviewEntityDrag(document, entity, delta, dragEnd) {
  const kind = getEntityKind(entity);
  if (kind === "line" || kind === "polyline") {
    entity.points = getEntityPoints(entity).map(point => addWorldPoints(point, delta));
    return true;
  }

  if (kind === "point") {
    const point = getPointEntityLocation(entity);
    if (!point) {
      return false;
    }

    entity.points = [addWorldPoints(point, delta)];
    return true;
  }

  if (kind === "circle" || kind === "arc") {
    const center = getEntityCenter(entity);
    if (!center) {
      return false;
    }

    const radius = distanceBetweenWorldPoints(center, dragEnd);
    if (radius <= WORLD_GEOMETRY_TOLERANCE) {
      return false;
    }

    entity.radius = radius;
    return true;
  }

  return false;
}

function translatePreviewPolylineSegment(document, entityId, segmentIndex, delta) {
  const entity = findCanvasDocumentEntity(document, entityId);
  const points = entity ? getEntityPoints(entity) : [];
  if (!entity || getEntityKind(entity) !== "polyline" || segmentIndex < 0 || segmentIndex >= points.length - 1) {
    return false;
  }

  points[segmentIndex] = addWorldPoints(points[segmentIndex], delta);
  points[segmentIndex + 1] = addWorldPoints(points[segmentIndex + 1], delta);
  entity.points = points;
  return true;
}

function findCanvasDocumentEntity(document, entityId) {
  return getDocumentEntities(document)
    .find(entity => StringComparer(getEntityId(entity), entityId)) || null;
}

function cloneCanvasDocument(document) {
  const entities = getDocumentEntities(document).map(cloneCanvasEntity);
  return {
    ...document,
    entities,
    bounds: readBounds(readProperty(document, "bounds", "Bounds")) || computeEntityBounds(entities),
    dimensions: getDocumentDimensions(document).slice(),
    constraints: getDocumentConstraints(document).slice()
  };
}

function cloneCanvasEntity(entity) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);
  const startAngle = getEntityStartAngle(entity);
  const endAngle = getEntityEndAngle(entity);
  return {
    ...entity,
    id: getEntityId(entity),
    kind: getEntityKind(entity),
    points: getEntityPoints(entity).map(point => ({ ...point })),
    center: center ? { ...center } : null,
    radius: Number.isFinite(radius) ? radius : null,
    startAngleDegrees: Number.isFinite(startAngle) ? startAngle : null,
    endAngleDegrees: Number.isFinite(endAngle) ? endAngle : null,
    isConstruction: Boolean(readProperty(entity, "isConstruction", "IsConstruction"))
  };
}

function parsePointSelectionKey(selectionKey) {
  const key = String(selectionKey || "");
  const separatorIndex = key.indexOf(POINT_KEY_SEPARATOR);
  if (separatorIndex < 0) {
    return null;
  }

  const entityId = key.slice(0, separatorIndex);
  const parts = key.slice(separatorIndex + POINT_KEY_SEPARATOR.length).split("|");
  if (!entityId || parts.length < 3) {
    return null;
  }

  return {
    entityId,
    label: parts.slice(0, parts.length - 2).join("|")
  };
}

function parseSegmentSelectionKey(selectionKey) {
  const key = String(selectionKey || "");
  const separatorIndex = key.indexOf(SEGMENT_KEY_SEPARATOR);
  if (separatorIndex < 0) {
    return null;
  }

  const entityId = key.slice(0, separatorIndex);
  const segmentIndex = Number(key.slice(separatorIndex + SEGMENT_KEY_SEPARATOR.length));
  return entityId && Number.isInteger(segmentIndex)
    ? { entityId, segmentIndex }
    : null;
}

function addWorldPoints(point, delta) {
  return {
    x: point.x + delta.x,
    y: point.y + delta.y
  };
}

function projectWorldPointToLine(point, start, end) {
  const deltaX = end.x - start.x;
  const deltaY = end.y - start.y;
  const lengthSquared = deltaX * deltaX + deltaY * deltaY;
  if (lengthSquared <= WORLD_GEOMETRY_TOLERANCE * WORLD_GEOMETRY_TOLERANCE) {
    return start;
  }

  const scalar = (((point.x - start.x) * deltaX) + ((point.y - start.y) * deltaY)) / lengthSquared;
  return {
    x: start.x + deltaX * scalar,
    y: start.y + deltaY * scalar
  };
}

function parseSegmentTargetKey(state, key) {
  const separatorIndex = key.indexOf(SEGMENT_KEY_SEPARATOR);
  if (separatorIndex < 0) {
    return null;
  }

  const entityId = key.slice(0, separatorIndex);
  const segmentIndex = Number(key.slice(separatorIndex + SEGMENT_KEY_SEPARATOR.length));
  if (!Number.isInteger(segmentIndex)) {
    return null;
  }

  const entity = getDocumentEntities(state.document)
    .find(candidate => StringComparer(getEntityId(candidate), entityId));
  return entity ? createPolylineSegmentTarget(entity, segmentIndex, null) : null;
}

function parseDimensionTargetKey(state, key) {
  if (!String(key || "").startsWith("persistent-")) {
    return null;
  }

  const dimension = getPersistentDimensionDescriptors(state)
    .find(candidate => StringComparer(candidate.key, key));
  return dimension ? createDimensionTarget(dimension) : null;
}

export function applyDirectSelectionClick(state, selectionKey) {
  if (!selectionKey) {
    return false;
  }

  if (state.activeSelectionKey === selectionKey) {
    const removed = state.selectedKeys.delete(selectionKey);
    state.activeSelectionKey = null;
    return removed;
  }

  if (!state.selectedKeys.has(selectionKey)) {
    state.selectedKeys.add(selectionKey);
  }

  state.activeSelectionKey = selectionKey;
  return true;
}

export function syncActiveSelectionWithSelectedKeys(state) {
  if (state.activeSelectionKey && !state.selectedKeys.has(state.activeSelectionKey)) {
    state.activeSelectionKey = null;
    return true;
  }

  return false;
}

export function applyDraftDimensionValue(state, dimensionKey, value) {
  const dimension = String(dimensionKey || "").toLowerCase();
  const numericValue = Number(value);
  if (!Number.isFinite(numericValue) || numericValue <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

  const tool = getSketchCreationTool(state);
  if (!tool || !state.toolDraft || state.toolDraft.points.length === 0) {
    return false;
  }

  const points = state.toolDraft.points;
  const anchor = points[0];
  const previewPoint = state.toolDraft.previewPoint || { x: anchor.x + 1, y: anchor.y };
  let nextPreviewPoint = null;

  if (tool === "line" && dimension === "length") {
    nextPreviewPoint = pointFromAnchorWithLength(anchor, previewPoint, numericValue);
  } else if (tool === "midpointline" && dimension === "length") {
    nextPreviewPoint = pointFromAnchorWithLength(anchor, previewPoint, numericValue / 2);
  } else if (tool === "centercircle" && dimension === "radius") {
    nextPreviewPoint = pointFromAnchorWithLength(anchor, previewPoint, numericValue);
  } else if (tool === "centerpointarc" && dimension === "radius") {
    if (points.length === 1) {
      nextPreviewPoint = pointFromAnchorWithLength(anchor, previewPoint, numericValue);
    } else {
      const radiusPoint = points[1];
      const nextRadiusPoint = pointFromAnchorWithLength(anchor, radiusPoint, numericValue);
      state.toolDraft.points = [anchor, nextRadiusPoint];
      nextPreviewPoint = previewPoint;
    }
  } else if (tool === "centerpointarc" && dimension === "sweep" && points.length >= 2) {
    nextPreviewPoint = getCenterPointArcEndPointWithSweep(points[0], points[1], previewPoint, numericValue);
  } else if (tool === "threepointarc" && dimension === "radius" && points.length >= 2) {
    nextPreviewPoint = getThreePointArcEndPointWithRadius(points[0], points[1], previewPoint, numericValue);
  } else if (tool === "threepointarc" && dimension === "sweep" && points.length >= 2) {
    nextPreviewPoint = getThreePointArcEndPointWithSweep(points[0], points[1], previewPoint, numericValue);
  } else if (tool === "twopointrectangle" && (dimension === "width" || dimension === "height")) {
    const width = dimension === "width" ? numericValue : Math.abs(previewPoint.x - anchor.x);
    const height = dimension === "height" ? numericValue : Math.abs(previewPoint.y - anchor.y);
    const signX = previewPoint.x < anchor.x ? -1 : 1;
    const signY = previewPoint.y < anchor.y ? -1 : 1;
    nextPreviewPoint = {
      x: anchor.x + width * signX,
      y: anchor.y + height * signY
    };
  } else if (tool === "alignedrectangle" && dimension === "length") {
    if (points.length === 1) {
      nextPreviewPoint = pointFromAnchorWithLength(anchor, previewPoint, numericValue);
    } else {
      const baselineEnd = points[1];
      const nextBaselineEnd = pointFromAnchorWithLength(anchor, baselineEnd, numericValue);
      const depthVector = getAlignedRectangleDepthVector(anchor, baselineEnd, previewPoint);
      state.toolDraft.points = [anchor, nextBaselineEnd];
      nextPreviewPoint = {
        x: nextBaselineEnd.x + depthVector.x,
        y: nextBaselineEnd.y + depthVector.y
      };
    }
  } else if (tool === "alignedrectangle" && dimension === "depth" && points.length >= 2) {
    const baselineEnd = points[1];
    const normal = getLeftNormal(anchor, baselineEnd);
    if (normal) {
      const currentDepth = dotPoints(subtractPoints(previewPoint, baselineEnd), normal);
      const sign = currentDepth < 0 ? -1 : 1;
      nextPreviewPoint = {
        x: baselineEnd.x + normal.x * numericValue * sign,
        y: baselineEnd.y + normal.y * numericValue * sign
      };
    }
  }

  if (!nextPreviewPoint) {
    return false;
  }

  state.toolDraft.previewPoint = nextPreviewPoint;
  state.toolDraft.dimensionValues = {
    ...(state.toolDraft.dimensionValues || {}),
    [dimension]: numericValue
  };
  return true;
}

function getLockedDraftDimensionValue(state, dimensionKey) {
  const dimension = String(dimensionKey || "").toLowerCase();
  const value = state && state.toolDraft && state.toolDraft.dimensionValues
    ? state.toolDraft.dimensionValues[dimension]
    : null;
  const numericValue = Number(value);
  return Number.isFinite(numericValue) ? numericValue : null;
}

function clearDraftDimensionValue(state, dimensionKey) {
  const dimension = String(dimensionKey || "").toLowerCase();
  if (!dimension || !state || !state.toolDraft || !state.toolDraft.dimensionValues || !(dimension in state.toolDraft.dimensionValues)) {
    return false;
  }

  const nextDimensionValues = { ...state.toolDraft.dimensionValues };
  delete nextDimensionValues[dimension];
  state.toolDraft.dimensionValues = nextDimensionValues;
  return true;
}

export function applyLockedDraftDimensions(state) {
  if (!state || !state.toolDraft || !state.toolDraft.dimensionValues) {
    return false;
  }

  let changed = false;
  for (const [dimension, value] of Object.entries(state.toolDraft.dimensionValues)) {
    if (applyDraftDimensionValue(state, dimension, value)) {
      changed = true;
    }
  }

  return changed;
}

export function getSketchToolDimensionLocks(toolDraft) {
  const keys = [];
  const values = [];
  const dimensionValues = toolDraft && toolDraft.dimensionValues
    ? toolDraft.dimensionValues
    : {};

  for (const [key, value] of Object.entries(dimensionValues)) {
    const normalizedKey = String(key || "").trim().toLowerCase();
    const numericValue = Number(value);
    if (!normalizedKey || !Number.isFinite(numericValue) || numericValue <= WORLD_GEOMETRY_TOLERANCE) {
      continue;
    }

    keys.push(normalizedKey);
    values.push(numericValue);
  }

  return { keys, values };
}

export function isDynamicTargetCurrentToPointer(state, target) {
  if (!target || !target.dynamic) {
    return true;
  }

  if (!state || !state.pointerScreenPoint || !target.point) {
    return false;
  }

  const targetScreenPoint = worldToScreen(state, target.point);
  return distanceBetweenScreenPoints(state.pointerScreenPoint, targetScreenPoint) <= SNAP_POINT_TOLERANCE;
}

function pointFromAnchorWithLength(anchor, referencePoint, length) {
  const dx = referencePoint.x - anchor.x;
  const dy = referencePoint.y - anchor.y;
  const currentLength = Math.hypot(dx, dy);
  if (currentLength <= WORLD_GEOMETRY_TOLERANCE) {
    return {
      x: anchor.x + length,
      y: anchor.y
    };
  }

  return {
    x: anchor.x + dx / currentLength * length,
    y: anchor.y + dy / currentLength * length
  };
}

function getThreePointArcEndPointWithSweep(first, through, currentEnd, sweepDegrees) {
  const arcState = getThreePointArcConstructionState(first, through, currentEnd);
  if (!arcState || !Number.isFinite(sweepDegrees) || sweepDegrees <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const throughSweep = arcState.direction > 0
    ? getCounterClockwiseDeltaDegrees(arcState.firstAngleDegrees, arcState.throughAngleDegrees)
    : FULL_CIRCLE_DEGREES - getCounterClockwiseDeltaDegrees(arcState.firstAngleDegrees, arcState.throughAngleDegrees);
  if (sweepDegrees <= throughSweep + WORLD_GEOMETRY_TOLERANCE || sweepDegrees >= FULL_CIRCLE_DEGREES) {
    return null;
  }

  return pointOnCircle(
    arcState.center,
    arcState.radius,
    arcState.firstAngleDegrees + arcState.direction * sweepDegrees);
}

function getCenterPointArcEndPointWithSweep(center, startRadiusPoint, currentEnd, sweepDegrees) {
  const radius = distanceBetweenWorldPoints(center, startRadiusPoint);
  if (!Number.isFinite(sweepDegrees)
    || sweepDegrees <= WORLD_GEOMETRY_TOLERANCE
    || sweepDegrees >= FULL_CIRCLE_DEGREES
    || radius <= WORLD_GEOMETRY_TOLERANCE
    || distanceBetweenWorldPoints(center, currentEnd) <= WORLD_GEOMETRY_TOLERANCE) {
    return currentEnd;
  }

  const startAngle = getPointAngleDegrees(center, startRadiusPoint);
  const currentEndAngle = getPointAngleDegrees(center, currentEnd);
  const counterClockwiseDelta = getCounterClockwiseDeltaDegrees(startAngle, currentEndAngle);
  const direction = counterClockwiseDelta <= FULL_CIRCLE_DEGREES / 2 ? 1 : -1;
  return pointOnCircle(center, radius, startAngle + direction * sweepDegrees);
}

function getThreePointArcEndPointWithRadius(first, through, currentEnd, radius) {
  if (!Number.isFinite(radius) || radius <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const currentState = getThreePointArcConstructionState(first, through, currentEnd);
  const centers = getCircleCentersFromChordAndRadius(first, through, radius);
  if (!currentState || centers.length === 0) {
    return null;
  }

  const center = centers
    .sort((left, right) =>
      distanceBetweenWorldPoints(left, currentState.center) - distanceBetweenWorldPoints(right, currentState.center))[0];
  const firstAngle = getPointAngleDegrees(center, first);
  return pointOnCircle(center, radius, firstAngle + currentState.direction * currentState.sweepDegrees);
}

function getCircleCentersFromChordAndRadius(first, second, radius) {
  const chordLength = distanceBetweenWorldPoints(first, second);
  if (chordLength <= WORLD_GEOMETRY_TOLERANCE || radius < chordLength / 2) {
    return [];
  }

  const middle = midpoint(first, second);
  const halfChord = chordLength / 2;
  const centerDistance = Math.sqrt(Math.max(0, radius * radius - halfChord * halfChord));
  const chordDirection = {
    x: (second.x - first.x) / chordLength,
    y: (second.y - first.y) / chordLength
  };
  const normal = { x: -chordDirection.y, y: chordDirection.x };

  return [
    {
      x: middle.x + normal.x * centerDistance,
      y: middle.y + normal.y * centerDistance
    },
    {
      x: middle.x - normal.x * centerDistance,
      y: middle.y - normal.y * centerDistance
    }
  ];
}

export function getAlignedRectangleCorners(first, second, depthPoint) {
  const depthVector = getAlignedRectangleDepthVector(first, second, depthPoint);
  return [
    { x: first.x, y: first.y },
    { x: second.x, y: second.y },
    { x: second.x + depthVector.x, y: second.y + depthVector.y },
    { x: first.x + depthVector.x, y: first.y + depthVector.y }
  ];
}

export function getCenterRectangleCorners(center, corner) {
  const opposite = mirrorPoint(center, corner);
  const minX = Math.min(opposite.x, corner.x);
  const maxX = Math.max(opposite.x, corner.x);
  const minY = Math.min(opposite.y, corner.y);
  const maxY = Math.max(opposite.y, corner.y);

  return [
    { x: minX, y: minY },
    { x: maxX, y: minY },
    { x: maxX, y: maxY },
    { x: minX, y: maxY }
  ];
}

function getAlignedRectangleDepthVector(first, second, depthPoint) {
  const normal = getLeftNormal(first, second);
  if (!normal) {
    return { x: 0, y: 0 };
  }

  const depth = dotPoints(subtractPoints(depthPoint, second), normal);
  return {
    x: normal.x * depth,
    y: normal.y * depth
  };
}

function getLeftNormal(first, second) {
  const dx = second.x - first.x;
  const dy = second.y - first.y;
  const length = Math.hypot(dx, dy);
  if (length <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  return {
    x: -dy / length,
    y: dx / length
  };
}

export function getThreePointCircle(first, second, third) {
  const ax = first.x;
  const ay = first.y;
  const bx = second.x;
  const by = second.y;
  const cx = third.x;
  const cy = third.y;
  const determinant = 2 * (ax * (by - cy) + bx * (cy - ay) + cx * (ay - by));
  if (Math.abs(determinant) <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const aSquared = ax * ax + ay * ay;
  const bSquared = bx * bx + by * by;
  const cSquared = cx * cx + cy * cy;
  const center = {
    x: (aSquared * (by - cy) + bSquared * (cy - ay) + cSquared * (ay - by)) / determinant,
    y: (aSquared * (cx - bx) + bSquared * (ax - cx) + cSquared * (bx - ax)) / determinant
  };

  return {
    center,
    radius: distanceBetweenWorldPoints(center, first)
  };
}

export function getThreePointArc(first, through, end) {
  const arcState = getThreePointArcConstructionState(first, through, end);
  if (!arcState) {
    return null;
  }

  if (arcState.direction > 0) {
    return {
      center: arcState.center,
      radius: arcState.radius,
      startAngleDegrees: arcState.firstAngleDegrees,
      endAngleDegrees: arcState.firstAngleDegrees + arcState.sweepDegrees
    };
  }

  return {
    center: arcState.center,
    radius: arcState.radius,
    startAngleDegrees: arcState.endAngleDegrees,
    endAngleDegrees: arcState.endAngleDegrees + arcState.sweepDegrees
  };
}

export function getThreePointArcConstructionState(first, through, end) {
  const circle = getThreePointCircle(first, through, end);
  if (!circle || circle.radius <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const firstAngle = getPointAngleDegrees(circle.center, first);
  const throughAngle = getPointAngleDegrees(circle.center, through);
  const endAngle = getPointAngleDegrees(circle.center, end);
  const throughDelta = getCounterClockwiseDeltaDegrees(firstAngle, throughAngle);
  const endDelta = getCounterClockwiseDeltaDegrees(firstAngle, endAngle);
  const direction = throughDelta <= endDelta + WORLD_GEOMETRY_TOLERANCE ? 1 : -1;
  const sweepDegrees = direction > 0
    ? endDelta
    : FULL_CIRCLE_DEGREES - endDelta;

  return {
    center: circle.center,
    radius: circle.radius,
    firstAngleDegrees: firstAngle,
    throughAngleDegrees: throughAngle,
    endAngleDegrees: endAngle,
    direction,
    sweepDegrees
  };
}

export function getCenterPointArc(center, startRadiusPoint, endAnglePoint) {
  const radius = distanceBetweenWorldPoints(center, startRadiusPoint);
  if (radius <= WORLD_GEOMETRY_TOLERANCE
    || distanceBetweenWorldPoints(center, endAnglePoint) <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const startAngle = getPointAngleDegrees(center, startRadiusPoint);
  const endAngle = getPointAngleDegrees(center, endAnglePoint);
  const sweep = getShortestVisualArcSweep(startAngle, endAngle);
  return {
    center,
    radius,
    startAngleDegrees: sweep.startAngleDegrees,
    endAngleDegrees: sweep.endAngleDegrees
  };
}

export function getTangentArc(start, tangentPoint, end) {
  const tangentLength = distanceBetweenWorldPoints(start, tangentPoint);
  const chordLength = distanceBetweenWorldPoints(start, end);
  if (tangentLength <= WORLD_GEOMETRY_TOLERANCE || chordLength <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const tangent = {
    x: (tangentPoint.x - start.x) / tangentLength,
    y: (tangentPoint.y - start.y) / tangentLength
  };
  const normal = { x: -tangent.y, y: tangent.x };
  const chord = { x: start.x - end.x, y: start.y - end.y };
  const denominator = 2 * dotPoints(normal, chord);
  if (Math.abs(denominator) <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const offset = -dotPoints(chord, chord) / denominator;
  const center = {
    x: start.x + normal.x * offset,
    y: start.y + normal.y * offset
  };
  const radius = distanceBetweenWorldPoints(center, start);
  if (radius <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const startAngle = getPointAngleDegrees(center, start);
  const endAngle = getPointAngleDegrees(center, end);
  const radiusVector = { x: start.x - center.x, y: start.y - center.y };
  const counterClockwiseTangent = {
    x: -radiusVector.y / radius,
    y: radiusVector.x / radius
  };
  if (dotPoints(counterClockwiseTangent, tangent) >= 0) {
    return {
      center,
      radius,
      startAngleDegrees: startAngle,
      endAngleDegrees: startAngle + getCounterClockwiseDeltaDegrees(startAngle, endAngle)
    };
  }

  return {
    center,
    radius,
    startAngleDegrees: endAngle,
    endAngleDegrees: endAngle + getCounterClockwiseDeltaDegrees(endAngle, startAngle)
  };
}

function getPointAngleDegrees(center, point) {
  const angle = radiansToDegrees(Math.atan2(point.y - center.y, point.x - center.x));
  return angle < 0 ? angle + FULL_CIRCLE_DEGREES : angle;
}

function getShortestVisualArcSweep(startAngleDegrees, endAngleDegrees) {
  const counterClockwiseDelta = getCounterClockwiseDeltaDegrees(startAngleDegrees, endAngleDegrees);
  if (counterClockwiseDelta <= FULL_CIRCLE_DEGREES / 2) {
    return {
      startAngleDegrees,
      endAngleDegrees: startAngleDegrees + counterClockwiseDelta
    };
  }

  return {
    startAngleDegrees: endAngleDegrees,
    endAngleDegrees: endAngleDegrees + (FULL_CIRCLE_DEGREES - counterClockwiseDelta)
  };
}

function isSketchToolPointSetValid(tool, points) {
  if (!points || points.length < getSketchToolPointCount(tool)) {
    return false;
  }

  if (tool === "threepointarc") {
    return getThreePointArc(points[0], points[1], points[2]) !== null;
  }

  if (tool === "tangentarc") {
    return getTangentArc(points[0], points[1], points[2]) !== null;
  }

  if (tool === "centerpointarc") {
    return getCenterPointArc(points[0], points[1], points[2]) !== null;
  }

  return true;
}

function clearSelectedTargets(state) {
  if (state.selectedKeys.size === 0 && !state.activeSelectionKey) {
    return false;
  }

  state.selectedKeys.clear();
  state.activeSelectionKey = null;
  return true;
}

export function prepareSelectionForToolEntry(state, nextTool, previousTool = null) {
  const normalizedNextTool = normalizeToolName(nextTool);
  const normalizedPreviousTool = normalizeToolName(previousTool);
  if (normalizedNextTool !== "dimension" || normalizedPreviousTool === "dimension") {
    return false;
  }

  return clearSelectedTargets(state);
}

function notifySelectionChanged(state) {
  invokeDotNet(state, "OnSelectionChangedFromCanvas", Array.from(state.selectedKeys), state.activeSelectionKey || null);
}

function setHoveredTarget(state, target) {
  const previousTarget = state.hoveredTarget;
  const previousKey = state.hoveredTarget ? state.hoveredTarget.key : null;
  const previousNotifyKey = state.hoveredTarget && !state.hoveredTarget.dynamic
    ? previousKey
    : null;
  const nextKey = target ? target.key : null;
  const nextNotifyKey = target && !target.dynamic
    ? nextKey
    : null;

  if (previousKey === nextKey && sameOptionalWorldPoint(previousTarget && previousTarget.snapPoint, target && target.snapPoint)) {
    return false;
  }

  state.hoveredTarget = target;
  rememberAcquiredSnapPoint(state, target);
  if (previousNotifyKey !== nextNotifyKey) {
    invokeDotNet(state, "OnEntityHovered", nextNotifyKey);
  }

  updateDebugAttributes(state);
  return true;
}

function sameOptionalWorldPoint(first, second) {
  if (!first && !second) {
    return true;
  }

  if (!first || !second) {
    return false;
  }

  return distanceBetweenWorldPoints(first, second) <= WORLD_GEOMETRY_TOLERANCE;
}

function rememberAcquiredSnapPoint(state, target) {
  const isPersistentPointTarget = target
    && target.kind === "entity"
    && getEntityKind(target.entity) === "point"
    && target.snapPoint;
  const acquiredPoint = isPersistentPointTarget
    ? target.snapPoint
    : target && target.kind === "point" ? target.point : null;

  if (!getSketchCreationTool(state)
    || !target
    || target.dynamic
    || !acquiredPoint) {
    return;
  }

  const duplicateIndex = state.acquiredSnapPoints.findIndex(acquired =>
    acquired.key === target.key
    || distanceBetweenWorldPoints(acquired.point, acquiredPoint) <= WORLD_GEOMETRY_TOLERANCE);
  if (duplicateIndex >= 0) {
    state.acquiredSnapPoints.splice(duplicateIndex, 1);
  }

  state.acquiredSnapPoints.push({
    key: target.key,
    label: sanitizeKeyPart(target.label || target.entityId || "point"),
    point: acquiredPoint
  });

  while (state.acquiredSnapPoints.length > MAX_ACQUIRED_SNAP_POINTS) {
    state.acquiredSnapPoints.shift();
  }
}

function pruneInteractionState(state, removePointTargets = false) {
  for (const selectedKey of Array.from(state.selectedKeys)) {
    if ((removePointTargets && isPointSelectionKey(selectedKey)) || !resolveSelectionTarget(state, selectedKey)) {
      state.selectedKeys.delete(selectedKey);
    }
  }

  syncActiveSelectionWithSelectedKeys(state);

  if (state.hoveredTarget
    && ((removePointTargets && isPointSelectionKey(state.hoveredTarget.key))
      || !resolveSelectionTarget(state, state.hoveredTarget.key))) {
    setHoveredTarget(state, null);
  }

  updateDebugAttributes(state);
}

function clearInteractionState(state) {
  cancelGeometryDrag(state);
  state.constraintGroupDrag = null;
  state.hoveredTarget = null;
  state.selectedKeys.clear();
  state.activeSelectionKey = null;
  state.clickCandidate = null;
  state.selectionBox = null;
  updateDebugAttributes(state);
}

function updateDebugAttributes(state) {
  const entities = getDocumentEntities(state.document);
  state.canvas.dataset.entityCount = String(entities.length);
  state.canvas.dataset.selectedCount = String(state.selectedKeys.size);
  state.canvas.dataset.selectedKeys = Array.from(state.selectedKeys).join(",");
  state.canvas.dataset.activeSelectionKey = state.activeSelectionKey || "";
  state.canvas.dataset.hoveredId = state.hoveredTarget ? state.hoveredTarget.key : "";
  state.canvas.dataset.hoveredKind = state.hoveredTarget ? state.hoveredTarget.kind : "";
  state.canvas.dataset.hoveredSnapPoint = state.hoveredTarget && state.hoveredTarget.snapPoint
    ? `${formatKeyNumber(state.hoveredTarget.snapPoint.x)},${formatKeyNumber(state.hoveredTarget.snapPoint.y)}`
    : "";
  state.canvas.dataset.acquiredSnapCount = String(state.acquiredSnapPoints.length);
  state.canvas.dataset.selectionBoxMode = state.selectionBox
    ? `${state.selectionBox.operation}:${isCrossingSelection(state.selectionBox) ? "crossing" : "window"}`
    : "";
  state.canvas.dataset.originAxes = state.showOriginAxes ? "true" : "false";
  state.canvas.dataset.showAllConstraints = state.showAllConstraints ? "true" : "false";
  state.canvas.dataset.visibleConstraintGroupCount = String(getVisibleConstraintGlyphGroups(state).length);
  state.canvas.dataset.constraintGroupDragging = state.constraintGroupDrag ? state.constraintGroupDrag.groupKey : "";
  state.canvas.dataset.grainDirection = normalizeGrainDirection(state.grainDirection);
  state.canvas.dataset.constructionMode = state.constructionMode ? "true" : "false";
  state.canvas.dataset.activeTool = normalizeToolName(state.activeTool);
  state.canvas.dataset.polarSnapIncrement = String(normalizePolarSnapIncrement(state.polarSnapIncrementDegrees));
  state.canvas.dataset.toolDraftPointCount = state.toolDraft ? String(state.toolDraft.points.length) : "0";
  state.canvas.dataset.scale = String(state.view.scale);
  state.canvas.dataset.offsetX = String(state.view.offsetX);
  state.canvas.dataset.offsetY = String(state.view.offsetY);

  const rect = state.canvas.getBoundingClientRect();
  state.canvas.dataset.canvasLeft = String(rect.left);
  state.canvas.dataset.canvasTop = String(rect.top);
  state.canvas.dataset.canvasWidth = String(rect.width);
  state.canvas.dataset.canvasHeight = String(rect.height);

  const firstEntity = entities[0];
  if (!firstEntity) {
    state.canvas.dataset.firstEntity = "";
    state.canvas.dataset.firstPointCount = "0";
    state.canvas.dataset.firstScreenStart = "";
    state.canvas.dataset.firstScreenEnd = "";
    return;
  }

  const firstPoints = getEntityPoints(firstEntity);
  state.canvas.dataset.firstEntity = `${getEntityKind(firstEntity)}:${getEntityId(firstEntity) || ""}`;
  state.canvas.dataset.firstPointCount = String(firstPoints.length);

  if (firstPoints.length >= 2) {
    const firstScreenStart = worldToScreen(state, firstPoints[0]);
    const firstScreenEnd = worldToScreen(state, firstPoints[1]);
    state.canvas.dataset.firstScreenStart = `${firstScreenStart.x},${firstScreenStart.y}`;
    state.canvas.dataset.firstScreenEnd = `${firstScreenEnd.x},${firstScreenEnd.y}`;
  } else {
    state.canvas.dataset.firstScreenStart = "";
    state.canvas.dataset.firstScreenEnd = "";
  }
}

function getCanvasCssSize(state) {
  const rect = state.canvas.getBoundingClientRect();
  const width = rect.width || state.canvas.clientWidth || state.canvas.width / state.pixelRatio || 1;
  const height = rect.height || state.canvas.clientHeight || state.canvas.height / state.pixelRatio || 1;

  return {
    width: Math.max(1, width),
    height: Math.max(1, height)
  };
}

function getPointerScreenPoint(state, event) {
  const rect = state.canvas.getBoundingClientRect();

  return {
    x: event.clientX - rect.left,
    y: event.clientY - rect.top
  };
}

function isPanPointerDown(event) {
  return isPanPointerDownForTool(event, null);
}

export function isPanPointerDownForTool(event, toolName) {
  return event.button === 1;
}

function isPrimaryPointerButton(event) {
  return event.button === 0 || event.buttons === 1;
}

function isSecondaryPointerButton(event) {
  return event.button === 2 || event.buttons === 2;
}

function isEditableKeyTarget(target) {
  if (!target || typeof target.closest !== "function") {
    return false;
  }

  return Boolean(target.closest("input, textarea, select, [contenteditable='true']"));
}

function getSelectionBoxOperation(event) {
  return event.ctrlKey || event.metaKey ? "deselect" : "add";
}

function isCrossingSelection(selectionBox) {
  return selectionBox.end.x < selectionBox.start.x;
}

function normalizeScreenRect(first, second) {
  return {
    minX: Math.min(first.x, second.x),
    minY: Math.min(first.y, second.y),
    maxX: Math.max(first.x, second.x),
    maxY: Math.max(first.y, second.y)
  };
}

function pointInScreenRect(point, rect) {
  return point.x >= rect.minX && point.x <= rect.maxX && point.y >= rect.minY && point.y <= rect.maxY;
}

function screenRectsOverlap(first, second) {
  return first.minX <= second.maxX
    && first.maxX >= second.minX
    && first.minY <= second.maxY
    && first.maxY >= second.minY;
}

function screenSegmentIntersectsRect(start, end, rect) {
  const corners = [
    { x: rect.minX, y: rect.minY },
    { x: rect.maxX, y: rect.minY },
    { x: rect.maxX, y: rect.maxY },
    { x: rect.minX, y: rect.maxY }
  ];

  return segmentsIntersect(start, end, corners[0], corners[1])
    || segmentsIntersect(start, end, corners[1], corners[2])
    || segmentsIntersect(start, end, corners[2], corners[3])
    || segmentsIntersect(start, end, corners[3], corners[0]);
}

function segmentsIntersect(firstStart, firstEnd, secondStart, secondEnd) {
  const d1 = orientation(firstStart, firstEnd, secondStart);
  const d2 = orientation(firstStart, firstEnd, secondEnd);
  const d3 = orientation(secondStart, secondEnd, firstStart);
  const d4 = orientation(secondStart, secondEnd, firstEnd);

  return d1 * d2 <= 0 && d3 * d4 <= 0;
}

function orientation(a, b, c) {
  return (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);
}

function includeScreenBoundsPoint(bounds, point) {
  if (!bounds) {
    return {
      minX: point.x,
      minY: point.y,
      maxX: point.x,
      maxY: point.y
    };
  }

  return {
    minX: Math.min(bounds.minX, point.x),
    minY: Math.min(bounds.minY, point.y),
    maxX: Math.max(bounds.maxX, point.x),
    maxY: Math.max(bounds.maxY, point.y)
  };
}

function normalizeWheelDelta(event) {
  if (event.deltaMode === 1) {
    return event.deltaY * 16;
  }

  if (event.deltaMode === 2) {
    return event.deltaY * 100;
  }

  return event.deltaY;
}

function normalizeGrainDirection(direction) {
  const normalized = String(direction || "none").toLowerCase();
  return normalized === "globalx" || normalized === "globaly" ? normalized : "none";
}

function normalizePolarSnapIncrement(incrementDegrees) {
  const parsed = Number(incrementDegrees);
  if (!Number.isFinite(parsed) || parsed <= 0 || parsed > 180) {
    return 15;
  }

  return parsed;
}

function normalizeToolName(toolName) {
  return String(toolName || "select").replace(/[-_\s]/g, "").toLowerCase();
}

function getSketchCreationTool(state) {
  switch (normalizeToolName(state.activeTool)) {
    case "point":
      return "point";
    case "line":
      return "line";
    case "midpointline":
      return "midpointline";
    case "twopointrectangle":
      return "twopointrectangle";
    case "centerrectangle":
      return "centerrectangle";
    case "alignedrectangle":
      return "alignedrectangle";
    case "centercircle":
      return "centercircle";
    case "threepointcircle":
      return "threepointcircle";
    case "threepointarc":
      return "threepointarc";
    case "tangentarc":
      return "tangentarc";
    case "centerpointarc":
      return "centerpointarc";
    default:
      return null;
  }
}

function getModifyTool(state) {
  switch (normalizeToolName(state && state.activeTool)) {
    case "offset":
      return "offset";
    case "translate":
      return "translate";
    case "rotate":
      return "rotate";
    case "scale":
      return "scale";
    case "mirror":
      return "mirror";
    case "linearpattern":
      return "linearpattern";
    case "circularpattern":
      return "circularpattern";
    default:
      return null;
  }
}

export function getSketchToolPointCount(tool) {
  switch (tool) {
    case "point":
      return 1;
    case "alignedrectangle":
    case "threepointcircle":
    case "threepointarc":
    case "tangentarc":
    case "centerpointarc":
      return 3;
    default:
      return 2;
  }
}

export function getModifyToolPointCount(tool) {
  switch (tool) {
    case "offset":
      return 1;
    case "rotate":
    case "scale":
    case "circularpattern":
      return 3;
    default:
      return 2;
  }
}

function isPowerTrimTool(state) {
  return normalizeToolName(state && state.activeTool) === "powertrim";
}

function isSplitAtPointTool(state) {
  return normalizeToolName(state && state.activeTool) === "splitatpoint";
}

function isConstructionTool(state) {
  return normalizeToolName(state && state.activeTool) === "construction";
}

function isDimensionTool(state) {
  return normalizeToolName(state && state.activeTool) === "dimension";
}

export function getSplitAtPointRequest(state, screenPoint) {
  if (!isSplitAtPointTool(state)) {
    return null;
  }

  const target = findNearestTarget(state, screenPoint);
  const point = getTargetSplitPoint(target);
  if (!target || !point) {
    return null;
  }

  return {
    targetKey: target.key,
    point
  };
}

export function getPowerTrimRequest(state, screenPoint) {
  if (!isPowerTrimTool(state)) {
    return null;
  }

  const target = findNearestTarget(state, screenPoint);
  const point = screenToWorld(state, screenPoint);
  const targetKey = target && target.kind === "entity"
    ? target.key
    : target && (target.kind === "point" || target.kind === "segment")
      ? target.entityId
      : null;
  if (!target || target.dynamic || !targetKey || !point) {
    return null;
  }

  return {
    targetKey,
    point
  };
}

export function getConstructionToggleRequest(state, screenPoint) {
  if (!isConstructionTool(state)) {
    return null;
  }

  const target = findNearestTarget(state, screenPoint);
  if (!target || target.dynamic || !target.key) {
    return null;
  }

  return {
    targetKey: target.key
  };
}

function getTargetSplitPoint(target) {
  if (!target) {
    return null;
  }

  if (target.kind === "point" && target.point) {
    return target.point;
  }

  return target.snapPoint || null;
}

function flattenPointCoordinates(points) {
  return points.flatMap(point => [point.x, point.y]);
}

function isPointSelectionKey(selectionKey) {
  return String(selectionKey || "").includes(POINT_KEY_SEPARATOR);
}

function capturePointer(canvas, pointerId) {
  if (typeof canvas.setPointerCapture !== "function") {
    return;
  }

  try {
    canvas.setPointerCapture(pointerId);
  } catch {
    // Pointer capture can fail if the browser has already cancelled the pointer.
  }
}

function releasePointer(canvas, pointerId) {
  if (typeof canvas.releasePointerCapture !== "function") {
    return;
  }

  try {
    canvas.releasePointerCapture(pointerId);
  } catch {
    // Pointer capture can fail if the browser has already released the pointer.
  }
}

function focusCanvas(canvas) {
  focusElement(canvas);
}

function focusElement(element) {
  if (!element || typeof element.focus !== "function") {
    return;
  }

  try {
    element.focus({ preventScroll: true });
  } catch {
    element.focus();
  }
}

function invokeDotNet(state, methodName, ...args) {
  if (!state.dotnetRef || typeof state.dotnetRef.invokeMethodAsync !== "function") {
    state.canvas.dataset.lastDotnetCallback = `${methodName}:missing`;
    return;
  }

  state.canvas.dataset.lastDotnetCallback = `${methodName}:pending`;
  const invocation = state.dotnetRef.invokeMethodAsync(methodName, ...args);
  if (invocation && typeof invocation.catch === "function") {
    invocation.then(() => {
      state.canvas.dataset.lastDotnetCallback = `${methodName}:ok`;
    }).catch(error => {
      state.canvas.dataset.lastDotnetCallback = `${methodName}:error`;
      console.error(`DXFER canvas callback ${methodName} failed.`, error);
    });
  }
}

function getDocumentEntities(document) {
  const entities = readProperty(document, "entities", "Entities");
  return Array.isArray(entities) ? entities : [];
}

function getDocumentDimensions(document) {
  const dimensions = readProperty(document, "dimensions", "Dimensions");
  return Array.isArray(dimensions) ? dimensions : [];
}

function getDocumentConstraints(document) {
  const constraints = readProperty(document, "constraints", "Constraints");
  return Array.isArray(constraints) ? constraints : [];
}

function getDocumentBounds(document) {
  const dtoBounds = readBounds(readProperty(document, "bounds", "Bounds"));
  if (dtoBounds) {
    return dtoBounds;
  }

  return computeEntityBounds(getDocumentEntities(document));
}

function computeEntityBounds(entities) {
  let bounds = null;

  for (const entity of entities) {
    for (const point of getEntityBoundsPoints(entity)) {
      bounds = includeBoundsPoint(bounds, point);
    }
  }

  return bounds;
}

function getEntityBoundsPoints(entity) {
  const kind = getEntityKind(entity);

  if (kind === "point") {
    const point = getPointEntityLocation(entity);
    return point ? [point] : [];
  }

  if (kind === "line" || kind === "polyline" || kind === "spline") {
    return getEntityPoints(entity);
  }

  if (kind === "circle") {
    const center = getEntityCenter(entity);
    const radius = getEntityRadius(entity);

    if (!center || !isFinitePositive(radius)) {
      return [];
    }

    return [
      { x: center.x - radius, y: center.y - radius },
      { x: center.x + radius, y: center.y + radius }
    ];
  }

  if (kind === "arc") {
    return getArcWorldPoints(entity);
  }

  return [];
}

function includeBoundsPoint(bounds, point) {
  if (!point) {
    return bounds;
  }

  if (!bounds) {
    return {
      minX: point.x,
      minY: point.y,
      maxX: point.x,
      maxY: point.y
    };
  }

  return {
    minX: Math.min(bounds.minX, point.x),
    minY: Math.min(bounds.minY, point.y),
    maxX: Math.max(bounds.maxX, point.x),
    maxY: Math.max(bounds.maxY, point.y)
  };
}

function readBounds(value) {
  if (!value) {
    return null;
  }

  const minX = Number(readProperty(value, "minX", "MinX"));
  const minY = Number(readProperty(value, "minY", "MinY"));
  const maxX = Number(readProperty(value, "maxX", "MaxX"));
  const maxY = Number(readProperty(value, "maxY", "MaxY"));

  if (![minX, minY, maxX, maxY].every(Number.isFinite)) {
    return null;
  }

  return {
    minX: Math.min(minX, maxX),
    minY: Math.min(minY, maxY),
    maxX: Math.max(minX, maxX),
    maxY: Math.max(minY, maxY)
  };
}

function getEntityId(entity) {
  const id = readProperty(entity, "id", "Id");
  return id === null || id === undefined ? null : String(id);
}

function getEntityKind(entity) {
  const kind = readProperty(entity, "kind", "Kind");
  return kind === null || kind === undefined ? "" : String(kind).toLowerCase();
}

function getEntityPoints(entity) {
  const rawPoints = readProperty(entity, "points", "Points");

  if (!Array.isArray(rawPoints)) {
    return [];
  }

  return rawPoints
    .map(readPoint)
    .filter(point => point !== null);
}

function getPointEntityLocation(entity) {
  const points = getEntityPoints(entity);
  return points.length > 0
    ? points[0]
    : getEntityCenter(entity);
}

function getEntityCenter(entity) {
  return readPoint(readProperty(entity, "center", "Center"));
}

function getEntityRadius(entity) {
  return Number(readProperty(entity, "radius", "Radius"));
}

function getSketchItemId(item) {
  const id = readProperty(item, "id", "Id");
  return id === null || id === undefined ? "" : String(id);
}

function getSketchItemKind(item) {
  const kind = readProperty(item, "kind", "Kind");
  return kind === null || kind === undefined ? "" : String(kind).replace(/[\s_-]/g, "").toLowerCase();
}

function getSketchReferenceKeys(item) {
  const referenceKeys = readProperty(item, "referenceKeys", "ReferenceKeys");
  return Array.isArray(referenceKeys)
    ? referenceKeys.map(key => String(key))
    : [];
}

function getSketchConstraintState(item) {
  const state = readProperty(item, "state", "State");
  return state === null || state === undefined ? "" : String(state).toLowerCase();
}

function resolveSketchReferencePoint(state, referenceKey) {
  const pointTarget = parsePointTargetKey(state, String(referenceKey || ""));
  if (pointTarget) {
    return pointTarget.point;
  }

  const reference = parseSketchReference(referenceKey);
  if (!reference) {
    return null;
  }

  const entity = findDocumentEntity(state, reference.entityId);
  if (!entity) {
    return null;
  }

  if (Number.isInteger(reference.segmentIndex)) {
    const points = getEntityPoints(entity);
    if (reference.segmentIndex < 0 || reference.segmentIndex >= points.length - 1) {
      return null;
    }

    if (reference.target === "start") {
      return points[reference.segmentIndex] || null;
    }

    if (reference.target === "end") {
      return points[reference.segmentIndex + 1] || null;
    }

    return null;
  }

  switch (getEntityKind(entity)) {
    case "point":
      return getPointEntityLocation(entity);
    case "line": {
      const points = getEntityPoints(entity);
      if (reference.target === "start") {
        return points[0] || null;
      }

      if (reference.target === "end") {
        return points[1] || null;
      }

      return null;
    }
    case "circle":
    case "arc":
      return reference.target === "center" ? getEntityCenter(entity) : null;
    default:
      return null;
  }
}

function resolveSketchLineReference(state, referenceKey) {
  const reference = parseSketchReference(referenceKey);
  if (!reference) {
    return null;
  }

  const entity = findDocumentEntity(state, reference.entityId);
  if (!entity) {
    return null;
  }

  if (Number.isInteger(reference.segmentIndex)) {
    if (reference.target !== "entity") {
      return null;
    }

    const points = getEntityPoints(entity);
    const segmentIndex = reference.segmentIndex;
    return segmentIndex >= 0 && segmentIndex < points.length - 1
      ? { start: points[segmentIndex], end: points[segmentIndex + 1] }
      : null;
  }

  if (getEntityKind(entity) !== "line") {
    return null;
  }

  const points = getEntityPoints(entity);
  return points.length >= 2
    ? { start: points[0], end: points[1] }
    : null;
}

function resolveSketchEntityAnchorPoint(state, referenceKey) {
  const reference = parseSketchReference(referenceKey);
  if (!reference) {
    return null;
  }

  const entity = findDocumentEntity(state, reference.entityId);
  if (!entity) {
    return null;
  }

  if (Number.isInteger(reference.segmentIndex)) {
    const line = resolveSketchLineReference(state, referenceKey);
    return line ? midpoint(line.start, line.end) : null;
  }

  switch (getEntityKind(entity)) {
    case "point":
      return getPointEntityLocation(entity);
    case "line": {
      const points = getEntityPoints(entity);
      return points.length >= 2 ? midpoint(points[0], points[1]) : null;
    }
    case "polyline":
    case "spline": {
      const points = getEntityPoints(entity);
      if (points.length === 0) {
        return null;
      }

      const sum = points.reduce((total, point) => ({
        x: total.x + point.x,
        y: total.y + point.y
      }), { x: 0, y: 0 });
      return {
        x: sum.x / points.length,
        y: sum.y / points.length
      };
    }
    case "circle":
    case "arc":
      return getEntityCenter(entity);
    default:
      return null;
  }
}

function resolveSketchCircleLikeReference(state, referenceKey) {
  const reference = parseSketchReference(referenceKey);
  if (!reference || reference.target === "center" || Number.isInteger(reference.segmentIndex)) {
    return null;
  }

  const entity = findDocumentEntity(state, reference.entityId);
  if (!entity || (getEntityKind(entity) !== "circle" && getEntityKind(entity) !== "arc")) {
    return null;
  }

  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);
  return center && isFinitePositive(radius)
    ? { center, radius }
    : null;
}

function parseSketchReference(referenceKey) {
  const key = String(referenceKey || "").trim();
  if (!key) {
    return null;
  }

  const pointSeparatorIndex = key.indexOf(POINT_KEY_SEPARATOR);
  if (pointSeparatorIndex >= 0) {
    const entityId = key.slice(0, pointSeparatorIndex);
    const tail = key.slice(pointSeparatorIndex + POINT_KEY_SEPARATOR.length);
    const label = tail.split("|")[0] || "";
    return {
      entityId,
      target: normalizeSketchReferenceTarget(label)
    };
  }

  const targetSeparatorIndex = key.lastIndexOf(":");
  const baseKey = targetSeparatorIndex > 0 && targetSeparatorIndex < key.length - 1
    ? key.slice(0, targetSeparatorIndex)
    : key;
  const segmentReference = parseSegmentReferenceKey(baseKey);
  const target = targetSeparatorIndex > 0 && targetSeparatorIndex < key.length - 1
    ? normalizeSketchReferenceTarget(key.slice(targetSeparatorIndex + 1))
    : "entity";
  if (segmentReference) {
    return {
      entityId: segmentReference.entityId,
      segmentIndex: segmentReference.segmentIndex,
      target
    };
  }

  if (targetSeparatorIndex > 0 && targetSeparatorIndex < key.length - 1) {
    return {
      entityId: key.slice(0, targetSeparatorIndex),
      target
    };
  }

  return {
    entityId: key,
    target: "entity"
  };
}

function parseSegmentReferenceKey(key) {
  const separatorIndex = String(key || "").indexOf(SEGMENT_KEY_SEPARATOR);
  if (separatorIndex < 0) {
    return null;
  }

  const entityId = key.slice(0, separatorIndex);
  const segmentIndex = Number(key.slice(separatorIndex + SEGMENT_KEY_SEPARATOR.length));
  if (!entityId || !Number.isInteger(segmentIndex)) {
    return null;
  }

  return { entityId, segmentIndex };
}

function getSketchReferenceBaseKey(reference) {
  if (!reference) {
    return "";
  }

  return Number.isInteger(reference.segmentIndex)
    ? `${reference.entityId}${SEGMENT_KEY_SEPARATOR}${reference.segmentIndex}`
    : reference.entityId;
}

function normalizeSketchReferenceTarget(target) {
  const normalized = String(target || "").toLowerCase();
  if (normalized === "start" || normalized === "end" || normalized === "center") {
    return normalized;
  }

  return "entity";
}

function findDocumentEntity(state, entityId) {
  return getDocumentEntities(state.document)
    .find(entity => StringComparer(getEntityId(entity), entityId)) || null;
}

function getEntityStartAngle(entity) {
  const value = Number(readProperty(entity, "startAngleDegrees", "StartAngleDegrees"));
  return Number.isFinite(value) ? value : 0;
}

function getEntityEndAngle(entity) {
  const value = Number(readProperty(entity, "endAngleDegrees", "EndAngleDegrees"));
  return Number.isFinite(value) ? value : FULL_CIRCLE_DEGREES;
}

function readPoint(value) {
  if (!value) {
    return null;
  }

  const x = Number(readProperty(value, "x", "X"));
  const y = Number(readProperty(value, "y", "Y"));

  if (!Number.isFinite(x) || !Number.isFinite(y)) {
    return null;
  }

  return { x, y };
}

function readProperty(value, camelName, pascalName) {
  if (!value) {
    return undefined;
  }

  return value[camelName] ?? value[pascalName];
}

function getArcWorldPoints(entity) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);

  if (!center || !isFinitePositive(radius)) {
    return [];
  }

  const startAngle = getEntityStartAngle(entity);
  const sweep = getPositiveSweepDegrees(startAngle, getEntityEndAngle(entity));
  const segmentCount = Math.max(8, Math.ceil(sweep / 8));
  const points = [];

  for (let index = 0; index <= segmentCount; index += 1) {
    const angle = startAngle + sweep * (index / segmentCount);
    points.push(pointOnCircle(center, radius, angle));
  }

  return points;
}

function getArcEndpointWorldPoints(entity) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);

  if (!center || !isFinitePositive(radius)) {
    return [];
  }

  return [
    pointOnCircle(center, radius, getEntityStartAngle(entity)),
    pointOnCircle(center, radius, getEntityEndAngle(entity))
  ];
}

export function applyPolarSnapIfRequested(state, point, tool, event) {
  if ((tool !== "line" && tool !== "midpointline")
    || !state.toolDraft
    || state.toolDraft.points.length === 0) {
    return point;
  }

  const anchor = state.toolDraft.points[0];
  const distance = distanceBetweenWorldPoints(anchor, point);
  if (distance <= WORLD_GEOMETRY_TOLERANCE) {
    return point;
  }

  const rawAngle = radiansToDegrees(Math.atan2(point.y - anchor.y, point.x - anchor.x));
  if (!event || !event.shiftKey) {
    const scale = Math.max(MIN_VIEW_SCALE, Number(state.view && state.view.scale) || 1);
    const horizontalDistance = Math.abs(point.y - anchor.y) * scale;
    const verticalDistance = Math.abs(point.x - anchor.x) * scale;
    if (Math.min(horizontalDistance, verticalDistance) > ORTHO_POLAR_SNAP_TOLERANCE) {
      return point;
    }

    const snappedAngle = horizontalDistance <= verticalDistance
      ? (point.x >= anchor.x ? 0 : 180)
      : (point.y >= anchor.y ? 90 : -90);
    return pointOnCircle(anchor, distance, snappedAngle);
  }

  const increment = normalizePolarSnapIncrement(state.polarSnapIncrementDegrees);
  const snappedAngle = Math.round(rawAngle / increment) * increment;
  return pointOnCircle(anchor, distance, snappedAngle);
}

function pointOnCircle(center, radius, angleDegrees) {
  const radians = degreesToRadians(angleDegrees);
  return {
    x: center.x + Math.cos(radians) * radius,
    y: center.y + Math.sin(radians) * radius
  };
}

function midpoint(start, end) {
  return {
    x: (start.x + end.x) / 2,
    y: (start.y + end.y) / 2
  };
}

function mirrorPoint(center, point) {
  return {
    x: (2 * center.x) - point.x,
    y: (2 * center.y) - point.y
  };
}

function subtractPoints(first, second) {
  return {
    x: first.x - second.x,
    y: first.y - second.y
  };
}

function subtractScreenPoints(first, second) {
  return subtractPoints(first, second);
}

function dotPoints(first, second) {
  return first.x * second.x + first.y * second.y;
}

function dotScreenPoints(first, second) {
  return dotPoints(first, second);
}

function normalizeScreenVector(vector) {
  const length = Math.hypot(vector.x, vector.y);
  return length <= WORLD_GEOMETRY_TOLERANCE
    ? null
    : {
      x: vector.x / length,
      y: vector.y / length
    };
}

function projectPointToWorldLine(point, line) {
  const deltaX = line.end.x - line.start.x;
  const deltaY = line.end.y - line.start.y;
  const lengthSquared = deltaX * deltaX + deltaY * deltaY;
  if (lengthSquared <= WORLD_GEOMETRY_TOLERANCE) {
    return line.start;
  }

  const scalar = (((point.x - line.start.x) * deltaX) + ((point.y - line.start.y) * deltaY)) / lengthSquared;
  return {
    x: line.start.x + deltaX * scalar,
    y: line.start.y + deltaY * scalar
  };
}

function getWorldLineIntersection(first, second) {
  if (!first || !second || !first.start || !first.end || !second.start || !second.end) {
    return null;
  }

  const firstVector = subtractPoints(first.end, first.start);
  const secondVector = subtractPoints(second.end, second.start);
  const denominator = crossPoints(firstVector, secondVector);
  if (Math.abs(denominator) <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const offset = subtractPoints(second.start, first.start);
  const scalar = crossPoints(offset, secondVector) / denominator;
  return {
    x: first.start.x + firstVector.x * scalar,
    y: first.start.y + firstVector.y * scalar
  };
}

function angleBetweenWorldLines(first, second) {
  const firstAngle = Math.atan2(first.end.y - first.start.y, first.end.x - first.start.x);
  const secondAngle = Math.atan2(second.end.y - second.start.y, second.end.x - second.start.x);
  const delta = Math.abs(radiansToDegrees(secondAngle - firstAngle)) % 180;
  return delta > 90 ? 180 - delta : delta;
}

function crossPoints(first, second) {
  return first.x * second.y - first.y * second.x;
}

function addUniquePoint(points, point) {
  const duplicate = points.some(existing =>
    distanceBetweenWorldPoints(existing, point) <= WORLD_GEOMETRY_TOLERANCE);

  if (!duplicate) {
    points.push(point);
  }
}

function addUniqueNumber(values, value, tolerance = 0.000001) {
  if (!Number.isFinite(value)) {
    return;
  }

  const normalizedValue = normalizeAngleDegrees(value);
  const duplicate = values.some(existing => {
    const delta = Math.abs(normalizeAngleDegrees(existing) - normalizedValue);
    return Math.min(delta, FULL_CIRCLE_DEGREES - delta) <= tolerance;
  });
  if (!duplicate) {
    values.push(value);
  }
}

function solveQuadratic(a, b, c) {
  if (Math.abs(a) <= WORLD_GEOMETRY_TOLERANCE) {
    if (Math.abs(b) <= WORLD_GEOMETRY_TOLERANCE) {
      return [];
    }

    return [-c / b];
  }

  const discriminant = (b * b) - (4 * a * c);
  if (discriminant < -WORLD_GEOMETRY_TOLERANCE) {
    return [];
  }

  if (Math.abs(discriminant) <= WORLD_GEOMETRY_TOLERANCE) {
    return [-b / (2 * a)];
  }

  const root = Math.sqrt(Math.max(0, discriminant));
  return [
    (-b - root) / (2 * a),
    (-b + root) / (2 * a)
  ];
}

function isCircleTangentToLinearSegmentAtPoint(circle, segment, point) {
  const direction = subtractPoints(segment.end, segment.start);
  const radius = subtractPoints(point, circle.center);
  const directionLength = Math.hypot(direction.x, direction.y);
  const radiusLength = Math.hypot(radius.x, radius.y);
  if (directionLength <= WORLD_GEOMETRY_TOLERANCE || radiusLength <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

  return Math.abs(dotPoints(direction, radius) / (directionLength * radiusLength)) <= 0.000001;
}

function isCircleTangentToCurveAtPoint(circle, curveCenter, point) {
  const candidateRadius = subtractPoints(point, circle.center);
  const targetRadius = subtractPoints(point, curveCenter);
  const candidateLength = Math.hypot(candidateRadius.x, candidateRadius.y);
  const targetLength = Math.hypot(targetRadius.x, targetRadius.y);
  if (candidateLength <= WORLD_GEOMETRY_TOLERANCE || targetLength <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

  return Math.abs(crossPoints(candidateRadius, targetRadius) / (candidateLength * targetLength)) <= 0.000001;
}

function isUnitParameter(parameter) {
  return parameter >= -WORLD_GEOMETRY_TOLERANCE && parameter <= 1 + WORLD_GEOMETRY_TOLERANCE;
}

function isPointOnSegmentPrimitive(point, segment) {
  const projection = closestPointOnWorldSegment(point, segment.start, segment.end);
  return projection
    && isUnitParameter(projection.parameter)
    && projection.distance <= WORLD_GEOMETRY_TOLERANCE;
}

function isPointOnCurvePrimitive(point, curve) {
  const distance = distanceBetweenWorldPoints(point, curve.center);
  const radialTolerance = Math.max(WORLD_GEOMETRY_TOLERANCE, curve.radius * 0.000001);
  if (Math.abs(distance - curve.radius) > radialTolerance) {
    return false;
  }

  if (curve.startAngle === null || curve.endAngle === null) {
    return true;
  }

  const angleDegrees = radiansToDegrees(Math.atan2(point.y - curve.center.y, point.x - curve.center.x));
  return isAngleOnArc(angleDegrees, curve.startAngle, curve.endAngle);
}

function isAngleOnArc(angleDegrees, startAngleDegrees, endAngleDegrees) {
  const sweep = getPositiveSweepDegrees(startAngleDegrees, endAngleDegrees);
  if (sweep >= FULL_CIRCLE_DEGREES) {
    return true;
  }

  const delta = getCounterClockwiseDeltaDegrees(startAngleDegrees, angleDegrees);
  return delta <= sweep + 0.000001;
}

function getPositiveSweepDegrees(startAngleDegrees, endAngleDegrees) {
  const rawSweep = endAngleDegrees - startAngleDegrees;
  if (Math.abs(rawSweep) >= FULL_CIRCLE_DEGREES) {
    return FULL_CIRCLE_DEGREES;
  }

  let sweep = rawSweep % FULL_CIRCLE_DEGREES;
  if (sweep <= 0) {
    sweep += FULL_CIRCLE_DEGREES;
  }

  return sweep;
}

function getCounterClockwiseDeltaDegrees(startAngleDegrees, angleDegrees) {
  const delta = (angleDegrees - startAngleDegrees) % FULL_CIRCLE_DEGREES;
  return delta < 0 ? delta + FULL_CIRCLE_DEGREES : delta;
}

function closestPointOnScreenSegment(point, start, end) {
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const lengthSquared = dx * dx + dy * dy;

  if (lengthSquared === 0) {
    return {
      point: start,
      distance: distanceBetweenScreenPoints(point, start)
    };
  }

  const projected = ((point.x - start.x) * dx + (point.y - start.y) * dy) / lengthSquared;
  const clamped = clamp(projected, 0, 1);
  const closestPoint = {
    x: start.x + dx * clamped,
    y: start.y + dy * clamped
  };

  return {
    point: closestPoint,
    distance: distanceBetweenScreenPoints(point, closestPoint)
  };
}

function closestPointOnWorldSegment(point, start, end) {
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const lengthSquared = dx * dx + dy * dy;

  if (lengthSquared <= WORLD_GEOMETRY_TOLERANCE * WORLD_GEOMETRY_TOLERANCE) {
    return {
      point: start,
      distance: distanceBetweenWorldPoints(point, start),
      parameter: 0
    };
  }

  const parameter = ((point.x - start.x) * dx + (point.y - start.y) * dy) / lengthSquared;
  const clamped = clamp(parameter, 0, 1);
  const closestPoint = {
    x: start.x + dx * clamped,
    y: start.y + dy * clamped
  };

  return {
    point: closestPoint,
    distance: distanceBetweenWorldPoints(point, closestPoint),
    parameter
  };
}

function distanceBetweenScreenPoints(first, second) {
  return Math.hypot(first.x - second.x, first.y - second.y);
}

function distanceBetweenWorldPoints(first, second) {
  return Math.hypot(first.x - second.x, first.y - second.y);
}

function degreesToRadians(degrees) {
  return degrees * Math.PI / 180;
}

function radiansToDegrees(radians) {
  return radians * 180 / Math.PI;
}

function normalizeAngleDegrees(angleDegrees) {
  const normalized = angleDegrees % FULL_CIRCLE_DEGREES;
  return normalized < 0 ? normalized + FULL_CIRCLE_DEGREES : normalized;
}

function isFinitePositive(value) {
  return Number.isFinite(value) && value > 0;
}

function clamp(value, minimum, maximum) {
  return Math.min(maximum, Math.max(minimum, value));
}

function formatKeyNumber(value) {
  return String(Math.round(value * 1000000) / 1000000);
}

function formatDimensionValue(value) {
  if (!Number.isFinite(value)) {
    return "";
  }

  const rounded = Math.round(value * 1000) / 1000;
  return rounded.toFixed(3).replace(/\.?0+$/, "");
}

function sanitizeKeyPart(value) {
  return String(value).replaceAll("|", "-");
}

function StringComparer(first, second) {
  return first === second;
}

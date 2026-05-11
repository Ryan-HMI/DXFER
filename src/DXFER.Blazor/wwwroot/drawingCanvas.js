import {
  addUniqueNumber,
  addUniquePoint,
  angleBetweenWorldLines,
  areWorldLinesParallel,
  clamp,
  closestPointOnScreenSegment,
  closestPointOnWorldSegment,
  crossPoints,
  degreesToRadians,
  distanceBetweenScreenPoints,
  distanceBetweenWorldPoints,
  dotPoints,
  dotScreenPoints,
  getCounterClockwiseDeltaDegrees,
  getParallelWorldLineDistance,
  getPositiveSweepDegrees,
  getWorldLineIntersection,
  isAngleOnArc,
  isCircleTangentToCurveAtPoint,
  isCircleTangentToLinearSegmentAtPoint,
  isFinitePositive,
  isPointOnCurvePrimitive,
  isPointOnSegmentPrimitive,
  isUnitParameter,
  midpoint,
  mirrorPoint,
  normalizeAngleDegrees,
  normalizeScreenVector,
  projectPointToWorldLine,
  radiansToDegrees,
  solveQuadratic,
  subtractPoints,
  subtractScreenPoints
} from "./canvas/geometry.js";
import {
  getLinearDimensionScreenGeometry,
  getRadialDimensionScreenGeometry
} from "./canvas/dimensions.js";
import {
  formatDimensionValue,
  getDimensionDisplayText,
  getDimensionInputClassName,
  getDimensionLabel,
  getDimensionRenderStyle,
  shouldAutoSelectDimensionInputValue,
  shouldCommitDimensionInputOnBlur,
  shouldCommitDimensionInputOnChange,
  shouldRefreshDimensionInputValue
} from "./canvas/dimensionPresentation.js";
import {
  getDefaultActiveDimensionKey,
  getDimensionKeys,
  getNextDimensionKey,
  getPendingPersistentDimensionEditId,
  getVisibleDimensionDescriptors,
  resolveActiveDimensionKey
} from "./canvas/dimensionInputState.js";
import {
  getPersistentDimensionCommitValue
} from "./canvas/dimensionInputParsing.js";
import {
  getPointTargetMarker,
  isDynamicTargetCurrentToPointer,
  isPanPointerDownForTool,
  isTargetEligibleForConstraintTool,
  shouldDrawSelectionTargetOverlay
} from "./canvas/targets.js";
import {
  CONSTRAINT_KEY_PREFIX,
  POINT_KEY_SEPARATOR,
  SEGMENT_KEY_SEPARATOR,
  parsePointTargetKeyParts,
  parseSegmentTargetKeyParts
} from "./canvas/targetKeys.js";

export {
  getLinearDimensionScreenGeometry,
  getRadialDimensionScreenGeometry
} from "./canvas/dimensions.js";
export {
  getDimensionDisplayText,
  getDimensionInputClassName,
  getDimensionRenderStyle,
  shouldAutoSelectDimensionInputValue,
  shouldCommitDimensionInputOnBlur,
  shouldCommitDimensionInputOnChange,
  shouldRefreshDimensionInputValue
} from "./canvas/dimensionPresentation.js";
export {
  getDefaultActiveDimensionKey,
  getDimensionKeys,
  getNextDimensionKey,
  getPendingPersistentDimensionEditId,
  getVisibleDimensionDescriptors,
  resolveActiveDimensionKey
} from "./canvas/dimensionInputState.js";
export {
  getPersistentDimensionCommitValue
} from "./canvas/dimensionInputParsing.js";
export {
  getPointTargetMarker,
  isDynamicTargetCurrentToPointer,
  isPanPointerDownForTool,
  isTargetEligibleForConstraintTool,
  shouldDrawSelectionTargetOverlay
} from "./canvas/targets.js";

const DEFAULT_FIT_MARGIN = 32;
const DEFAULT_BLANK_VIEW_SCALE = 24;
const HIT_TEST_TOLERANCE = 9;
const SNAP_POINT_TOLERANCE = 8;
const PRIORITY_POINT_HIT_TOLERANCE = 4;
const STRONG_POINT_HIT_TOLERANCE = 1;
const CLICK_MOVE_TOLERANCE = 5;
const MIN_VIEW_SCALE = 0.000001;
const MAX_VIEW_SCALE = 1000000;
const FULL_CIRCLE_DEGREES = 360;
const MAX_ACQUIRED_SNAP_POINTS = 2;
const ACQUIRED_SNAP_POINT_TTL_MS = 3000;
const DYNAMIC_POINT_KEY_PREFIX = "__dynamic";
const WORLD_GEOMETRY_TOLERANCE = 0.000001;
const ORTHO_POLAR_SNAP_TOLERANCE = 6;
const MAX_INFERENCE_GUIDE_SCREEN_DISTANCE = 360;
const DIMENSION_INPUT_SCREEN_MARGIN_X = 52;
const DIMENSION_INPUT_SCREEN_MARGIN_Y = 18;
const DIMENSION_ARROWHEAD_SIZE = 15;
const DIMENSION_TEXT_GAP_PADDING = 5;
const DIMENSION_INPUT_LEADER_GAP = 12;
const SKETCH_CHAIN_TOGGLE_REARM_TOLERANCE = SNAP_POINT_TOLERANCE + 5;
const SKETCH_CHAIN_DIMENSION_SUPPRESS_TOLERANCE = SNAP_POINT_TOLERANCE * 3;
const CONSTRAINT_GLYPH_SIZE = 16;
const CONSTRAINT_GLYPH_GAP = 4;
const CONSTRAINT_GROUP_HIT_PADDING = 4;
const CONSTRAINT_LEADER_MIN_DISTANCE = 10;
const CONSTRAINT_DEFAULT_OFFSET_DISTANCE = 26;
const POWER_TRIM_PATH_MIN_SCREEN_DISTANCE = 2;
const POWER_TRIM_EXTEND_HIT_TEST_TOLERANCE = 18;
const SPLINE_TANGENT_HANDLE_SCALE = 0.25;
const SPLINE_FIT_SEGMENTS_PER_SPAN = 32;

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
    pendingPersistentDimensionEditIds: null,
    visibleDimensionKeys: [],
    dimensionDrag: null,
    geometryDrag: null,
    powerTrimDrag: null,
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
      applyConstraintVisibilityState(state, visible);
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
    dimensionValues: {},
    polygonSideCount: null
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

  if (!isSketchChainVertexTarget(target, chainPoint)
    && chainDistance > SNAP_POINT_TOLERANCE) {
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
  const diagnosticEntityIds = getSketchDiagnosticEntityIds(state.document);
  for (const entity of entities) {
    const affectedByDiagnostics = diagnosticEntityIds.has(getEntityId(entity));
    drawEntity(state, entity, {
      strokeStyle: affectedByDiagnostics ? "#f87171" : entity.isConstruction ? "#64748b" : "#94a3b8",
      lineWidth: affectedByDiagnostics ? 2.15 : entity.isConstruction ? 1.1 : 1.5,
      lineDash: entity.isConstruction ? [8, 5] : []
    });
    drawPersistentSplineTangentHandles(state, entity);
  }

  for (const selectedKey of state.selectedKeys) {
    const target = resolveSelectionTarget(state, selectedKey);
    if (target && shouldDrawSelectionTargetOverlay(target) && !isDimensionTargetBeingEdited(state, target)) {
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

  const powerTrimHoverMode = getPowerTrimHoverModeForState(state);
  if (state.hoveredTarget
    && shouldDrawSelectionTargetOverlay(state.hoveredTarget)
    && !isDimensionTargetBeingEdited(state, state.hoveredTarget)) {
    const isSelected = state.selectedKeys.has(state.hoveredTarget.key);
    const isActive = state.hoveredTarget.key === state.activeSelectionKey;
    const isPowerTrimExtendHover = powerTrimHoverMode === "extend";
    drawTarget(state, state.hoveredTarget, {
      strokeStyle: isPowerTrimExtendHover ? "#22d3ee" : isActive ? "#bae6fd" : isSelected ? "#38bdf8" : "#f59e0b",
      lineWidth: state.hoveredTarget.kind === "point" ? isActive ? 3 : 1.8 : isActive ? 4.5 : isSelected ? 2.5 : 3.5,
      lineDash: isPowerTrimExtendHover ? [10, 4] : getTargetSelectionLineDash(state.hoveredTarget, isSelected ? [6, 4] : []),
      glow: isActive || isPowerTrimExtendHover
    });
    if (isPowerTrimExtendHover) {
      drawPowerTrimExtendIndicator(state, state.hoveredTarget, state.pointerScreenPoint);
    }
  }

  if (state.selectionBox) {
    drawSelectionBox(state);
  }

  if (state.powerTrimDrag) {
    drawPowerTrimDrag(state);
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

function isDimensionTargetBeingEdited(state, target) {
  if (!target || target.kind !== "dimension" || !target.dimensionId || !state.persistentDimensionInputs) {
    return false;
  }

  const input = state.persistentDimensionInputs.get(target.dimensionId);
  return Boolean(input && input.dataset.dimensionEditing === "true");
}

function drawAcquiredSnapPoints(state) {
  pruneExpiredAcquiredSnapPoints(state);
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

function getInteractionTimestamp() {
  return typeof performance !== "undefined" && typeof performance.now === "function"
    ? performance.now()
    : Date.now();
}

export function pruneExpiredAcquiredSnapPoints(state, now = getInteractionTimestamp()) {
  if (!state || !Array.isArray(state.acquiredSnapPoints)) {
    return false;
  }

  let changed = false;
  const retained = [];
  for (const acquired of state.acquiredSnapPoints) {
    if (!acquired) {
      changed = true;
      continue;
    }

    if (!Number.isFinite(acquired.acquiredAt)) {
      acquired.acquiredAt = now;
      retained.push(acquired);
      continue;
    }

    if (now - acquired.acquiredAt <= ACQUIRED_SNAP_POINT_TTL_MS) {
      retained.push(acquired);
    } else {
      changed = true;
    }
  }

  if (changed) {
    state.acquiredSnapPoints = retained;
  }

  return changed;
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

function drawPowerTrimDrag(state) {
  const { context, powerTrimDrag } = state;
  if (!powerTrimDrag || powerTrimDrag.points.length < 2) {
    return;
  }

  context.save();
  context.strokeStyle = "#f59e0b";
  context.lineWidth = 2.25;
  context.lineCap = "round";
  context.lineJoin = "round";
  context.setLineDash([7, 4]);
  context.beginPath();
  context.moveTo(powerTrimDrag.points[0].x, powerTrimDrag.points[0].y);
  for (let index = 1; index < powerTrimDrag.points.length; index += 1) {
    context.lineTo(powerTrimDrag.points[index].x, powerTrimDrag.points[index].y);
  }
  context.stroke();
  context.restore();
}

function drawPowerTrimExtendIndicator(state, target, screenPoint) {
  if (!target || !target.snapPoint || !screenPoint) {
    return;
  }

  const { context } = state;
  const entityKind = getEntityKind(target.entity);
  const drawCursorConnector = entityKind === "line" || entityKind === "polyline" || entityKind === "polygon";
  const endpoint = worldToScreen(state, target.snapPoint);
  context.save();
  context.strokeStyle = "#22d3ee";
  context.fillStyle = "rgba(34, 211, 238, 0.16)";
  context.lineWidth = 2;
  context.lineCap = "round";
  if (drawCursorConnector) {
    context.setLineDash([5, 4]);
    context.beginPath();
    context.moveTo(endpoint.x, endpoint.y);
    context.lineTo(screenPoint.x, screenPoint.y);
    context.stroke();
  }

  context.setLineDash([]);
  const markerPoint = drawCursorConnector ? screenPoint : endpoint;
  context.beginPath();
  context.arc(markerPoint.x, markerPoint.y, 5, 0, Math.Tau);
  context.fill();
  context.stroke();
  context.restore();
}

function drawToolPreview(state) {
  const tool = getSketchCreationTool(state);
  const modifyTool = getModifyTool(state);
  if (tool && isAddSplinePointSketchTool(tool)) {
    return drawModifyToolPreview(state, "addsplinepoint");
  }

  if (!tool && modifyTool) {
    return drawModifyToolPreview(state, modifyTool);
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
  } else if (tool === "ellipse") {
    if (points.length === 1) {
      const major = getCenteredAxisDiameterPoints(first, second);
      if (major) {
        drawWorldPolyline(state, [major.start, major.end]);
        addLinearPreviewDimension(dimensions, state, "major", "Major diameter", major.start, major.end);
      }
    } else {
      const ellipse = getEllipseFromPoints(first, second, third);
      if (ellipse) {
        drawWorldPolyline(state, getEllipseWorldPoints(ellipse));
        addEllipsePreviewDimensions(dimensions, state, ellipse);
      }
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
        drawWorldPolyline(state, getTangentArcFallbackPreviewPoints(first, second, third));
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
  } else if (tool === "ellipticalarc") {
    if (points.length === 1) {
      drawWorldPolyline(state, [first, second]);
      addLinearPreviewDimension(dimensions, state, "major", "Major", first, second);
    } else if (points.length === 2) {
      const ellipse = getEllipseFromPoints(first, second, third);
      if (ellipse) {
        drawWorldPolyline(state, getEllipseWorldPoints(ellipse));
        addEllipsePreviewDimensions(dimensions, state, ellipse);
      }
    } else {
      const ellipse = getEllipseFromPoints(first, second, points[2], third);
      if (ellipse) {
        drawWorldPolyline(state, getEllipseWorldPoints(ellipse));
        addEllipsePreviewDimensions(dimensions, state, ellipse);
      }
    }
  } else if (tool === "conic") {
    if (points.length === 1) {
      drawWorldPolyline(state, [first, third]);
    } else {
      drawWorldPolyline(state, getQuadraticBezierWorldPoints(first, second, third));
      drawWorldPolyline(state, [first, second, third]);
    }
  } else if (tool === "inscribedpolygon" || tool === "circumscribedpolygon") {
    drawWorldPolyline(state, getPolygonGuideCircleWorldPoints(first, second));
    const polygonPoints = getPolygonWorldPoints(first, second, tool === "circumscribedpolygon", getPolygonSideCount(state));
    drawWorldPolyline(state, polygonPoints.concat(polygonPoints[0]));
    addPolygonPreviewDimension(dimensions, state, first, second, tool === "circumscribedpolygon");
  } else if (tool === "spline") {
    const draftPoints = points.concat(third).slice(0, getSketchToolPointCount(tool));
    if (draftPoints.length < 3) {
      drawWorldPolyline(state, draftPoints);
    } else {
      drawWorldPolyline(state, getSplineFitWorldPoints(draftPoints));
      drawWorldPolyline(state, draftPoints);
      drawSplineTangentHandles(state, draftPoints);
    }
  } else if (tool === "bezier" || tool === "splinecontrolpoint") {
    const draftPoints = points.concat(third).slice(0, getSketchToolPointCount(tool));
    if (draftPoints.length < 3) {
      drawWorldPolyline(state, draftPoints);
    } else {
      drawWorldPolyline(state, getCubicBezierWorldPoints(draftPoints));
      drawWorldPolyline(state, draftPoints);
    }
  } else if (tool === "slot") {
    if (points.length === 1) {
      drawWorldPolyline(state, [first, second]);
      addLinearPreviewDimension(dimensions, state, "length", "Length", first, second);
    } else {
      const slot = getSlotPreviewGeometry(first, second, third);
      if (slot) {
        drawWorldPolyline(state, slot.points.concat(slot.points[0]));
        addLinearPreviewDimension(dimensions, state, "length", "Length", first, second);
        addRadiusPreviewDimension(dimensions, state, first, slot.radiusPoint);
      }
    }
  }

  context.setLineDash([]);
  for (const point of getSketchToolPreviewMarkerPoints(tool, points)) {
    const marker = worldToScreen(state, point);
    context.beginPath();
    context.arc(marker.x, marker.y, 3.5, 0, Math.PI * 2);
    context.fill();
  }
  context.restore();
  return dimensions;
}

export function getSketchToolPreviewMarkerPoints(tool, points) {
  if (!Array.isArray(points)) {
    return [];
  }

  if (normalizeToolName(tool) === "tangentarc" && points.length > 1) {
    return [copyPoint(points[0])];
  }

  return points.map(copyPoint);
}

export function getTangentArcFallbackPreviewPoints(first, tangentPoint, end) {
  if (!first
    || !end
    || distanceBetweenWorldPoints(first, end) <= WORLD_GEOMETRY_TOLERANCE) {
    return [];
  }

  return [copyPoint(first), copyPoint(end)];
}

function getTargetSelectionLineDash(target, fallback = []) {
  return target && target.entity && target.entity.isConstruction
    ? [8, 5]
    : fallback;
}

function drawModifyToolPreview(state, tool) {
  if (!tool || !state.toolDraft) {
    return [];
  }

  const points = state.toolDraft.previewPoint
    ? state.toolDraft.points.concat(state.toolDraft.previewPoint)
    : state.toolDraft.points;
  const { context } = state;
  const dimensions = [];

  context.save();
  context.strokeStyle = "#facc15";
  context.fillStyle = "#facc15";
  context.lineWidth = 1.5;
  context.setLineDash([6, 4]);

  if (points.length === 0 && tool !== "offset") {
    context.restore();
    return [];
  }

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
    drawOffsetModifyPreview(state, state.toolDraft.previewPoint || points[0], dimensions);
  }

  for (const point of points) {
    const marker = worldToScreen(state, point);
    context.beginPath();
    context.arc(marker.x, marker.y, 3.5, 0, Math.PI * 2);
    context.fill();
  }

  context.restore();
  return dimensions;
}

function drawOffsetModifyPreview(state, throughPoint, dimensions) {
  if (!throughPoint) {
    return;
  }

  const preview = getOffsetPreviewGeometry(state, throughPoint);
  for (const points of preview.offsetPointSets) {
    drawWorldPolyline(state, points);
  }

  const marker = worldToScreen(state, throughPoint);
  state.context.beginPath();
  state.context.arc(marker.x, marker.y, 5, 0, Math.PI * 2);
  state.context.stroke();

  if (preview.dimension) {
    dimensions.push(preview.dimension);
  }
}

function getOffsetPreviewGeometry(state, throughPoint) {
  const preview = {
    offsetPointSets: [],
    dimension: null
  };
  const targets = getOffsetPreviewTargets(state);
  let firstDimension = null;

  for (const target of targets) {
    const offset = getOffsetPreviewForEntity(state, target.entity, throughPoint);
    if (!offset) {
      continue;
    }

    preview.offsetPointSets.push(offset.points);
    if (!firstDimension && offset.dimension) {
      firstDimension = offset.dimension;
    }
  }

  preview.dimension = firstDimension;
  return preview;
}

function getOffsetPreviewTargets(state) {
  if (!state || !state.selectedKeys || state.selectedKeys.size === 0) {
    return [];
  }

  const targets = [];
  const seenEntityIds = new Set();
  for (const key of state.selectedKeys) {
    const target = resolveSelectionTarget(state, key);
    if (!target || !target.entity) {
      continue;
    }

    const entityId = getEntityId(target.entity);
    if (!entityId || seenEntityIds.has(entityId)) {
      continue;
    }

    seenEntityIds.add(entityId);
    targets.push(target);
  }

  return targets;
}

function getOffsetPreviewForEntity(state, entity, throughPoint) {
  const kind = getEntityKind(entity);
  if (kind === "circle" || kind === "arc") {
    return getOffsetPreviewForCircleLike(state, entity, throughPoint);
  }

  return getOffsetPreviewForLinearizedEntity(state, entity, throughPoint);
}

function getOffsetPreviewForCircleLike(state, entity, throughPoint) {
  const center = getEntityCenter(entity);
  const currentRadius = getEntityRadius(entity);
  const nextRadius = center ? distanceBetweenWorldPoints(center, throughPoint) : Number.NaN;
  if (!center || !isFinitePositive(currentRadius) || !isFinitePositive(nextRadius)) {
    return null;
  }

  const kind = getEntityKind(entity);
  const previewEntity = kind === "arc"
    ? {
      kind: "arc",
      center,
      radius: nextRadius,
      startAngleDegrees: getEntityStartAngle(entity),
      endAngleDegrees: getEntityEndAngle(entity)
    }
    : null;
  const points = kind === "arc"
    ? getArcWorldPoints(previewEntity)
    : getCircleWorldPoints(center, nextRadius);
  const direction = normalizeScreenVector(subtractPoints(throughPoint, center)) || { x: 1, y: 0 };
  const originalEdge = {
    x: center.x + direction.x * currentRadius,
    y: center.y + direction.y * currentRadius
  };
  return {
    points,
    dimension: getOffsetPreviewDimensionDescriptor(state, originalEdge, throughPoint, Math.abs(nextRadius - currentRadius))
  };
}

function getOffsetPreviewForLinearizedEntity(state, entity, throughPoint) {
  const points = getOffsetPreviewSourcePoints(entity);
  if (points.length < 2) {
    return null;
  }

  const projection = getClosestWorldSegmentProjection(throughPoint, points, getEntityKind(entity) === "polygon");
  if (!projection || Math.abs(projection.signedDistance) <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const offsetVector = {
    x: projection.normal.x * projection.signedDistance,
    y: projection.normal.y * projection.signedDistance
  };
  const offsetPoints = points.map(point => ({
    x: point.x + offsetVector.x,
    y: point.y + offsetVector.y
  }));

  if (getEntityKind(entity) === "polygon" && offsetPoints.length > 2) {
    offsetPoints.push(offsetPoints[0]);
  }

  const originalClosestPoint = projection.point;
  const offsetClosestPoint = {
    x: originalClosestPoint.x + offsetVector.x,
    y: originalClosestPoint.y + offsetVector.y
  };
  return {
    points: offsetPoints,
    dimension: getOffsetPreviewDimensionDescriptor(
      state,
      originalClosestPoint,
      offsetClosestPoint,
      Math.abs(projection.signedDistance))
  };
}

function getOffsetPreviewSourcePoints(entity) {
  const kind = getEntityKind(entity);
  if (kind === "line" || kind === "polyline" || kind === "spline" || kind === "polygon") {
    return getEntityPoints(entity);
  }

  if (kind === "ellipse") {
    return getEllipseWorldPointsFromEntity(entity);
  }

  return [];
}

function getOffsetPreviewDimensionDescriptor(state, first, second, value) {
  if (!formatDimensionValue(value)) {
    return null;
  }

  const start = worldToScreen(state, first);
  const end = worldToScreen(state, second);
  const midpointScreen = midpointScreenPoint(start, end);
  const normal = getScreenNormal(start, end);
  return {
    key: "offset",
    label: "Offset",
    value,
    point: {
      x: midpointScreen.x + normal.x * 18,
      y: midpointScreen.y + normal.y * 18
    }
  };
}

function getCircleWorldPoints(center, radius) {
  const points = [];
  for (let index = 0; index <= 48; index += 1) {
    points.push(pointOnCircle(center, radius, index * FULL_CIRCLE_DEGREES / 48));
  }

  return points;
}

function getClosestWorldSegmentProjection(point, points, closed = false) {
  let closest = null;
  const segmentCount = closed ? points.length : points.length - 1;
  for (let index = 0; index < segmentCount; index += 1) {
    const start = points[index];
    const end = points[(index + 1) % points.length];
    const projection = projectPointToWorldSegment(point, start, end);
    if (!projection || (closest && projection.distance >= closest.distance)) {
      continue;
    }

    closest = projection;
  }

  return closest;
}

function projectPointToWorldSegment(point, start, end) {
  const segment = subtractPoints(end, start);
  const lengthSquared = dotPoints(segment, segment);
  if (lengthSquared <= WORLD_GEOMETRY_TOLERANCE * WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const parameter = clamp(dotPoints(subtractPoints(point, start), segment) / lengthSquared, 0, 1);
  const closest = {
    x: start.x + segment.x * parameter,
    y: start.y + segment.y * parameter
  };
  const length = Math.sqrt(lengthSquared);
  const normal = {
    x: -segment.y / length,
    y: segment.x / length
  };
  const signedDistance = dotPoints(subtractPoints(point, closest), normal);
  return {
    point: closest,
    normal,
    signedDistance,
    distance: Math.abs(signedDistance)
  };
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

function drawSplineTangentHandles(state, fitPoints) {
  drawSplineTangentHandleSet(state, getSplineTangentHandles(fitPoints));
}

function drawPersistentSplineTangentHandles(state, entity) {
  drawPersistentSplineEditPoints(state, entity);
  drawSplineTangentHandleSet(state, getPersistentSplineTangentHandlesForEntity(entity));
}

function drawPersistentSplineEditPoints(state, entity) {
  const points = getSplineEditableSnapPoints(entity);
  if (points.length === 0) {
    return;
  }

  const { context } = state;
  context.save();
  context.strokeStyle = "rgba(103, 232, 249, 0.62)";
  context.fillStyle = "rgba(103, 232, 249, 0.24)";
  context.lineWidth = 1;
  for (const item of points) {
    const point = worldToScreen(state, item.point);
    context.beginPath();
    context.arc(point.x, point.y, 3.5, 0, Math.PI * 2);
    context.fill();
    context.stroke();
  }

  context.restore();
}

function drawSplineTangentHandleSet(state, handles) {
  if (handles.length === 0) {
    return;
  }

  const { context } = state;
  context.save();
  context.strokeStyle = "rgba(103, 232, 249, 0.58)";
  context.fillStyle = "rgba(103, 232, 249, 0.86)";
  context.lineWidth = 1;
  context.setLineDash([3, 3]);

  for (const handle of handles) {
    const point = worldToScreen(state, handle.point);
    for (const endpoint of [handle.backward, handle.forward]) {
      if (!endpoint) {
        continue;
      }

      const screenEndpoint = worldToScreen(state, endpoint);
      context.beginPath();
      context.moveTo(point.x, point.y);
      context.lineTo(screenEndpoint.x, screenEndpoint.y);
      context.stroke();
      context.fillRect(screenEndpoint.x - 2, screenEndpoint.y - 2, 4, 4);
    }
  }

  context.restore();
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

function addEllipsePreviewDimensions(dimensions, state, ellipse) {
  const major = getEllipseAxisDiameterPoints(ellipse, "major");
  if (major) {
    addLinearPreviewDimension(dimensions, state, "major", "Major diameter", major.start, major.end);
  }

  const minor = getEllipseAxisDiameterPoints(ellipse, "minor");
  if (minor) {
    addLinearPreviewDimension(dimensions, state, "minor", "Minor diameter", minor.start, minor.end);
  }
}

function addPolygonPreviewDimension(dimensions, state, center, radiusPoint, circumscribed) {
  addLinearPreviewDimension(
    dimensions,
    state,
    circumscribed ? "apothem" : "radius",
    circumscribed ? "Apothem" : "Radius",
    center,
    radiusPoint);
  addPolygonSideCountPreviewDimension(dimensions, state, center, radiusPoint);
}

function addPolygonSideCountPreviewDimension(dimensions, state, center, radiusPoint) {
  const sideCount = getPolygonSideCount(state);
  if (!Number.isInteger(sideCount) || sideCount < 3) {
    return;
  }

  const centerScreen = worldToScreen(state, center);
  const edgeScreen = worldToScreen(state, radiusPoint);
  dimensions.push({
    key: "sides",
    label: "Sides",
    value: sideCount,
    point: {
      x: (centerScreen.x + edgeScreen.x) / 2,
      y: (centerScreen.y + edgeScreen.y) / 2 - 28
    }
  });
}

export function getThreePointArcPreviewDimensions(state, first, through, end) {
  const arc = getThreePointArc(first, through, end);
  if (!arc) {
    return [];
  }

  const dimensions = [];
  addArcRadiusPreviewDimension(dimensions, state, arc);
  return dimensions;
}

function addArcRadiusPreviewDimension(dimensions, state, arc) {
  if (!arc || !arc.center || !isFinitePositive(arc.radius)) {
    return;
  }

  const value = arc.radius;
  if (!formatDimensionValue(value)) {
    return;
  }

  const sweep = getPositiveSweepDegrees(arc.startAngleDegrees, arc.endAngleDegrees);
  const midAngle = arc.startAngleDegrees + sweep / 2;
  const arcPoint = pointOnCircle(arc.center, arc.radius, midAngle);
  const centerScreen = worldToScreen(state, arc.center);
  const arcScreen = worldToScreen(state, arcPoint);
  const radialDirection = normalizeScreenVector({
    x: arcScreen.x - centerScreen.x,
    y: arcScreen.y - centerScreen.y
  }) || { x: 1, y: 0 };
  dimensions.push({
    key: "radius",
    label: "Radius",
    value,
    point: {
      x: arcScreen.x + radialDirection.x * 34,
      y: arcScreen.y + radialDirection.y * 34
    }
  });
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
  const stateText = getSketchDimensionState(dimension);
  const affectedReferenceKeys = getSketchAffectedReferenceKeys(dimension);
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
    state: stateText,
    affectedReferenceKeys,
    anchorPoint,
    point: worldToScreen(state, anchorPoint),
    geometry: getDimensionGeometry(state, kind, referenceKeys, anchorPoint)
  };
}

function drawPersistentDimensions(state, dimensions) {
  state.context.save();
  state.context.globalCompositeOperation = "source-over";
  for (const dimension of dimensions) {
    drawDimensionGraphics(state, dimension, false, getPersistentDimensionVisualState(state, dimension));
  }
  state.context.restore();
}

function getPersistentDimensionVisualState(state, dimension) {
  const selectionKey = getPersistentDimensionSelectionKey(dimension);
  const selected = Boolean(selectionKey && state.selectedKeys && state.selectedKeys.has(selectionKey));
  const active = Boolean(selectionKey && state.activeSelectionKey === selectionKey);
  const hovered = Boolean(selectionKey
    && state.hoveredTarget
    && state.hoveredTarget.key === selectionKey
    && !isDimensionTargetBeingEdited(state, state.hoveredTarget));

  return {
    selected,
    active,
    hovered,
    unsatisfied: getSketchDimensionState(dimension) === "unsatisfied"
  };
}

function getPersistentDimensionSelectionKey(dimension) {
  if (!dimension) {
    return "";
  }

  return dimension.key || (dimension.id ? `persistent-${dimension.id}` : "");
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

    if (kind === "circle" || kind === "arc" || kind === "polygon") {
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
      if (areWorldLinesParallel(firstLine, secondLine)) {
        return {
          kind: "pointtolinedistance",
          referenceKeys: referenceKeys.slice(0, 2),
          value: getParallelWorldLineDistance(firstLine, secondLine)
        };
      }

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

  if (kind === "angle") {
    const arcAngle = resolveSketchArcAngleReference(state, referenceKeys[0]);
    if (arcAngle) {
      return getArcSweepDimensionAnchorPoint(arcAngle);
    }
  }

  if (kind === "count") {
    return resolveSketchEntityAnchorPoint(state, referenceKeys[0]);
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
        diameter: kind === "diameter",
        startAngleDegrees: circleLike.startAngleDegrees,
        endAngleDegrees: circleLike.endAngleDegrees
      }
      : null;
  }

  if (kind === "pointtolinedistance") {
    const firstPoint = resolveSketchReferencePoint(state, referenceKeys[0]);
    const secondPoint = resolveSketchReferencePoint(state, referenceKeys[1]);
    const firstLine = resolveSketchLineReference(state, referenceKeys[0]);
    const secondLine = resolveSketchLineReference(state, referenceKeys[1]);
    if (firstLine && secondLine && areWorldLinesParallel(firstLine, secondLine)) {
      const point = midpoint(secondLine.start, secondLine.end);
      return {
        type: "linear",
        start: point,
        end: projectPointToWorldLine(point, firstLine),
        anchorPoint
      };
    }

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
    const arcAngle = resolveSketchArcAngleReference(state, referenceKeys[0]);
    if (arcAngle) {
      return {
        type: "arcangle",
        center: arcAngle.center,
        radius: arcAngle.radius,
        startAngleDegrees: arcAngle.startAngleDegrees,
        endAngleDegrees: arcAngle.endAngleDegrees,
        anchorPoint
      };
    }

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

function getArcSweepDimensionAnchorPoint(arcAngle) {
  const sweep = getPositiveSweepDegrees(arcAngle.startAngleDegrees, arcAngle.endAngleDegrees);
  return pointOnCircle(arcAngle.center, arcAngle.radius * 0.7, arcAngle.startAngleDegrees + sweep / 2);
}

function drawDimensionGraphics(state, dimension, isPreview, visualState = null) {
  if (!dimension || !dimension.geometry) {
    return;
  }

  if (dimension.geometry.type === "linear") {
    drawLinearDimensionGraphics(state, dimension, isPreview, visualState);
  } else if (dimension.geometry.type === "radial") {
    drawRadialDimensionGraphics(state, dimension, isPreview, visualState);
  } else if (dimension.geometry.type === "arcangle") {
    drawArcAngleDimensionGraphics(state, dimension, isPreview, visualState);
  } else if (dimension.geometry.type === "angle") {
    drawAngleDimensionGraphics(state, dimension, isPreview, visualState);
  }
}

function drawLinearDimensionGraphics(state, dimension, isPreview, visualState = null) {
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
  const style = getDimensionRenderStyle(isPreview, visualState);
  context.save();
  applyDimensionGraphicsStyle(context, style);

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

  drawDimensionCanvasText(state, dimension, isPreview, visualState);
}

function drawRadialDimensionGraphics(state, dimension, isPreview, visualState = null) {
  const geometry = dimension.geometry;
  const center = worldToScreen(state, geometry.center);
  const anchor = worldToScreen(state, geometry.anchorPoint);
  const radius = geometry.radius * state.view.scale;
  const text = getDimensionDisplayText(dimension);
  const textWidth = text ? measureDimensionCanvasText(state, text) : 0;
  const edgeOverride = getArcRadialDimensionEdgeOverride(state, geometry);
  const radialGeometry = getRadialDimensionScreenGeometry(center, radius, anchor, geometry.diameter, textWidth, edgeOverride);
  const { context } = state;
  const style = getDimensionRenderStyle(isPreview, visualState);

  context.save();
  applyDimensionGraphicsStyle(context, style);

  for (const segment of radialGeometry.segments) {
    drawScreenLine(context, segment.start, segment.end);
  }

  for (const arrow of radialGeometry.arrows) {
    drawArrowhead(context, arrow.point, arrow.toward, DIMENSION_ARROWHEAD_SIZE);
  }

  context.restore();
  drawDimensionCanvasText(state, dimension, isPreview, visualState);
}

function getArcRadialDimensionEdgeOverride(state, geometry) {
  if (!geometry
    || !Number.isFinite(geometry.startAngleDegrees)
    || !Number.isFinite(geometry.endAngleDegrees)
    || !geometry.center
    || !geometry.anchorPoint
    || !isFinitePositive(geometry.radius)) {
    return null;
  }

  const anchorAngle = radiansToDegrees(Math.atan2(
    geometry.anchorPoint.y - geometry.center.y,
    geometry.anchorPoint.x - geometry.center.x));
  const clampedAngle = getClosestAngleOnSweep(anchorAngle, geometry.startAngleDegrees, geometry.endAngleDegrees);
  return worldToScreen(state, pointOnCircle(geometry.center, geometry.radius, clampedAngle));
}

function getClosestAngleOnSweep(angle, startAngle, endAngle) {
  const sweep = getPositiveSweepDegrees(startAngle, endAngle);
  const normalizedOffset = normalizeAngleDegrees(angle - startAngle);
  if (normalizedOffset <= sweep) {
    return startAngle + normalizedOffset;
  }

  return normalizedOffset - sweep < 360 - normalizedOffset
    ? startAngle + sweep
    : startAngle;
}

function drawAngleDimensionGraphics(state, dimension, isPreview, visualState = null) {
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
  const style = getDimensionRenderStyle(isPreview, visualState);
  context.save();
  applyDimensionGraphicsStyle(context, style);
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
  drawDimensionCanvasText(state, dimension, isPreview, visualState);
}

function drawArcAngleDimensionGraphics(state, dimension, isPreview, visualState = null) {
  const geometry = getArcAngleDimensionScreenGeometry(
    state,
    dimension.geometry.center,
    dimension.geometry.radius,
    dimension.geometry.startAngleDegrees,
    dimension.geometry.endAngleDegrees,
    dimension.geometry.anchorPoint);
  if (!geometry) {
    return;
  }

  const { context } = state;
  const style = getDimensionRenderStyle(isPreview, visualState);
  context.save();
  applyDimensionGraphicsStyle(context, style);
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
  drawDimensionCanvasText(state, dimension, isPreview, visualState);
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

export function getArcAngleDimensionScreenGeometry(state, center, radius, startAngleDegrees, endAngleDegrees, anchor) {
  if (!center || !anchor || !isFinitePositive(radius)) {
    return null;
  }

  const screenCenter = worldToScreen(state, center);
  const screenAnchor = worldToScreen(state, anchor);
  const screenRadius = radius * state.view.scale;
  if (!isFinitePositive(screenRadius)) {
    return null;
  }

  const arcStart = worldToScreen(state, pointOnCircle(center, radius, startAngleDegrees));
  const arcEnd = worldToScreen(state, pointOnCircle(center, radius, endAngleDegrees));
  const startScreenAngle = Math.atan2(arcStart.y - screenCenter.y, arcStart.x - screenCenter.x);
  const endScreenAngle = Math.atan2(arcEnd.y - screenCenter.y, arcEnd.x - screenCenter.x);
  const sweep = getShortestScreenAngleSweep(startScreenAngle, endScreenAngle);
  const anchorDistance = distanceBetweenScreenPoints(screenCenter, screenAnchor);
  const outsideRadius = Math.max(screenRadius + 18, anchorDistance);
  const insideRadius = Math.max(18, Math.min(screenRadius - 10, anchorDistance));
  const dimensionRadius = anchorDistance < screenRadius && screenRadius > 34
    ? insideRadius
    : outsideRadius;
  const extensionPastDimension = 6;
  const startExtensionEnd = pointFromScreenAngle(
    screenCenter,
    startScreenAngle,
    dimensionRadius + Math.sign(dimensionRadius - screenRadius) * extensionPastDimension);
  const endExtensionEnd = pointFromScreenAngle(
    screenCenter,
    endScreenAngle,
    dimensionRadius + Math.sign(dimensionRadius - screenRadius) * extensionPastDimension);
  const arcDimensionStart = pointFromScreenAngle(screenCenter, sweep.start, dimensionRadius);
  const arcDimensionEnd = pointFromScreenAngle(screenCenter, sweep.end, dimensionRadius);
  const arrowSweep = Math.max(0.05, Math.min(0.18, Math.abs(sweep.end - sweep.start) / 5));

  return {
    vertex: screenCenter,
    radius: dimensionRadius,
    sweep,
    extensionSegments: [
      { start: arcStart, end: startExtensionEnd },
      { start: arcEnd, end: endExtensionEnd }
    ],
    arrows: [
      {
        point: arcDimensionStart,
        toward: pointFromScreenAngle(screenCenter, sweep.start + arrowSweep, dimensionRadius)
      },
      {
        point: arcDimensionEnd,
        toward: pointFromScreenAngle(screenCenter, sweep.end - arrowSweep, dimensionRadius)
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

function drawDimensionCanvasText(state, dimension, isPreview, visualState = null) {
  const text = getDimensionDisplayText(dimension);
  if (!text || state.dimensionOverlay && !isPreview) {
    return;
  }

  drawFloatingDimensionLabel(state, text, dimension.point, getDimensionRenderStyle(isPreview, visualState));
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

function applyDimensionGraphicsStyle(context, style) {
  context.strokeStyle = style.strokeStyle;
  context.fillStyle = style.strokeStyle;
  context.lineWidth = style.lineWidth || 1.15;
  context.setLineDash(style.lineDash || []);
  if (style.glow) {
    context.shadowColor = "rgba(125, 211, 252, 0.5)";
    context.shadowBlur = 8;
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
    const visualState = getPersistentDimensionVisualState(state, dimension);
    if (!isFocused && !isEditing) {
      input.value = getDimensionDisplayText(dimension);
    }

    input.className = getDimensionInputClassName({
      persistent: true,
      selected: visualState.selected,
      active: visualState.active,
      editing: isEditing,
      unsatisfied: visualState.unsatisfied
    });
    input.dataset.dimensionId = dimension.id;
    input.dataset.dimensionLabel = dimension.label;
    input.dataset.dimensionKind = dimension.kind;
    input.dataset.dimensionValue = String(dimension.value);
    input.dataset.dimensionEditing = isEditing ? "true" : "false";
    const inputPoint = clampDimensionInputScreenPoint(state, dimension.point);
    input.style.left = `${inputPoint.x}px`;
    input.style.top = `${inputPoint.y}px`;
  }

  focusPendingPersistentDimensionEditIfNeeded(state, dimensions);
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
  input.className = getDimensionInputClassName({ persistent: true });
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
    const wasEditing = input.dataset.dimensionEditing === "true";
    const skipNextBlurCommit = input.dataset.skipNextBlurCommit === "true";
    input.dataset.skipNextBlurCommit = "false";
    input.dataset.dimensionEditing = "false";
    input.classList.remove("drawing-dimension-input-active");
    if (shouldCommitDimensionInputOnBlur(state.suppressDimensionInputCommit, skipNextBlurCommit, wasEditing)) {
      commitPersistentDimensionInputValue(state, input);
    }
  });
  input.addEventListener("keydown", event => handlePersistentDimensionInputKeyDown(state, input, event));
  input.addEventListener("change", () => {
    const skipNextChangeCommit = input.dataset.skipNextChangeCommit === "true";
    input.dataset.skipNextChangeCommit = "false";
    if (shouldCommitDimensionInputOnChange(skipNextChangeCommit, input.dataset.dimensionEditing === "true")) {
      commitPersistentDimensionInputValue(state, input);
    }
  });

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
  const changed = setPersistentDimensionSelection(state, dimensionId);
  if (changed) {
    notifySelectionChanged(state);
    updateDebugAttributes(state);
    draw(state);
  }

  return changed;
}

function setPersistentDimensionSelection(state, dimensionId) {
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
  return changed;
}

function focusPendingPersistentDimensionEditIfNeeded(state, dimensions) {
  const pendingIds = state.pendingPersistentDimensionEditIds;
  if (!pendingIds) {
    return false;
  }

  const dimensionId = getPendingPersistentDimensionEditId(dimensions, pendingIds);
  if (!dimensionId) {
    return false;
  }

  const input = state.persistentDimensionInputs.get(dimensionId);
  if (!input) {
    return false;
  }

  state.pendingPersistentDimensionEditIds = null;
  const changed = setPersistentDimensionSelection(state, dimensionId);
  if (changed) {
    notifySelectionChanged(state);
    updateDebugAttributes(state);
  }

  input.dataset.dimensionEditing = "true";
  input.className = getDimensionInputClassName({
    persistent: true,
    selected: true,
    active: true,
    editing: true
  });
  focusElement(input);
  selectInputText(input);
  requestDimensionRedraw(state);
  return true;
}

function getCurrentPersistentDimensionIds(state) {
  return new Set(getDocumentDimensions(state.document)
    .map(dimension => String(getSketchItemId(dimension) || ""))
    .filter(id => Boolean(id)));
}

function requestDimensionRedraw(state) {
  if (!state || state.disposed || typeof window === "undefined" || typeof window.requestAnimationFrame !== "function") {
    return;
  }

  window.requestAnimationFrame(() => {
    if (!state.disposed) {
      draw(state);
    }
  });
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

  drawConstraintGlyphLeader(state, group);

  for (let index = 0; index < group.constraints.length; index++) {
    const constraint = group.constraints[index];
    const icon = getConstraintGlyphIcon(getSketchItemKind(constraint));
    const point = getConstraintGlyphItemPoint(group, index);
    drawConstraintGlyph(state, icon, point, constraint);
  }

  context.restore();
}

function drawConstraintGlyphLeader(state, group) {
  const leader = getConstraintGlyphLeader(group);
  if (!leader) {
    return;
  }

  const { context } = state;
  context.save();
  context.strokeStyle = "rgba(143, 161, 182, 0.82)";
  context.fillStyle = "rgba(143, 161, 182, 0.82)";
  context.lineWidth = 1;
  context.setLineDash([4, 3]);
  context.beginPath();
  context.moveTo(leader.start.x, leader.start.y);
  context.lineTo(leader.end.x, leader.end.y);
  context.stroke();
  context.setLineDash([]);
  context.beginPath();
  context.arc(leader.start.x, leader.start.y, 2, 0, Math.PI * 2);
  context.fill();
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

export function applyConstraintVisibilityState(state, visible) {
  if (!state) {
    return false;
  }

  const nextVisible = Boolean(visible);
  const changed = state.showAllConstraints !== nextVisible;
  state.showAllConstraints = nextVisible;

  if (changed) {
    if (state.constraintGroupOffsets && typeof state.constraintGroupOffsets.clear === "function") {
      state.constraintGroupOffsets.clear();
    }

    state.constraintGroupDrag = null;
  }

  return changed;
}

export function getConstraintGlyphGroups(state) {
  const constraints = getDocumentConstraints(state.document);
  const groupsByKey = new Map();
  for (const constraint of constraints) {
    if (!getConstraintGlyphIcon(getSketchItemKind(constraint))) {
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
  const storedOffset = getStoredConstraintGroupOffset(state, group.key);
  const offset = storedOffset || getDefaultConstraintGroupOffset(state, group, anchorScreenPoint);
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
    point,
    manualOffset: storedOffset !== null
  };
  result.rect = getConstraintGlyphGroupRect(result);
  return result;
}

export function getConstraintGlyphLeader(group) {
  if (!group || !group.anchorScreenPoint || !group.point || !group.rect) {
    return null;
  }

  if (!group.manualOffset) {
    return null;
  }

  const start = group.anchorScreenPoint;
  const dx = group.point.x - start.x;
  const dy = group.point.y - start.y;
  const distance = Math.hypot(dx, dy);
  if (distance < CONSTRAINT_LEADER_MIN_DISTANCE || isPointInScreenRect(start, group.rect)) {
    return null;
  }

  const end = getScreenSegmentRectEntryPoint(start, group.point, group.rect);
  return end ? { start, end } : null;
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

  if (target.kind === "constraint" && target.constraintId) {
    return Array.isArray(group.constraints)
      && group.constraints.some(constraint => StringComparer(getSketchItemId(constraint), target.constraintId));
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

function getStoredConstraintGroupOffset(state, groupKey) {
  const offset = state.constraintGroupOffsets && state.constraintGroupOffsets.get(groupKey);
  return offset && Number.isFinite(offset.x) && Number.isFinite(offset.y)
    ? offset
    : null;
}

function getDefaultConstraintGroupOffset(state, group, anchorScreenPoint) {
  const normal = getConstraintGroupReferenceNormal(state, group) || getAnchorOutwardScreenNormal(state, anchorScreenPoint) || { x: 0, y: -1 };
  return {
    x: normal.x * CONSTRAINT_DEFAULT_OFFSET_DISTANCE,
    y: normal.y * CONSTRAINT_DEFAULT_OFFSET_DISTANCE
  };
}

function getConstraintGroupReferenceNormal(state, group) {
  if (!group || !Array.isArray(group.constraints)) {
    return null;
  }

  for (const constraint of group.constraints) {
    for (const referenceKey of getSketchReferenceKeys(constraint)) {
      const line = resolveSketchReferenceHostLine(state, referenceKey);
      if (!line) {
        continue;
      }

      const normal = getScreenNormal(worldToScreen(state, line.start), worldToScreen(state, line.end));
      if (normal && Number.isFinite(normal.x) && Number.isFinite(normal.y)) {
        return normal.y > 0 ? { x: -normal.x, y: -normal.y } : normal;
      }
    }
  }

  return null;
}

function getAnchorOutwardScreenNormal(state, anchorScreenPoint) {
  if (!state || !anchorScreenPoint) {
    return null;
  }

  const size = getCanvasCssSize(state);
  const center = { x: size.width / 2, y: size.height / 2 };
  return normalizeScreenVector({
    x: anchorScreenPoint.x - center.x,
    y: anchorScreenPoint.y - center.y
  });
}

function resolveSketchReferenceHostLine(state, referenceKey) {
  const directLine = resolveSketchLineReference(state, referenceKey);
  if (directLine) {
    return directLine;
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
    return reference.segmentIndex >= 0 && reference.segmentIndex < points.length - 1
      ? { start: points[reference.segmentIndex], end: points[reference.segmentIndex + 1] }
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

export function getConstraintGlyphGroupHit(state, screenPoint) {
  if (!screenPoint) {
    return null;
  }

  let nearestHit = null;
  for (const group of getVisibleConstraintGlyphGroups(state)) {
    const itemHit = getConstraintGlyphItemHit(group, screenPoint);
    const distance = itemHit
      ? itemHit.distance
      : getConstraintGlyphGroupScreenDistance(group, screenPoint);
    if (distance > CONSTRAINT_GROUP_HIT_PADDING) {
      continue;
    }

    if (!nearestHit || distance < nearestHit.distance) {
      nearestHit = {
        group,
        target: itemHit ? createConstraintTarget(itemHit.constraint) : null,
        distance
      };
    }
  }

  return nearestHit;
}

function getConstraintGlyphItemHit(group, screenPoint) {
  if (!group || !Array.isArray(group.constraints) || !screenPoint) {
    return null;
  }

  let nearestHit = null;
  for (let index = 0; index < group.constraints.length; index += 1) {
    const constraint = group.constraints[index];
    const point = getConstraintGlyphItemPoint(group, index);
    const rect = {
      minX: point.x - CONSTRAINT_GLYPH_SIZE / 2,
      minY: point.y - CONSTRAINT_GLYPH_SIZE / 2,
      maxX: point.x + CONSTRAINT_GLYPH_SIZE / 2,
      maxY: point.y + CONSTRAINT_GLYPH_SIZE / 2
    };
    const dx = Math.max(0, rect.minX - screenPoint.x, screenPoint.x - rect.maxX);
    const dy = Math.max(0, rect.minY - screenPoint.y, screenPoint.y - rect.maxY);
    const distance = Math.hypot(dx, dy);
    if (!nearestHit || distance < nearestHit.distance) {
      nearestHit = { constraint, distance };
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

function isPointInScreenRect(point, rect) {
  return point
    && rect
    && point.x >= rect.minX
    && point.x <= rect.maxX
    && point.y >= rect.minY
    && point.y <= rect.maxY;
}

function getScreenSegmentRectEntryPoint(start, end, rect) {
  if (!start || !end || !rect) {
    return null;
  }

  const dx = end.x - start.x;
  const dy = end.y - start.y;
  if (Math.hypot(dx, dy) <= CONSTRAINT_LEADER_MIN_DISTANCE) {
    return null;
  }

  const candidates = [];
  addRectIntersectionCandidate(candidates, start, dx, dy, rect, "x", rect.minX);
  addRectIntersectionCandidate(candidates, start, dx, dy, rect, "x", rect.maxX);
  addRectIntersectionCandidate(candidates, start, dx, dy, rect, "y", rect.minY);
  addRectIntersectionCandidate(candidates, start, dx, dy, rect, "y", rect.maxY);
  candidates.sort((first, second) => first.t - second.t);

  return candidates.length > 0 ? candidates[0].point : null;
}

function addRectIntersectionCandidate(candidates, start, dx, dy, rect, axis, value) {
  const denominator = axis === "x" ? dx : dy;
  if (Math.abs(denominator) <= 0.000001) {
    return;
  }

  const t = (value - (axis === "x" ? start.x : start.y)) / denominator;
  if (t <= 0 || t > 1) {
    return;
  }

  const point = {
    x: start.x + dx * t,
    y: start.y + dy * t
  };
  if (point.x < rect.minX - 0.000001
    || point.x > rect.maxX + 0.000001
    || point.y < rect.minY - 0.000001
    || point.y > rect.maxY + 0.000001) {
    return;
  }

  candidates.push({ t, point });
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

function drawConstraintGlyph(state, icon, point, constraint) {
  if (!icon) {
    return;
  }

  const { context } = state;
  const stateText = getSketchConstraintState(constraint);
  const selected = state.selectedKeys && state.selectedKeys.has(`${CONSTRAINT_KEY_PREFIX}${getSketchItemId(constraint)}`);
  context.save();
  context.fillStyle = selected
    ? "rgba(14, 165, 233, 0.82)"
    : stateText === "unsatisfied"
    ? "rgba(127, 29, 29, 0.78)"
    : "rgba(51, 65, 85, 0.82)";
  context.strokeStyle = selected ? "#bae6fd" : stateText === "unsatisfied" ? "#f87171" : "#64748b";
  context.lineWidth = selected ? 1.35 : 1;
  context.setLineDash([]);
  context.beginPath();
  context.rect(point.x - 8, point.y - 8, 16, 16);
  context.fill();
  context.stroke();
  drawConstraintGlyphIcon(
    context,
    icon,
    point,
    selected ? "#ffffff" : stateText === "unsatisfied" ? "#fecaca" : "#e2e8f0"
  );
  context.restore();
}

function drawConstraintGlyphIcon(context, icon, point, color) {
  context.save();
  context.translate(point.x, point.y);
  context.strokeStyle = color;
  context.fillStyle = color;
  context.lineWidth = 1.45;
  context.lineCap = "round";
  context.lineJoin = "round";

  switch (icon) {
    case "coincident":
      drawCircleIcon(context, -2.2, 2.2, 2.3);
      drawCircleIcon(context, 2.2, -2.2, 2.3);
      drawGlyphLine(context, -0.6, 0.6, 0.6, -0.6);
      break;
    case "concentric":
      drawCircleIcon(context, 0, 0, 4.7);
      drawCircleIcon(context, 0, 0, 2.2);
      break;
    case "parallel":
      drawGlyphLine(context, -4.3, 4.9, 1.2, -4.9);
      drawGlyphLine(context, -0.7, 4.9, 4.8, -4.9);
      break;
    case "tangent":
      drawCircleIcon(context, -1.7, 1.5, 3.2);
      drawGlyphLine(context, 0.7, -1.4, 5.5, -6.2);
      break;
    case "horizontal":
      drawGlyphLine(context, -5.4, 0, 5.4, 0);
      drawGlyphLine(context, -2.5, -2.7, 2.5, -2.7);
      break;
    case "vertical":
      drawGlyphLine(context, 0, -5.4, 0, 5.4);
      drawGlyphLine(context, 2.7, -2.5, 2.7, 2.5);
      break;
    case "perpendicular":
      drawGlyphLine(context, -4.6, 4.3, -4.6, -4.3);
      drawGlyphLine(context, -4.6, 4.3, 4.6, 4.3);
      drawGlyphLine(context, -1.6, 4.3, -1.6, 1.3);
      drawGlyphLine(context, -4.6, 1.3, -1.6, 1.3);
      break;
    case "equal":
      drawGlyphLine(context, -4.7, -2, 4.7, -2);
      drawGlyphLine(context, -4.7, 2, 4.7, 2);
      break;
    case "midpoint":
      drawGlyphLine(context, -5, 0, 5, 0);
      context.beginPath();
      context.moveTo(0, -4.5);
      context.lineTo(4, 3.2);
      context.lineTo(-4, 3.2);
      context.closePath();
      context.stroke();
      break;
    case "fix":
      drawGlyphLine(context, 0, -5.2, 0, 1.8);
      drawGlyphLine(context, -4.2, 1.8, 4.2, 1.8);
      drawGlyphLine(context, -2.7, 4.8, 2.7, 4.8);
      drawGlyphLine(context, -2.2, 1.8, -2.7, 4.8);
      drawGlyphLine(context, 2.2, 1.8, 2.7, 4.8);
      break;
    default:
      break;
  }

  context.restore();
}

function drawCircleIcon(context, x, y, radius) {
  context.beginPath();
  context.arc(x, y, radius, 0, Math.PI * 2);
  context.stroke();
}

function drawGlyphLine(context, x1, y1, x2, y2) {
  context.beginPath();
  context.moveTo(x1, y1);
  context.lineTo(x2, y2);
  context.stroke();
}

export function getConstraintGlyphIcon(kind) {
  switch (kind) {
    case "coincident":
      return "coincident";
    case "concentric":
      return "concentric";
    case "parallel":
      return "parallel";
    case "tangent":
      return "tangent";
    case "horizontal":
      return "horizontal";
    case "vertical":
      return "vertical";
    case "perpendicular":
      return "perpendicular";
    case "equal":
      return "equal";
    case "midpoint":
      return "midpoint";
    case "fix":
      return "fix";
    default:
      return "";
  }
}

export function getConstraintGlyphText(kind) {
  return getConstraintGlyphIcon(kind);
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
      if (commitCurrentSketchTool(state) || commitCurrentModifyTool(state)) {
        focusCanvasWithoutDimensionCommit(state);
        updateDebugAttributes(state);
        draw(state);
      } else if (advanceCurrentSketchToolPoint(state)) {
        updateDebugAttributes(state);
        draw(state);
      }
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
    input.dataset.skipNextBlurCommit = "true";
    input.dataset.skipNextChangeCommit = "true";
    const nextKey = getNextDimensionKey(getVisibleDimensionDescriptors(state), input.dataset.dimensionKey, event.shiftKey);
    focusDimensionInputByKey(
      state,
      nextKey,
      true,
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

function focusDimensionInputByKey(state, key, selectContents, clearLock = false) {
  const input = key ? state.dimensionInputs.get(key) : null;
  if (input) {
    focusDimensionInput(state, input, selectContents, clearLock);
  }
}

function focusDimensionInput(state, input, selectContents, clearLock = false) {
  if (!input) {
    return;
  }

  state.activeDimensionKey = input.dataset.dimensionKey || null;
  if (clearLock && clearDraftDimensionValue(state, state.activeDimensionKey)) {
    input.dataset.dimensionEditing = "false";
  }
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

export function markDimensionInputsToSkipNextBlurCommit(state) {
  markDimensionInputCollectionToSkipNextCommit(state.dimensionInputs);
  markDimensionInputCollectionToSkipNextCommit(state.persistentDimensionInputs);
}

function markDimensionInputCollectionToSkipNextCommit(inputs) {
  if (!inputs) {
    return;
  }

  for (const input of inputs.values()) {
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

function drawFloatingDimensionLabel(state, label, point, style = null) {
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
  context.strokeStyle = style && style.strokeStyle ? style.strokeStyle : "#38bdf8";
  context.lineWidth = 1;
  context.setLineDash([]);
  if (style && style.glow) {
    context.shadowColor = "rgba(125, 211, 252, 0.5)";
    context.shadowBlur = 8;
  }

  context.beginPath();
  context.roundRect(x, y, width, height, 3);
  context.fill();
  context.stroke();

  context.fillStyle = style && style.textStyle ? style.textStyle : "#7dd3fc";
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
    case "polygon":
      return buildPolygonPath(state, entity);
    case "spline":
      return buildPolylinePath(state, entity);
    case "circle":
      return buildCirclePath(state, entity);
    case "arc":
      return buildArcPath(state, entity);
    case "ellipse":
      return buildEllipsePath(state, entity);
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

function buildPolygonPath(state, entity) {
  const points = getEntityPoints(entity);
  if (points.length < 3) {
    return false;
  }

  state.context.beginPath();
  const guidePoints = getPolygonGuideCircleWorldPointsForEntity(entity);
  if (guidePoints.length >= 2) {
    const firstGuidePoint = worldToScreen(state, guidePoints[0]);
    state.context.moveTo(firstGuidePoint.x, firstGuidePoint.y);
    for (let index = 1; index < guidePoints.length; index += 1) {
      const guidePoint = worldToScreen(state, guidePoints[index]);
      state.context.lineTo(guidePoint.x, guidePoint.y);
    }
  }

  const first = worldToScreen(state, points[0]);
  state.context.moveTo(first.x, first.y);
  for (let index = 1; index < points.length; index += 1) {
    const screenPoint = worldToScreen(state, points[index]);
    state.context.lineTo(screenPoint.x, screenPoint.y);
  }

  state.context.closePath();
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

function buildEllipsePath(state, entity) {
  const points = getEllipseWorldPointsFromEntity(entity);
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
    const nearestTarget = isPowerTrimTool(state)
      ? findNearestPowerTrimTarget(state, screenPoint)
      : findNearestTarget(state, screenPoint);
    if (setHoveredTarget(state, nearestTarget)) {
      draw(state);
    }

    if (isPowerTrimTool(state) && state.powerTrimDrag && state.powerTrimDrag.pointerId === event.pointerId) {
      updatePowerTrimDrag(state, screenPoint);
      event.preventDefault();
      updateDebugAttributes(state);
      draw(state);
      return;
    }

    if (state.clickCandidate) {
      const moveDistance = distanceBetweenScreenPoints(screenPoint, state.clickCandidate.screenPoint);
      if (moveDistance > CLICK_MOVE_TOLERANCE) {
        state.clickCandidate.cancelled = true;
        if (isPowerTrimTool(state) && state.clickCandidate.pointerId === event.pointerId) {
          startPowerTrimDrag(state, state.clickCandidate, screenPoint);
          event.preventDefault();
          updateDebugAttributes(state);
          draw(state);
          return;
        }
      }
    }

    return;
  }

  const sketchTool = getSketchCreationTool(state);
  const modifyTool = getModifyTool(state);
  if (sketchTool || modifyTool) {
    const addSplinePointSketchTool = isAddSplinePointSketchTool(sketchTool);
    const nearestTarget = modifyTool === "addsplinepoint"
      ? findNearestSelectedSplineTarget(state, screenPoint)
      : addSplinePointSketchTool
        ? findNearestSplineTarget(state, screenPoint)
        : findNearestTarget(state, screenPoint);
    setHoveredTarget(state, nearestTarget);

    if (sketchTool && !addSplinePointSketchTool && tryToggleSketchChainToolAtPoint(state, screenPoint, nearestTarget)) {
      updateDebugAttributes(state);
      draw(state);
      return;
    }

    if (modifyTool === "addsplinepoint" || addSplinePointSketchTool) {
      state.toolDraft = state.toolDraft || createEmptyToolDraft();
      state.toolDraft.points = [];
      state.toolDraft.previewPoint = getAddSplinePointPickPoint(nearestTarget);
      updateDebugAttributes(state);
      draw(state);
    } else if (modifyTool === "offset") {
      state.toolDraft = state.toolDraft || createEmptyToolDraft();
      state.toolDraft.previewPoint = getSketchWorldPoint(state, screenPoint, nearestTarget, event);
      applyLockedDraftDimensions(state);
      updateDebugAttributes(state);
      draw(state);
    } else if (state.toolDraft && state.toolDraft.points.length > 0) {
      state.toolDraft.previewPoint = getSketchWorldPoint(state, screenPoint, nearestTarget, event);
      if (sketchTool) {
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

  const constraintGroupHit = getConstraintGlyphGroupHit(state, screenPoint);
  if (constraintGroupHit && constraintGroupHit.target) {
    if (setHoveredTarget(state, constraintGroupHit.target)) {
      draw(state);
    }

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
        constraintGroupAnchorScreenPoint: constraintGroupHit.group.anchorScreenPoint,
        constraintTarget: constraintGroupHit.target
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

  if (state.powerTrimDrag && state.powerTrimDrag.pointerId === event.pointerId) {
    updatePowerTrimDrag(state, screenPoint);
    const requests = getPowerTrimCrossingRequests(state, state.powerTrimDrag.points);
    state.powerTrimDrag = null;
    state.clickCandidate = null;
    if (requests.length > 0) {
      invokeDotNet(
        state,
        "OnPowerTrimCrossingRequested",
        requests.map(request => request.targetKey),
        flattenPointCoordinates(requests.map(request => request.point)));
    }

    const nearestTarget = findNearestTarget(state, screenPoint);
    setHoveredTarget(state, nearestTarget);
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
        if (candidate.constraintTarget && applyDirectSelectionClick(state, candidate.constraintTarget.key)) {
          notifySelectionChanged(state);
        }
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
    || target.kind === "entity";
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
  const dragPoint = getGeometryDragWorldPoint(state, drag, screenPoint);
  drag.currentWorldPoint = dragPoint.point;
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
  setHoveredTarget(state, dragPoint.target || resolveSelectionTarget(state, drag.targetKey));
  return true;
}

function getGeometryDragWorldPoint(state, drag, screenPoint) {
  const fallback = screenToWorld(state, screenPoint);
  const originalDocument = state.document;
  state.document = drag.originalDocument;
  try {
    const target = findNearestTarget(state, screenPoint);
    if (isUsableGeometryDragSnapTarget(drag, target)) {
      return {
        point: getTargetWorldSnapPoint(target) || fallback,
        target
      };
    }
  } finally {
    state.document = originalDocument;
  }

  return {
    point: fallback,
    target: null
  };
}

function isUsableGeometryDragSnapTarget(drag, target) {
  if (!drag || !target || target.dynamic || target.kind === "dimension") {
    return false;
  }

  const draggedEntityId = getSelectionEntityId(drag.targetKey);
  return target.key !== drag.targetKey
    && (!draggedEntityId || !target.entityId || !StringComparer(target.entityId, draggedEntityId))
    && Boolean(getTargetWorldSnapPoint(target));
}

function getTargetWorldSnapPoint(target) {
  if (!target) {
    return null;
  }

  return target.point || target.snapPoint || null;
}

function getSelectionEntityId(selectionKey) {
  const key = String(selectionKey || "");
  const pointIndex = key.indexOf(POINT_KEY_SEPARATOR);
  if (pointIndex > 0) {
    return key.slice(0, pointIndex);
  }

  const segmentIndex = key.indexOf(SEGMENT_KEY_SEPARATOR);
  if (segmentIndex > 0) {
    return key.slice(0, segmentIndex);
  }

  return key || null;
}

export function finishGeometryDrag(state, screenPoint, event = {}) {
  const drag = state.geometryDrag;
  if (!drag) {
    return false;
  }

  updateGeometryDrag(state, screenPoint, event);
  const endWorldPoint = drag.currentWorldPoint || screenToWorld(state, screenPoint);
  const optimisticDocument = state.document;
  const originalDocument = drag.originalDocument;

  state.geometryDrag = null;
  if (drag.moved) {
    const invocation = invokeDotNet(
      state,
      "OnGeometryDragRequested",
      drag.targetKey,
      drag.startWorldPoint.x,
      drag.startWorldPoint.y,
      endWorldPoint.x,
      endWorldPoint.y,
      Boolean(drag.constrainToCurrentVector));
    if (invocation && typeof invocation.catch === "function") {
      invocation.catch(() => {
        if (state.document === optimisticDocument) {
          state.document = originalDocument;
          pruneInteractionState(state, true);
          updateDebugAttributes(state);
          draw(state);
        }
      });
    }
  } else {
    state.document = originalDocument;
  }

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

  if (isAddSplinePointSketchTool(tool)) {
    commitAddSplinePointOnClickedSpline(state, screenPoint);
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
  if (isVariablePointSketchTool(tool)) {
    state.toolDraft = {
      points: nextPoints,
      previewPoint: null,
      dimensionValues: {}
    };
    setHoveredTarget(state, null);
    return;
  }

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

  const clickTarget = tool === "addsplinepoint"
    ? findNearestSelectedSplineTarget(state, screenPoint)
    : findNearestTarget(state, screenPoint);
  if (clickTarget) {
    setHoveredTarget(state, clickTarget);
  }

  const worldPoint = tool === "addsplinepoint"
    ? getAddSplinePointPickPoint(clickTarget)
    : getSketchWorldPoint(state, screenPoint, clickTarget, event);
  if (!worldPoint) {
    return false;
  }

  if (getModifyToolPointCount(tool) === 1) {
    const commitPoint = tool === "offset" && state.toolDraft && state.toolDraft.previewPoint
      ? state.toolDraft.previewPoint
      : worldPoint;
    commitModifyToolPoints(state, tool, [commitPoint]);
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

  if (tool !== "addsplinepoint") {
    state.activeTool = "select";
    if (state.canvas && state.canvas.dataset) {
      state.canvas.dataset.activeTool = state.activeTool;
    }
  }

  state.toolDraft = createEmptyToolDraft();
  clearTransientDimensionInputs(state);
  setHoveredTarget(state, null);
  invokeDotNet(state, "OnModifyToolCommitted", tool, flattenPointCoordinates(points));
  return true;
}

function commitCurrentModifyTool(state) {
  const tool = getModifyTool(state);
  if (!tool || !state.toolDraft || !state.toolDraft.previewPoint) {
    return false;
  }

  if (tool === "offset") {
    return commitModifyToolPoints(state, tool, [state.toolDraft.previewPoint]);
  }

  const points = state.toolDraft.points.concat(state.toolDraft.previewPoint);
  if (points.length < getModifyToolPointCount(tool)) {
    return false;
  }

  return commitModifyToolPoints(state, tool, points);
}

function getAddSplinePointPickPoint(target) {
  if (!target || getEntityKind(target.entity) !== "spline") {
    return null;
  }

  return target.snapPoint || target.point || null;
}

function commitAddSplinePointOnClickedSpline(state, screenPoint) {
  const request = getAddSplinePointRequest(state, screenPoint);
  if (!request) {
    state.toolDraft = createEmptyToolDraft();
    setHoveredTarget(state, null);
    return false;
  }

  state.toolDraft = createEmptyToolDraft();
  state.selectedKeys.clear();
  state.selectedKeys.add(request.targetKey);
  state.activeSelectionKey = request.targetKey;
  setHoveredTarget(state, request.target);
  notifySelectionChanged(state);
  invokeDotNet(state, "OnAddSplinePointRequested", request.targetKey, request.point.x, request.point.y);
  return true;
}

export function shouldPreserveDraftDimensionsForNextPoint(tool, nextPoints) {
  return Array.isArray(nextPoints)
    && nextPoints.length === 2
    && (tool === "alignedrectangle"
      || tool === "centerpointarc"
      || tool === "ellipse"
      || tool === "ellipticalarc"
      || tool === "slot");
}

export function getNextSketchToolDimensionFocusKey(tool, nextPoints) {
  if (!Array.isArray(nextPoints) || nextPoints.length !== 2) {
    return null;
  }

  if (tool === "alignedrectangle") {
    return "depth";
  }

  if (tool === "slot") {
    return "radius";
  }

  if (tool === "ellipse" || tool === "ellipticalarc") {
    return "minor";
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

    state.pendingPersistentDimensionEditIds = getCurrentPersistentDimensionIds(state);
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

export function getDimensionSelectionKey(target) {
  if (!target || target.dynamic) {
    return null;
  }

  if (target.kind === "point" && target.entityId) {
    return getDimensionLineLikeReferenceFromPointTarget(target) || target.key;
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

function getDimensionLineLikeReferenceFromPointTarget(target) {
  if (!target || !target.entity || !target.entityId) {
    return null;
  }

  const kind = getEntityKind(target.entity);
  const label = String(target.label || "").toLowerCase();
  if (kind === "line" && label === "mid") {
    return target.entityId;
  }

  if (kind === "polyline") {
    const segmentIndex = parseIndexedPointLabel(label, "mid-");
    if (segmentIndex !== null) {
      return `${target.entityId}${SEGMENT_KEY_SEPARATOR}${segmentIndex}`;
    }
  }

  return null;
}

export function isDimensionReferenceSetComplete(state, selectionKeys, lastTarget) {
  if (!Array.isArray(selectionKeys) || selectionKeys.length === 0) {
    return false;
  }

  if (selectionKeys.length >= 2) {
    return getDimensionModelFromReferences(state, selectionKeys) !== null;
  }

  if (getDimensionModelFromReferences(state, selectionKeys) !== null) {
    return true;
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

function commitCurrentSketchTool(state, allowVariablePointCommit = false) {
  const tool = getSketchCreationTool(state);
  if (!tool || !state.toolDraft || state.toolDraft.points.length === 0) {
    return false;
  }

  const points = state.toolDraft.previewPoint
    ? state.toolDraft.points.concat(state.toolDraft.previewPoint)
    : state.toolDraft.points.slice();
  if (isVariablePointSketchTool(tool)) {
    if (!allowVariablePointCommit || points.length < 2) {
      return false;
    }
  } else if (!state.toolDraft.previewPoint || points.length < getSketchToolPointCount(tool)) {
    return false;
  }

  const previousPoint = points[points.length - 2] || null;
  const lastPoint = points[points.length - 1] || null;
  if (previousPoint && lastPoint && distanceBetweenWorldPoints(previousPoint, lastPoint) <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

  if (!isSketchToolPointSetValid(tool, points)) {
    return false;
  }

  commitSketchToolPoints(state, tool, points);
  return true;
}

function advanceCurrentSketchToolPoint(state) {
  const tool = getSketchCreationTool(state);
  if (!tool || !state.toolDraft || state.toolDraft.points.length === 0 || !state.toolDraft.previewPoint) {
    return false;
  }

  const nextPoints = state.toolDraft.points.concat(state.toolDraft.previewPoint);
  if (isVariablePointSketchTool(tool)) {
    const previousPoint = state.toolDraft.points[state.toolDraft.points.length - 1];
    if (distanceBetweenWorldPoints(previousPoint, state.toolDraft.previewPoint) <= WORLD_GEOMETRY_TOLERANCE) {
      return false;
    }

    state.toolDraft = {
      points: nextPoints,
      previewPoint: null,
      dimensionValues: {}
    };
    setHoveredTarget(state, null);
    return true;
  }

  if (nextPoints.length >= getSketchToolPointCount(tool)) {
    return false;
  }

  const previousPoint = state.toolDraft.points[state.toolDraft.points.length - 1];
  if (distanceBetweenWorldPoints(previousPoint, state.toolDraft.previewPoint) <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

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
  state.powerTrimDrag = null;
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
  if (state.panning || state.geometryDrag || state.constraintGroupDrag || state.powerTrimDrag) {
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
  state.powerTrimDrag = null;
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
  if (adjustPolygonSideCount(state, normalizedDelta)) {
    updateDebugAttributes(state);
    draw(state);
    return;
  }

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

  const sketchTool = getSketchCreationTool(state);
  if (isVariablePointSketchTool(sketchTool) && commitCurrentSketchTool(state, true)) {
    focusCanvasWithoutDimensionCommit(state);
    updateDebugAttributes(state);
    draw(state);
    event.preventDefault();
    return;
  }

  if (sketchTool) {
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
  const view = getFitViewForDocument(state.document, size);

  state.view.scale = view.scale;
  state.view.offsetX = view.offsetX;
  state.view.offsetY = view.offsetY;
  updateDebugAttributes(state);
}

export function getFitViewForDocument(document, size) {
  const width = Math.max(1, Number(size && size.width) || 1);
  const height = Math.max(1, Number(size && size.height) || 1);
  const bounds = getDocumentBounds(document);

  if (!bounds) {
    return {
      scale: DEFAULT_BLANK_VIEW_SCALE,
      offsetX: width / 2,
      offsetY: height / 2
    };
  }

  const worldWidth = Math.max(bounds.maxX - bounds.minX, 1);
  const worldHeight = Math.max(bounds.maxY - bounds.minY, 1);
  const availableWidth = Math.max(1, width - DEFAULT_FIT_MARGIN * 2);
  const availableHeight = Math.max(1, height - DEFAULT_FIT_MARGIN * 2);
  const scale = clamp(
    Math.min(availableWidth / worldWidth, availableHeight / worldHeight),
    MIN_VIEW_SCALE,
    MAX_VIEW_SCALE);
  const centerX = (bounds.minX + bounds.maxX) / 2;
  const centerY = (bounds.minY + bounds.maxY) / 2;

  return {
    scale,
    offsetX: width / 2 - centerX * scale,
    offsetY: height / 2 + centerY * scale
  };
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
  const constraintTool = getConstraintTool(state);
  const { nearestPointHit, nearestEdgeHit } = getNearestEntityHits(state, screenPoint, constraintTool);

  if (nearestPointHit && nearestPointHit.distance <= SNAP_POINT_TOLERANCE) {
    return nearestPointHit.target;
  }

  if (constraintTool) {
    return nearestEdgeHit && nearestEdgeHit.distance <= HIT_TEST_TOLERANCE
      ? nearestEdgeHit.target
      : null;
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

function findNearestPowerTrimTarget(state, screenPoint) {
  const { nearestPointHit, nearestEdgeHit, edgeHits } = getNearestEntityHits(state, screenPoint, null, true);
  const rawPoint = screenToWorld(state, screenPoint);
  const alternateEdgeHit = getPowerTrimAlternateEdgeHit(nearestPointHit, edgeHits);
  if (alternateEdgeHit) {
    return alternateEdgeHit.target;
  }

  if (nearestEdgeHit && nearestEdgeHit.distance <= HIT_TEST_TOLERANCE) {
    return nearestEdgeHit.target;
  }

  if (nearestEdgeHit
    && nearestEdgeHit.distance <= POWER_TRIM_EXTEND_HIT_TEST_TOLERANCE
    && isPowerTrimExtendPick(nearestEdgeHit.target, rawPoint)) {
    return nearestEdgeHit.target;
  }

  return nearestPointHit && nearestPointHit.distance <= SNAP_POINT_TOLERANCE
    ? nearestPointHit.target
    : null;
}

function getPowerTrimAlternateEdgeHit(pointHit, edgeHits) {
  if (!pointHit
    || pointHit.distance > SNAP_POINT_TOLERANCE
    || !Array.isArray(edgeHits)
    || edgeHits.length === 0) {
    return null;
  }

  const pointEntityKind = getEntityKind(pointHit.target && pointHit.target.entity);
  if (pointEntityKind !== "line" && pointEntityKind !== "polyline" && pointEntityKind !== "polygon") {
    return null;
  }

  return edgeHits
    .filter(hit => hit
      && hit.distance <= HIT_TEST_TOLERANCE
      && hit.target
      && hit.target.entityId !== pointHit.target.entityId)
    .sort((first, second) => first.distance - second.distance)[0] || null;
}

function getNearestEntityHits(state, screenPoint, constraintTool = null, includeEdgeHits = false) {
  let nearestPointHit = null;
  let nearestEdgeHit = null;
  const edgeHits = includeEdgeHits ? [] : null;

  for (const entity of getDocumentEntities(state.document)) {
    const pointHit = getEntityPointHit(state, entity, screenPoint);
    if (pointHit
      && (!constraintTool || isTargetEligibleForConstraintTool(constraintTool, pointHit.target))
      && (!nearestPointHit || pointHit.distance < nearestPointHit.distance)) {
      nearestPointHit = pointHit;
    }

    const edgeHit = getEntityEdgeHit(state, entity, screenPoint);
    if (edgeHit
      && (!constraintTool || isTargetEligibleForConstraintTool(constraintTool, edgeHit.target))
      && (!nearestEdgeHit || edgeHit.distance < nearestEdgeHit.distance)) {
      nearestEdgeHit = edgeHit;
    }

    if (edgeHits && edgeHit && (!constraintTool || isTargetEligibleForConstraintTool(constraintTool, edgeHit.target))) {
      edgeHits.push(edgeHit);
    }
  }

  return { nearestPointHit, nearestEdgeHit, edgeHits };
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

function findNearestSelectedSplineTarget(state, screenPoint) {
  return findNearestSplineTarget(state, screenPoint, true);
}

function findNearestSplineTarget(state, screenPoint, selectedOnly = false) {
  let nearestHit = null;
  for (const entity of getDocumentEntities(state.document)) {
    const id = getEntityId(entity);
    if (!id || getEntityKind(entity) !== "spline" || getEntityFitPoints(entity).length < 2) {
      continue;
    }

    if (selectedOnly && !state.selectedKeys?.has(id)) {
      continue;
    }

    const hit = getSampledCurveEdgeHit(state, entity, screenPoint);
    if (hit && (!nearestHit || hit.distance < nearestHit.distance)) {
      nearestHit = hit;
    }
  }

  return nearestHit && nearestHit.distance <= HIT_TEST_TOLERANCE
    ? nearestHit.target
    : null;
}

export function getAddSplinePointRequest(state, screenPoint, selectedOnly = false) {
  const target = findNearestSplineTarget(state, screenPoint, selectedOnly);
  const point = getAddSplinePointPickPoint(target);
  return target && point
    ? { targetKey: target.key, target, point }
    : null;
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
    const edgeOverride = getArcRadialDimensionEdgeOverride(state, geometry);
    const screenGeometry = getRadialDimensionScreenGeometry(center, radius, anchor, geometry.diameter, textWidth, edgeOverride);
    return getScreenSegmentsDistance(screenPoint, screenGeometry.segments);
  }

  if (geometry.type === "arcangle") {
    const angleGeometry = getArcAngleDimensionScreenGeometry(
      state,
      geometry.center,
      geometry.radius,
      geometry.startAngleDegrees,
      geometry.endAngleDegrees,
      geometry.anchorPoint);
    if (!angleGeometry) {
      return Number.POSITIVE_INFINITY;
    }

    const extensionDistance = getScreenSegmentsDistance(screenPoint, angleGeometry.extensionSegments);
    const radialDistance = Math.abs(distanceBetweenScreenPoints(screenPoint, angleGeometry.vertex) - angleGeometry.radius);
    return Math.min(extensionDistance, radialDistance);
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
      || (priority > nearestPriority
        && distance <= PRIORITY_POINT_HIT_TOLERANCE
        && nearestHit.distance <= SNAP_POINT_TOLERANCE
        && nearestHit.distance > STRONG_POINT_HIT_TOLERANCE)
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
    case "polygon":
      return getPolygonEdgeHit(state, entity, screenPoint);
    case "spline":
      return getSampledCurveEdgeHit(state, entity, screenPoint);
    case "circle":
      return getCircleEdgeHit(state, entity, screenPoint);
    case "arc":
      return getArcEdgeHit(state, entity, screenPoint);
    case "ellipse":
      return getEllipseEdgeHit(state, entity, screenPoint);
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
  pruneExpiredAcquiredSnapPoints(state);
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
      touchAcquiredSnapPoint(acquired);
      candidates.push({
        label: `project-v-${acquired.label}`,
        point: verticalPoint,
        guides: [{ orientation: "vertical", point: acquired.point }],
        priority: 2
      });
    }

    const horizontalPoint = { x: worldPoint.x, y: acquired.point.y };
    if (isWorldPointWithinScreenDistance(state, acquired.point, horizontalPoint)) {
      touchAcquiredSnapPoint(acquired);
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

  touchAcquiredSnapPoint(first);
  touchAcquiredSnapPoint(second);
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

  touchAcquiredSnapPoint(verticalSource);
  touchAcquiredSnapPoint(horizontalSource);
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

      touchAcquiredSnapPoint(acquired);
      candidates.push({
        label: `project-${snap.orientation}-${acquired.label}-${entityId}-${snap.index}`,
        point: snap.point,
        guides: [{ orientation: snap.orientation === "vertical" ? "vertical" : "horizontal", point: acquired.point }],
        priority: 7
      });
    }
  }
}

function touchAcquiredSnapPoint(acquired) {
  if (acquired) {
    acquired.acquiredAt = getInteractionTimestamp();
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

  return getSampledWorldPointsEdgeHit(state, target, points, screenPoint);
}

function getEllipseEdgeHit(state, entity, screenPoint) {
  const target = createEntityTarget(entity);
  const points = getEllipseWorldPointsFromEntity(entity);
  if (!target || points.length < 2) {
    return null;
  }

  return getSampledWorldPointsEdgeHit(state, target, points, screenPoint);
}

function getSampledWorldPointsEdgeHit(state, target, points, screenPoint) {
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

function getPolygonEdgeHit(state, entity, screenPoint) {
  const target = createEntityTarget(entity);
  const points = getEntityPoints(entity);
  if (!target || points.length < 3) {
    return null;
  }

  let closestDistance = Number.POSITIVE_INFINITY;
  let closestProjectionPoint = null;

  for (let index = 0; index < points.length; index += 1) {
    const projection = closestPointOnScreenSegment(
      screenPoint,
      worldToScreen(state, points[index]),
      worldToScreen(state, points[(index + 1) % points.length]));
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

  if ((getEntityKind(entity) === "circle" || getEntityKind(entity) === "polygon") && points.length > 2) {
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

  if (kind === "line" || kind === "polyline" || kind === "spline" || kind === "polygon") {
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

  if (kind === "ellipse") {
    return getEllipseWorldPointsFromEntity(entity).map(point => worldToScreen(state, point));
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
    case "polygon":
      return getPolygonSnapPoints(entity, entities);
    case "spline":
      return getSplineSnapPoints(entity, entities);
    case "circle":
      return getCircleSnapPoints(entity, entities);
    case "arc":
      return getArcSnapPoints(entity, entities);
    case "ellipse":
      return getEllipseSnapPoints(entity, entities);
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

function getPolygonSnapPoints(entity, entities) {
  const points = getEntityPoints(entity);
  if (points.length < 3) {
    return [];
  }

  const snapPoints = [];
  const center = getEntityCenter(entity);
  if (center) {
    snapPoints.push({ label: "center", point: center, priority: 8 });
  }

  for (let index = 0; index < points.length; index += 1) {
    snapPoints.push({ label: `vertex-${index}`, point: points[index] });
  }

  for (let index = 0; index < points.length; index += 1) {
    snapPoints.push({ label: `mid-${index}`, point: midpoint(points[index], points[(index + 1) % points.length]) });
  }

  addIntersectionSnapPoints(snapPoints, entity, entities);
  addTangentSnapPointsForLinearEntity(snapPoints, entity, getLinearSegments(entity), entities);
  return snapPoints;
}

function getSplineSnapPoints(entity, entities) {
  const points = getEntityPoints(entity);
  const snapPoints = [
    ...getSplineEditableSnapPoints(entity),
    ...getSplineTangentHandleSnapPoints(entity)
  ];

  if (points.length < 2) {
    addIntersectionSnapPoints(snapPoints, entity, entities);
    return snapPoints;
  }

  const defaultSnapPoints = [
    { label: "start", point: points[0] },
    { label: "end", point: points[points.length - 1] },
    { label: "mid", point: points[Math.floor(points.length / 2)] }
  ];

  for (const point of defaultSnapPoints) {
    addUniqueSnapPoint(snapPoints, point.label, point.point, 0);
  }

  addIntersectionSnapPoints(snapPoints, entity, entities);
  return snapPoints;
}

function getSplineEditableSnapPoints(entity) {
  const fitPoints = getEntityFitPoints(entity);
  if (fitPoints.length >= 2) {
    return fitPoints.map((point, index) => ({
      label: `fit-${index}`,
      point,
      priority: 30
    }));
  }

  const controlPoints = getEntityControlPoints(entity);
  if (controlPoints.length >= 2) {
    return controlPoints.map((point, index) => ({
      label: `control-${index}`,
      point,
      priority: 30
    }));
  }

  return [];
}

function getSplineTangentHandleSnapPoints(entity) {
  const handles = getPersistentSplineTangentHandlesForEntity(entity);
  const snapPoints = [];
  if (handles[0]?.forward) {
    snapPoints.push({
      label: "tangent-start",
      point: handles[0].forward,
      priority: 40
    });
  }

  if (handles[1]?.backward) {
    snapPoints.push({
      label: "tangent-end",
      point: handles[1].backward,
      priority: 40
    });
  }

  return snapPoints;
}

function getEditableSplineHandleSourcePoints(entity) {
  const fitPoints = getEntityFitPoints(entity);
  if (fitPoints.length >= 2) {
    return fitPoints;
  }

  return getEntityControlPoints(entity);
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

function getEllipseSnapPoints(entity, entities) {
  const ellipse = getEllipseFromEntity(entity);
  if (!ellipse) {
    return [];
  }

  const startParameter = getEntityStartAngle(entity);
  const endParameter = getEntityEndAngle(entity);
  const sweep = getPositiveSweepDegrees(startParameter, endParameter);
  const snapPoints = [
    { label: "center", point: ellipse.center },
    { label: "start", point: getEllipsePointAtParameter(ellipse, startParameter) },
    { label: "end", point: getEllipsePointAtParameter(ellipse, endParameter) },
    { label: "mid", point: getEllipsePointAtParameter(ellipse, startParameter + sweep / 2) }
  ];

  for (const parameter of [0, 90, 180, 270]) {
    if (isAngleOnArc(parameter, startParameter, endParameter)) {
      snapPoints.push({
        label: `quadrant-${parameter}`,
        point: getEllipsePointAtParameter(ellipse, parameter)
      });
    }
  }

  addIntersectionSnapPoints(snapPoints, entity, entities);
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

  if (kind === "polygon" && points.length >= 3) {
    const segments = [];
    for (let index = 0; index < points.length; index += 1) {
      segments.push({
        start: points[index],
        end: points[(index + 1) % points.length],
        segmentIndex: index
      });
    }

    return segments;
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

  if (kind === "line" || kind === "polyline" || kind === "spline" || kind === "polygon") {
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

function createConstraintTarget(constraint) {
  const id = getSketchItemId(constraint);
  if (!id) {
    return null;
  }

  return {
    kind: "constraint",
    key: `${CONSTRAINT_KEY_PREFIX}${id}`,
    constraintId: id,
    constraint
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

  const constraintTarget = parseConstraintTargetKey(state, key);
  if (constraintTarget) {
    return constraintTarget;
  }

  const entity = getDocumentEntities(state.document)
    .find(candidate => StringComparer(getEntityId(candidate), key));
  return entity ? createEntityTarget(entity) : null;
}

function parseConstraintTargetKey(state, key) {
  if (!String(key || "").startsWith(CONSTRAINT_KEY_PREFIX)) {
    return null;
  }

  const constraintId = key.slice(CONSTRAINT_KEY_PREFIX.length);
  const constraint = getDocumentConstraints(state.document)
    .find(candidate => StringComparer(getSketchItemId(candidate), constraintId));
  return constraint ? createConstraintTarget(constraint) : null;
}

function parsePointTargetKey(state, key) {
  const parts = parsePointTargetKeyParts(key);
  if (!parts) {
    return null;
  }

  const entity = getDocumentEntities(state.document)
    .find(candidate => StringComparer(getEntityId(candidate), parts.entityId));
  if (!entity) {
    return null;
  }

  return {
    kind: "point",
    key,
    entityId: parts.entityId,
    entity,
    label: parts.label,
    point: resolveCurrentPointTargetPoint(entity, parts.label, parts.point)
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

  if (kind === "polygon" && points.length >= 3) {
    const center = getEntityCenter(entity);
    if (normalizedLabel === "center" && center) {
      return center;
    }

    const vertexIndex = parseIndexedPointLabel(normalizedLabel, "vertex-");
    if (vertexIndex !== null && vertexIndex >= 0 && vertexIndex < points.length) {
      return points[vertexIndex];
    }

    const segmentIndex = parseIndexedPointLabel(normalizedLabel, "mid-");
    if (segmentIndex !== null && segmentIndex >= 0 && segmentIndex < points.length) {
      return midpoint(points[segmentIndex], points[(segmentIndex + 1) % points.length]);
    }
  }

  if (kind === "spline") {
    const fitPoints = getEntityFitPoints(entity);
    if (fitPoints.length >= 2) {
      const fitIndex = parseIndexedPointLabel(normalizedLabel, "fit-");
      if (fitIndex !== null && fitIndex >= 0 && fitIndex < fitPoints.length) {
        return fitPoints[fitIndex];
      }

      if (normalizedLabel === "start") {
        return fitPoints[0];
      }

      if (normalizedLabel === "end") {
        return fitPoints[fitPoints.length - 1];
      }

      if (normalizedLabel === "tangent-start") {
        return getEntityStartTangentHandle(entity)
          || getPersistentSplineTangentHandlesForEntity(entity)[0]?.forward
          || fallbackPoint;
      }

      if (normalizedLabel === "tangent-end") {
        return getEntityEndTangentHandle(entity)
          || getPersistentSplineTangentHandlesForEntity(entity)[1]?.backward
          || fallbackPoint;
      }
    }

    const controlPoints = getEntityControlPoints(entity);
    const controlIndex = parseIndexedPointLabel(normalizedLabel, "control-");
    if (controlIndex !== null && controlIndex >= 0 && controlIndex < controlPoints.length) {
      return controlPoints[controlIndex];
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
  const translatedRectangleEntityIds = applyDimensionedRectanglePreviewTranslation(previewDocument, selectionKey, delta);
  const partiallyDimensionedRectangleHandled = !translatedRectangleEntityIds
    ? applyPartiallyDimensionedRectanglePreviewDrag(previewDocument, selectionKey, delta)
    : false;
  const resizedRectangleCorner = !translatedRectangleEntityIds
    && !partiallyDimensionedRectangleHandled
    ? applyUndimensionedRectanglePreviewCornerResize(previewDocument, selectionKey, delta)
    : false;
  const resizedRectangleEdge = !translatedRectangleEntityIds
    && !partiallyDimensionedRectangleHandled
    && !resizedRectangleCorner
    ? applyUndimensionedRectanglePreviewEdgeResize(previewDocument, selectionKey, delta)
    : false;
  const genericDragHandled = !translatedRectangleEntityIds
    && !partiallyDimensionedRectangleHandled
    && !resizedRectangleCorner
    && !resizedRectangleEdge
    ? applyDimensionAwareGeometryDragPreviewMutation(
      previewDocument,
      document,
      selectionKey,
      delta,
      dragEnd,
      constrainToCurrentVector)
    : false;
  if (!translatedRectangleEntityIds
    && !partiallyDimensionedRectangleHandled
    && !resizedRectangleCorner
    && !resizedRectangleEdge
    && !genericDragHandled) {
    return null;
  }

  if (translatedRectangleEntityIds) {
    translatePreviewDimensionAnchorsForEntityIds(previewDocument, translatedRectangleEntityIds, delta);
  } else if (!partiallyDimensionedRectangleHandled && !resizedRectangleCorner && !resizedRectangleEdge) {
    translatePreviewDimensionAnchorsForDrag(previewDocument, selectionKey, delta);
  }

  propagatePreviewCoincidentConstraints(document, previewDocument);
  previewDocument.bounds = computeEntityBounds(getDocumentEntities(previewDocument));
  return previewDocument;
}

function applyDimensionAwareGeometryDragPreviewMutation(
  previewDocument,
  originalDocument,
  selectionKey,
  delta,
  dragEnd,
  constrainToCurrentVector = false) {
  const editCandidate = cloneCanvasDocument(originalDocument);
  if (!applyGeometryDragPreviewMutation(editCandidate, selectionKey, delta, dragEnd, constrainToCurrentVector)) {
    return false;
  }

  const affectedEntityIds = new Set([getReferenceEntityId(selectionKey)].filter(id => id));
  if (previewDrivingDimensionsRemainSatisfiedForEntity(editCandidate, affectedEntityIds)) {
    copyPreviewDocumentGeometry(previewDocument, editCandidate);
    return true;
  }

  const translatedEntityId = getSelectedPreviewEntityId(originalDocument, selectionKey);
  if (!translatedEntityId) {
    return false;
  }

  const translationCandidate = cloneCanvasDocument(originalDocument);
  if (!translatePreviewEntityById(translationCandidate, translatedEntityId, delta)) {
    return false;
  }

  translatePreviewDimensionAnchorsForEntityIds(
    translationCandidate,
    new Set([translatedEntityId]),
    delta);
  if (!previewDrivingDimensionsRemainSatisfiedForEntity(translationCandidate, new Set([translatedEntityId]))) {
    return false;
  }

  copyPreviewDocumentGeometry(previewDocument, translationCandidate);
  return true;
}

function applyDimensionedRectanglePreviewTranslation(document, selectionKey, delta) {
  const selectedEntityId = getSelectedPreviewLineEntityId(document, selectionKey);
  if (!selectedEntityId) {
    return null;
  }

  const rectangleEntityIds = getDimensionedRectanglePreviewEntityIds(document, selectedEntityId);
  if (!rectangleEntityIds) {
    return null;
  }

  for (const entity of getDocumentEntities(document)) {
    if (getEntityKind(entity) === "line" && rectangleEntityIds.has(getEntityId(entity))) {
      entity.points = getEntityPoints(entity).map(point => addWorldPoints(point, delta));
    }
  }

  return rectangleEntityIds;
}

function applyPartiallyDimensionedRectanglePreviewDrag(document, selectionKey, delta) {
  const selectedEntityId = getSelectedPreviewLineEntityId(document, selectionKey);
  if (!selectedEntityId) {
    return false;
  }

  const rectangleEntityIds = getRectanglePreviewEntityIds(document, selectedEntityId);
  if (!rectangleEntityIds) {
    return false;
  }

  const drivingDimensionCount = countDrivingPreviewDimensionsForEntityGroup(
    getDocumentDimensions(document),
    rectangleEntityIds);
  if (drivingDimensionCount <= 0 || drivingDimensionCount >= 2) {
    return false;
  }

  const selectedEntity = findCanvasDocumentEntity(document, selectedEntityId);
  const selectedPoints = getEntityPoints(selectedEntity);
  if (selectedPoints.length < 2) {
    return false;
  }

  if (!getSelectedPreviewRectangleEdgeEntityId(document, selectionKey)) {
    const corner = getSelectedPreviewRectangleCorner(document, selectionKey);
    if (corner) {
      return applyPartiallyDimensionedRectanglePreviewCornerDrag(
        document,
        rectangleEntityIds,
        corner.point,
        delta);
    }

    translatePreviewRectangleEntities(document, rectangleEntityIds, delta);
    translatePreviewDimensionAnchorsForEntityIds(document, rectangleEntityIds, delta);
    return true;
  }

  const parallelDelta = projectWorldDeltaOntoLine(delta, selectedPoints[0], selectedPoints[1]);
  const perpendicularDelta = subtractPoints(delta, parallelDelta);
  const parallelCandidate = cloneCanvasDocument(document);
  translatePreviewRectangleEntities(parallelCandidate, rectangleEntityIds, parallelDelta);
  translatePreviewDimensionAnchorsForEntityIds(parallelCandidate, rectangleEntityIds, parallelDelta);

  if (Math.hypot(perpendicularDelta.x, perpendicularDelta.y) > WORLD_GEOMETRY_TOLERANCE) {
    const resizeCandidate = cloneCanvasDocument(parallelCandidate);
    const translatedSelectedPoints = selectedPoints.map(point => addWorldPoints(point, parallelDelta));
    resizePreviewRectangleEdgeEntities(
      resizeCandidate,
      rectangleEntityIds,
      translatedSelectedPoints,
      perpendicularDelta);
    translatePreviewDimensionAnchorsForEntityIds(
      resizeCandidate,
      new Set([selectedEntityId]),
      perpendicularDelta);
    if (previewDrivingDimensionsRemainSatisfied(resizeCandidate)) {
      copyPreviewDocumentGeometry(document, resizeCandidate);
      return true;
    }

    translatePreviewRectangleEntities(parallelCandidate, rectangleEntityIds, perpendicularDelta);
    translatePreviewDimensionAnchorsForEntityIds(parallelCandidate, rectangleEntityIds, perpendicularDelta);
  }

  copyPreviewDocumentGeometry(document, parallelCandidate);
  return true;
}

function applyPartiallyDimensionedRectanglePreviewCornerDrag(document, rectangleEntityIds, cornerPoint, delta) {
  const axes = getPreviewRectangleCornerAxes(document, rectangleEntityIds, cornerPoint);
  if (!axes) {
    return false;
  }

  const components = [
    projectWorldDeltaOntoAxis(delta, axes.firstAxis),
    projectWorldDeltaOntoAxis(delta, axes.secondAxis)
  ];
  let workingDocument = cloneCanvasDocument(document);
  let currentCorner = { ...cornerPoint };
  for (const component of components) {
    if (Math.hypot(component.x, component.y) <= WORLD_GEOMETRY_TOLERANCE) {
      continue;
    }

    const resizeCandidate = cloneCanvasDocument(workingDocument);
    const resizedWholeEntityIds = resizePreviewRectangleCornerEntities(
      resizeCandidate,
      rectangleEntityIds,
      currentCorner,
      component);
    if (resizedWholeEntityIds) {
      translatePreviewDimensionAnchorsForEntityIds(resizeCandidate, resizedWholeEntityIds, component);
      if (previewDrivingDimensionsRemainSatisfied(resizeCandidate)) {
        workingDocument = resizeCandidate;
        currentCorner = addWorldPoints(currentCorner, component);
        continue;
      }
    }

    translatePreviewRectangleEntities(workingDocument, rectangleEntityIds, component);
    translatePreviewDimensionAnchorsForEntityIds(workingDocument, rectangleEntityIds, component);
    currentCorner = addWorldPoints(currentCorner, component);
  }

  copyPreviewDocumentGeometry(document, workingDocument);
  return true;
}

function applyUndimensionedRectanglePreviewCornerResize(document, selectionKey, delta) {
  const corner = getSelectedPreviewRectangleCorner(document, selectionKey);
  if (!corner) {
    return false;
  }

  const rectangleEntityIds = getRectanglePreviewEntityIds(document, corner.entityId);
  if (!rectangleEntityIds
    || countDrivingPreviewDimensionsForEntityGroup(getDocumentDimensions(document), rectangleEntityIds) > 0) {
    return false;
  }

  const adjacentPoints = getPreviewRectangleAdjacentCornerPoints(document, rectangleEntityIds, corner.point);
  if (adjacentPoints.length !== 2) {
    return false;
  }

  const opposite = {
    x: adjacentPoints[0].x + adjacentPoints[1].x - corner.point.x,
    y: adjacentPoints[0].y + adjacentPoints[1].y - corner.point.y
  };
  const firstAxis = normalizeWorldVector(subtractPoints(adjacentPoints[0], opposite));
  const secondAxis = normalizeWorldVector(subtractPoints(adjacentPoints[1], opposite));
  if (!firstAxis || !secondAxis) {
    return false;
  }

  const movedCorner = addWorldPoints(corner.point, delta);
  const movedOffset = subtractPoints(movedCorner, opposite);
  const firstLength = dotPoints(movedOffset, firstAxis);
  const secondLength = dotPoints(movedOffset, secondAxis);
  const nextFirstAdjacent = {
    x: opposite.x + firstAxis.x * firstLength,
    y: opposite.y + firstAxis.y * firstLength
  };
  const nextSecondAdjacent = {
    x: opposite.x + secondAxis.x * secondLength,
    y: opposite.y + secondAxis.y * secondLength
  };
  const nextCorner = {
    x: nextFirstAdjacent.x + nextSecondAdjacent.x - opposite.x,
    y: nextFirstAdjacent.y + nextSecondAdjacent.y - opposite.y
  };

  for (const entity of getDocumentEntities(document)) {
    if (getEntityKind(entity) !== "line" || !rectangleEntityIds.has(getEntityId(entity))) {
      continue;
    }

    entity.points = getEntityPoints(entity).map(point => {
      if (distanceBetweenWorldPoints(point, corner.point) <= WORLD_GEOMETRY_TOLERANCE) {
        return { ...nextCorner };
      }

      if (distanceBetweenWorldPoints(point, adjacentPoints[0]) <= WORLD_GEOMETRY_TOLERANCE) {
        return { ...nextFirstAdjacent };
      }

      if (distanceBetweenWorldPoints(point, adjacentPoints[1]) <= WORLD_GEOMETRY_TOLERANCE) {
        return { ...nextSecondAdjacent };
      }

      return point;
    });
  }

  return true;
}

function getSelectedPreviewRectangleCorner(document, selectionKey) {
  const pointReference = parsePointSelectionKey(selectionKey);
  if (!pointReference) {
    return null;
  }

  const label = String(pointReference.label || "").split("|")[0].toLowerCase();
  if (label !== "start" && label !== "end") {
    return null;
  }

  const entity = findCanvasDocumentEntity(document, pointReference.entityId);
  if (getEntityKind(entity) !== "line") {
    return null;
  }

  const points = getEntityPoints(entity);
  if (points.length < 2) {
    return null;
  }

  return {
    entityId: pointReference.entityId,
    point: label === "start" ? points[0] : points[1]
  };
}

function getPreviewRectangleAdjacentCornerPoints(document, rectangleEntityIds, cornerPoint) {
  const adjacent = [];
  for (const entity of getDocumentEntities(document)) {
    if (getEntityKind(entity) !== "line" || !rectangleEntityIds.has(getEntityId(entity))) {
      continue;
    }

    const points = getEntityPoints(entity);
    if (points.length < 2) {
      continue;
    }

    if (distanceBetweenWorldPoints(points[0], cornerPoint) <= WORLD_GEOMETRY_TOLERANCE) {
      addUniquePreviewPoint(adjacent, points[1]);
    } else if (distanceBetweenWorldPoints(points[1], cornerPoint) <= WORLD_GEOMETRY_TOLERANCE) {
      addUniquePreviewPoint(adjacent, points[0]);
    }
  }

  return adjacent;
}

function addUniquePreviewPoint(points, point) {
  if (!points.some(existing => distanceBetweenWorldPoints(existing, point) <= WORLD_GEOMETRY_TOLERANCE)) {
    points.push(point);
  }
}

function applyUndimensionedRectanglePreviewEdgeResize(document, selectionKey, delta) {
  const selectedEntityId = getSelectedPreviewRectangleEdgeEntityId(document, selectionKey);
  if (!selectedEntityId) {
    return false;
  }

  const rectangleEntityIds = getRectanglePreviewEntityIds(document, selectedEntityId);
  if (!rectangleEntityIds
    || countDrivingPreviewDimensionsForEntityGroup(getDocumentDimensions(document), rectangleEntityIds) > 0) {
    return false;
  }

  const selectedEntity = findCanvasDocumentEntity(document, selectedEntityId);
  const selectedPoints = getEntityPoints(selectedEntity);
  if (selectedPoints.length < 2) {
    return false;
  }

  const resizeDelta = projectWorldDeltaPerpendicularToLine(delta, selectedPoints[0], selectedPoints[1]);
  if (Math.hypot(resizeDelta.x, resizeDelta.y) <= WORLD_GEOMETRY_TOLERANCE) {
    return true;
  }

  for (const entity of getDocumentEntities(document)) {
    if (getEntityKind(entity) !== "line" || !rectangleEntityIds.has(getEntityId(entity))) {
      continue;
    }

    entity.points = getEntityPoints(entity).map(point =>
      shouldMovePreviewRectangleEndpoint(point, selectedPoints)
        ? addWorldPoints(point, resizeDelta)
        : point);
  }

  return true;
}

function getPreviewRectangleCornerAxes(document, rectangleEntityIds, cornerPoint) {
  const geometry = getPreviewRectangleCornerGeometry(document, rectangleEntityIds, cornerPoint);
  return geometry
    ? {
      firstAxis: geometry.firstAxis,
      secondAxis: geometry.secondAxis
    }
    : null;
}

function getPreviewRectangleCornerGeometry(document, rectangleEntityIds, cornerPoint) {
  const adjacentPoints = getPreviewRectangleAdjacentCornerPoints(document, rectangleEntityIds, cornerPoint);
  if (adjacentPoints.length !== 2) {
    return null;
  }

  const opposite = {
    x: adjacentPoints[0].x + adjacentPoints[1].x - cornerPoint.x,
    y: adjacentPoints[0].y + adjacentPoints[1].y - cornerPoint.y
  };
  const firstAxis = normalizeWorldVector(subtractPoints(adjacentPoints[0], opposite));
  const secondAxis = normalizeWorldVector(subtractPoints(adjacentPoints[1], opposite));
  if (!firstAxis || !secondAxis) {
    return null;
  }

  return {
    firstAdjacent: adjacentPoints[0],
    secondAdjacent: adjacentPoints[1],
    opposite,
    firstAxis,
    secondAxis
  };
}

function resizePreviewRectangleCornerEntities(document, rectangleEntityIds, cornerPoint, delta) {
  const geometry = getPreviewRectangleCornerGeometry(document, rectangleEntityIds, cornerPoint);
  if (!geometry) {
    return null;
  }

  const movedCorner = addWorldPoints(cornerPoint, delta);
  const movedOffset = subtractPoints(movedCorner, geometry.opposite);
  const firstLength = dotPoints(movedOffset, geometry.firstAxis);
  const secondLength = dotPoints(movedOffset, geometry.secondAxis);
  const nextFirstAdjacent = {
    x: geometry.opposite.x + geometry.firstAxis.x * firstLength,
    y: geometry.opposite.y + geometry.firstAxis.y * firstLength
  };
  const nextSecondAdjacent = {
    x: geometry.opposite.x + geometry.secondAxis.x * secondLength,
    y: geometry.opposite.y + geometry.secondAxis.y * secondLength
  };
  const nextCorner = {
    x: nextFirstAdjacent.x + nextSecondAdjacent.x - geometry.opposite.x,
    y: nextFirstAdjacent.y + nextSecondAdjacent.y - geometry.opposite.y
  };
  const resizedWholeEntityIds = new Set();

  for (const entity of getDocumentEntities(document)) {
    if (getEntityKind(entity) !== "line" || !rectangleEntityIds.has(getEntityId(entity))) {
      continue;
    }

    const points = getEntityPoints(entity);
    if (points.length < 2) {
      continue;
    }

    const nextPoints = points.map(point => getNextPreviewRectangleCornerPoint(
      point,
      cornerPoint,
      geometry.firstAdjacent,
      geometry.secondAdjacent,
      nextCorner,
      nextFirstAdjacent,
      nextSecondAdjacent));
    const startDelta = subtractPoints(nextPoints[0], points[0]);
    const endDelta = subtractPoints(nextPoints[1], points[1]);
    if (distanceBetweenWorldPoints(startDelta, delta) <= WORLD_GEOMETRY_TOLERANCE
      && distanceBetweenWorldPoints(endDelta, delta) <= WORLD_GEOMETRY_TOLERANCE) {
      resizedWholeEntityIds.add(getEntityId(entity));
    }

    entity.points = nextPoints;
  }

  return resizedWholeEntityIds;
}

function getNextPreviewRectangleCornerPoint(
  point,
  cornerPoint,
  firstAdjacent,
  secondAdjacent,
  nextCorner,
  nextFirstAdjacent,
  nextSecondAdjacent) {
  if (distanceBetweenWorldPoints(point, cornerPoint) <= WORLD_GEOMETRY_TOLERANCE) {
    return { ...nextCorner };
  }

  if (distanceBetweenWorldPoints(point, firstAdjacent) <= WORLD_GEOMETRY_TOLERANCE) {
    return { ...nextFirstAdjacent };
  }

  return distanceBetweenWorldPoints(point, secondAdjacent) <= WORLD_GEOMETRY_TOLERANCE
    ? { ...nextSecondAdjacent }
    : point;
}

function translatePreviewRectangleEntities(document, rectangleEntityIds, delta) {
  if (Math.hypot(delta.x, delta.y) <= WORLD_GEOMETRY_TOLERANCE) {
    return;
  }

  for (const entity of getDocumentEntities(document)) {
    if (getEntityKind(entity) === "line" && rectangleEntityIds.has(getEntityId(entity))) {
      entity.points = getEntityPoints(entity).map(point => addWorldPoints(point, delta));
    }
  }
}

function resizePreviewRectangleEdgeEntities(document, rectangleEntityIds, selectedPoints, delta) {
  for (const entity of getDocumentEntities(document)) {
    if (getEntityKind(entity) !== "line" || !rectangleEntityIds.has(getEntityId(entity))) {
      continue;
    }

    entity.points = getEntityPoints(entity).map(point =>
      shouldMovePreviewRectangleEndpoint(point, selectedPoints)
        ? addWorldPoints(point, delta)
        : point);
  }
}

function getSelectedPreviewRectangleEdgeEntityId(document, selectionKey) {
  const key = String(selectionKey || "");
  if (parseSegmentSelectionKey(key)) {
    return null;
  }

  const pointReference = parsePointSelectionKey(key);
  if (pointReference) {
    const label = String(pointReference.label || "").split("|")[0].toLowerCase();
    if (label !== "mid") {
      return null;
    }
  }

  const entityId = pointReference ? pointReference.entityId : key;
  const entity = findCanvasDocumentEntity(document, entityId);
  return getEntityKind(entity) === "line" ? entityId : null;
}

function getSelectedPreviewLineEntityId(document, selectionKey) {
  const key = String(selectionKey || "");
  if (parseSegmentSelectionKey(key)) {
    return null;
  }

  const pointReference = parsePointSelectionKey(key);
  const entityId = pointReference ? pointReference.entityId : key;
  const entity = findCanvasDocumentEntity(document, entityId);
  return getEntityKind(entity) === "line" ? entityId : null;
}

function getDimensionedRectanglePreviewEntityIds(document, selectedEntityId) {
  const rectangleEntityIds = getRectanglePreviewEntityIds(document, selectedEntityId);
  return rectangleEntityIds
    && countDrivingPreviewDimensionsForEntityGroup(getDocumentDimensions(document), rectangleEntityIds) >= 2
    ? rectangleEntityIds
    : null;
}

function getRectanglePreviewEntityIds(document, selectedEntityId) {
  const lineIds = new Set(getDocumentEntities(document)
    .filter(entity => getEntityKind(entity) === "line")
    .map(getEntityId)
    .filter(id => id));
  if (!lineIds.has(selectedEntityId)) {
    return null;
  }

  const adjacency = new Map(Array.from(lineIds, entityId => [entityId, new Set()]));
  for (const constraint of getDocumentConstraints(document)) {
    if (getSketchConstraintKind(constraint) !== "coincident" || getSketchItemState(constraint) === "suppressed") {
      continue;
    }

    const pair = getTwoSketchReferenceEntityIds(constraint);
    if (!pair || !lineIds.has(pair[0]) || !lineIds.has(pair[1]) || StringComparer(pair[0], pair[1])) {
      continue;
    }

    adjacency.get(pair[0]).add(pair[1]);
    adjacency.get(pair[1]).add(pair[0]);
  }

  const rectangleEntityIds = new Set([selectedEntityId]);
  const queue = [selectedEntityId];
  while (queue.length > 0) {
    const current = queue.shift();
    for (const next of adjacency.get(current) || []) {
      if (!rectangleEntityIds.has(next)) {
        rectangleEntityIds.add(next);
        queue.push(next);
      }
    }
  }

  return rectangleEntityIds.size === 4
    && hasRectanglePreviewLineRelations(getDocumentConstraints(document), rectangleEntityIds)
    ? rectangleEntityIds
    : null;
}

function hasRectanglePreviewLineRelations(constraints, entityIds) {
  let coincidentCount = 0;
  const parallelPairs = new Set();
  let perpendicularCount = 0;
  for (const constraint of constraints) {
    if (getSketchItemState(constraint) === "suppressed") {
      continue;
    }

    const kind = getSketchConstraintKind(constraint);
    const pair = getTwoSketchReferenceEntityIds(constraint);
    if (!pair || !entityIds.has(pair[0]) || !entityIds.has(pair[1]) || StringComparer(pair[0], pair[1])) {
      continue;
    }

    if (kind === "coincident") {
      coincidentCount += 1;
    } else if (kind === "parallel") {
      parallelPairs.add(pair[0] <= pair[1] ? `${pair[0]}|${pair[1]}` : `${pair[1]}|${pair[0]}`);
    } else if (kind === "perpendicular") {
      perpendicularCount += 1;
    }
  }

  return coincidentCount >= 4
    && parallelPairs.size >= 2
    && perpendicularCount >= 1;
}

function countDrivingPreviewDimensionsForEntityGroup(dimensions, entityIds) {
  return dimensions.filter(dimension => {
    const isDriving = readProperty(dimension, "isDriving", "IsDriving");
    const referenceKeys = getSketchReferenceKeys(dimension);
    return isDriving !== false
      && String(isDriving).toLowerCase() !== "false"
      && referenceKeys.length > 0
      && referenceKeys.every(key => entityIds.has(getReferenceEntityId(key)));
  }).length;
}

function previewDrivingDimensionsRemainSatisfied(document) {
  const state = { document };
  return getDocumentDimensions(document).every(dimension => {
    const isDriving = readProperty(dimension, "isDriving", "IsDriving");
    if (isDriving === false || String(isDriving).toLowerCase() === "false") {
      return true;
    }

    const expected = Number(readProperty(dimension, "value", "Value"));
    if (!Number.isFinite(expected)) {
      return true;
    }

    const model = getDimensionModelFromReferences(
      state,
      getSketchReferenceKeys(dimension),
      Boolean(readProperty(dimension, "radialDiameter", "RadialDiameter")));
    return model
      ? Math.abs(Math.abs(model.value) - Math.abs(expected)) <= WORLD_GEOMETRY_TOLERANCE
      : false;
  });
}

function previewDrivingDimensionsRemainSatisfiedForEntity(document, entityIds) {
  const state = { document };
  return getDocumentDimensions(document).every(dimension => {
    const isDriving = readProperty(dimension, "isDriving", "IsDriving");
    if (isDriving === false || String(isDriving).toLowerCase() === "false") {
      return true;
    }

    const referenceKeys = getSketchReferenceKeys(dimension);
    if (referenceKeys.length === 0
      || !referenceKeys.every(key => entityIds.has(getReferenceEntityId(key)))) {
      return true;
    }

    const expected = Number(readProperty(dimension, "value", "Value"));
    if (!Number.isFinite(expected)) {
      return true;
    }

    const dimensionKind = String(readProperty(dimension, "kind", "Kind") || "").toLowerCase();
    if (dimensionKind === "count") {
      const entity = findCanvasDocumentEntity(document, getReferenceEntityId(referenceKeys[0]));
      const sideCount = Number(readProperty(entity, "sideCount", "SideCount"));
      return Number.isFinite(sideCount)
        ? Math.round(sideCount) === Math.round(expected)
        : true;
    }

    const model = getDimensionModelFromReferences(
      state,
      referenceKeys,
      Boolean(readProperty(dimension, "radialDiameter", "RadialDiameter")));
    return model
      ? Math.abs(Math.abs(model.value) - Math.abs(expected)) <= WORLD_GEOMETRY_TOLERANCE
      : true;
  });
}

function copyPreviewDocumentGeometry(target, source) {
  target.entities = source.entities;
  target.Entities = source.entities;
  target.dimensions = source.dimensions;
  target.Dimensions = source.dimensions;
}

function getSketchConstraintKind(constraint) {
  const kind = readProperty(constraint, "kind", "Kind");
  return kind === null || kind === undefined ? "" : String(kind).toLowerCase();
}

function getTwoSketchReferenceEntityIds(item) {
  const referenceKeys = getSketchReferenceKeys(item);
  if (referenceKeys.length < 2) {
    return null;
  }

  const firstEntityId = getReferenceEntityId(referenceKeys[0]);
  const secondEntityId = getReferenceEntityId(referenceKeys[1]);
  return firstEntityId && secondEntityId ? [firstEntityId, secondEntityId] : null;
}

function propagatePreviewCoincidentConstraints(originalDocument, previewDocument) {
  const constraints = getDocumentConstraints(previewDocument)
    .filter(constraint => getSketchConstraintKind(constraint) === "coincident"
      && getSketchItemState(constraint) !== "suppressed");
  if (constraints.length === 0) {
    return;
  }

  const fixedReferences = getPreviewFixedReferenceKeys(previewDocument);
  const queue = getChangedPreviewPointReferences(originalDocument, previewDocument);
  const queued = new Set(queue.map(sketchReferenceToKey));
  const guardLimit = Math.max(128, constraints.length * 32);
  let guard = 0;
  while (queue.length > 0 && guard++ < guardLimit) {
    const changedReference = queue.shift();
    queued.delete(sketchReferenceToKey(changedReference));
    const changedPoint = getPreviewReferencePoint(previewDocument, changedReference);
    if (!changedPoint) {
      continue;
    }

    for (const constraint of constraints) {
      const pair = getTwoPreviewPointReferences(constraint);
      if (!pair) {
        continue;
      }

      const changedKey = sketchReferenceToKey(changedReference);
      const firstKey = sketchReferenceToKey(pair.first);
      const secondKey = sketchReferenceToKey(pair.second);
      const otherReference = StringComparer(firstKey, changedKey)
        ? pair.second
        : StringComparer(secondKey, changedKey)
          ? pair.first
          : null;
      if (!otherReference || fixedReferences.has(sketchReferenceToKey(otherReference))) {
        continue;
      }

      const before = getPreviewReferencePoint(previewDocument, otherReference);
      if (!before
        || distanceBetweenWorldPoints(before, changedPoint) <= WORLD_GEOMETRY_TOLERANCE
        || !setPreviewReferencePoint(previewDocument, otherReference, changedPoint)) {
        continue;
      }

      const otherKey = sketchReferenceToKey(otherReference);
      if (!queued.has(otherKey)) {
        queued.add(otherKey);
        queue.push(otherReference);
      }
    }
  }
}

function getPreviewFixedReferenceKeys(document) {
  const fixedReferences = new Set();
  for (const constraint of getDocumentConstraints(document)) {
    if (getSketchConstraintKind(constraint) !== "fix" || getSketchItemState(constraint) === "suppressed") {
      continue;
    }

    for (const referenceKey of getSketchReferenceKeys(constraint)) {
      const reference = parseSketchReference(referenceKey);
      if (reference) {
        fixedReferences.add(sketchReferenceToKey(reference));
      }
    }
  }

  return fixedReferences;
}

function getChangedPreviewPointReferences(originalDocument, previewDocument) {
  const changed = [];
  for (const reference of enumeratePreviewPointReferences(originalDocument)) {
    const before = getPreviewReferencePoint(originalDocument, reference);
    const after = getPreviewReferencePoint(previewDocument, reference);
    if (before && after && distanceBetweenWorldPoints(before, after) > WORLD_GEOMETRY_TOLERANCE) {
      changed.push(reference);
    }
  }

  return changed;
}

function enumeratePreviewPointReferences(document) {
  const references = [];
  for (const entity of getDocumentEntities(document)) {
    const entityId = getEntityId(entity);
    if (!entityId) {
      continue;
    }

    switch (getEntityKind(entity)) {
      case "line":
        references.push({ entityId, target: "start" }, { entityId, target: "end" });
        break;
      case "polyline": {
        const points = getEntityPoints(entity);
        for (let index = 0; index < points.length - 1; index += 1) {
          references.push(
            { entityId, segmentIndex: index, target: "start" },
            { entityId, segmentIndex: index, target: "end" });
        }

        break;
      }
      case "circle":
      case "polygon":
      case "ellipse":
        references.push({ entityId, target: "center" });
        break;
      case "arc":
        references.push(
          { entityId, target: "start" },
          { entityId, target: "end" },
          { entityId, target: "center" });
        break;
      case "point":
        references.push({ entityId, target: "entity" });
        break;
    }
  }

  return references;
}

function getTwoPreviewPointReferences(constraint) {
  const referenceKeys = getSketchReferenceKeys(constraint);
  if (referenceKeys.length < 2) {
    return null;
  }

  const first = parseSketchReference(referenceKeys[0]);
  const second = parseSketchReference(referenceKeys[1]);
  return first && second ? { first, second } : null;
}

function getPreviewReferencePoint(document, reference) {
  const entity = findCanvasDocumentEntity(document, reference.entityId);
  if (!entity) {
    return null;
  }

  const kind = getEntityKind(entity);
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

  if (kind === "line") {
    const points = getEntityPoints(entity);
    if (reference.target === "start") {
      return points[0] || null;
    }

    if (reference.target === "end") {
      return points[1] || null;
    }

    return null;
  }

  if (kind === "arc") {
    const center = getEntityCenter(entity);
    const radius = getEntityRadius(entity);
    if (reference.target === "center") {
      return center;
    }

    if (!center || !isFinitePositive(radius)) {
      return null;
    }

    if (reference.target === "start") {
      return pointOnCircle(center, radius, getEntityStartAngle(entity));
    }

    if (reference.target === "end") {
      return pointOnCircle(center, radius, getEntityEndAngle(entity));
    }
  }

  if ((kind === "circle" || kind === "polygon" || kind === "ellipse") && reference.target === "center") {
    return getEntityCenter(entity);
  }

  return kind === "point" && (reference.target === "entity" || reference.target === "center")
    ? getPointEntityLocation(entity)
    : null;
}

function setPreviewReferencePoint(document, reference, point) {
  const entity = findCanvasDocumentEntity(document, reference.entityId);
  if (!entity || !point) {
    return false;
  }

  if (Number.isInteger(reference.segmentIndex)) {
    const points = getEntityPoints(entity);
    if (reference.segmentIndex < 0 || reference.segmentIndex >= points.length - 1) {
      return false;
    }

    if (reference.target === "start") {
      points[reference.segmentIndex] = copyPoint(point);
    } else if (reference.target === "end") {
      points[reference.segmentIndex + 1] = copyPoint(point);
    } else {
      return false;
    }

    entity.points = points;
    return true;
  }

  const kind = getEntityKind(entity);
  if (kind === "line") {
    const points = getEntityPoints(entity);
    if (points.length < 2) {
      return false;
    }

    if (reference.target === "start") {
      entity.points = [copyPoint(point), points[1]];
      return true;
    }

    if (reference.target === "end") {
      entity.points = [points[0], copyPoint(point)];
      return true;
    }
  }

  if (kind === "arc") {
    if (reference.target === "center") {
      entity.center = copyPoint(point);
      return true;
    }

    if (reference.target === "start" || reference.target === "end") {
      const center = getEntityCenter(entity);
      if (!center) {
        return false;
      }

      const radius = distanceBetweenWorldPoints(center, point);
      if (radius <= WORLD_GEOMETRY_TOLERANCE) {
        return false;
      }

      entity.radius = radius;
      const angle = radiansToDegrees(Math.atan2(point.y - center.y, point.x - center.x));
      if (reference.target === "start") {
        entity.startAngleDegrees = angle;
      } else {
        entity.endAngleDegrees = angle;
      }

      return true;
    }
  }

  if ((kind === "circle" || kind === "polygon" || kind === "ellipse") && reference.target === "center") {
    entity.center = copyPoint(point);
    return true;
  }

  if (kind === "point" && (reference.target === "entity" || reference.target === "center")) {
    entity.points = [copyPoint(point)];
    return true;
  }

  return false;
}

function sketchReferenceToKey(reference) {
  const baseKey = Number.isInteger(reference.segmentIndex)
    ? `${reference.entityId}${SEGMENT_KEY_SEPARATOR}${reference.segmentIndex}`
    : reference.entityId;
  return reference.target === "entity"
    ? baseKey
    : `${baseKey}:${reference.target}`;
}

function applyGeometryDragPreviewMutation(document, selectionKey, delta, dragEnd, constrainToCurrentVector = false) {
  const dragStart = subtractPoints(dragEnd, delta);
  const segmentReference = parseSegmentSelectionKey(selectionKey);
  if (segmentReference) {
    return translatePreviewPolylineSegment(document, segmentReference.entityId, segmentReference.segmentIndex, delta);
  }

  const pointReference = parsePointSelectionKey(selectionKey);
  if (pointReference) {
    const entity = findCanvasDocumentEntity(document, pointReference.entityId);
    return entity
      ? applyPreviewPointDrag(
        document,
        entity,
        pointReference.label,
        pointReference.point || dragStart,
        delta,
        dragEnd,
        constrainToCurrentVector)
      : false;
  }

  const entity = findCanvasDocumentEntity(document, selectionKey);
  return entity ? applyPreviewEntityDrag(document, entity, delta, dragStart, dragEnd) : false;
}

function applyPreviewPointDrag(document, entity, label, dragStart, delta, dragEnd, constrainToCurrentVector = false) {
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

  if (kind === "ellipse") {
    return applyPreviewEllipsePointDrag(entity, normalizedLabel, delta, dragEnd);
  }

  if (kind === "spline") {
    return applyPreviewSplinePointDrag(entity, normalizedLabel, delta, dragEnd);
  }

  if (kind === "polygon" && normalizedLabel === "center") {
    return translatePreviewPolygon(entity, delta);
  }

  if (kind === "polygon"
    && (normalizedLabel.startsWith("vertex-") || normalizedLabel.startsWith("mid-"))) {
    return scalePreviewPolygon(entity, dragStart, dragEnd);
  }

  return false;
}

function applyPreviewEllipsePointDrag(entity, normalizedLabel, delta, dragEnd) {
  const center = getEntityCenter(entity);
  const ellipse = getEllipseFromEntity(entity);
  if (!center || !ellipse) {
    return false;
  }

  if (normalizedLabel === "center") {
    entity.center = addWorldPoints(center, delta);
    return true;
  }

  if (normalizedLabel === "start"
    || normalizedLabel === "end"
    || normalizedLabel === "quadrant-0") {
    return setPreviewEllipseMajorAxis(entity, subtractPoints(dragEnd, center), ellipse.minorRadiusRatio);
  }

  if (normalizedLabel === "quadrant-180") {
    return setPreviewEllipseMajorAxis(entity, subtractPoints(center, dragEnd), ellipse.minorRadiusRatio);
  }

  if (normalizedLabel === "quadrant-90" || normalizedLabel === "quadrant-270") {
    const minorLength = Math.abs(dotPoints(subtractPoints(dragEnd, center), ellipse.minorUnit));
    if (minorLength <= WORLD_GEOMETRY_TOLERANCE || ellipse.majorLength <= WORLD_GEOMETRY_TOLERANCE) {
      return false;
    }

    entity.minorRadiusRatio = minorLength / ellipse.majorLength;
    return true;
  }

  return false;
}

function setPreviewEllipseMajorAxis(entity, majorAxisEndPoint, minorRadiusRatio) {
  if (Math.hypot(majorAxisEndPoint.x, majorAxisEndPoint.y) <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

  entity.majorAxisEndPoint = {
    x: majorAxisEndPoint.x,
    y: majorAxisEndPoint.y
  };
  entity.minorRadiusRatio = minorRadiusRatio;
  return true;
}

function applyPreviewSplinePointDrag(entity, normalizedLabel, delta, dragEnd) {
  const fitPoints = getEntityFitPoints(entity);
  if (fitPoints.length >= 2) {
    if (normalizedLabel === "tangent-start" || normalizedLabel === "tangent-end") {
      return setPreviewSplineTangentHandle(entity, normalizedLabel, dragEnd, fitPoints);
    }

    return applyPreviewSplineEditablePointDrag(entity, fitPoints, "fit", normalizedLabel, delta, dragEnd);
  }

  const controlPoints = getEntityControlPoints(entity);
  return controlPoints.length >= 2
    ? applyPreviewSplineEditablePointDrag(entity, controlPoints, "control", normalizedLabel, delta, dragEnd)
    : false;
}

function applyPreviewSplineEditablePointDrag(entity, sourcePoints, sourceKind, normalizedLabel, delta, dragEnd) {
  const points = sourcePoints.map(point => ({ ...point }));
  const pointPrefix = `${sourceKind}-`;
  let pointIndex = parseIndexedPointLabel(normalizedLabel, pointPrefix);
  if (pointIndex === null && normalizedLabel === "start") {
    pointIndex = 0;
  } else if (pointIndex === null && normalizedLabel === "end") {
    pointIndex = points.length - 1;
  }

  if (pointIndex !== null) {
    if (pointIndex < 0 || pointIndex >= points.length) {
      return false;
    }

    points[pointIndex] = addWorldPoints(points[pointIndex], delta);
    setPreviewSplineEditablePoints(entity, sourceKind, points, pointIndex, delta);
    return true;
  }

  return false;
}

function setPreviewSplineTangentHandle(entity, normalizedLabel, dragEnd, fitPoints) {
  if (!dragEnd || !Number.isFinite(Number(dragEnd.x)) || !Number.isFinite(Number(dragEnd.y))) {
    return false;
  }

  if (normalizedLabel === "tangent-start") {
    entity.startTangentHandle = copyPoint(dragEnd);
  } else {
    entity.endTangentHandle = copyPoint(dragEnd);
  }

  entity.points = getSplineFitWorldPoints(
    fitPoints,
    SPLINE_FIT_SEGMENTS_PER_SPAN,
    getEntityStartTangentHandle(entity),
    getEntityEndTangentHandle(entity));
  return true;
}

function setPreviewSplineEditablePoints(entity, sourceKind, points, movedPointIndex = null, delta = null) {
  const copiedPoints = points.map(point => ({ ...point }));
  if (sourceKind === "fit") {
    if (movedPointIndex === 0 && getEntityStartTangentHandle(entity) && delta) {
      entity.startTangentHandle = addWorldPoints(getEntityStartTangentHandle(entity), delta);
    }

    if (movedPointIndex === copiedPoints.length - 1 && getEntityEndTangentHandle(entity) && delta) {
      entity.endTangentHandle = addWorldPoints(getEntityEndTangentHandle(entity), delta);
    }

    entity.fitPoints = copiedPoints.map(point => ({ ...point }));
    entity.controlPoints = copiedPoints.map(point => ({ ...point }));
    entity.points = getSplineFitWorldPoints(
      copiedPoints,
      SPLINE_FIT_SEGMENTS_PER_SPAN,
      getEntityStartTangentHandle(entity),
      getEntityEndTangentHandle(entity));
    return;
  }

  entity.controlPoints = copiedPoints.map(point => ({ ...point }));
  entity.points = copiedPoints.map(point => ({ ...point }));
}

function applyPreviewEntityDrag(document, entity, delta, dragStart, dragEnd) {
  const kind = getEntityKind(entity);
  if (kind === "line" || kind === "polyline") {
    entity.points = getEntityPoints(entity).map(point => addWorldPoints(point, delta));
    return true;
  }

  if (kind === "polygon") {
    return scalePreviewPolygon(entity, dragStart, dragEnd);
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

  if (kind === "ellipse") {
    const center = getEntityCenter(entity);
    if (!center) {
      return false;
    }

    entity.center = addWorldPoints(center, delta);
    return true;
  }

  if (kind === "spline") {
    entity.points = getEntityPoints(entity).map(point => addWorldPoints(point, delta));
    const fitPoints = getEntityFitPoints(entity);
    if (fitPoints.length > 0) {
      entity.fitPoints = fitPoints.map(point => addWorldPoints(point, delta));
    }

    const controlPoints = getEntityControlPoints(entity);
    if (controlPoints.length > 0) {
      entity.controlPoints = controlPoints.map(point => addWorldPoints(point, delta));
    }

    const startTangentHandle = getEntityStartTangentHandle(entity);
    if (startTangentHandle) {
      entity.startTangentHandle = addWorldPoints(startTangentHandle, delta);
    }

    const endTangentHandle = getEntityEndTangentHandle(entity);
    if (endTangentHandle) {
      entity.endTangentHandle = addWorldPoints(endTangentHandle, delta);
    }

    return true;
  }

  return false;
}

function getSelectedPreviewEntityId(document, selectionKey) {
  const key = String(selectionKey || "");
  const segmentReference = parseSegmentSelectionKey(key);
  if (segmentReference) {
    return findCanvasDocumentEntity(document, segmentReference.entityId)
      ? segmentReference.entityId
      : null;
  }

  const pointReference = parsePointSelectionKey(key);
  if (pointReference) {
    return findCanvasDocumentEntity(document, pointReference.entityId)
      ? pointReference.entityId
      : null;
  }

  return findCanvasDocumentEntity(document, key) ? key : null;
}

function translatePreviewEntityById(document, entityId, delta) {
  const entity = findCanvasDocumentEntity(document, entityId);
  if (!entity || !delta || !Number.isFinite(delta.x) || !Number.isFinite(delta.y)) {
    return false;
  }

  const kind = getEntityKind(entity);
  if (kind === "line" || kind === "polyline") {
    entity.points = getEntityPoints(entity).map(point => addWorldPoints(point, delta));
    return true;
  }

  if (kind === "polygon") {
    return translatePreviewPolygon(entity, delta);
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

    entity.center = addWorldPoints(center, delta);
    entity.points = getEntityPoints(entity).map(point => addWorldPoints(point, delta));
    return true;
  }

  if (kind === "ellipse") {
    const center = getEntityCenter(entity);
    if (!center) {
      return false;
    }

    entity.center = addWorldPoints(center, delta);
    entity.points = getEntityPoints(entity).map(point => addWorldPoints(point, delta));
    return true;
  }

  if (kind === "spline") {
    entity.points = getEntityPoints(entity).map(point => addWorldPoints(point, delta));
    const fitPoints = getEntityFitPoints(entity);
    if (fitPoints.length > 0) {
      entity.fitPoints = fitPoints.map(point => addWorldPoints(point, delta));
    }

    const controlPoints = getEntityControlPoints(entity);
    if (controlPoints.length > 0) {
      entity.controlPoints = controlPoints.map(point => addWorldPoints(point, delta));
    }

    const startTangentHandle = getEntityStartTangentHandle(entity);
    if (startTangentHandle) {
      entity.startTangentHandle = addWorldPoints(startTangentHandle, delta);
    }

    const endTangentHandle = getEntityEndTangentHandle(entity);
    if (endTangentHandle) {
      entity.endTangentHandle = addWorldPoints(endTangentHandle, delta);
    }

    return true;
  }

  return false;
}

function translatePreviewPolygon(entity, delta) {
  const center = getEntityCenter(entity);
  if (!center) {
    return false;
  }

  entity.center = addWorldPoints(center, delta);
  entity.points = getEntityPoints(entity).map(point => addWorldPoints(point, delta));
  return true;
}

function scalePreviewPolygon(entity, dragStart, dragEnd) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);
  if (!center || !isFinitePositive(radius) || !dragStart || !dragEnd) {
    return false;
  }

  const startDistance = distanceBetweenWorldPoints(center, dragStart);
  const endDistance = distanceBetweenWorldPoints(center, dragEnd);
  if (startDistance <= WORLD_GEOMETRY_TOLERANCE || endDistance <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

  const nextRadius = radius * endDistance / startDistance;
  if (!isFinitePositive(nextRadius)) {
    return false;
  }

  entity.radius = nextRadius;
  entity.Radius = nextRadius;
  updatePreviewPolygonPoints(entity);
  return true;
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

function translatePreviewDimensionAnchorsForDrag(document, selectionKey, delta) {
  if (!document || !delta || !Number.isFinite(delta.x) || !Number.isFinite(delta.y)) {
    return false;
  }

  const translatedEntityId = getTranslatedDimensionEntityId(document, selectionKey);
  if (!translatedEntityId) {
    return false;
  }

  return translatePreviewDimensionAnchorsForEntityIds(
    document,
    new Set([translatedEntityId]),
    delta);
}

function translatePreviewDimensionAnchorsForEntityIds(document, entityIds, delta) {
  let changed = false;
  document.dimensions = getDocumentDimensions(document).map(dimension => {
    const referenceKeys = getSketchReferenceKeys(dimension);
    const anchor = readPoint(readProperty(dimension, "anchor", "Anchor"));
    if (!anchor
      || referenceKeys.length === 0
      || !referenceKeys.every(key => entityIds.has(getReferenceEntityId(key)))) {
      return dimension;
    }

    changed = true;
    return {
      ...dimension,
      anchor: addWorldPoints(anchor, delta),
      Anchor: addWorldPoints(anchor, delta)
    };
  });

  return changed;
}

function getTranslatedDimensionEntityId(document, selectionKey) {
  const key = String(selectionKey || "");
  const segmentReference = parseSegmentSelectionKey(key);
  if (segmentReference) {
    return segmentReference.entityId;
  }

  const pointReference = parsePointSelectionKey(key);
  if (pointReference) {
    const entity = findCanvasDocumentEntity(document, pointReference.entityId);
    const kind = getEntityKind(entity);
    const label = String(pointReference.label || "").split("|")[0].toLowerCase();
    if ((kind === "line" && label === "mid")
      || (kind === "polyline" && label.startsWith("mid-"))
      || ((kind === "circle" || kind === "arc" || kind === "ellipse") && label === "center")
      || (kind === "polygon" && label === "center")
      || (kind === "point" && label === "point")) {
      return pointReference.entityId;
    }

    return null;
  }

  const entity = findCanvasDocumentEntity(document, key);
  const kind = getEntityKind(entity);
  return kind === "line" || kind === "polyline" || kind === "ellipse" || kind === "point"
    ? key
    : null;
}

function getReferenceEntityId(referenceKey) {
  const key = String(referenceKey || "");
  const pointIndex = key.indexOf(POINT_KEY_SEPARATOR);
  if (pointIndex > 0) {
    return key.slice(0, pointIndex);
  }

  const segmentIndex = key.indexOf(SEGMENT_KEY_SEPARATOR);
  if (segmentIndex > 0) {
    return key.slice(0, segmentIndex);
  }

  const targetIndex = key.lastIndexOf(":");
  return targetIndex > 0 ? key.slice(0, targetIndex) : key;
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
    dimensions: getDocumentDimensions(document).map(cloneCanvasDimension),
    constraints: getDocumentConstraints(document).slice()
  };
}

function cloneCanvasDimension(dimension) {
  const anchor = readPoint(readProperty(dimension, "anchor", "Anchor"));
  return {
    ...dimension,
    referenceKeys: getSketchReferenceKeys(dimension).slice(),
    ReferenceKeys: getSketchReferenceKeys(dimension).slice(),
    anchor: anchor ? { ...anchor } : null,
    Anchor: anchor ? { ...anchor } : null
  };
}

function cloneCanvasEntity(entity) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);
  const startAngle = getEntityStartAngle(entity);
  const endAngle = getEntityEndAngle(entity);
  const majorAxisEndPoint = getEntityMajorAxisEndPoint(entity);
  const minorRadiusRatio = getEntityMinorRadiusRatio(entity);
  return {
    ...entity,
    id: getEntityId(entity),
    kind: getEntityKind(entity),
    points: getEntityPoints(entity).map(point => ({ ...point })),
    fitPoints: getEntityFitPoints(entity).map(point => ({ ...point })),
    controlPoints: getEntityControlPoints(entity).map(point => ({ ...point })),
    center: center ? { ...center } : null,
    radius: Number.isFinite(radius) ? radius : null,
    startAngleDegrees: Number.isFinite(startAngle) ? startAngle : null,
    endAngleDegrees: Number.isFinite(endAngle) ? endAngle : null,
    majorAxisEndPoint: majorAxisEndPoint ? { ...majorAxisEndPoint } : null,
    minorRadiusRatio: Number.isFinite(minorRadiusRatio) ? minorRadiusRatio : null,
    isConstruction: Boolean(readProperty(entity, "isConstruction", "IsConstruction"))
  };
}

function parsePointSelectionKey(selectionKey) {
  const parts = parsePointTargetKeyParts(selectionKey);
  if (!parts || !parts.entityId) {
    return null;
  }

  return {
    entityId: parts.entityId,
    label: parts.label,
    point: parts.point
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

function shouldMovePreviewRectangleEndpoint(point, selectedPoints) {
  return selectedPoints.some(selectedPoint =>
    distanceBetweenWorldPoints(point, selectedPoint) <= WORLD_GEOMETRY_TOLERANCE);
}

function projectWorldDeltaPerpendicularToLine(delta, start, end) {
  const axisX = end.x - start.x;
  const axisY = end.y - start.y;
  const lengthSquared = axisX * axisX + axisY * axisY;
  if (lengthSquared <= WORLD_GEOMETRY_TOLERANCE * WORLD_GEOMETRY_TOLERANCE) {
    return delta;
  }

  const alongScale = ((delta.x * axisX) + (delta.y * axisY)) / lengthSquared;
  return {
    x: delta.x - axisX * alongScale,
    y: delta.y - axisY * alongScale
  };
}

function projectWorldDeltaOntoLine(delta, start, end) {
  const axisX = end.x - start.x;
  const axisY = end.y - start.y;
  const lengthSquared = axisX * axisX + axisY * axisY;
  if (lengthSquared <= WORLD_GEOMETRY_TOLERANCE * WORLD_GEOMETRY_TOLERANCE) {
    return { x: 0, y: 0 };
  }

  const alongScale = ((delta.x * axisX) + (delta.y * axisY)) / lengthSquared;
  return {
    x: axisX * alongScale,
    y: axisY * alongScale
  };
}

function projectWorldDeltaOntoAxis(delta, axis) {
  const scalar = dotPoints(delta, axis);
  return {
    x: axis.x * scalar,
    y: axis.y * scalar
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
  const parts = parseSegmentTargetKeyParts(key);
  if (!parts) {
    return null;
  }

  const entity = getDocumentEntities(state.document)
    .find(candidate => StringComparer(getEntityId(candidate), parts.entityId));
  return entity ? createPolylineSegmentTarget(entity, parts.segmentIndex, null) : null;
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
  const modifyTool = getModifyTool(state);
  if (modifyTool === "offset" && dimension === "offset") {
    return applyOffsetDraftDimensionValue(state, numericValue);
  }

  if (!tool || !state.toolDraft || state.toolDraft.points.length === 0) {
    return false;
  }

  if ((tool === "inscribedpolygon" || tool === "circumscribedpolygon") && dimension === "sides") {
    const sideCount = clamp(Math.round(numericValue), 3, 64);
    state.toolDraft.polygonSideCount = sideCount;
    state.toolDraft.dimensionValues = {
      ...(state.toolDraft.dimensionValues || {}),
      sides: sideCount
    };
    return true;
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
  } else if ((tool === "ellipse" || tool === "ellipticalarc") && dimension === "major") {
    const majorRadius = numericValue / 2;
    if (points.length === 1) {
      nextPreviewPoint = pointFromAnchorWithLength(anchor, previewPoint, majorRadius);
    } else {
      const nextMajorPoint = pointFromAnchorWithLength(anchor, points[1], majorRadius);
      const minorPoint = points.length >= 3 ? points[2] : previewPoint;
      const nextMinorPoint = getEllipseMinorPointWithLength(anchor, nextMajorPoint, minorPoint, getEllipseMinorLength(anchor, points[1], minorPoint));
      state.toolDraft.points = points.length >= 3
        ? [anchor, nextMajorPoint, nextMinorPoint]
        : [anchor, nextMajorPoint];
      nextPreviewPoint = points.length >= 3 ? previewPoint : nextMinorPoint;
    }
  } else if ((tool === "ellipse" || tool === "ellipticalarc") && dimension === "minor" && points.length >= 2) {
    nextPreviewPoint = getEllipseMinorPointWithLength(anchor, points[1], previewPoint, numericValue / 2);
  } else if (tool === "inscribedpolygon" && dimension === "radius") {
    nextPreviewPoint = pointFromAnchorWithLength(anchor, previewPoint, numericValue);
  } else if (tool === "circumscribedpolygon" && dimension === "apothem") {
    nextPreviewPoint = pointFromAnchorWithLength(anchor, previewPoint, numericValue);
  } else if (tool === "slot" && dimension === "length") {
    if (points.length === 1) {
      nextPreviewPoint = pointFromAnchorWithLength(anchor, previewPoint, numericValue);
    } else {
      const nextEnd = pointFromAnchorWithLength(anchor, points[1], numericValue);
      const radius = getSlotRadius(anchor, points[1], previewPoint);
      state.toolDraft.points = [anchor, nextEnd];
      nextPreviewPoint = getSlotRadiusPointWithLength(anchor, nextEnd, previewPoint, radius);
    }
  } else if (tool === "slot" && dimension === "radius" && points.length >= 2) {
    nextPreviewPoint = getSlotRadiusPointWithLength(anchor, points[1], previewPoint, numericValue);
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
  } else if (tool === "tangentarc" && dimension === "radius" && points.length >= 2) {
    nextPreviewPoint = getTangentArcEndPointWithRadius(points[0], points[1], previewPoint, numericValue);
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

function applyOffsetDraftDimensionValue(state, numericValue) {
  if (!state || !state.toolDraft) {
    return false;
  }

  const throughPoint = state.toolDraft.previewPoint;
  const target = getPrimaryOffsetPreviewTarget(state);
  if (!target || !target.entity || !throughPoint) {
    return false;
  }

  const nextPreviewPoint = getOffsetThroughPointAtDistance(target.entity, throughPoint, numericValue);
  if (!nextPreviewPoint) {
    return false;
  }

  state.toolDraft.previewPoint = nextPreviewPoint;
  state.toolDraft.dimensionValues = {
    ...(state.toolDraft.dimensionValues || {}),
    offset: numericValue
  };
  return true;
}

function getPrimaryOffsetPreviewTarget(state) {
  const targets = getOffsetPreviewTargets(state);
  return targets.length > 0 ? targets[0] : null;
}

function getOffsetThroughPointAtDistance(entity, throughPoint, distance) {
  const kind = getEntityKind(entity);
  if (kind === "circle" || kind === "arc") {
    const center = getEntityCenter(entity);
    const radius = getEntityRadius(entity);
    if (!center || !isFinitePositive(radius)) {
      return null;
    }

    const direction = normalizeScreenVector(subtractPoints(throughPoint, center)) || { x: 1, y: 0 };
    const currentDistance = distanceBetweenWorldPoints(center, throughPoint);
    const sign = currentDistance < radius ? -1 : 1;
    const nextRadius = Math.max(WORLD_GEOMETRY_TOLERANCE, radius + distance * sign);
    return {
      x: center.x + direction.x * nextRadius,
      y: center.y + direction.y * nextRadius
    };
  }

  const points = getOffsetPreviewSourcePoints(entity);
  if (points.length < 2) {
    return null;
  }

  const projection = getClosestWorldSegmentProjection(throughPoint, points, kind === "polygon");
  if (!projection) {
    return null;
  }

  const sign = projection.signedDistance < 0 ? -1 : 1;
  return {
    x: projection.point.x + projection.normal.x * distance * sign,
    y: projection.point.y + projection.normal.y * distance * sign
  };
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
  const polygonSideCount = toolDraft
    ? Number(toolDraft.polygonSideCount)
    : Number.NaN;
  const exportValues = { ...dimensionValues };
  if (!("sides" in exportValues) && Number.isInteger(polygonSideCount) && polygonSideCount >= 3) {
    exportValues.sides = polygonSideCount;
  }

  for (const [key, value] of Object.entries(exportValues)) {
    const normalizedKey = String(key || "").trim().toLowerCase();
    const numericValue = Number(value);
    if (!normalizedKey
      || !Number.isFinite(numericValue)
      || (normalizedKey === "sides" ? numericValue < 3 : numericValue <= WORLD_GEOMETRY_TOLERANCE)) {
      continue;
    }

    keys.push(normalizedKey);
    values.push(normalizedKey === "sides" ? clamp(Math.round(numericValue), 3, 64) : numericValue);
  }

  return { keys, values };
}

function isVariablePointSketchTool(tool) {
  return tool === "spline" || tool === "splinecontrolpoint";
}

function isAddSplinePointSketchTool(tool) {
  return tool === "splinecontrolpoint";
}

function getPolygonSideCount(state) {
  const sideCount = state && state.toolDraft
    ? Number(state.toolDraft.polygonSideCount)
    : Number.NaN;
  return Number.isInteger(sideCount) && sideCount >= 3
    ? clamp(sideCount, 3, 64)
    : 6;
}

export function adjustPolygonSideCount(state, wheelDelta) {
  const tool = getSketchCreationTool(state);
  if ((tool !== "inscribedpolygon" && tool !== "circumscribedpolygon")
    || !state.toolDraft
    || !Array.isArray(state.toolDraft.points)
    || state.toolDraft.points.length === 0) {
    return false;
  }

  const direction = wheelDelta < 0 ? 1 : -1;
  state.toolDraft.polygonSideCount = clamp(getPolygonSideCount(state) + direction, 3, 64);
  return true;
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

function getTangentArcEndPointWithRadius(start, tangentPoint, currentEnd, radius) {
  const tangentLength = distanceBetweenWorldPoints(start, tangentPoint);
  if (!Number.isFinite(radius)
    || radius <= WORLD_GEOMETRY_TOLERANCE
    || tangentLength <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const tangent = {
    x: (tangentPoint.x - start.x) / tangentLength,
    y: (tangentPoint.y - start.y) / tangentLength
  };
  const normal = { x: -tangent.y, y: tangent.x };
  const currentArc = getTangentArc(start, tangentPoint, currentEnd);
  const sideValue = currentArc
    ? dotPoints(subtractPoints(currentArc.center, start), normal)
    : dotPoints(subtractPoints(currentEnd, start), normal);
  const side = Math.abs(sideValue) > WORLD_GEOMETRY_TOLERANCE
    ? Math.sign(sideValue)
    : 1;
  const center = {
    x: start.x + normal.x * radius * side,
    y: start.y + normal.y * radius * side
  };

  if (currentArc) {
    return pointOnCircle(
      center,
      radius,
      getPointAngleDegrees(currentArc.center, currentEnd));
  }

  const startAngle = getPointAngleDegrees(center, start);
  return pointOnCircle(center, radius, startAngle + side * 90);
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
  if (!points || (!isVariablePointSketchTool(tool) && points.length < getSketchToolPointCount(tool))) {
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

  if (tool === "ellipse") {
    return getEllipseFromPoints(points[0], points[1], points[2]) !== null;
  }

  if (tool === "ellipticalarc") {
    return getEllipseFromPoints(points[0], points[1], points[2], points[3]) !== null;
  }

  if (tool === "inscribedpolygon" || tool === "circumscribedpolygon") {
    return distanceBetweenWorldPoints(points[0], points[1]) > WORLD_GEOMETRY_TOLERANCE;
  }

  if (tool === "conic") {
    return distanceBetweenWorldPoints(points[0], points[2]) > WORLD_GEOMETRY_TOLERANCE;
  }

  if (isVariablePointSketchTool(tool)) {
    return points.length >= 2
      && points.slice(1).some(point => distanceBetweenWorldPoints(points[0], point) > WORLD_GEOMETRY_TOLERANCE);
  }

  if (tool === "bezier") {
    return points.slice(1).some(point => distanceBetweenWorldPoints(points[0], point) > WORLD_GEOMETRY_TOLERANCE);
  }

  if (tool === "slot") {
    return getSlotPreviewGeometry(points[0], points[1], points[2]) !== null;
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

export function setHoveredTarget(state, target) {
  const previousTarget = state.hoveredTarget;
  const previousKey = state.hoveredTarget ? state.hoveredTarget.key : null;
  const previousNotifyKey = state.hoveredTarget && !state.hoveredTarget.dynamic
    ? previousKey
    : null;
  const nextKey = target ? target.key : null;
  const nextNotifyKey = target && !target.dynamic
    ? nextKey
    : null;

  if (areEquivalentHoveredTargets(previousTarget, target)) {
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

function areEquivalentHoveredTargets(previousTarget, target) {
  if (previousTarget === target) {
    return true;
  }

  if (!previousTarget || !target) {
    return false;
  }

  return previousTarget.kind === target.kind
    && previousTarget.key === target.key
    && previousTarget.label === target.label
    && previousTarget.segmentIndex === target.segmentIndex
    && sameOptionalWorldPoint(previousTarget.point, target.point)
    && sameOptionalWorldPoint(previousTarget.snapPoint, target.snapPoint)
    && sameOptionalReference(previousTarget.entity, target.entity)
    && sameOptionalReference(previousTarget.dimension, target.dimension)
    && sameOptionalReference(previousTarget.constraint, target.constraint);
}

function sameOptionalReference(first, second) {
  return first === second || (!first && !second);
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
  pruneExpiredAcquiredSnapPoints(state);
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
    point: acquiredPoint,
    acquiredAt: getInteractionTimestamp()
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
  state.powerTrimDrag = null;
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
  state.canvas.dataset.powerTrimMode = getPowerTrimHoverModeForState(state) || "";
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
  state.canvas.dataset.dimensionDraftKeys = state.dimensionDraft && Array.isArray(state.dimensionDraft.selectionKeys)
    ? state.dimensionDraft.selectionKeys.join(",")
    : "";
  state.canvas.dataset.dimensionDraftComplete = state.dimensionDraft && state.dimensionDraft.complete ? "true" : "false";
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
    case "ellipse":
      return "ellipse";
    case "threepointarc":
      return "threepointarc";
    case "tangentarc":
      return "tangentarc";
    case "centerpointarc":
      return "centerpointarc";
    case "ellipticalarc":
      return "ellipticalarc";
    case "conic":
      return "conic";
    case "inscribedpolygon":
      return "inscribedpolygon";
    case "circumscribedpolygon":
      return "circumscribedpolygon";
    case "spline":
      return "spline";
    case "bezier":
      return "bezier";
    case "splinecontrolpoint":
      return "splinecontrolpoint";
    case "slot":
      return "slot";
    default:
      return null;
  }
}

function getModifyTool(state) {
  switch (normalizeToolName(state && state.activeTool)) {
    case "addsplinepoint":
      return "addsplinepoint";
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

function getConstraintTool(state) {
  const tool = normalizeToolName(state && state.activeTool);
  switch (tool) {
    case "coincident":
    case "concentric":
    case "parallel":
    case "tangent":
    case "horizontal":
    case "vertical":
    case "perpendicular":
    case "equal":
    case "midpoint":
    case "fix":
      return tool;
    default:
      return null;
  }
}

export function getSketchToolPointCount(tool) {
  switch (tool) {
    case "point":
      return 1;
    case "ellipticalarc":
      return 4;
    case "spline":
    case "splinecontrolpoint":
      return Number.MAX_SAFE_INTEGER;
    case "alignedrectangle":
    case "threepointcircle":
    case "ellipse":
    case "threepointarc":
    case "tangentarc":
    case "centerpointarc":
    case "conic":
    case "slot":
      return 3;
    default:
      return 2;
  }
}

export function getModifyToolPointCount(tool) {
  switch (tool) {
    case "addsplinepoint":
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

  const target = findNearestPowerTrimTarget(state, screenPoint);
  const rawPoint = screenToWorld(state, screenPoint);
  const point = getPowerTrimPickPoint(target, rawPoint);
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

export function getPowerTrimRequestMode(state, screenPoint) {
  if (!isPowerTrimTool(state)) {
    return null;
  }

  const target = findNearestPowerTrimTarget(state, screenPoint);
  const rawPoint = screenToWorld(state, screenPoint);
  const point = getPowerTrimPickPoint(target, rawPoint);
  const targetKey = target && target.kind === "entity"
    ? target.key
    : target && (target.kind === "point" || target.kind === "segment")
      ? target.entityId
      : null;
  if (!target || target.dynamic || !targetKey || !point) {
    return null;
  }

  return isPowerTrimExtendPick(target, rawPoint) ? "extend" : "trim";
}

function getPowerTrimHoverModeForState(state) {
  return isPowerTrimTool(state) && state.pointerScreenPoint
    ? getPowerTrimRequestMode(state, state.pointerScreenPoint)
    : null;
}

function getPowerTrimPickPoint(target, rawPoint) {
  if (!target) {
    return rawPoint;
  }

  if (isPowerTrimExtendPick(target, rawPoint)) {
    return rawPoint;
  }

  return target.snapPoint || rawPoint;
}

function isPowerTrimExtendPick(target, rawPoint) {
  return Boolean(target && target.snapPoint && isOffEndOpenPathPowerTrimPick(target, rawPoint));
}

function isOffEndOpenPathPowerTrimPick(target, rawPoint) {
  if (!rawPoint || !target || !target.entity) {
    return false;
  }

  const kind = getEntityKind(target.entity);
  if (kind === "arc" && target.kind === "entity") {
    return isOffEndArcPowerTrimPick(target.entity, rawPoint);
  }

  if (kind === "ellipse" && target.kind === "entity") {
    return isOffEndEllipsePowerTrimPick(target.entity, rawPoint);
  }

  const points = getEntityPoints(target.entity);
  if (points.length < 2) {
    return false;
  }

  if (kind === "line" && target.kind === "entity") {
    const projection = closestPointOnWorldSegment(rawPoint, points[0], points[1]);
    return projection
      && (projection.parameter < -WORLD_GEOMETRY_TOLERANCE
        || projection.parameter > 1 + WORLD_GEOMETRY_TOLERANCE);
  }

  if (kind === "spline" && target.kind === "entity") {
    return isOffEndPointSequencePowerTrimPick(points, rawPoint);
  }

  if (kind !== "polyline" || target.kind !== "segment") {
    return false;
  }

  const segmentIndex = Number(target.segmentIndex);
  if (!Number.isInteger(segmentIndex) || segmentIndex < 0 || segmentIndex >= points.length - 1) {
    return false;
  }

  const projection = closestPointOnWorldSegment(rawPoint, points[segmentIndex], points[segmentIndex + 1]);
  if (!projection) {
    return false;
  }

  if (segmentIndex === 0 && projection.parameter < -WORLD_GEOMETRY_TOLERANCE) {
    return true;
  }

  return projection
    && segmentIndex === points.length - 2
    && projection.parameter > 1 + WORLD_GEOMETRY_TOLERANCE;
}

function isOffEndArcPowerTrimPick(entity, rawPoint) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);
  if (!center || !isFinitePositive(radius)) {
    return false;
  }

  const startAngle = getEntityStartAngle(entity);
  const sweep = getPositiveSweepDegrees(startAngle, getEntityEndAngle(entity));
  if (sweep >= FULL_CIRCLE_DEGREES) {
    return false;
  }

  const distance = distanceBetweenWorldPoints(center, rawPoint);
  if (distance <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

  const rawAngle = radiansToDegrees(Math.atan2(rawPoint.y - center.y, rawPoint.x - center.x));
  return isParameterOutsideOpenSweep(startAngle, rawAngle, sweep);
}

function isOffEndEllipsePowerTrimPick(entity, rawPoint) {
  const ellipse = getEllipseFromEntity(entity);
  if (!ellipse) {
    return false;
  }

  const sweep = getPositiveSweepDegrees(ellipse.startParameterDegrees, ellipse.endParameterDegrees);
  if (sweep >= FULL_CIRCLE_DEGREES) {
    return false;
  }

  const offset = subtractPoints(rawPoint, ellipse.center);
  const localX = dotPoints(offset, ellipse.majorUnit) / ellipse.majorLength;
  const localY = dotPoints(offset, ellipse.minorUnit) / ellipse.minorLength;
  if (Math.hypot(localX, localY) <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

  const rawParameter = radiansToDegrees(Math.atan2(localY, localX));
  return isParameterOutsideOpenSweep(ellipse.startParameterDegrees, rawParameter, sweep);
}

function isParameterOutsideOpenSweep(startParameterDegrees, rawParameterDegrees, sweepDegrees) {
  const delta = getCounterClockwiseDeltaDegrees(startParameterDegrees, rawParameterDegrees);
  return delta > sweepDegrees + WORLD_GEOMETRY_TOLERANCE;
}

function isOffEndPointSequencePowerTrimPick(points, rawPoint) {
  if (!Array.isArray(points) || points.length < 2 || !rawPoint) {
    return false;
  }

  const firstProjection = closestPointOnWorldSegment(rawPoint, points[0], points[1]);
  if (firstProjection && firstProjection.parameter < -WORLD_GEOMETRY_TOLERANCE) {
    return true;
  }

  const lastProjection = closestPointOnWorldSegment(rawPoint, points[points.length - 2], points[points.length - 1]);
  return lastProjection && lastProjection.parameter > 1 + WORLD_GEOMETRY_TOLERANCE;
}

function startPowerTrimDrag(state, candidate, screenPoint) {
  state.powerTrimDrag = {
    pointerId: candidate.pointerId,
    points: [candidate.screenPoint]
  };
  updatePowerTrimDrag(state, screenPoint);
  setHoveredTarget(state, null);
}

function updatePowerTrimDrag(state, screenPoint) {
  if (!state.powerTrimDrag) {
    return;
  }

  const points = state.powerTrimDrag.points;
  const lastPoint = points[points.length - 1];
  if (!lastPoint || distanceBetweenScreenPoints(lastPoint, screenPoint) >= POWER_TRIM_PATH_MIN_SCREEN_DISTANCE) {
    points.push(screenPoint);
  }
}

export function getPowerTrimCrossingRequests(state, screenPoints) {
  if (!isPowerTrimTool(state) || !Array.isArray(screenPoints) || screenPoints.length < 2) {
    return [];
  }

  const requestsByTarget = new Map();
  for (let index = 1; index < screenPoints.length; index += 1) {
    addPowerTrimCrossingRequestsForScreenSegment(
      state,
      screenPoints[index - 1],
      screenPoints[index],
      requestsByTarget);
  }

  return Array.from(requestsByTarget.values());
}

function addPowerTrimCrossingRequestsForScreenSegment(state, pathStart, pathEnd, requestsByTarget) {
  if (distanceBetweenScreenPoints(pathStart, pathEnd) <= WORLD_GEOMETRY_TOLERANCE) {
    return;
  }

  for (const entity of getDocumentEntities(state.document)) {
    if (!isPowerTrimCrossingEntity(entity)) {
      continue;
    }

    const target = createEntityTarget(entity);
    if (!target || requestsByTarget.has(target.key)) {
      continue;
    }

    const segments = getEntityScreenSegments(state, entity);
    for (const segment of segments) {
      const intersection = getScreenSegmentIntersectionPoint(pathStart, pathEnd, segment.start, segment.end);
      if (!intersection) {
        continue;
      }

      requestsByTarget.set(target.key, {
        targetKey: target.key,
        point: screenToWorld(state, intersection)
      });
      break;
    }
  }
}

function isPowerTrimCrossingEntity(entity) {
  const kind = getEntityKind(entity);
  return kind === "line"
    || kind === "polyline"
    || kind === "circle"
    || kind === "arc"
    || kind === "ellipse"
    || kind === "spline"
    || kind === "polygon";
}

function getScreenSegmentIntersectionPoint(firstStart, firstEnd, secondStart, secondEnd) {
  const firstDelta = {
    x: firstEnd.x - firstStart.x,
    y: firstEnd.y - firstStart.y
  };
  const secondDelta = {
    x: secondEnd.x - secondStart.x,
    y: secondEnd.y - secondStart.y
  };
  const denominator = crossScreen(firstDelta, secondDelta);
  if (Math.abs(denominator) <= 0.000001) {
    return null;
  }

  const offset = {
    x: secondStart.x - firstStart.x,
    y: secondStart.y - firstStart.y
  };
  const firstParameter = crossScreen(offset, secondDelta) / denominator;
  const secondParameter = crossScreen(offset, firstDelta) / denominator;
  if (firstParameter < -0.000001
    || firstParameter > 1.000001
    || secondParameter < -0.000001
    || secondParameter > 1.000001) {
    return null;
  }

  return {
    x: firstStart.x + firstDelta.x * firstParameter,
    y: firstStart.y + firstDelta.y * firstParameter
  };
}

function crossScreen(first, second) {
  return first.x * second.y - first.y * second.x;
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
    return null;
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

  return invocation;
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
  const entities = getDocumentEntities(document);
  if (entities.length === 0) {
    return null;
  }

  const dtoBounds = readBounds(readProperty(document, "bounds", "Bounds"));
  if (dtoBounds) {
    return dtoBounds;
  }

  return computeEntityBounds(entities);
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

  if (kind === "ellipse") {
    return getEllipseWorldPointsFromEntity(entity);
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

function getEntityMajorAxisEndPoint(entity) {
  return readPoint(readProperty(entity, "majorAxisEndPoint", "MajorAxisEndPoint"));
}

function getEntityMinorRadiusRatio(entity) {
  return Number(readProperty(entity, "minorRadiusRatio", "MinorRadiusRatio"));
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

function getSketchAffectedReferenceKeys(item) {
  const affectedReferenceKeys = readProperty(item, "affectedReferenceKeys", "AffectedReferenceKeys");
  if (Array.isArray(affectedReferenceKeys) && affectedReferenceKeys.length > 0) {
    return affectedReferenceKeys.map(key => String(key));
  }

  return getSketchReferenceKeys(item);
}

function getSketchItemState(item) {
  const state = readProperty(item, "state", "State");
  return state === null || state === undefined ? "" : String(state).toLowerCase();
}

function getSketchConstraintState(item) {
  return getSketchItemState(item);
}

export function getSketchDimensionState(item) {
  return getSketchItemState(item);
}

export function getSketchDiagnosticEntityIds(document) {
  const entityIds = new Set();
  const diagnosticItems = [
    ...getDocumentConstraints(document),
    ...getDocumentDimensions(document)
  ];

  for (const item of diagnosticItems) {
    if (getSketchItemState(item) !== "unsatisfied") {
      continue;
    }

    for (const referenceKey of getSketchAffectedReferenceKeys(item)) {
      const reference = parseSketchReference(referenceKey);
      if (reference && reference.entityId) {
        entityIds.add(reference.entityId);
      }
    }
  }

  return entityIds;
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
    case "polygon":
    case "ellipse":
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
    case "polygon":
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
    case "ellipse":
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
  if (!entity
    || (getEntityKind(entity) !== "circle"
      && getEntityKind(entity) !== "arc"
      && getEntityKind(entity) !== "polygon")) {
    return null;
  }

  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);
  if (!center || !isFinitePositive(radius)) {
    return null;
  }

  return getEntityKind(entity) === "arc"
    ? {
      center,
      radius,
      startAngleDegrees: getEntityStartAngle(entity),
      endAngleDegrees: getEntityEndAngle(entity)
    }
    : { center, radius };
}

function resolveSketchArcAngleReference(state, referenceKey) {
  const reference = parseSketchReference(referenceKey);
  if (!reference || reference.target === "center" || Number.isInteger(reference.segmentIndex)) {
    return null;
  }

  const entity = findDocumentEntity(state, reference.entityId);
  if (!entity || getEntityKind(entity) !== "arc") {
    return null;
  }

  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);
  if (!center || !isFinitePositive(radius)) {
    return null;
  }

  const startAngleDegrees = getEntityStartAngle(entity);
  const endAngleDegrees = getEntityEndAngle(entity);
  return {
    center,
    radius,
    startAngleDegrees,
    endAngleDegrees,
    firstLine: {
      start: center,
      end: pointOnCircle(center, radius, startAngleDegrees)
    },
    secondLine: {
      start: center,
      end: pointOnCircle(center, radius, endAngleDegrees)
    }
  };
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
  if (Number.isFinite(value)) {
    return value;
  }

  const parameterValue = Number(readProperty(entity, "startParameterDegrees", "StartParameterDegrees"));
  return Number.isFinite(parameterValue) ? parameterValue : 0;
}

function getEntityEndAngle(entity) {
  const value = Number(readProperty(entity, "endAngleDegrees", "EndAngleDegrees"));
  if (Number.isFinite(value)) {
    return value;
  }

  const parameterValue = Number(readProperty(entity, "endParameterDegrees", "EndParameterDegrees"));
  return Number.isFinite(parameterValue) ? parameterValue : FULL_CIRCLE_DEGREES;
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
  if (!supportsPolarSnap(tool)
    || !state.toolDraft
    || state.toolDraft.points.length === 0) {
    return point;
  }

  const anchor = getPolarSnapAnchor(state, tool);
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

function supportsPolarSnap(tool) {
  return tool === "line"
    || tool === "alignedrectangle"
    || tool === "ellipse"
    || tool === "ellipticalarc"
    || tool === "midpointline"
    || tool === "inscribedpolygon"
    || tool === "circumscribedpolygon";
}

function getPolarSnapAnchor(state, tool) {
  const points = state && state.toolDraft && Array.isArray(state.toolDraft.points)
    ? state.toolDraft.points
    : [];
  if (tool === "alignedrectangle" && points.length >= 2) {
    return points[points.length - 1];
  }

  return points[0];
}

function pointOnCircle(center, radius, angleDegrees) {
  const radians = degreesToRadians(angleDegrees);
  return {
    x: center.x + Math.cos(radians) * radius,
    y: center.y + Math.sin(radians) * radius
  };
}

export function getEllipseFromPoints(center, majorPoint, minorPoint, endParameterPoint = null) {
  if (!center || !majorPoint || !minorPoint) {
    return null;
  }

  const majorVector = subtractPoints(majorPoint, center);
  const majorLength = Math.hypot(majorVector.x, majorVector.y);
  if (majorLength <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const majorUnit = {
    x: majorVector.x / majorLength,
    y: majorVector.y / majorLength
  };
  const minorUnit = {
    x: -majorUnit.y,
    y: majorUnit.x
  };
  const minorOffset = subtractPoints(minorPoint, center);
  const minorLength = Math.abs(dotPoints(minorOffset, minorUnit));
  if (minorLength <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const ellipse = {
    center,
    majorPoint,
    majorVector,
    majorUnit,
    majorLength,
    minorUnit,
    minorLength,
    minorRadiusRatio: minorLength / majorLength,
    startParameterDegrees: 0,
    endParameterDegrees: 360
  };

  if (endParameterPoint) {
    ellipse.endParameterDegrees = getEllipseParameterDegrees(ellipse, endParameterPoint);
  }

  return ellipse;
}

export function getEllipseAxisDiameterPoints(ellipse, axis) {
  if (!ellipse || !ellipse.center) {
    return null;
  }

  const normalizedAxis = String(axis || "").toLowerCase();
  const unit = normalizedAxis === "minor" ? ellipse.minorUnit : ellipse.majorUnit;
  const length = normalizedAxis === "minor" ? ellipse.minorLength : ellipse.majorLength;
  if (!unit || !isFinitePositive(length)) {
    return null;
  }

  return {
    start: {
      x: ellipse.center.x - unit.x * length,
      y: ellipse.center.y - unit.y * length
    },
    end: {
      x: ellipse.center.x + unit.x * length,
      y: ellipse.center.y + unit.y * length
    }
  };
}

export function getCenteredAxisDiameterPoints(center, axisPoint) {
  if (!center || !axisPoint) {
    return null;
  }

  const vector = subtractPoints(axisPoint, center);
  if (Math.hypot(vector.x, vector.y) <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  return {
    start: {
      x: center.x - vector.x,
      y: center.y - vector.y
    },
    end: {
      x: center.x + vector.x,
      y: center.y + vector.y
    }
  };
}

function getEllipseFromEntity(entity) {
  const center = getEntityCenter(entity);
  const majorAxisEndPoint = getEntityMajorAxisEndPoint(entity);
  const minorRatio = getEntityMinorRadiusRatio(entity);
  if (!center || !majorAxisEndPoint || !Number.isFinite(minorRatio) || minorRatio <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const majorPoint = {
    x: center.x + majorAxisEndPoint.x,
    y: center.y + majorAxisEndPoint.y
  };
  const majorLength = Math.hypot(majorAxisEndPoint.x, majorAxisEndPoint.y);
  if (majorLength <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const majorUnit = {
    x: majorAxisEndPoint.x / majorLength,
    y: majorAxisEndPoint.y / majorLength
  };
  return {
    center,
    majorPoint,
    majorVector: majorAxisEndPoint,
    majorUnit,
    majorLength,
    minorUnit: {
      x: -majorUnit.y,
      y: majorUnit.x
    },
    minorLength: majorLength * minorRatio,
    minorRadiusRatio: minorRatio,
    startParameterDegrees: getEntityStartAngle(entity),
    endParameterDegrees: getEntityEndAngle(entity)
  };
}

function getEllipsePointAtParameter(ellipse, parameterDegrees) {
  const radians = degreesToRadians(parameterDegrees);
  return {
    x: ellipse.center.x
      + ellipse.majorUnit.x * ellipse.majorLength * Math.cos(radians)
      + ellipse.minorUnit.x * ellipse.minorLength * Math.sin(radians),
    y: ellipse.center.y
      + ellipse.majorUnit.y * ellipse.majorLength * Math.cos(radians)
      + ellipse.minorUnit.y * ellipse.minorLength * Math.sin(radians)
  };
}

function getEllipseWorldPoints(ellipse) {
  const sweep = getPositiveSweepDegrees(ellipse.startParameterDegrees, ellipse.endParameterDegrees);
  const segmentCount = Math.max(12, Math.ceil(sweep / 8));
  const points = [];
  for (let index = 0; index <= segmentCount; index += 1) {
    const parameter = ellipse.startParameterDegrees + sweep * index / segmentCount;
    points.push(getEllipsePointAtParameter(ellipse, parameter));
  }

  return points;
}

function getEllipseWorldPointsFromEntity(entity) {
  const ellipse = getEllipseFromEntity(entity);
  return ellipse ? getEllipseWorldPoints(ellipse) : [];
}

function getEllipseParameterDegrees(ellipse, point) {
  const offset = subtractPoints(point, ellipse.center);
  const x = dotPoints(offset, ellipse.majorUnit) / ellipse.majorLength;
  const y = dotPoints(offset, ellipse.minorUnit) / ellipse.minorLength;
  let degrees = radiansToDegrees(Math.atan2(y, x));
  if (degrees <= WORLD_GEOMETRY_TOLERANCE) {
    degrees += FULL_CIRCLE_DEGREES;
  }

  return degrees;
}

function getEllipseMinorLength(center, majorPoint, minorPoint) {
  const ellipse = getEllipseFromPoints(center, majorPoint, minorPoint);
  return ellipse ? ellipse.minorLength : distanceBetweenWorldPoints(center, minorPoint);
}

function getEllipseMinorPointWithLength(center, majorPoint, referencePoint, length) {
  const majorVector = subtractPoints(majorPoint, center);
  const majorLength = Math.hypot(majorVector.x, majorVector.y);
  if (majorLength <= WORLD_GEOMETRY_TOLERANCE) {
    return referencePoint;
  }

  const minorUnit = {
    x: -majorVector.y / majorLength,
    y: majorVector.x / majorLength
  };
  const sign = dotPoints(subtractPoints(referencePoint, center), minorUnit) < 0 ? -1 : 1;
  return {
    x: center.x + minorUnit.x * length * sign,
    y: center.y + minorUnit.y * length * sign
  };
}

export function getPolygonWorldPoints(center, radiusPoint, circumscribed = false, sideCount = 6) {
  const radius = distanceBetweenWorldPoints(center, radiusPoint);
  if (!Number.isFinite(radius) || radius <= WORLD_GEOMETRY_TOLERANCE || sideCount < 3) {
    return [];
  }

  let vertexRadius = radius;
  let angle = Math.atan2(radiusPoint.y - center.y, radiusPoint.x - center.x);
  if (circumscribed) {
    vertexRadius = radius / Math.cos(Math.PI / sideCount);
    angle += Math.PI / sideCount;
  }

  return Array.from({ length: sideCount }, (_, index) => ({
    x: center.x + Math.cos(angle + index * Math.PI * 2 / sideCount) * vertexRadius,
    y: center.y + Math.sin(angle + index * Math.PI * 2 / sideCount) * vertexRadius
  }));
}

export function getPolygonGuideCircleWorldPoints(center, radiusPoint) {
  const radius = distanceBetweenWorldPoints(center, radiusPoint);
  return Number.isFinite(radius) && radius > WORLD_GEOMETRY_TOLERANCE
    ? getCircleWorldPoints(center, radius)
    : [];
}

export function getPolygonGuideCircleWorldPointsForEntity(entity) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);
  return center && isFinitePositive(radius)
    ? getCircleWorldPoints(center, radius)
    : [];
}

function updatePreviewPolygonPoints(entity) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);
  if (!center || !isFinitePositive(radius)) {
    return false;
  }

  const radiusPoint = pointOnCircle(center, radius, getPolygonRotationAngle(entity));
  const points = getPolygonWorldPoints(
    center,
    radiusPoint,
    getPolygonCircumscribed(entity),
    getPolygonEntitySideCount(entity));
  if (points.length < 3) {
    return false;
  }

  entity.points = points;
  entity.Points = points;
  return true;
}

function getPolygonRotationAngle(entity) {
  const value = Number(readProperty(entity, "rotationAngleDegrees", "RotationAngleDegrees"));
  if (Number.isFinite(value)) {
    return value;
  }

  const center = getEntityCenter(entity);
  const firstPoint = getEntityPoints(entity)[0];
  return center && firstPoint
    ? radiansToDegrees(Math.atan2(firstPoint.y - center.y, firstPoint.x - center.x))
    : 0;
}

function getPolygonEntitySideCount(entity) {
  const sideCount = Number(readProperty(entity, "sideCount", "SideCount"));
  if (Number.isFinite(sideCount)) {
    return clamp(Math.round(sideCount), 3, 64);
  }

  return clamp(getEntityPoints(entity).length || 6, 3, 64);
}

function getPolygonCircumscribed(entity) {
  const value = readProperty(entity, "circumscribed", "Circumscribed");
  return value === true || String(value).toLowerCase() === "true";
}

function getQuadraticBezierWorldPoints(start, control, end, segmentCount = 32) {
  return Array.from({ length: segmentCount + 1 }, (_, index) => {
    const t = index / segmentCount;
    const inverse = 1 - t;
    return {
      x: inverse * inverse * start.x + 2 * inverse * t * control.x + t * t * end.x,
      y: inverse * inverse * start.y + 2 * inverse * t * control.y + t * t * end.y
    };
  });
}

function getCubicBezierWorldPoints(points, segmentCount = 40) {
  if (!Array.isArray(points) || points.length < 2) {
    return [];
  }

  if (points.length < 4) {
    return getQuadraticBezierWorldPoints(points[0], points[1], points[2] || points[1], segmentCount);
  }

  return Array.from({ length: segmentCount + 1 }, (_, index) => {
    const t = index / segmentCount;
    const inverse = 1 - t;
    return {
      x: inverse * inverse * inverse * points[0].x
        + 3 * inverse * inverse * t * points[1].x
        + 3 * inverse * t * t * points[2].x
        + t * t * t * points[3].x,
      y: inverse * inverse * inverse * points[0].y
        + 3 * inverse * inverse * t * points[1].y
        + 3 * inverse * t * t * points[2].y
        + t * t * t * points[3].y
    };
  });
}

export function getSplineFitWorldPoints(
  points,
  segmentCountPerSpan = SPLINE_FIT_SEGMENTS_PER_SPAN,
  startTangentHandle = null,
  endTangentHandle = null) {
  if (!Array.isArray(points) || points.length < 2) {
    return [];
  }

  if (points.length === 2) {
    const startTangent = getSplineStartTangent(points, startTangentHandle);
    const endTangent = getSplineEndTangent(points, endTangentHandle);
    if (isTangentCollinearWithChord(points[0], points[1], startTangent)
      && isTangentCollinearWithChord(points[0], points[1], endTangent)) {
      return points.slice();
    }
  }

  const segmentCount = Math.max(2, Math.floor(segmentCountPerSpan));
  const tangents = getSplineFitTangents(points, startTangentHandle, endTangentHandle);
  const samples = [];
  for (let index = 0; index < points.length - 1; index += 1) {
    const start = points[index];
    const end = points[index + 1];
    const startTangent = tangents[index];
    const endTangent = tangents[index + 1];

    for (let step = 0; step <= segmentCount; step += 1) {
      if (index > 0 && step === 0) {
        continue;
      }

      if (step === 0) {
        samples.push(copyPoint(start));
      } else if (step === segmentCount) {
        samples.push(copyPoint(end));
      } else {
        samples.push(getCubicHermiteWorldPoint(start, end, startTangent, endTangent, step / segmentCount));
      }
    }
  }

  return samples;
}

function getSplineFitTangents(points, startTangentHandle, endTangentHandle) {
  return points.map((point, index) => {
    if (index === 0) {
      return getSplineStartTangent(points, startTangentHandle);
    }

    if (index === points.length - 1) {
      return getSplineEndTangent(points, endTangentHandle);
    }

    return {
      x: (points[index + 1].x - points[index - 1].x) / 2,
      y: (points[index + 1].y - points[index - 1].y) / 2
    };
  });
}

function getSplineStartTangent(points, startTangentHandle) {
  const handle = readPoint(startTangentHandle);
  if (handle && distanceBetweenWorldPoints(points[0], handle) > WORLD_GEOMETRY_TOLERANCE) {
    return {
      x: (handle.x - points[0].x) / SPLINE_TANGENT_HANDLE_SCALE,
      y: (handle.y - points[0].y) / SPLINE_TANGENT_HANDLE_SCALE
    };
  }

  return {
    x: points[1].x - points[0].x,
    y: points[1].y - points[0].y
  };
}

function getSplineEndTangent(points, endTangentHandle) {
  const lastIndex = points.length - 1;
  const handle = readPoint(endTangentHandle);
  if (handle && distanceBetweenWorldPoints(points[lastIndex], handle) > WORLD_GEOMETRY_TOLERANCE) {
    return {
      x: (points[lastIndex].x - handle.x) / SPLINE_TANGENT_HANDLE_SCALE,
      y: (points[lastIndex].y - handle.y) / SPLINE_TANGENT_HANDLE_SCALE
    };
  }

  return {
    x: points[lastIndex].x - points[lastIndex - 1].x,
    y: points[lastIndex].y - points[lastIndex - 1].y
  };
}

function getCubicHermiteWorldPoint(start, end, startTangent, endTangent, t) {
  const t2 = t * t;
  const t3 = t2 * t;
  const h00 = (2 * t3) - (3 * t2) + 1;
  const h10 = t3 - (2 * t2) + t;
  const h01 = (-2 * t3) + (3 * t2);
  const h11 = t3 - t2;
  return {
    x: h00 * start.x + h10 * startTangent.x + h01 * end.x + h11 * endTangent.x,
    y: h00 * start.y + h10 * startTangent.y + h01 * end.y + h11 * endTangent.y
  };
}

function isTangentCollinearWithChord(start, end, tangent) {
  const chord = subtractPoints(end, start);
  return Math.abs((chord.x * tangent.y) - (chord.y * tangent.x)) <= WORLD_GEOMETRY_TOLERANCE;
}

export function getSplineTangentHandles(points, handleScale = 0.25) {
  if (!Array.isArray(points) || points.length < 2) {
    return [];
  }

  const first = points[0];
  const last = points[points.length - 1];
  const firstNeighbor = getNearestDistinctSplinePoint(points, 1, 1);
  const lastNeighbor = getNearestDistinctSplinePoint(points, points.length - 2, -1);

  return [
    getSplineEndpointTangentHandle(first, firstNeighbor, handleScale, true),
    getSplineEndpointTangentHandle(last, lastNeighbor, handleScale, false)
  ];
}

export function getPersistentSplineTangentHandlesForEntity(entity) {
  if (getEntityKind(entity) !== "spline") {
    return [];
  }

  const fitPoints = getEntityFitPoints(entity);
  const controlPoints = getEntityControlPoints(entity);
  const handlePoints = fitPoints.length >= 2
    ? fitPoints
    : controlPoints.length >= 2
      ? controlPoints
      : getEntityPoints(entity);
  const handles = getSplineTangentHandles(handlePoints, SPLINE_TANGENT_HANDLE_SCALE);
  const startTangentHandle = getEntityStartTangentHandle(entity);
  if (handles[0] && startTangentHandle) {
    handles[0] = {
      ...handles[0],
      forward: startTangentHandle
    };
  }

  const endTangentHandle = getEntityEndTangentHandle(entity);
  if (handles[1] && endTangentHandle) {
    handles[1] = {
      ...handles[1],
      backward: endTangentHandle
    };
  }

  return handles;
}

function getEntityFitPoints(entity) {
  const rawPoints = readProperty(entity, "fitPoints", "FitPoints");

  if (!Array.isArray(rawPoints)) {
    return [];
  }

  return rawPoints
    .map(readPoint)
    .filter(point => point !== null);
}

function getEntityControlPoints(entity) {
  const rawPoints = readProperty(entity, "controlPoints", "ControlPoints");

  if (!Array.isArray(rawPoints)) {
    return [];
  }

  return rawPoints
    .map(readPoint)
    .filter(point => point !== null);
}

function getEntityStartTangentHandle(entity) {
  return readPoint(readProperty(entity, "startTangentHandle", "StartTangentHandle"));
}

function getEntityEndTangentHandle(entity) {
  return readPoint(readProperty(entity, "endTangentHandle", "EndTangentHandle"));
}

function getNearestDistinctSplinePoint(points, startIndex, step) {
  for (let index = startIndex; index >= 0 && index < points.length; index += step) {
    if (distanceBetweenWorldPoints(points[index], points[startIndex - step]) > WORLD_GEOMETRY_TOLERANCE) {
      return points[index];
    }
  }

  return null;
}

function getSplineEndpointTangentHandle(point, neighbor, handleScale, isStart) {
  const direction = neighbor
    ? normalizeWorldVector(subtractPoints(neighbor, point))
    : null;
  if (!direction) {
    return {
      point: copyPoint(point),
      backward: null,
      forward: null
    };
  }

  const handleLength = Math.max(
    WORLD_GEOMETRY_TOLERANCE,
    distanceBetweenWorldPoints(point, neighbor) * handleScale);
  const endpoint = {
    x: point.x + direction.x * handleLength,
    y: point.y + direction.y * handleLength
  };

  return {
    point: copyPoint(point),
    backward: isStart ? null : endpoint,
    forward: isStart ? endpoint : null
  };
}

function getSlotPreviewGeometry(startCenter, endCenter, radiusPoint) {
  const axis = subtractPoints(endCenter, startCenter);
  const axisLength = Math.hypot(axis.x, axis.y);
  if (axisLength <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const normal = {
    x: -axis.y / axisLength,
    y: axis.x / axisLength
  };
  const radius = Math.abs(dotPoints(subtractPoints(radiusPoint, startCenter), normal));
  if (radius <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const radiusPointOnNormal = {
    x: startCenter.x + normal.x * radius,
    y: startCenter.y + normal.y * radius
  };
  return {
    radius,
    radiusPoint: radiusPointOnNormal,
    points: [
      { x: startCenter.x + normal.x * radius, y: startCenter.y + normal.y * radius },
      { x: endCenter.x + normal.x * radius, y: endCenter.y + normal.y * radius },
      { x: endCenter.x - normal.x * radius, y: endCenter.y - normal.y * radius },
      { x: startCenter.x - normal.x * radius, y: startCenter.y - normal.y * radius }
    ]
  };
}

function getSlotRadius(startCenter, endCenter, radiusPoint) {
  const slot = getSlotPreviewGeometry(startCenter, endCenter, radiusPoint);
  return slot ? slot.radius : distanceBetweenWorldPoints(startCenter, radiusPoint);
}

function getSlotRadiusPointWithLength(startCenter, endCenter, referencePoint, radius) {
  const axis = subtractPoints(endCenter, startCenter);
  const axisLength = Math.hypot(axis.x, axis.y);
  if (axisLength <= WORLD_GEOMETRY_TOLERANCE) {
    return referencePoint;
  }

  const normal = {
    x: -axis.y / axisLength,
    y: axis.x / axisLength
  };
  const sign = dotPoints(subtractPoints(referencePoint, startCenter), normal) < 0 ? -1 : 1;
  return {
    x: startCenter.x + normal.x * radius * sign,
    y: startCenter.y + normal.y * radius * sign
  };
}

function formatKeyNumber(value) {
  return String(Math.round(value * 1000000) / 1000000);
}

function sanitizeKeyPart(value) {
  return String(value).replaceAll("|", "-");
}

function StringComparer(first, second) {
  return first === second;
}

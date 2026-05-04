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
    activeDimensionKey: null,
    suppressDimensionInputCommit: false,
    document: null,
    showOriginAxes: false,
    grainDirection: "none",
    polarSnapIncrementDegrees: 15,
    activeTool: "select",
    toolDraft: createEmptyToolDraft(),
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
      }
      pruneInteractionState(state, true);
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

    setOriginAxesVisible(visible) {
      state.showOriginAxes = Boolean(visible);
      updateDebugAttributes(state);
      draw(state);
    },

    setActiveTool(toolName) {
      state.activeTool = normalizeToolName(toolName);
      state.toolDraft = createEmptyToolDraft();
      state.acquiredSnapPoints = [];
      state.canvas.dataset.activeTool = state.activeTool;
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
      strokeStyle: "#94a3b8",
      lineWidth: 1.5,
      lineDash: []
    });
  }

  for (const selectedKey of state.selectedKeys) {
    const target = resolveSelectionTarget(state, selectedKey);
    if (target) {
      const isActive = selectedKey === state.activeSelectionKey;
      drawTarget(state, target, {
        strokeStyle: isActive ? "#7dd3fc" : "#2d7898",
        lineWidth: target.kind === "point" ? isActive ? 3 : 1.65 : isActive ? 4.5 : 2.4,
        lineDash: [],
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
      lineDash: isSelected ? [6, 4] : [],
      glow: isActive
    });
  }

  if (state.selectionBox) {
    drawSelectionBox(state);
  }

  const previewDimensions = drawToolPreview(state);
  updateDimensionInputs(state, previewDimensions);
  if (!state.dimensionOverlay) {
    drawPreviewDimensionFallbackLabels(state, previewDimensions);
  }
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
  context.beginPath();
  context.moveTo(point.x, point.y);
  context.lineTo(
    point.x - size * Math.cos(angle - Math.PI / 6),
    point.y - size * Math.sin(angle - Math.PI / 6));
  context.lineTo(
    point.x - size * Math.cos(angle + Math.PI / 6),
    point.y - size * Math.sin(angle + Math.PI / 6));
  context.closePath();
  context.fill();
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
  if (!tool || !state.toolDraft || state.toolDraft.points.length === 0 || !state.toolDraft.previewPoint) {
    return [];
  }

  const first = state.toolDraft.points[0];
  const second = state.toolDraft.previewPoint;
  const { context } = state;
  const dimensions = [];

  context.save();
  context.strokeStyle = "#facc15";
  context.fillStyle = "#facc15";
  context.lineWidth = 1.5;
  context.setLineDash([6, 4]);

  if (tool === "line") {
    const start = worldToScreen(state, first);
    const end = worldToScreen(state, second);
    context.beginPath();
    context.moveTo(start.x, start.y);
    context.lineTo(end.x, end.y);
    context.stroke();
    addLinearPreviewDimension(dimensions, state, "length", "Length", first, second);
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
  } else if (tool === "centercircle") {
    const center = worldToScreen(state, first);
    const radius = distanceBetweenWorldPoints(first, second) * state.view.scale;
    if (radius > 0) {
      context.beginPath();
      context.arc(center.x, center.y, radius, 0, Math.PI * 2);
      context.stroke();
      addRadiusPreviewDimension(dimensions, state, first, second);
    }
  }

  const marker = worldToScreen(state, first);
  context.setLineDash([]);
  context.beginPath();
  context.arc(marker.x, marker.y, 3.5, 0, Math.PI * 2);
  context.fill();
  context.restore();
  return dimensions;
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

function drawPreviewDimensionFallbackLabels(state, dimensions) {
  for (const dimension of dimensions) {
    drawFloatingDimensionLabel(state, formatDimensionValue(dimension.value), dimension.point);
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

  state.activeDimensionKey = getDefaultActiveDimensionKey(dimensions, state.activeDimensionKey);

  for (const dimension of dimensions) {
    const input = getOrCreateDimensionInput(state, dimension);
    const lockedValue = getLockedDraftDimensionValue(state, dimension.key);
    const hasLockedValue = Number.isFinite(lockedValue);
    const displayValue = hasLockedValue
      ? formatDimensionValue(lockedValue)
      : formatDimensionValue(dimension.value);

    const isFocused = document.activeElement === input;
    const isEditing = input.dataset.dimensionEditing === "true";
    const isActiveDimension = dimension.key === state.activeDimensionKey;
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
    input.classList.toggle("drawing-dimension-input-active", input.dataset.dimensionKey === state.activeDimensionKey);
  }

  focusActiveDimensionInputIfNeeded(state, dimensions);
}

export function getDefaultActiveDimensionKey(dimensions, requestedKey) {
  if (!Array.isArray(dimensions) || dimensions.length === 0) {
    return null;
  }

  const keys = dimensions
    .map(dimension => dimension && dimension.key)
    .filter(key => Boolean(key));
  if (keys.length === 0) {
    return null;
  }

  return keys.includes(requestedKey)
    ? requestedKey
    : keys[0];
}

export function getNextDimensionKey(dimensions, currentKey, reverse = false) {
  if (!Array.isArray(dimensions) || dimensions.length === 0) {
    return null;
  }

  const keys = dimensions
    .map(dimension => dimension && dimension.key)
    .filter(key => Boolean(key));
  if (keys.length === 0) {
    return null;
  }

  const currentIndex = Math.max(0, keys.indexOf(currentKey));
  const offset = reverse ? -1 : 1;
  const nextIndex = (currentIndex + offset + keys.length) % keys.length;
  return keys[nextIndex];
}

export function shouldRefreshDimensionInputValue(isFocused, hasLockedValue, hasTransientEdit = false) {
  return !hasTransientEdit && (!isFocused || !hasLockedValue);
}

export function shouldAutoSelectDimensionInputValue(isFocused, hasLockedValue, hasTransientEdit = false, isActiveDimension = false) {
  return isFocused && isActiveDimension && !hasLockedValue && !hasTransientEdit;
}

export function shouldCommitDimensionInputOnBlur(suppressDimensionInputCommit = false, skipNextBlurCommit = false) {
  return !suppressDimensionInputCommit && !skipNextBlurCommit;
}

export function shouldCommitDimensionInputOnChange(skipNextChangeCommit = false) {
  return !skipNextChangeCommit;
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
  const activeKey = getDefaultActiveDimensionKey(dimensions, state.activeDimensionKey);
  if (!activeKey) {
    return;
  }

  const activeInput = state.dimensionInputs.get(activeKey);
  if (!activeInput || document.activeElement === activeInput) {
    return;
  }

  const focusedInput = getFocusedDimensionInput(state);
  if (focusedInput && state.dimensionInputs.has(focusedInput.dataset.dimensionKey)) {
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
    if (shouldCommitDimensionInputOnBlur(state.suppressDimensionInputCommit, skipNextBlurCommit)) {
      commitDimensionInputValue(state, input);
    }
    if (state.activeDimensionKey === input.dataset.dimensionKey) {
      state.activeDimensionKey = null;
    }
    input.classList.remove("drawing-dimension-input-active");
  });
  input.addEventListener("input", () => commitDimensionInputValue(state, input));
  input.addEventListener("keydown", event => handleDimensionInputKeyDown(state, input, event));
  input.addEventListener("change", () => {
    const skipNextChangeCommit = input.dataset.skipNextChangeCommit === "true";
    input.dataset.skipNextChangeCommit = "false";
    if (shouldCommitDimensionInputOnChange(skipNextChangeCommit)) {
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
      state.activeDimensionKey === input.dataset.dimensionKey)) {
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
  for (const candidate of state.dimensionInputs.values()) {
    candidate.classList.toggle("drawing-dimension-input-active", candidate === input);
  }
  selectInputText(input);
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

function getVisibleDimensionDescriptors(state) {
  return Array.from(state.dimensionInputs.keys()).map(key => ({ key }));
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

  if (getSketchCreationTool(state)) {
    const nearestTarget = findNearestTarget(state, screenPoint);
    setHoveredTarget(state, nearestTarget);

    if (state.toolDraft && state.toolDraft.points.length > 0) {
      state.toolDraft.previewPoint = getSketchWorldPoint(state, screenPoint, nearestTarget, event);
      applyLockedDraftDimensions(state);
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

  if (isPrimaryPointerButton(event)) {
    state.clickCandidate = {
      screenPoint,
      pointerId: event.pointerId,
      cancelled: false
    };

    if (getSketchCreationTool(state)) {
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
      if (getSketchCreationTool(state)) {
        handleSketchToolClick(state, screenPoint, event);
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
  if (!state.toolDraft || state.toolDraft.points.length === 0) {
    state.toolDraft = {
      points: [worldPoint],
      previewPoint: null,
      dimensionValues: {}
    };
    setHoveredTarget(state, null);
    return;
  }

  const first = state.toolDraft.points[0];
  state.toolDraft.previewPoint = worldPoint;
  applyLockedDraftDimensions(state);
  worldPoint = state.toolDraft.previewPoint;

  if (distanceBetweenWorldPoints(first, worldPoint) <= WORLD_GEOMETRY_TOLERANCE) {
    state.toolDraft.previewPoint = null;
    return;
  }

  commitSketchToolPoints(state, tool, first, worldPoint);
}

function commitCurrentSketchTool(state) {
  const tool = getSketchCreationTool(state);
  if (!tool || !state.toolDraft || state.toolDraft.points.length === 0 || !state.toolDraft.previewPoint) {
    return false;
  }

  const first = state.toolDraft.points[0];
  const second = state.toolDraft.previewPoint;
  if (distanceBetweenWorldPoints(first, second) <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

  commitSketchToolPoints(state, tool, first, second);
  return true;
}

function commitSketchToolPoints(state, tool, first, second) {
  state.toolDraft = tool === "line"
    ? { points: [second], previewPoint: null, dimensionValues: {} }
    : createEmptyToolDraft();
  clearDimensionInputEditState(state);
  setHoveredTarget(state, null);
  invokeDotNet(state, "OnSketchToolCommitted", tool, [first.x, first.y, second.x, second.y]);
}

function handlePointerCancel(state, event) {
  state.panning = false;
  state.lastPointerScreen = null;
  state.pointerScreenPoint = null;
  state.clickCandidate = null;
  state.selectionBox = null;
  releasePointer(state.canvas, event.pointerId);

  if (setHoveredTarget(state, null)) {
    draw(state);
  }
}

function handlePointerLeave(state) {
  if (state.panning) {
    return;
  }

  state.pointerScreenPoint = null;

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
  state.acquiredSnapPoints = [];
  clearDimensionInputEditState(state);
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

function findNearestTarget(state, screenPoint) {
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

function getEntityPointHit(state, entity, screenPoint) {
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

function getDynamicSketchSnapHit(state, screenPoint, highlightedTarget = null) {
  const tool = getSketchCreationTool(state);
  if (!tool) {
    return null;
  }

  const worldPoint = screenToWorld(state, screenPoint);
  const candidates = [];
  addAcquiredProjectionSnapCandidates(candidates, state, worldPoint);
  addHighlightedGeometryOrthoSnapCandidates(candidates, state, highlightedTarget);
  addDynamicTangentSnapCandidates(candidates, state, tool);

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
    priority: 5
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

function addDynamicTangentSnapCandidates(candidates, state, tool) {
  if (tool !== "line" && tool !== "midpointline") {
    return;
  }

  if (!state.toolDraft || state.toolDraft.points.length === 0) {
    return;
  }

  const anchor = state.toolDraft.points[0];
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

function getEntityScreenDistance(state, entity, screenPoint) {
  const hit = getEntityEdgeHit(state, entity, screenPoint);
  return hit ? hit.distance : Number.POSITIVE_INFINITY;
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

function getPointTargetMarker(target) {
  const label = String(target && target.label ? target.label : "").toLowerCase();
  return label === "mid" || label.startsWith("mid-") || label.startsWith("midpoint-")
    ? "midpoint"
    : "point";
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

  return {
    kind: "point",
    key,
    entityId,
    entity,
    label: parts.slice(0, parts.length - 2).join("|"),
    point: { x, y }
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

  const anchor = state.toolDraft.points[0];
  const previewPoint = state.toolDraft.previewPoint || { x: anchor.x + 1, y: anchor.y };
  let nextPreviewPoint = null;

  if (tool === "line" && dimension === "length") {
    nextPreviewPoint = pointFromAnchorWithLength(anchor, previewPoint, numericValue);
  } else if (tool === "midpointline" && dimension === "length") {
    nextPreviewPoint = pointFromAnchorWithLength(anchor, previewPoint, numericValue / 2);
  } else if (tool === "centercircle" && dimension === "radius") {
    nextPreviewPoint = pointFromAnchorWithLength(anchor, previewPoint, numericValue);
  } else if (tool === "twopointrectangle" && (dimension === "width" || dimension === "height")) {
    const width = dimension === "width" ? numericValue : Math.abs(previewPoint.x - anchor.x);
    const height = dimension === "height" ? numericValue : Math.abs(previewPoint.y - anchor.y);
    const signX = previewPoint.x < anchor.x ? -1 : 1;
    const signY = previewPoint.y < anchor.y ? -1 : 1;
    nextPreviewPoint = {
      x: anchor.x + width * signX,
      y: anchor.y + height * signY
    };
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

function clearSelectedTargets(state) {
  if (state.selectedKeys.size === 0 && !state.activeSelectionKey) {
    return false;
  }

  state.selectedKeys.clear();
  state.activeSelectionKey = null;
  return true;
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
  if (!getSketchCreationTool(state)
    || !target
    || target.kind !== "point"
    || target.dynamic
    || !target.point) {
    return;
  }

  const duplicateIndex = state.acquiredSnapPoints.findIndex(acquired =>
    acquired.key === target.key
    || distanceBetweenWorldPoints(acquired.point, target.point) <= WORLD_GEOMETRY_TOLERANCE);
  if (duplicateIndex >= 0) {
    state.acquiredSnapPoints.splice(duplicateIndex, 1);
  }

  state.acquiredSnapPoints.push({
    key: target.key,
    label: sanitizeKeyPart(target.label || target.entityId || "point"),
    point: target.point
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
  state.canvas.dataset.grainDirection = normalizeGrainDirection(state.grainDirection);
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
    case "line":
      return "line";
    case "midpointline":
      return "midpointline";
    case "twopointrectangle":
      return "twopointrectangle";
    case "centercircle":
      return "centercircle";
    default:
      return null;
  }
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

function getEntityCenter(entity) {
  return readPoint(readProperty(entity, "center", "Center"));
}

function getEntityRadius(entity) {
  return Number(readProperty(entity, "radius", "Radius"));
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

function dotPoints(first, second) {
  return first.x * second.x + first.y * second.y;
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

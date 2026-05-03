const DEFAULT_FIT_MARGIN = 32;
const HIT_TEST_TOLERANCE = 9;
const SNAP_POINT_TOLERANCE = 8;
const CLICK_MOVE_TOLERANCE = 5;
const MIN_VIEW_SCALE = 0.000001;
const MAX_VIEW_SCALE = 1000000;
const FULL_CIRCLE_DEGREES = 360;
const SEGMENT_KEY_SEPARATOR = "|segment|";
const POINT_KEY_SEPARATOR = "|point|";

export function createDrawingCanvas(canvas, dotnetRef) {
  const context = canvas.getContext("2d");
  if (!context) {
    throw new Error("DXFER drawing canvas requires a 2D rendering context.");
  }

  const state = {
    canvas,
    context,
    dotnetRef,
    document: null,
    showOriginAxes: false,
    hoveredTarget: null,
    selectedKeys: new Set(),
    view: {
      scale: 1,
      offsetX: 0,
      offsetY: 0
    },
    pixelRatio: 1,
    disposed: false,
    panning: false,
    lastPointerScreen: null,
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

  const resizeObserver = typeof ResizeObserver === "undefined"
    ? null
    : new ResizeObserver(resize);

  if (resizeObserver) {
    resizeObserver.observe(state.canvas);
  }

  resizeCanvas(state);

  return {
    setDocument(document) {
      state.document = document || null;
      pruneInteractionState(state);
      fitToExtents(state);
      updateDebugAttributes(state);
      draw(state);
    },

    fitToExtents() {
      fitToExtents(state);
      updateDebugAttributes(state);
      draw(state);
    },

    setOriginAxesVisible(visible) {
      state.showOriginAxes = Boolean(visible);
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

      if (resizeObserver) {
        resizeObserver.disconnect();
      }

      state.canvas.style.touchAction = state.previousTouchAction;
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
      drawTarget(state, target, {
        strokeStyle: "#38bdf8",
        lineWidth: target.kind === "point" ? 2 : 4,
        lineDash: []
      });
    }
  }

  if (state.hoveredTarget) {
    const isSelected = state.selectedKeys.has(state.hoveredTarget.key);
    drawTarget(state, state.hoveredTarget, {
      strokeStyle: isSelected ? "#fde68a" : "#f59e0b",
      lineWidth: state.hoveredTarget.kind === "point" ? 2 : isSelected ? 2 : 3.5,
      lineDash: isSelected ? [6, 4] : []
    });
  }

  if (state.selectionBox) {
    drawSelectionBox(state);
  }

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

function drawTarget(state, target, style) {
  const { context } = state;

  if (target.kind === "point") {
    drawPointTarget(state, target.point, style);
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

  if (target.closestPoint) {
    drawClosestPointMarker(state, target.closestPoint, style.strokeStyle);
  }
}

function drawPointTarget(state, point, style) {
  const { context } = state;
  const screenPoint = worldToScreen(state, point);

  context.save();
  context.strokeStyle = style.strokeStyle;
  context.fillStyle = "#0f172a";
  context.lineWidth = style.lineWidth;
  context.setLineDash([]);
  context.beginPath();
  context.arc(screenPoint.x, screenPoint.y, 5, 0, Math.PI * 2);
  context.fill();
  context.stroke();
  context.beginPath();
  context.moveTo(screenPoint.x - 8, screenPoint.y);
  context.lineTo(screenPoint.x + 8, screenPoint.y);
  context.moveTo(screenPoint.x, screenPoint.y - 8);
  context.lineTo(screenPoint.x, screenPoint.y + 8);
  context.stroke();
  context.restore();
}

function drawClosestPointMarker(state, point, strokeStyle) {
  const { context } = state;
  const screenPoint = worldToScreen(state, point);

  context.save();
  context.strokeStyle = strokeStyle;
  context.fillStyle = "#0f172a";
  context.lineWidth = 1.5;
  context.beginPath();
  context.arc(screenPoint.x, screenPoint.y, 3.5, 0, Math.PI * 2);
  context.fill();
  context.stroke();
  context.restore();
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

function buildEntityPath(state, entity) {
  const kind = getEntityKind(entity);

  switch (kind) {
    case "line":
      return buildLinePath(state, entity);
    case "polyline":
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

  if (state.panning && state.lastPointerScreen) {
    state.view.offsetX += screenPoint.x - state.lastPointerScreen.x;
    state.view.offsetY += screenPoint.y - state.lastPointerScreen.y;
    state.lastPointerScreen = screenPoint;
    event.preventDefault();
    updateDebugAttributes(state);
    draw(state);
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
  capturePointer(state.canvas, event.pointerId);

  if (isPanPointerDown(event)) {
    state.panning = true;
    state.lastPointerScreen = screenPoint;
    state.clickCandidate = null;
    state.selectionBox = null;
    event.preventDefault();
    return;
  }

  if (event.button === 0) {
    state.clickCandidate = {
      screenPoint,
      pointerId: event.pointerId,
      cancelled: false
    };
  }
}

function handlePointerUp(state, event) {
  if (state.disposed) {
    return;
  }

  const screenPoint = getPointerScreenPoint(state, event);

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
      invokeDotNet(state, "OnSelectionChangedFromCanvas", Array.from(state.selectedKeys));
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
      const clickedTarget = findNearestTarget(state, screenPoint);
      if (clickedTarget) {
        toggleSelectedTarget(state, clickedTarget);
        invokeDotNet(state, "OnEntityClicked", clickedTarget.key);
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

function handlePointerCancel(state, event) {
  state.panning = false;
  state.lastPointerScreen = null;
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

function handleWheel(state, event) {
  if (state.disposed) {
    return;
  }

  event.preventDefault();

  const screenPoint = getPointerScreenPoint(state, event);
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

  const screenPoint = getPointerScreenPoint(state, event);
  const target = findNearestWholeEntityTarget(state, screenPoint);
  if (!target) {
    return;
  }

  state.selectedKeys.clear();
  state.selectedKeys.add(target.key);
  setHoveredTarget(state, target);
  invokeDotNet(state, "OnSelectionChangedFromCanvas", Array.from(state.selectedKeys));
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

  for (const snapPoint of getSnapPoints(entity)) {
    const screenSnapPoint = worldToScreen(state, snapPoint.point);
    const distance = distanceBetweenScreenPoints(screenPoint, screenSnapPoint);
    if (!nearestHit || distance < nearestHit.distance) {
      nearestHit = {
        target: createPointTarget(entity, snapPoint.label, snapPoint.point),
        distance
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
    case "circle":
      return getCircleEdgeHit(state, entity, screenPoint);
    case "arc":
      return getArcEdgeHit(state, entity, screenPoint);
    default:
      return null;
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

  target.closestPoint = screenToWorld(state, projection.point);
  return {
    target,
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
  let closestWorldPoint = null;

  for (let index = 1; index < points.length; index += 1) {
    const projection = closestPointOnScreenSegment(
      screenPoint,
      worldToScreen(state, points[index - 1]),
      worldToScreen(state, points[index]));
    if (projection.distance < closestDistance) {
      closestDistance = projection.distance;
      closestSegmentIndex = index - 1;
      closestWorldPoint = screenToWorld(state, projection.point);
    }
  }

  return closestSegmentIndex < 0
    ? null
    : {
      target: createPolylineSegmentTarget(entity, closestSegmentIndex, closestWorldPoint),
      distance: closestDistance
    };
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
  const closestScreenPoint = vectorLength <= 0.000001
    ? { x: screenCenter.x + screenRadius, y: screenCenter.y }
    : {
      x: screenCenter.x + vectorX / vectorLength * screenRadius,
      y: screenCenter.y + vectorY / vectorLength * screenRadius
    };

  target.closestPoint = screenToWorld(state, closestScreenPoint);
  return {
    target,
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
    const closestWorldPoint = pointOnCircle(center, radius, angleDegrees);
    target.closestPoint = closestWorldPoint;
    return {
      target,
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

  target.closestPoint = nearestEndpoint.point;
  return {
    target,
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

  if (kind === "line" || kind === "polyline") {
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

function getSnapPoints(entity) {
  const kind = getEntityKind(entity);

  switch (kind) {
    case "line":
      return getLineSnapPoints(entity);
    case "polyline":
      return getPolylineSnapPoints(entity);
    case "circle":
      return getCircleSnapPoints(entity);
    case "arc":
      return getArcSnapPoints(entity);
    default:
      return [];
  }
}

function getLineSnapPoints(entity) {
  const points = getEntityPoints(entity);
  if (points.length < 2) {
    return [];
  }

  return [
    { label: "start", point: points[0] },
    { label: "end", point: points[1] },
    { label: "mid", point: midpoint(points[0], points[1]) }
  ];
}

function getPolylineSnapPoints(entity) {
  const points = getEntityPoints(entity);
  const snapPoints = [];

  for (let index = 0; index < points.length; index += 1) {
    snapPoints.push({ label: `vertex-${index}`, point: points[index] });
  }

  for (let index = 1; index < points.length; index += 1) {
    snapPoints.push({ label: `mid-${index - 1}`, point: midpoint(points[index - 1], points[index]) });
  }

  return snapPoints;
}

function getCircleSnapPoints(entity) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);
  if (!center || !isFinitePositive(radius)) {
    return [];
  }

  return [
    { label: "center", point: center },
    { label: "quadrant-0", point: pointOnCircle(center, radius, 0) },
    { label: "quadrant-90", point: pointOnCircle(center, radius, 90) },
    { label: "quadrant-180", point: pointOnCircle(center, radius, 180) },
    { label: "quadrant-270", point: pointOnCircle(center, radius, 270) }
  ];
}

function getArcSnapPoints(entity) {
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

  return snapPoints;
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

function createPolylineSegmentTarget(entity, segmentIndex, closestPoint) {
  const id = getEntityId(entity);
  if (!id) {
    return null;
  }

  return {
    kind: "segment",
    key: `${id}${SEGMENT_KEY_SEPARATOR}${segmentIndex}`,
    entityId: id,
    entity,
    segmentIndex,
    closestPoint
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

function toggleSelectedTarget(state, target) {
  if (state.selectedKeys.has(target.key)) {
    state.selectedKeys.delete(target.key);
  } else {
    state.selectedKeys.add(target.key);
  }
}

function clearSelectedTargets(state) {
  if (state.selectedKeys.size === 0) {
    return false;
  }

  state.selectedKeys.clear();
  return true;
}

function setHoveredTarget(state, target) {
  const previousKey = state.hoveredTarget ? state.hoveredTarget.key : null;
  const nextKey = target ? target.key : null;
  if (previousKey === nextKey) {
    return false;
  }

  state.hoveredTarget = target;
  invokeDotNet(state, "OnEntityHovered", nextKey);
  updateDebugAttributes(state);
  return true;
}

function pruneInteractionState(state) {
  for (const selectedKey of Array.from(state.selectedKeys)) {
    if (!resolveSelectionTarget(state, selectedKey)) {
      state.selectedKeys.delete(selectedKey);
    }
  }

  if (state.hoveredTarget && !resolveSelectionTarget(state, state.hoveredTarget.key)) {
    setHoveredTarget(state, null);
  }

  updateDebugAttributes(state);
}

function updateDebugAttributes(state) {
  const entities = getDocumentEntities(state.document);
  state.canvas.dataset.entityCount = String(entities.length);
  state.canvas.dataset.selectedCount = String(state.selectedKeys.size);
  state.canvas.dataset.selectedKeys = Array.from(state.selectedKeys).join(",");
  state.canvas.dataset.hoveredId = state.hoveredTarget ? state.hoveredTarget.key : "";
  state.canvas.dataset.hoveredKind = state.hoveredTarget ? state.hoveredTarget.kind : "";
  state.canvas.dataset.selectionBoxMode = state.selectionBox
    ? `${state.selectionBox.operation}:${isCrossingSelection(state.selectionBox) ? "crossing" : "window"}`
    : "";
  state.canvas.dataset.originAxes = state.showOriginAxes ? "true" : "false";
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
  return event.button === 1 || event.button === 2 || (event.button === 0 && event.shiftKey);
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

  if (kind === "line" || kind === "polyline") {
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

function distanceBetweenScreenPoints(first, second) {
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

function sanitizeKeyPart(value) {
  return String(value).replaceAll("|", "-");
}

function StringComparer(first, second) {
  return first === second;
}

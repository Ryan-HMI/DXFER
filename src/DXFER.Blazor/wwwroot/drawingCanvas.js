const DEFAULT_FIT_MARGIN = 32;
const HIT_TEST_TOLERANCE = 9;
const CLICK_MOVE_TOLERANCE = 5;
const MIN_VIEW_SCALE = 0.000001;
const MAX_VIEW_SCALE = 1000000;
const FULL_CIRCLE_DEGREES = 360;

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
    hoveredId: null,
    selectedIds: new Set(),
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
    previousTouchAction: canvas.style.touchAction
  };

  const resize = () => resizeCanvas(state);
  const onPointerMove = event => handlePointerMove(state, event);
  const onPointerDown = event => handlePointerDown(state, event);
  const onPointerUp = event => handlePointerUp(state, event);
  const onPointerCancel = event => handlePointerCancel(state, event);
  const onPointerLeave = () => handlePointerLeave(state);
  const onWheel = event => handleWheel(state, event);
  const onContextMenu = event => event.preventDefault();

  state.canvas.style.touchAction = "none";
  state.canvas.addEventListener("pointermove", onPointerMove);
  state.canvas.addEventListener("pointerdown", onPointerDown);
  state.canvas.addEventListener("pointerup", onPointerUp);
  state.canvas.addEventListener("pointercancel", onPointerCancel);
  state.canvas.addEventListener("pointerleave", onPointerLeave);
  state.canvas.addEventListener("wheel", onWheel, { passive: false });
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

    dispose() {
      state.disposed = true;
      state.canvas.removeEventListener("pointermove", onPointerMove);
      state.canvas.removeEventListener("pointerdown", onPointerDown);
      state.canvas.removeEventListener("pointerup", onPointerUp);
      state.canvas.removeEventListener("pointercancel", onPointerCancel);
      state.canvas.removeEventListener("pointerleave", onPointerLeave);
      state.canvas.removeEventListener("wheel", onWheel);
      state.canvas.removeEventListener("contextmenu", onContextMenu);
      window.removeEventListener("resize", resize);

      if (resizeObserver) {
        resizeObserver.disconnect();
      }

      state.canvas.style.touchAction = state.previousTouchAction;
      setHoveredEntity(state, null);
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

  const entities = getDocumentEntities(state.document);
  for (const entity of entities) {
    drawEntity(state, entity, {
      strokeStyle: "#94a3b8",
      lineWidth: 1.5,
      lineDash: []
    });
  }

  for (const entity of entities) {
    const id = getEntityId(entity);
    if (id && state.selectedIds.has(id)) {
      drawEntity(state, entity, {
        strokeStyle: "#38bdf8",
        lineWidth: 4,
        lineDash: []
      });
    }
  }

  if (state.hoveredId) {
    const hoveredEntity = entities.find(entity => getEntityId(entity) === state.hoveredId);
    if (hoveredEntity) {
      const isSelected = state.selectedIds.has(state.hoveredId);
      drawEntity(state, hoveredEntity, {
        strokeStyle: isSelected ? "#fde68a" : "#f59e0b",
        lineWidth: isSelected ? 2 : 3.5,
        lineDash: isSelected ? [6, 4] : []
      });
    }
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
    }
  }

  const nearestId = findNearestEntityId(state, screenPoint);
  if (setHoveredEntity(state, nearestId)) {
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

    const nearestId = findNearestEntityId(state, screenPoint);
    if (setHoveredEntity(state, nearestId)) {
      draw(state);
    }

    return;
  }

  const candidate = state.clickCandidate;
  state.clickCandidate = null;

  if (candidate && candidate.pointerId === event.pointerId && !candidate.cancelled) {
    const moveDistance = distanceBetweenScreenPoints(screenPoint, candidate.screenPoint);
    if (moveDistance <= CLICK_MOVE_TOLERANCE) {
      const clickedId = findNearestEntityId(state, screenPoint);
      if (clickedId) {
        toggleSelectedEntity(state, clickedId);
        invokeDotNet(state, "OnEntityClicked", clickedId);
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
  releasePointer(state.canvas, event.pointerId);

  if (setHoveredEntity(state, null)) {
    draw(state);
  }
}

function handlePointerLeave(state) {
  if (state.panning) {
    return;
  }

  if (setHoveredEntity(state, null)) {
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

  const nearestId = findNearestEntityId(state, screenPoint);
  setHoveredEntity(state, nearestId);
  updateDebugAttributes(state);
  draw(state);
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

function findNearestEntityId(state, screenPoint) {
  let nearestId = null;
  let nearestDistance = Number.POSITIVE_INFINITY;

  for (const entity of getDocumentEntities(state.document)) {
    const id = getEntityId(entity);
    if (!id) {
      continue;
    }

    const distance = getEntityScreenDistance(state, entity, screenPoint);
    if (distance < nearestDistance) {
      nearestDistance = distance;
      nearestId = id;
    }
  }

  return nearestDistance <= HIT_TEST_TOLERANCE ? nearestId : null;
}

function getEntityScreenDistance(state, entity, screenPoint) {
  const kind = getEntityKind(entity);

  switch (kind) {
    case "line":
      return getLineScreenDistance(state, entity, screenPoint);
    case "polyline":
      return getPolylineScreenDistance(state, entity, screenPoint);
    case "circle":
      return getCircleScreenDistance(state, entity, screenPoint);
    case "arc":
      return getArcScreenDistance(state, entity, screenPoint);
    default:
      return Number.POSITIVE_INFINITY;
  }
}

function getLineScreenDistance(state, entity, screenPoint) {
  const points = getEntityPoints(entity);
  if (points.length < 2) {
    return Number.POSITIVE_INFINITY;
  }

  return distanceToScreenSegment(
    screenPoint,
    worldToScreen(state, points[0]),
    worldToScreen(state, points[1]));
}

function getPolylineScreenDistance(state, entity, screenPoint) {
  const points = getEntityPoints(entity);
  if (points.length < 2) {
    return Number.POSITIVE_INFINITY;
  }

  let closest = Number.POSITIVE_INFINITY;

  for (let index = 1; index < points.length; index += 1) {
    closest = Math.min(
      closest,
      distanceToScreenSegment(
        screenPoint,
        worldToScreen(state, points[index - 1]),
        worldToScreen(state, points[index])));
  }

  return closest;
}

function getCircleScreenDistance(state, entity, screenPoint) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);

  if (!center || !isFinitePositive(radius)) {
    return Number.POSITIVE_INFINITY;
  }

  const screenCenter = worldToScreen(state, center);
  const screenRadius = radius * state.view.scale;
  return Math.abs(distanceBetweenScreenPoints(screenPoint, screenCenter) - screenRadius);
}

function getArcScreenDistance(state, entity, screenPoint) {
  const center = getEntityCenter(entity);
  const radius = getEntityRadius(entity);

  if (!center || !isFinitePositive(radius)) {
    return Number.POSITIVE_INFINITY;
  }

  const worldPoint = screenToWorld(state, screenPoint);
  const angleDegrees = radiansToDegrees(Math.atan2(worldPoint.y - center.y, worldPoint.x - center.x));
  const startAngle = getEntityStartAngle(entity);
  const endAngle = getEntityEndAngle(entity);

  if (isAngleOnArc(angleDegrees, startAngle, endAngle)) {
    const screenCenter = worldToScreen(state, center);
    const screenRadius = radius * state.view.scale;
    return Math.abs(distanceBetweenScreenPoints(screenPoint, screenCenter) - screenRadius);
  }

  const endpoints = getArcEndpointWorldPoints(entity);
  if (endpoints.length === 0) {
    return Number.POSITIVE_INFINITY;
  }

  return endpoints.reduce((closest, point) => {
    const screenEndpoint = worldToScreen(state, point);
    return Math.min(closest, distanceBetweenScreenPoints(screenPoint, screenEndpoint));
  }, Number.POSITIVE_INFINITY);
}

function toggleSelectedEntity(state, id) {
  if (state.selectedIds.has(id)) {
    state.selectedIds.delete(id);
  } else {
    state.selectedIds.add(id);
  }
}

function setHoveredEntity(state, id) {
  if (state.hoveredId === id) {
    return false;
  }

  state.hoveredId = id;
  invokeDotNet(state, "OnEntityHovered", id);
  updateDebugAttributes(state);
  return true;
}

function pruneInteractionState(state) {
  const entityIds = new Set(
    getDocumentEntities(state.document)
      .map(entity => getEntityId(entity))
      .filter(id => id));

  for (const selectedId of state.selectedIds) {
    if (!entityIds.has(selectedId)) {
      state.selectedIds.delete(selectedId);
    }
  }

  if (state.hoveredId && !entityIds.has(state.hoveredId)) {
    setHoveredEntity(state, null);
  }

  updateDebugAttributes(state);
}

function updateDebugAttributes(state) {
  const entities = getDocumentEntities(state.document);
  state.canvas.dataset.entityCount = String(entities.length);
  state.canvas.dataset.selectedCount = String(state.selectedIds.size);
  state.canvas.dataset.hoveredId = state.hoveredId || "";
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
    return;
  }

  const invocation = state.dotnetRef.invokeMethodAsync(methodName, ...args);
  if (invocation && typeof invocation.catch === "function") {
    invocation.catch(error => {
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

function distanceToScreenSegment(point, start, end) {
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const lengthSquared = dx * dx + dy * dy;

  if (lengthSquared === 0) {
    return distanceBetweenScreenPoints(point, start);
  }

  const projected = ((point.x - start.x) * dx + (point.y - start.y) * dy) / lengthSquared;
  const clamped = clamp(projected, 0, 1);

  return distanceBetweenScreenPoints(point, {
    x: start.x + dx * clamped,
    y: start.y + dy * clamped
  });
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

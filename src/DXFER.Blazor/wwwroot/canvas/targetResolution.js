const DEFAULT_SNAP_POINT_TOLERANCE = 8;
const DEFAULT_HIT_TEST_TOLERANCE = 9;

export function resolveNearestTargetHit({
  nearestPointHit = null,
  nearestEdgeHit = null,
  dimensionHit = null,
  dynamicPointHit = null,
  constraintTool = null,
  snapPointTolerance = DEFAULT_SNAP_POINT_TOLERANCE,
  hitTestTolerance = DEFAULT_HIT_TEST_TOLERANCE,
  edgeFallback = true
} = {}) {
  if (nearestPointHit && nearestPointHit.distance <= snapPointTolerance) {
    return nearestPointHit.target;
  }

  if (constraintTool) {
    return nearestEdgeHit && nearestEdgeHit.distance <= hitTestTolerance
      ? nearestEdgeHit.target
      : null;
  }

  if (dimensionHit && dimensionHit.distance <= hitTestTolerance) {
    return dimensionHit.target;
  }

  if (dynamicPointHit && dynamicPointHit.distance <= snapPointTolerance) {
    return dynamicPointHit.target;
  }

  if (edgeFallback && nearestEdgeHit && nearestEdgeHit.distance <= hitTestTolerance) {
    return nearestEdgeHit.target;
  }

  return null;
}

export function getPowerTrimAlternateEdgeHit(pointHit, edgeHits, {
  snapPointTolerance = DEFAULT_SNAP_POINT_TOLERANCE,
  hitTestTolerance = DEFAULT_HIT_TEST_TOLERANCE,
  getEntityKind = defaultGetEntityKind
} = {}) {
  if (!pointHit
    || pointHit.distance > snapPointTolerance
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
      && hit.distance <= hitTestTolerance
      && hit.target
      && hit.target.entityId !== pointHit.target.entityId)
    .sort((first, second) => first.distance - second.distance)[0] || null;
}

function defaultGetEntityKind(entity) {
  const kind = entity && (entity.kind ?? entity.Kind);
  return kind === null || kind === undefined ? "" : String(kind).toLowerCase();
}

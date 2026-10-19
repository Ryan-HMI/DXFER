const SNAP_TOLERANCE = 0.000001;
const POINT_RELATIONS = new Set(["Coincident", "Midpoint", "PointOnLine", "Horizontal", "Vertical"]);

function samePoint(first, second) {
  return first && second
    && Number.isFinite(first.x) && Number.isFinite(first.y)
    && Number.isFinite(second.x) && Number.isFinite(second.y)
    && Math.hypot(first.x - second.x, first.y - second.y) <= SNAP_TOLERANCE;
}

function normalizeTool(tool) {
  return String(tool || "").replace(/[-_]/g, "").toLowerCase();
}

// pointCount is the number of accepted draft points before the current pick.
// Projected picks need more geometry than this API receives, so remain guide-only.
function hasPersistentDestination(tool, pointCount) {
  if (!Number.isInteger(pointCount) || pointCount < 0) return false;
  switch (tool) {
    case "point": return pointCount === 0;
    case "line":
    case "twopointrectangle": return pointCount <= 1;
    case "midpointline":
    case "centerrectangle": return pointCount === 1;
    case "alignedrectangle":
    case "slot": return pointCount <= 1;
    case "centercircle":
    case "ellipse":
    case "ellipticalarc":
    case "inscribedpolygon":
    case "circumscribedpolygon":
    case "spline":
    case "splinecontrolpoint": return pointCount === 0;
    case "threepointarc":
    case "tangentarc": return pointCount === 0 || pointCount === 2;
    case "centerpointarc": return pointCount <= 1;
    case "bezier": return pointCount === 0 || pointCount === 3;
    default: return false;
  }
}

function snapTypeFor(target) {
  const label = String(target.label || "").toLowerCase();
  const entityKind = String(target.entity?.kind || target.entity?.Kind || "").toLowerCase();
  if (target.entityId === "$origin" || target.key === "$origin:center") return "Origin";
  if (target.tangentReferenceKey || /^(circle-tangent|tangent)(-|$)/.test(label)) return "Tangent";
  if (/^normal(-|$)/.test(label)) return "Normal";
  if (/^perpendicular(-|$)/.test(label)) return "Perpendicular";
  if (/^project-v-/.test(label) || /^project-vertical-/.test(label)) return "Vertical guide";
  if (/^project-h-/.test(label) || /^project-horizontal-/.test(label)) return "Horizontal guide";
  if (/^project-/.test(label)) return "Guide intersection";
  if (/^intersection-/.test(label)) return "Intersection";
  if (label === "mid" || /^(mid-|midpoint-)/.test(label)) return "Midpoint";
  if (label === "center") return "Center";
  if (label === "start" || label === "end") return "Endpoint";
  if (/^vertex-/.test(label)) return "Vertex";
  if (/^quadrant-/.test(label)) return "Quadrant";
  if (/^(major|minor)(-|$)/.test(label)) return "Axis endpoint";
  if (/^(control|fit)-/.test(label)) return "Control point";
  if (entityKind === "point") return "Point";
  if (target.kind === "segment" || entityKind === "line") return "On line";
  if (["circle", "arc", "ellipse", "spline"].includes(entityKind)) return "On curve";
  return "Snap";
}

function hasSupportedCoincidentSource(target) {
  const kind = String(target.entity?.kind || target.entity?.Kind || "").toLowerCase();
  // These entities expose a persistent center, not their displayed perimeter/axis handles.
  return !["circle", "ellipse", "polygon"].includes(kind)
    || String(target.label || "").toLowerCase() === "center";
}

function finitePoint(point) {
  return point && Number.isFinite(point.x) && Number.isFinite(point.y);
}

function closeFeature(first, second) {
  return finitePoint(first) && finitePoint(second)
    && Math.abs(first.x - second.x) <= SNAP_TOLERANCE && Math.abs(first.y - second.y) <= SNAP_TOLERANCE;
}

function onArc(entity, contact) {
  const start = entity.startAngleDegrees, end = entity.endAngleDegrees;
  if (!Number.isFinite(start) || !Number.isFinite(end) || !Number.isFinite(end - start)) return false;
  if (end - start >= 360) return true;
  const normalize = angle => ((angle % 360) + 360) % 360;
  const sweep = normalize(end - start);
  if (sweep === 0) return false;
  const angle = Math.atan2(contact.y - entity.center.y, contact.x - entity.center.x) * 180 / Math.PI;
  if (normalize(angle - normalize(start)) <= sweep) return true;
  return [start, end].some(a => Math.hypot(
    contact.x - entity.center.x - entity.radius * Math.cos(a * Math.PI / 180),
    contact.y - entity.center.y - entity.radius * Math.sin(a * Math.PI / 180)) <= SNAP_TOLERANCE);
}

// Matches the creation factory's finite-contact and endpoint/midpoint rules.
// Dynamic targets without source geometry cannot be verified by this descriptor.
export function getInferredLineConstraintKinds({ target, point, tool, pointCount, draftPoints } = {}) {
  tool = normalizeTool(tool);
  if (!["line", "midpointline"].includes(tool) || pointCount !== 1) return [];
  const anchor = draftPoints?.[0], entity = target?.entity;
  if (!entity || !finitePoint(anchor)) return null;
  if (!finitePoint(point)) return [];
  const start = tool === "midpointline" ? { x: 2 * anchor.x - point.x, y: 2 * anchor.y - point.y } : anchor;
  const length = Math.hypot(point.x - start.x, point.y - start.y);
  if (!finitePoint(start) || !Number.isFinite(length) || length <= SNAP_TOLERANCE) return [];
  const dx = (point.x - start.x) / length, dy = (point.y - start.y) / length;
  const kind = String(entity.kind || "").toLowerCase();
  if (kind === "circle" || kind === "arc") {
    const { center, radius } = entity;
    if (!finitePoint(center) || !Number.isFinite(radius) || radius <= SNAP_TOLERANCE) return [];
    const along = (center.x - start.x) * dx + (center.y - start.y) * dy;
    if (!Number.isFinite(along) || along < -SNAP_TOLERANCE || along > length + SNAP_TOLERANCE) return [];
    const contact = { x: start.x + along * dx, y: start.y + along * dy };
    return Math.abs(Math.hypot(contact.x - center.x, contact.y - center.y) - radius) <= SNAP_TOLERANCE
      && (kind !== "arc" || onArc(entity, contact)) ? ["Tangent"] : [];
  }
  if (kind !== "line" && kind !== "polyline") return [];
  const points = entity.points;
  if (!Array.isArray(points)) return [];
  for (let index = 1; index < points.length; index++) {
    const a = points[index - 1], b = points[index];
    if (!finitePoint(a) || !finitePoint(b)) continue;
    const peerLength = Math.hypot(b.x - a.x, b.y - a.y);
    if (!Number.isFinite(peerLength) || peerLength <= SNAP_TOLERANCE) continue;
    const mid = { x: (a.x + b.x) / 2, y: (a.y + b.y) / 2 };
    if (![a, b, mid].some(feature => closeFeature(start, feature) || closeFeature(point, feature))) continue;
    if (Math.abs(dx * (b.x - a.x) / peerLength + dy * (b.y - a.y) / peerLength) <= SNAP_TOLERANCE)
      return ["Perpendicular"];
  }
  return [];
}

export function getSnapCue({ target, point, tool, pointCount, intents = [], automaticConstraints = false, draftPoints } = {}) {
  if (!target || !samePoint(target.point || target.snapPoint, point)) return null;
  const normalizedTool = normalizeTool(tool);
  const snapType = snapTypeFor(target);
  const constraintKinds = [];
  let uncertainInference = false;
  if (automaticConstraints === true) {
    if (hasPersistentDestination(normalizedTool, pointCount) && Array.isArray(intents)) {
      for (const intent of intents) {
        if (!intent || !POINT_RELATIONS.has(intent.kind) || !samePoint(intent.point, point)
          || !Array.isArray(intent.referenceKeys) || intent.referenceKeys.length !== 1
          || typeof intent.referenceKeys[0] !== "string" || !intent.referenceKeys[0].trim()
          || intent.kind === "Coincident" && !hasSupportedCoincidentSource(target)) continue;
        if (!constraintKinds.includes(intent.kind)) constraintKinds.push(intent.kind);
      }
    }
    if (normalizedTool === "centercircle" && pointCount === 1
      && typeof target.tangentReferenceKey === "string" && target.tangentReferenceKey.trim()) {
      constraintKinds.push("Tangent");
    }
    const inferred = getInferredLineConstraintKinds({ target, point, tool: normalizedTool, pointCount, draftPoints });
    for (const kind of inferred || []) if (!constraintKinds.includes(kind)) constraintKinds.push(kind);
    uncertainInference = inferred === null && ["Tangent", "Perpendicular"].includes(snapType);
  }
  const detail = constraintKinds.length
    ? `Pending: ${constraintKinds.join(" + ")}`
    : automaticConstraints === false ? "Snap only (auto constraints off)"
      : uncertainInference ? "Constraint evaluated on commit" : "Snap only";
  return {
    snapType,
    constraintKinds,
    detail,
    text: `${snapType} - ${detail}`
  };
}

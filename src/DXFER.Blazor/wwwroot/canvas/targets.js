import {
  distanceBetweenScreenPoints
} from "./geometry.js";

const SNAP_POINT_TOLERANCE = 8;

export function shouldDrawSelectionTargetOverlay(target) {
  return Boolean(target) && target.kind !== "dimension" && target.kind !== "constraint";
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

export function isPanPointerDownForTool(event, toolName) {
  return event.button === 1;
}

export function isTargetEligibleForConstraintTool(tool, target) {
  const normalizedTool = normalizeToolName(tool);
  if (!normalizedTool || !target || target.dynamic || target.kind === "dimension" || target.kind === "constraint") {
    return false;
  }

  const kind = getTargetEntityKind(target);
  const isPoint = isEditableConstraintPointTarget(target, kind);
  const isLine = target.kind === "segment" || (target.kind === "entity" && kind === "line");
  const isCircleLike = kind === "circle" || kind === "arc";

  switch (normalizedTool) {
    case "coincident":
      return isPoint;
    case "horizontal":
    case "vertical":
      return isPoint || isLine;
    case "parallel":
    case "perpendicular":
      return isLine;
    case "tangent":
      return isLine || isCircleLike;
    case "concentric":
      return isCircleLike;
    case "equal":
      return isLine || isCircleLike;
    case "midpoint":
      return isPoint || isLine;
    case "fix":
      return target.kind === "entity" || target.kind === "segment" || isPoint;
    default:
      return false;
  }
}

function normalizeToolName(toolName) {
  return String(toolName || "select").replace(/[-_\s]/g, "").toLowerCase();
}

function worldToScreen(state, point) {
  return {
    x: point.x * state.view.scale + state.view.offsetX,
    y: -point.y * state.view.scale + state.view.offsetY
  };
}

function getTargetEntityKind(target) {
  const entity = target && target.entity;
  if (!entity) {
    return "";
  }

  const kind = entity.kind ?? entity.Kind;
  return kind === null || kind === undefined ? "" : String(kind).toLowerCase();
}

function isEditableConstraintPointTarget(target, kind) {
  if (target.kind === "entity" && kind === "point") {
    return true;
  }

  if (target.kind !== "point" || !target.entity) {
    return false;
  }

  const label = String(target.label || "").split("|")[0].toLowerCase();
  return label === "start" || label === "end" || label === "center";
}

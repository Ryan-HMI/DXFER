import {
  clamp
} from "./geometry.js";

const DIMENSION_LAYER_STROKE_STYLE = "#d5dde8";
const DIMENSION_LAYER_TEXT_STYLE = "#f3f6fb";
const DIMENSION_PREVIEW_STROKE_STYLE = "#facc15";

export function getDimensionRenderStyle(isPreview, visualState = null) {
  const normalizedVisualState = normalizeDimensionVisualState(visualState);
  if (!isPreview && normalizedVisualState.unsatisfied) {
    return {
      strokeStyle: "#f87171",
      textStyle: "#fecaca",
      lineDash: [],
      lineWidth: normalizedVisualState.selected ? 2.05 : 1.65,
      glow: false
    };
  }

  if (!isPreview && normalizedVisualState.active) {
    return {
      strokeStyle: "#bae6fd",
      textStyle: "#f8fdff",
      lineDash: [],
      lineWidth: 1.95,
      glow: true
    };
  }

  if (!isPreview && normalizedVisualState.selected) {
    return {
      strokeStyle: "#7dd3fc",
      textStyle: "#e0f2fe",
      lineDash: [],
      lineWidth: 1.7,
      glow: true
    };
  }

  if (!isPreview && normalizedVisualState.hovered) {
    return {
      strokeStyle: "#e2e8f0",
      textStyle: "#ffffff",
      lineDash: [],
      lineWidth: 1.45,
      glow: false
    };
  }

  return {
    strokeStyle: isPreview ? DIMENSION_PREVIEW_STROKE_STYLE : DIMENSION_LAYER_STROKE_STYLE,
    textStyle: isPreview ? DIMENSION_PREVIEW_STROKE_STYLE : DIMENSION_LAYER_TEXT_STYLE,
    lineDash: isPreview ? [5, 4] : [],
    lineWidth: 1.15,
    glow: false
  };
}

export function getDimensionDisplayText(dimension) {
  if (String(dimension && dimension.kind || "").toLowerCase() === "count") {
    const value = Number(dimension && dimension.value);
    return Number.isFinite(value) ? String(clamp(Math.round(value), 3, 64)) : "";
  }

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

export function getDimensionLabel(kind) {
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
    case "count":
      return "Sides";
    default:
      return "Distance";
  }
}

export function getDimensionInputClassName(options = {}) {
  const classes = ["drawing-dimension-input"];
  if (options.persistent) {
    classes.push("drawing-persistent-dimension-input");
  }

  if (options.editing) {
    classes.push("drawing-dimension-input-active");
  }

  if (options.selected) {
    classes.push("drawing-persistent-dimension-input-selected");
  }

  if (options.active) {
    classes.push("drawing-persistent-dimension-input-active");
  }

  if (options.unsatisfied) {
    classes.push("drawing-persistent-dimension-input-unsatisfied");
  }

  return classes.join(" ");
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

export function formatDimensionValue(value) {
  if (!Number.isFinite(value)) {
    return "";
  }

  const rounded = Math.round(value * 1000) / 1000;
  return rounded.toFixed(3).replace(/\.?0+$/, "");
}

function normalizeDimensionVisualState(visualState) {
  if (!visualState) {
    return {
      selected: false,
      active: false,
      hovered: false,
      unsatisfied: false
    };
  }

  if (typeof visualState === "boolean") {
    return {
      selected: visualState,
      active: false,
      hovered: false,
      unsatisfied: false
    };
  }

  return {
    selected: Boolean(visualState.selected || visualState.active),
    active: Boolean(visualState.active),
    hovered: Boolean(visualState.hovered),
    unsatisfied: Boolean(visualState.unsatisfied)
  };
}

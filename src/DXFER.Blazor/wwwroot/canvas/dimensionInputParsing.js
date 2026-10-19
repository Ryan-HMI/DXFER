import { formatDimensionValue } from "./dimensionPresentation.js?v=20261019-context-dimensions";

const WORLD_GEOMETRY_TOLERANCE = 0.000001;

export function getPersistentDimensionCommitValue(text, fallbackValue, hasUserEdit = false) {
  const fallback = Number(fallbackValue);
  const safeFallback = Number.isFinite(fallback) ? fallback : 0;
  const value = parseDimensionInputNumber(text);
  if (!Number.isFinite(value) || value <= WORLD_GEOMETRY_TOLERANCE
    || value === fallback
    || (!hasUserEdit && Number.isFinite(fallback)
      && value === parseDimensionInputNumber(formatDimensionValue(fallback)))) {
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

export function parseDimensionInputNumber(text) {
  const match = String(text || "")
    .trim()
    .match(/[+-]?(?:\d+(?:\.\d*)?|\.\d+)/);
  return match ? Number(match[0]) : Number.NaN;
}

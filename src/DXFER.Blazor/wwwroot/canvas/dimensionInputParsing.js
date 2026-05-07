const WORLD_GEOMETRY_TOLERANCE = 0.000001;

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

export function parseDimensionInputNumber(text) {
  const match = String(text || "")
    .trim()
    .match(/[+-]?(?:\d+(?:\.\d*)?|\.\d+)/);
  return match ? Number(match[0]) : Number.NaN;
}

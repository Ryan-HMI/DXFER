export const SEGMENT_KEY_SEPARATOR = "|segment|";
export const POINT_KEY_SEPARATOR = "|point|";
export const CONSTRAINT_KEY_PREFIX = "constraint:";

export function parsePointTargetKeyParts(key) {
  const normalizedKey = String(key || "");
  const separatorIndex = normalizedKey.indexOf(POINT_KEY_SEPARATOR);
  if (separatorIndex < 0) {
    return null;
  }

  const entityId = normalizedKey.slice(0, separatorIndex);
  const parts = normalizedKey.slice(separatorIndex + POINT_KEY_SEPARATOR.length).split("|");
  if (parts.length < 3) {
    return null;
  }

  const x = Number(parts[parts.length - 2]);
  const y = Number(parts[parts.length - 1]);
  if (!Number.isFinite(x) || !Number.isFinite(y)) {
    return null;
  }

  return {
    entityId,
    label: parts.slice(0, parts.length - 2).join("|"),
    point: { x, y }
  };
}

export function parseSegmentTargetKeyParts(key) {
  const normalizedKey = String(key || "");
  const separatorIndex = normalizedKey.indexOf(SEGMENT_KEY_SEPARATOR);
  if (separatorIndex < 0) {
    return null;
  }

  const entityId = normalizedKey.slice(0, separatorIndex);
  const segmentIndex = Number(normalizedKey.slice(separatorIndex + SEGMENT_KEY_SEPARATOR.length));
  if (!Number.isInteger(segmentIndex)) {
    return null;
  }

  return {
    entityId,
    segmentIndex
  };
}

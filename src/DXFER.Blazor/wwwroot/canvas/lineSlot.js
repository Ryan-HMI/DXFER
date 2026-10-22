import {parsePointTargetKeyParts} from './targetKeys.js';

const tolerance = 1e-6;
const finitePoint = p => p && Number.isFinite(p.x) && Number.isFinite(p.y);

export function lineSlotDraft(document, referenceKeys) {
  if (referenceKeys.length !== 1) return {reason: 'Select exactly one existing straight line for the slot centerline.'};
  const key = referenceKeys[0];
  const point = parsePointTargetKeyParts(key);
  const entityId = point && ['start', 'end', 'mid'].includes(point.label) ? point.entityId : key;
  const entity = document?.entities?.find(e => e.id === entityId);
  if (entity?.kind !== 'line' || entity.points?.length !== 2 || !entity.points.every(finitePoint))
    return {reason: 'Slot requires an existing straight LINE, not an arc, point, or polyline.'};
  if (entity.isLocked) return {reason: 'Unlock the source line before creating a slot.'};
  const [start, end] = entity.points;
  const length = Math.hypot(end.x - start.x, end.y - start.y);
  if (!Number.isFinite(length) || length <= tolerance)
    return {reason: 'The slot centerline must have a positive length.'};
  return {draft: {centerlineReferenceKey: entity.id, sourceIsConstruction: Boolean(entity.isConstruction), points: entity.points.map(p => ({...p})),
    previewPoint: null, dimensionValues: {}}};
}

export function lineSlotSourceIsCurrent(document, draft) {
  const current = lineSlotDraft(document, [draft?.centerlineReferenceKey]).draft;
  return Boolean(current && current.sourceIsConstruction === draft.sourceIsConstruction
    && draft.points?.length === 2 && current.points.every((p, i) =>
    p.x === draft.points[i].x && p.y === draft.points[i].y));
}

export function lineSlotPreview(start, end, pointer) {
  if (![start, end, pointer].every(finitePoint)) return null;
  const dx = end.x - start.x, dy = end.y - start.y, length = Math.hypot(dx, dy);
  if (!Number.isFinite(length) || length <= tolerance) return null;
  const normal = {x: -dy / length, y: dx / length};
  const radius = Math.abs((pointer.x - start.x) * normal.x + (pointer.y - start.y) * normal.y);
  if (!Number.isFinite(radius) || radius <= tolerance) return null;
  const heading = Math.atan2(dy, dx), points = [];
  for (const [center, angle] of [[end, heading + Math.PI / 2], [start, heading - Math.PI / 2]]) {
    for (let i = 0; i <= 24; i++) points.push({x: center.x + radius * Math.cos(angle - i * Math.PI / 24),
      y: center.y + radius * Math.sin(angle - i * Math.PI / 24)});
  }
  if (!points.every(finitePoint)) return null;
  const middle = {x: start.x / 2 + end.x / 2, y: start.y / 2 + end.y / 2};
  return {points, width: radius * 2,
    widthStart: {x: middle.x - normal.x * radius, y: middle.y - normal.y * radius},
    widthEnd: {x: middle.x + normal.x * radius, y: middle.y + normal.y * radius}};
}

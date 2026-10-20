// Guide geometry only; the existing pattern editor owns all parameter changes.
export function patternViewportGuides(data) {
  const values = ['sourceX', 'sourceY', 'pitchX', 'pitchY', 'angle', 'centerX', 'centerY', 'step'];
  const n = Object.fromEntries(values.map(key => [key, Number(data[key])]));
  if (Object.values(n).some(value => !Number.isFinite(value))) return [];
  const source = { x: n.sourceX, y: n.sourceY };
  if (data.patternKind === 'Radial') {
    const center = { x: n.centerX, y: n.centerY }, angle = n.step * Math.PI / 180;
    const x = source.x - center.x, y = source.y - center.y;
    return [{ start: center, end: source }, { start: center,
      end: { x: center.x + x * Math.cos(angle) - y * Math.sin(angle), y: center.y + x * Math.sin(angle) + y * Math.cos(angle) } }];
  }
  const angle = n.angle * Math.PI / 180;
  return [{ start: source, end: { x: source.x + n.pitchX * Math.cos(angle), y: source.y + n.pitchX * Math.sin(angle) } },
    { start: source, end: { x: source.x - n.pitchY * Math.sin(angle), y: source.y + n.pitchY * Math.cos(angle) } }];
}

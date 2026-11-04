const point = (x, y) => ({ x, y });
const line = (id, start, end) => ({ id, kind: 'line', points: [start, end] });

export function shortSlot({ angle = 37, length = 1, width = 20, horizontal = false } = {}) {
  const rotation = angle * Math.PI / 180;
  const c = Math.cos(rotation), s = Math.sin(rotation), radius = width / 2;
  const at = (x, y) => point(c * x - s * y, s * x + c * y);
  const entities = [
    { ...line('axis', at(0, 0), at(length, 0)), isConstruction: true },
    line('left', at(0, radius), at(length, radius)),
    { id: 'endArc', kind: 'arc', center: at(length, 0), radius,
      startAngleDegrees: angle - 90, endAngleDegrees: angle + 90 },
    line('right', at(length, -radius), at(0, -radius)),
    { id: 'startArc', kind: 'arc', center: at(0, 0), radius,
      startAngleDegrees: angle + 90, endAngleDegrees: angle + 270 }
  ];
  const constraints = [];
  const add = (kind, ...referenceKeys) => constraints.push({ id: `c${constraints.length}`, kind, referenceKeys, state: 'satisfied' });
  add('Coincident', 'axis:start', '$origin:center');
  if (horizontal) {
    add('Horizontal', 'axis');
    add('Horizontal', 'axis:start', 'axis:end');
  }
  add('Coincident', 'startArc:center', 'axis:start');
  add('Coincident', 'endArc:center', 'axis:end');
  add('Equal', 'startArc', 'endArc');
  add('Coincident', 'left:end', 'endArc:end');
  add('Coincident', 'right:start', 'endArc:start');
  add('Coincident', 'right:end', 'startArc:end');
  add('Coincident', 'left:start', 'startArc:start');
  add('Tangent', 'left', 'endArc');
  add('Tangent', 'right', 'endArc');
  add('Tangent', 'left', 'startArc');
  add('Tangent', 'right', 'startArc');
  const dimensions = [
    { id: 'length', kind: 'LinearDistance', referenceKeys: ['axis'], value: length, isDriving: true },
    { id: 'width', kind: 'PointToLineDistance', referenceKeys: ['left:start', 'right'], value: width, isDriving: true }
  ];
  return { entities, constraints, dimensions };
}

export function fourBar({ rightAngle = 80, ground = 14.583333333333334, left = 8, top = 12, right = 10 } = {}) {
  const angle = rightAngle * Math.PI / 180;
  const a = point(0, 0), b = point(ground, 0);
  const d = point(b.x + right * Math.cos(angle), right * Math.sin(angle));
  const diagonal = Math.hypot(d.x, d.y);
  const projected = (left ** 2 - top ** 2 + diagonal ** 2) / (2 * diagonal);
  const height = Math.sqrt(left ** 2 - projected ** 2);
  if (!Number.isFinite(height)) throw new Error('Unreachable four-bar baseline');
  const c = point(projected * d.x / diagonal - height * d.y / diagonal,
    projected * d.y / diagonal + height * d.x / diagonal);
  return {
    entities: [line('ground', a, b), line('left', a, c), line('top', c, d), line('right', b, d)],
    constraints: [
      { id: 'fixed-ground', kind: 'Fix', referenceKeys: ['ground'] },
      { id: 'horizontal-ground', kind: 'Horizontal', referenceKeys: ['ground'] },
      ...[['ground:start', 'left:start'], ['left:end', 'top:start'],
        ['top:end', 'right:end'], ['right:start', 'ground:end']].map((referenceKeys, i) =>
        ({ id: `joint${i}`, kind: 'Coincident', referenceKeys }))
    ],
    dimensions: [ ['left', left], ['top', top], ['right', right] ].map(([id, value]) =>
      ({ id: `${id}-length`, kind: 'LinearDistance', referenceKeys: [id], value, isDriving: true }))
  };
}

// Canvas counterpart of SplineEntity.Evaluate/BuildSamplePoints. Sample the
// native rational spline for display; never replace its control representation.
export function sampleControlSpline(points, degree, knots, weights) {
  if (!Array.isArray(points) || !Number.isInteger(degree) || degree < 1 || points.length <= degree
    || !Array.isArray(knots) || knots.length !== points.length + degree + 1
    || !Array.isArray(weights) || weights.length !== points.length
    || points.some(point => !Number.isFinite(point.x) || !Number.isFinite(point.y))
    || weights.some(weight => !Number.isFinite(weight) || weight <= 0)
    || knots.some((knot, index) => !Number.isFinite(knot) || index > 0 && knot < knots[index - 1])
    || knots[points.length] <= knots[degree]) {
    return null;
  }

  if (degree === 1) return points.map(point => ({ ...point }));
  let spanCount = 0;
  for (let index = degree; index < points.length; index++) {
    if (knots[index + 1] > knots[index]) spanCount++;
  }
  const intervals = Math.min(256, Math.max(24, spanCount * 16));
  const start = knots[degree];
  const end = knots[points.length];
  const samples = [];
  for (let sample = 0; sample <= intervals; sample++) {
    const parameter = sample === intervals ? end : start + (end - start) * sample / intervals;
    let span = points.length - 1;
    if (parameter < end) {
      let low = degree;
      let high = points.length;
      while (low + 1 < high) {
        const middle = Math.floor((low + high) / 2);
        if (parameter < knots[middle]) high = middle;
        else low = middle;
      }
      span = low;
    }
    const weighted = [];
    for (let index = 0; index <= degree; index++) {
      const control = span - degree + index;
      const weight = weights[control];
      weighted.push([points[control].x * weight, points[control].y * weight, weight]);
    }
    for (let level = 1; level <= degree; level++) {
      for (let index = degree; index >= level; index--) {
        const knot = span - degree + index;
        const denominator = knots[knot + degree - level + 1] - knots[knot];
        const alpha = denominator === 0 ? 0 : (parameter - knots[knot]) / denominator;
        for (let component = 0; component < 3; component++) {
          weighted[index][component] = (1 - alpha) * weighted[index - 1][component] + alpha * weighted[index][component];
        }
      }
    }
    const result = weighted[degree];
    const point = { x: result[0] / result[2], y: result[1] / result[2] };
    if (!Number.isFinite(point.x) || !Number.isFinite(point.y)) return null;
    const previous = samples.at(-1);
    if (!previous || Math.abs(previous.x - point.x) > 0.000001 || Math.abs(previous.y - point.y) > 0.000001) {
      samples.push(point);
    }
  }
  return samples.length >= 2 ? samples : null;
}

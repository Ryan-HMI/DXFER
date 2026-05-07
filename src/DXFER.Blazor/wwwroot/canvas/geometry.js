const WORLD_GEOMETRY_TOLERANCE = 0.000001;
const FULL_CIRCLE_DEGREES = 360;

export function midpoint(start, end) {
  return {
    x: (start.x + end.x) / 2,
    y: (start.y + end.y) / 2
  };
}

export function mirrorPoint(center, point) {
  return {
    x: (2 * center.x) - point.x,
    y: (2 * center.y) - point.y
  };
}

export function subtractPoints(first, second) {
  return {
    x: first.x - second.x,
    y: first.y - second.y
  };
}

export function subtractScreenPoints(first, second) {
  return subtractPoints(first, second);
}

export function dotPoints(first, second) {
  return first.x * second.x + first.y * second.y;
}

export function dotScreenPoints(first, second) {
  return dotPoints(first, second);
}

export function normalizeScreenVector(vector) {
  const length = Math.hypot(vector.x, vector.y);
  return length <= WORLD_GEOMETRY_TOLERANCE
    ? null
    : {
      x: vector.x / length,
      y: vector.y / length
    };
}

export function projectPointToWorldLine(point, line) {
  const deltaX = line.end.x - line.start.x;
  const deltaY = line.end.y - line.start.y;
  const lengthSquared = deltaX * deltaX + deltaY * deltaY;
  if (lengthSquared <= WORLD_GEOMETRY_TOLERANCE) {
    return line.start;
  }

  const scalar = (((point.x - line.start.x) * deltaX) + ((point.y - line.start.y) * deltaY)) / lengthSquared;
  return {
    x: line.start.x + deltaX * scalar,
    y: line.start.y + deltaY * scalar
  };
}

export function getWorldLineIntersection(first, second) {
  if (!first || !second || !first.start || !first.end || !second.start || !second.end) {
    return null;
  }

  const firstVector = subtractPoints(first.end, first.start);
  const secondVector = subtractPoints(second.end, second.start);
  const denominator = crossPoints(firstVector, secondVector);
  if (Math.abs(denominator) <= WORLD_GEOMETRY_TOLERANCE) {
    return null;
  }

  const offset = subtractPoints(second.start, first.start);
  const scalar = crossPoints(offset, secondVector) / denominator;
  return {
    x: first.start.x + firstVector.x * scalar,
    y: first.start.y + firstVector.y * scalar
  };
}

export function angleBetweenWorldLines(first, second) {
  const firstAngle = Math.atan2(first.end.y - first.start.y, first.end.x - first.start.x);
  const secondAngle = Math.atan2(second.end.y - second.start.y, second.end.x - second.start.x);
  const delta = Math.abs(radiansToDegrees(secondAngle - firstAngle)) % 180;
  return delta > 90 ? 180 - delta : delta;
}

export function areWorldLinesParallel(first, second) {
  if (!first || !second || !first.start || !first.end || !second.start || !second.end) {
    return false;
  }

  const firstVector = subtractPoints(first.end, first.start);
  const secondVector = subtractPoints(second.end, second.start);
  const firstLength = Math.hypot(firstVector.x, firstVector.y);
  const secondLength = Math.hypot(secondVector.x, secondVector.y);
  if (firstLength <= WORLD_GEOMETRY_TOLERANCE || secondLength <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

  return Math.abs(crossPoints(firstVector, secondVector)) <= WORLD_GEOMETRY_TOLERANCE * firstLength * secondLength;
}

export function getParallelWorldLineDistance(first, second) {
  const point = midpoint(second.start, second.end);
  return distanceBetweenWorldPoints(point, projectPointToWorldLine(point, first));
}

export function crossPoints(first, second) {
  return first.x * second.y - first.y * second.x;
}

export function addUniquePoint(points, point) {
  const duplicate = points.some(existing =>
    distanceBetweenWorldPoints(existing, point) <= WORLD_GEOMETRY_TOLERANCE);

  if (!duplicate) {
    points.push(point);
  }
}

export function addUniqueNumber(values, value, tolerance = 0.000001) {
  if (!Number.isFinite(value)) {
    return;
  }

  const normalizedValue = normalizeAngleDegrees(value);
  const duplicate = values.some(existing => {
    const delta = Math.abs(normalizeAngleDegrees(existing) - normalizedValue);
    return Math.min(delta, FULL_CIRCLE_DEGREES - delta) <= tolerance;
  });
  if (!duplicate) {
    values.push(value);
  }
}

export function solveQuadratic(a, b, c) {
  if (Math.abs(a) <= WORLD_GEOMETRY_TOLERANCE) {
    if (Math.abs(b) <= WORLD_GEOMETRY_TOLERANCE) {
      return [];
    }

    return [-c / b];
  }

  const discriminant = (b * b) - (4 * a * c);
  if (discriminant < -WORLD_GEOMETRY_TOLERANCE) {
    return [];
  }

  if (Math.abs(discriminant) <= WORLD_GEOMETRY_TOLERANCE) {
    return [-b / (2 * a)];
  }

  const root = Math.sqrt(Math.max(0, discriminant));
  return [
    (-b - root) / (2 * a),
    (-b + root) / (2 * a)
  ];
}

export function isCircleTangentToLinearSegmentAtPoint(circle, segment, point) {
  const direction = subtractPoints(segment.end, segment.start);
  const radius = subtractPoints(point, circle.center);
  const directionLength = Math.hypot(direction.x, direction.y);
  const radiusLength = Math.hypot(radius.x, radius.y);
  if (directionLength <= WORLD_GEOMETRY_TOLERANCE || radiusLength <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

  return Math.abs(dotPoints(direction, radius) / (directionLength * radiusLength)) <= 0.000001;
}

export function isCircleTangentToCurveAtPoint(circle, curveCenter, point) {
  const candidateRadius = subtractPoints(point, circle.center);
  const targetRadius = subtractPoints(point, curveCenter);
  const candidateLength = Math.hypot(candidateRadius.x, candidateRadius.y);
  const targetLength = Math.hypot(targetRadius.x, targetRadius.y);
  if (candidateLength <= WORLD_GEOMETRY_TOLERANCE || targetLength <= WORLD_GEOMETRY_TOLERANCE) {
    return false;
  }

  return Math.abs(crossPoints(candidateRadius, targetRadius) / (candidateLength * targetLength)) <= 0.000001;
}

export function isUnitParameter(parameter) {
  return parameter >= -WORLD_GEOMETRY_TOLERANCE && parameter <= 1 + WORLD_GEOMETRY_TOLERANCE;
}

export function isPointOnSegmentPrimitive(point, segment) {
  const projection = closestPointOnWorldSegment(point, segment.start, segment.end);
  return projection
    && isUnitParameter(projection.parameter)
    && projection.distance <= WORLD_GEOMETRY_TOLERANCE;
}

export function isPointOnCurvePrimitive(point, curve) {
  const distance = distanceBetweenWorldPoints(point, curve.center);
  const radialTolerance = Math.max(WORLD_GEOMETRY_TOLERANCE, curve.radius * 0.000001);
  if (Math.abs(distance - curve.radius) > radialTolerance) {
    return false;
  }

  if (curve.startAngle === null || curve.endAngle === null) {
    return true;
  }

  const angleDegrees = radiansToDegrees(Math.atan2(point.y - curve.center.y, point.x - curve.center.x));
  return isAngleOnArc(angleDegrees, curve.startAngle, curve.endAngle);
}

export function isAngleOnArc(angleDegrees, startAngleDegrees, endAngleDegrees) {
  const sweep = getPositiveSweepDegrees(startAngleDegrees, endAngleDegrees);
  if (sweep >= FULL_CIRCLE_DEGREES) {
    return true;
  }

  const delta = getCounterClockwiseDeltaDegrees(startAngleDegrees, angleDegrees);
  return delta <= sweep + 0.000001;
}

export function getPositiveSweepDegrees(startAngleDegrees, endAngleDegrees) {
  const rawSweep = endAngleDegrees - startAngleDegrees;
  if (Math.abs(rawSweep) >= FULL_CIRCLE_DEGREES) {
    return FULL_CIRCLE_DEGREES;
  }

  let sweep = rawSweep % FULL_CIRCLE_DEGREES;
  if (sweep <= 0) {
    sweep += FULL_CIRCLE_DEGREES;
  }

  return sweep;
}

export function getCounterClockwiseDeltaDegrees(startAngleDegrees, angleDegrees) {
  const delta = (angleDegrees - startAngleDegrees) % FULL_CIRCLE_DEGREES;
  return delta < 0 ? delta + FULL_CIRCLE_DEGREES : delta;
}

export function closestPointOnScreenSegment(point, start, end) {
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const lengthSquared = dx * dx + dy * dy;

  if (lengthSquared === 0) {
    return {
      point: start,
      distance: distanceBetweenScreenPoints(point, start)
    };
  }

  const projected = ((point.x - start.x) * dx + (point.y - start.y) * dy) / lengthSquared;
  const clamped = clamp(projected, 0, 1);
  const closestPoint = {
    x: start.x + dx * clamped,
    y: start.y + dy * clamped
  };

  return {
    point: closestPoint,
    distance: distanceBetweenScreenPoints(point, closestPoint)
  };
}

export function closestPointOnWorldSegment(point, start, end) {
  const dx = end.x - start.x;
  const dy = end.y - start.y;
  const lengthSquared = dx * dx + dy * dy;

  if (lengthSquared <= WORLD_GEOMETRY_TOLERANCE * WORLD_GEOMETRY_TOLERANCE) {
    return {
      point: start,
      distance: distanceBetweenWorldPoints(point, start),
      parameter: 0
    };
  }

  const parameter = ((point.x - start.x) * dx + (point.y - start.y) * dy) / lengthSquared;
  const clamped = clamp(parameter, 0, 1);
  const closestPoint = {
    x: start.x + dx * clamped,
    y: start.y + dy * clamped
  };

  return {
    point: closestPoint,
    distance: distanceBetweenWorldPoints(point, closestPoint),
    parameter
  };
}

export function distanceBetweenScreenPoints(first, second) {
  return Math.hypot(first.x - second.x, first.y - second.y);
}

export function distanceBetweenWorldPoints(first, second) {
  return Math.hypot(first.x - second.x, first.y - second.y);
}

export function degreesToRadians(degrees) {
  return degrees * Math.PI / 180;
}

export function radiansToDegrees(radians) {
  return radians * 180 / Math.PI;
}

export function normalizeAngleDegrees(angleDegrees) {
  const normalized = angleDegrees % FULL_CIRCLE_DEGREES;
  return normalized < 0 ? normalized + FULL_CIRCLE_DEGREES : normalized;
}

export function isFinitePositive(value) {
  return Number.isFinite(value) && value > 0;
}

export function clamp(value, minimum, maximum) {
  return Math.min(maximum, Math.max(minimum, value));
}

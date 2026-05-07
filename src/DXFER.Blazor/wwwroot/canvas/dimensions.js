import {
  clamp,
  distanceBetweenScreenPoints,
  dotScreenPoints,
  normalizeScreenVector,
  subtractScreenPoints
} from "./geometry.js";

const WORLD_GEOMETRY_TOLERANCE = 0.000001;
const DIMENSION_ARROWHEAD_SIZE = 15;
const DIMENSION_TEXT_GAP_PADDING = 5;
const DIMENSION_INPUT_LEADER_GAP = 12;

export function getLinearDimensionScreenGeometry(start, end, anchor, textWidth = 0) {
  const vector = normalizeScreenVector({ x: end.x - start.x, y: end.y - start.y });
  if (!vector) {
    return null;
  }

  const startOffset = dotScreenPoints(subtractScreenPoints(start, anchor), vector);
  const endOffset = dotScreenPoints(subtractScreenPoints(end, anchor), vector);
  const dimensionStart = { x: anchor.x + vector.x * startOffset, y: anchor.y + vector.y * startOffset };
  const dimensionEnd = { x: anchor.x + vector.x * endOffset, y: anchor.y + vector.y * endOffset };
  const dimensionLength = distanceBetweenScreenPoints(dimensionStart, dimensionEnd);
  const textGapHalfWidth = Math.min(dimensionLength / 2, Math.max(14, textWidth / 2 + DIMENSION_TEXT_GAP_PADDING));
  const firstExtension = getExtensionSegmentPastDimensionLine(start, dimensionStart);
  const secondExtension = getExtensionSegmentPastDimensionLine(end, dimensionEnd);
  const useInsideArrows = dimensionLength >= Math.max(36, textWidth + (DIMENSION_ARROWHEAD_SIZE * 4));
  const arrows = useInsideArrows
    ? [
      {
        point: dimensionStart,
        toward: { x: dimensionStart.x - vector.x, y: dimensionStart.y - vector.y }
      },
      {
        point: dimensionEnd,
        toward: { x: dimensionEnd.x + vector.x, y: dimensionEnd.y + vector.y }
      }
    ]
    : [
      {
        point: dimensionStart,
        toward: { x: dimensionStart.x + vector.x, y: dimensionStart.y + vector.y }
      },
      {
        point: dimensionEnd,
        toward: { x: dimensionEnd.x - vector.x, y: dimensionEnd.y - vector.y }
      }
    ];

  return {
    extensionSegments: [
      { start, end: firstExtension },
      { start: end, end: secondExtension }
    ],
    dimensionSegments: getLinearDimensionSegmentsAroundText(dimensionStart, dimensionEnd, anchor, textGapHalfWidth),
    arrows
  };
}

function getLinearDimensionSegmentsAroundText(start, end, anchor, textGap) {
  const direction = normalizeScreenVector({
    x: end.x - start.x,
    y: end.y - start.y
  });
  if (!direction) {
    return [];
  }

  const totalLength = distanceBetweenScreenPoints(start, end);
  const centerOffset = dotScreenPoints(subtractScreenPoints(anchor, start), direction);
  const gapStartOffset = centerOffset - textGap;
  const gapEndOffset = centerOffset + textGap;
  if (gapStartOffset > totalLength) {
    return [
      { start, end },
      {
        start: end,
        end: {
          x: start.x + direction.x * gapStartOffset,
          y: start.y + direction.y * gapStartOffset
        }
      }
    ];
  }

  if (gapEndOffset < 0) {
    return [
      {
        start: {
          x: start.x + direction.x * gapEndOffset,
          y: start.y + direction.y * gapEndOffset
        },
        end: start
      },
      { start, end }
    ];
  }

  return getSegmentPartsAroundPoint(start, end, anchor, textGap);
}

function getExtensionSegmentPastDimensionLine(referencePoint, dimensionPoint) {
  const direction = normalizeScreenVector(subtractScreenPoints(dimensionPoint, referencePoint));
  if (!direction) {
    return dimensionPoint;
  }

  return {
    x: dimensionPoint.x + direction.x * 6,
    y: dimensionPoint.y + direction.y * 6
  };
}

export function getRadialDimensionScreenGeometry(center, radius, anchor, diameter = false, textWidth = 0, edgeOverride = null) {
  const direction = normalizeScreenVector({
    x: anchor.x - center.x,
    y: anchor.y - center.y
  }) || { x: 1, y: 0 };
  const anchorDistance = distanceBetweenScreenPoints(center, anchor);
  const isInside = anchorDistance < radius;
  const textGap = Math.max(DIMENSION_INPUT_LEADER_GAP, textWidth / 2 + DIMENSION_TEXT_GAP_PADDING);
  const edge = edgeOverride || {
    x: center.x + direction.x * radius,
    y: center.y + direction.y * radius
  };
  const edgeDirection = normalizeScreenVector({
    x: edge.x - center.x,
    y: edge.y - center.y
  }) || direction;

  if (diameter) {
    if (isInside) {
      const oppositeEdge = {
        x: center.x - edgeDirection.x * radius,
        y: center.y - edgeDirection.y * radius
      };
      return {
        segments: getSegmentPartsAroundPoint(oppositeEdge, edge, anchor, textGap),
        arrows: [
          { point: oppositeEdge, toward: { x: oppositeEdge.x - edgeDirection.x, y: oppositeEdge.y - edgeDirection.y } },
          { point: edge, toward: { x: edge.x + edgeDirection.x, y: edge.y + edgeDirection.y } }
        ]
      };
    }

    return {
      segments: getLeaderSegmentsToAnchor(edge, anchor, textGap),
      arrows: [{ point: edge, toward: center }]
    };
  }

  if (edgeOverride) {
    return {
      segments: getLeaderSegmentsToAnchor(edge, anchor, textGap),
      arrows: [{ point: edge, toward: center }]
    };
  }

  if (isInside) {
    return {
      segments: getSegmentPartsAroundPoint(center, edge, anchor, textGap),
      arrows: [{ point: edge, toward: { x: edge.x + edgeDirection.x, y: edge.y + edgeDirection.y } }]
    };
  }

  return {
    segments: getLeaderSegmentsToAnchor(edge, anchor, textGap),
    arrows: [{ point: edge, toward: center }]
  };
}

function getLeaderSegmentsToAnchor(start, anchor, textGap) {
  const directionToStart = normalizeScreenVector({
    x: start.x - anchor.x,
    y: start.y - anchor.y
  });
  if (!directionToStart) {
    return [];
  }

  const distance = distanceBetweenScreenPoints(start, anchor);
  const gap = Math.min(Math.max(0, distance - 2), textGap);
  const leaderEnd = {
    x: anchor.x + directionToStart.x * gap,
    y: anchor.y + directionToStart.y * gap
  };
  return distance > gap + WORLD_GEOMETRY_TOLERANCE
    ? [{ start, end: leaderEnd }]
    : [];
}

function getSegmentPartsAroundPoint(start, end, gapCenter, textGap) {
  const direction = normalizeScreenVector({
    x: end.x - start.x,
    y: end.y - start.y
  });
  if (!direction) {
    return [];
  }

  const totalLength = distanceBetweenScreenPoints(start, end);
  const centerOffset = clamp(
    dotScreenPoints(subtractScreenPoints(gapCenter, start), direction),
    0,
    totalLength);
  const gap = Math.min(textGap, totalLength / 2);
  const gapStartOffset = clamp(centerOffset - gap, 0, totalLength);
  const gapEndOffset = clamp(centerOffset + gap, 0, totalLength);
  const gapStart = {
    x: start.x + direction.x * gapStartOffset,
    y: start.y + direction.y * gapStartOffset
  };
  const gapEnd = {
    x: start.x + direction.x * gapEndOffset,
    y: start.y + direction.y * gapEndOffset
  };
  const segments = [];
  if (gapStartOffset > WORLD_GEOMETRY_TOLERANCE) {
    segments.push({ start, end: gapStart });
  }

  if (totalLength - gapEndOffset > WORLD_GEOMETRY_TOLERANCE) {
    segments.push({ start: gapEnd, end });
  }

  return segments;
}

import { clamp } from "./geometry.js";

export const DEFAULT_DIMENSION_INPUT_SCREEN_MARGIN_X = 52;
export const DEFAULT_DIMENSION_INPUT_SCREEN_MARGIN_Y = 18;

export function getClampedDimensionInputScreenPoint(
  point,
  size,
  marginX = DEFAULT_DIMENSION_INPUT_SCREEN_MARGIN_X,
  marginY = DEFAULT_DIMENSION_INPUT_SCREEN_MARGIN_Y) {
  const width = Number.isFinite(size?.width) ? size.width : 1;
  const height = Number.isFinite(size?.height) ? size.height : 1;
  const maxX = Math.max(marginX, width - marginX);
  const maxY = Math.max(marginY, height - marginY);

  return {
    x: clamp(point.x, marginX, maxX),
    y: clamp(point.y, marginY, maxY)
  };
}

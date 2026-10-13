export function getUnobscuredViewport(size, occlusions = []) {
  const width = Math.max(1, Number(size?.width) || 1);
  const height = Math.max(1, Number(size?.height) || 1);
  const rectangles = occlusions
    .filter(r => [r.x, r.y, r.width, r.height].every(Number.isFinite) && r.width > 0 && r.height > 0)
    .map(r => ({ left: Math.max(0, r.x), right: Math.min(width, r.x + r.width),
      top: Math.max(0, r.y), bottom: Math.min(height, r.y + r.height) }))
    .filter(r => r.right > r.left && r.bottom > r.top);
  const edges = [...new Set([0, width, ...rectangles.flatMap(r => [r.left, r.right])])].sort((a, b) => a - b);
  let best = null;
  let bestArea = 0;
  const consider = (left, right, top, bottom) => {
    const area = (right - left) * (bottom - top);
    if (area > bestArea) {
      bestArea = area;
      best = { x: left, y: top, width: right - left, height: bottom - top };
    }
  };

  // Every maximal clear rectangle ends at a canvas or obstruction edge.
  for (let i = 0; i < edges.length - 1; i++) {
    for (let j = i + 1; j < edges.length; j++) {
      const left = edges[i], right = edges[j];
      const blocked = rectangles.filter(r => r.left < right && r.right > left).sort((a, b) => a.top - b.top);
      let top = 0;
      for (const r of blocked) {
        consider(left, right, top, r.top);
        top = Math.max(top, r.bottom);
      }
      consider(left, right, top, height);
    }
  }
  return best;
}

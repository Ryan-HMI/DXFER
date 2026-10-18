export const DEFAULT_CANVAS_BACKGROUND = '#262626';
export const CANVAS_BACKGROUND_KEY = 'dxfer.canvasBackground.v1';

export function normalizeCanvasBackground(value) {
  return typeof value === 'string' && /^#[0-9a-f]{6}$/i.test(value)
    ? value.toLowerCase() : DEFAULT_CANVAS_BACKGROUND;
}

export function isLightCanvasBackground(value) {
  const hex = normalizeCanvasBackground(value).slice(1);
  const rgb = [0,2,4].map(i => {
    const c = parseInt(hex.slice(i,i+2),16)/255;
    return c <= 0.04045 ? c/12.92 : ((c+0.055)/1.055)**2.4;
  });
  return rgb[0]*0.2126 + rgb[1]*0.7152 + rgb[2]*0.0722 > 0.179;
}

export function loadCanvasBackground() {
  try { return normalizeCanvasBackground(localStorage.getItem(CANVAS_BACKGROUND_KEY)); }
  catch { return DEFAULT_CANVAS_BACKGROUND; }
}

export function applyCanvasBackground(control, value, persist = false) {
  const color = normalizeCanvasBackground(value);
  const root = control.closest('.dxfer-workbench');
  if (!root) return color;
  root.style.setProperty('--dx-canvas-background',color);
  const light = isLightCanvasBackground(color);
  root.style.setProperty('--dx-canvas-label',light ? '#202020' : '#f3f6fb');
  root.style.setProperty('--dx-canvas-label-selected',light ? '#005a75' : '#e0f2fe');
  root.style.setProperty('--dx-canvas-label-error',light ? '#a51628' : '#fecaca');
  for (const canvas of root.querySelectorAll('canvas.drawing-canvas')) {
    canvas.dataset.backgroundColor = color;
    canvas.dispatchEvent(new Event('dxfer-background-changed'));
  }
  if (persist) {
    try { localStorage.setItem(CANVAS_BACKGROUND_KEY,color); } catch { /* Keep the current view usable when storage is blocked. */ }
  }
  return color;
}

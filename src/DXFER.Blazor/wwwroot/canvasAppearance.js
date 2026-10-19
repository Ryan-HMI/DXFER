export const DEFAULT_CANVAS_BACKGROUND = '#202020';
export {observeHotkeyDialog as observeAppearanceDialog} from './workbenchHotkeys.js?v=20261017-centered-dialog';
export function focusDrawingCanvas() { document.querySelector('canvas.drawing-canvas')?.focus(); }
export const CANVAS_BACKGROUND_KEY = 'dxfer.canvasBackground.v1';
export const CANVAS_APPEARANCE_KEY = 'dxfer.canvasAppearance.v2';
export const FOUNDRY_BACKGROUND = 'radial-gradient(ellipse at center, #172832 0%, #0b1016 70%)';

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

export function normalizeCanvasAppearance(value) {
  const appearance = value?.preset === 'neutral' ? {preset: 'neutral', color: DEFAULT_CANVAS_BACKGROUND}
    : value?.preset === 'custom' && validColor(value.color) ? {preset: 'custom', color: normalizeCanvasBackground(value.color)}
    : {preset: 'foundry', color: '#172832'};
  return {...appearance, panelColor: validColor(value?.panelColor) ? value.panelColor.toLowerCase() : appearance.preset === 'neutral' ? '#181818' : '#10141b'};
}

const validColor = value => typeof value === 'string' && /^#[0-9a-f]{6}$/i.test(value);
const blend = (color, target, amount) => '#' + [1,3,5].map(i => Math.round(parseInt(color.slice(i,i+2),16)*(1-amount)+target*amount).toString(16).padStart(2,'0')).join('');

export function panelColors(color) {
  const light = isLightCanvasBackground(color);
  if (color === '#10141b') return {panel:color, chrome:'#0a0d12', raised:'#151b24', hover:'#1a222d', text:'#f4f7fb', secondary:'#bbc5d1', muted:'#97a5b6', border:'#242d39', 'border-strong':'#687b90', accent:'#66d9ff'};
  return {panel:color, chrome:blend(color,0,light ? .06 : .25), raised:blend(color,light?0:255,.06), hover:blend(color,light?0:255,.12),
    text:light?'#181818':'#f4f7fb', secondary:light?'#303030':'#d1d1d1', muted:light?'#454545':'#bcbcbc',
    border:blend(color,light?0:255,.22), 'border-strong':blend(color,light?0:255,.55), accent:light?'#006582':'#66d9ff'};
}

export function paintCanvasBackground(context, width, height, value) {
  if (!(width > 0 && height > 0)) return;
  const appearance = normalizeCanvasAppearance(value);
  context.save();
  if (appearance.preset === 'foundry') {
    // Match CSS's centered, farthest-corner ellipse, independent of viewport aspect.
    context.translate(width / 2, height / 2);
    context.scale(width / 2, height / 2);
    const gradient = context.createRadialGradient(0, 0, 0, 0, 0, Math.SQRT2);
    gradient.addColorStop(0, '#172832');
    gradient.addColorStop(.7, '#0b1016');
    context.fillStyle = gradient;
    context.fillRect(-1, -1, 2, 2);
  } else {
    context.fillStyle = appearance.color;
    context.fillRect(0, 0, width, height);
  }
  context.restore();
}

export function loadCanvasAppearance() {
  try {
    const saved = localStorage.getItem(CANVAS_APPEARANCE_KEY);
    if (saved) return normalizeCanvasAppearance(JSON.parse(saved));
    const legacy = localStorage.getItem(CANVAS_BACKGROUND_KEY);
    // Restore the removed Foundry default; retain deliberately selected custom colors.
    return normalizeCanvasAppearance(legacy?.toLowerCase() === '#262626' ? null : {preset: 'custom', color: legacy});
  } catch { return normalizeCanvasAppearance(null); }
}

export function applyCanvasAppearance(control, value, persist = false) {
  const appearance = normalizeCanvasAppearance(value);
  const {color, preset} = appearance;
  const root = control.ownerDocument?.documentElement ?? control.closest('.dxfer-workbench');
  if (!root) return appearance;
  const background = preset === 'foundry' ? FOUNDRY_BACKGROUND : color;
  root.style.setProperty('--dx-canvas-background',background);
  const surfaces = panelColors(appearance.panelColor);
  for (const [key,value] of Object.entries(surfaces)) root.style.setProperty(`--dx-${key}`,value);
  root.style.setProperty('--dx-input',surfaces.chrome);
  root.style.setProperty('--dx-control-color-scheme',isLightCanvasBackground(surfaces.chrome) ? 'light' : 'dark');
  const light = isLightCanvasBackground(color);
  root.style.setProperty('--dx-canvas-label',light ? '#202020' : '#f3f6fb');
  root.style.setProperty('--dx-canvas-label-selected',light ? '#005a75' : '#e0f2fe');
  root.style.setProperty('--dx-canvas-label-error',light ? '#a51628' : '#fecaca');
  for (const canvas of root.querySelectorAll('canvas.drawing-canvas')) {
    canvas.dataset.backgroundColor = color;
    canvas.dataset.backgroundPreset = preset;
    canvas.dispatchEvent(new Event('dxfer-background-changed'));
  }
  if (persist) {
    try { localStorage.setItem(CANVAS_APPEARANCE_KEY,JSON.stringify(appearance)); } catch { /* Keep the current view usable when storage is blocked. */ }
  }
  return appearance;
}

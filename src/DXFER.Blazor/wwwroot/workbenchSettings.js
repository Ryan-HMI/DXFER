export {observeHotkeyDialog as observeSettingsDialog} from './workbenchHotkeys.js?v=20261017-centered-dialog';
export const AUTO_CONSTRAINTS_KEY = 'dxfer.automaticSnapConstraints.v1';
export const INVERT_WHEEL_ZOOM_KEY = 'dxfer.invertWheelZoom.v1';
export function loadInvertWheelZoom() {
    try { return localStorage.getItem(INVERT_WHEEL_ZOOM_KEY) === 'true'; }
    catch { return false; }
}
export function saveInvertWheelZoom(enabled) {
    try { localStorage.setItem(INVERT_WHEEL_ZOOM_KEY, String(enabled === true)); }
    catch { /* Current-session setting still works when storage is unavailable. */ }
}
export function loadAutomaticSnapConstraints() {
    try { return localStorage.getItem(AUTO_CONSTRAINTS_KEY) !== 'false'; }
    catch { return true; }
}
export function saveAutomaticSnapConstraints(enabled) {
    try { localStorage.setItem(AUTO_CONSTRAINTS_KEY, String(enabled === true)); }
    catch { /* Current-session setting still works when storage is unavailable. */ }
}
export function focusDrawingCanvas() { document.querySelector('canvas.drawing-canvas')?.focus(); }

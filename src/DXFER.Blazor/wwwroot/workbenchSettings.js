export {observeHotkeyDialog as observeSettingsDialog} from './workbenchHotkeys.js?v=20261017-centered-dialog';
export const AUTO_CONSTRAINTS_KEY = 'dxfer.automaticSnapConstraints.v1';
export function loadAutomaticSnapConstraints() {
    try { return localStorage.getItem(AUTO_CONSTRAINTS_KEY) !== 'false'; }
    catch { return true; }
}
export function saveAutomaticSnapConstraints(enabled) {
    try { localStorage.setItem(AUTO_CONSTRAINTS_KEY, String(enabled === true)); }
    catch { /* Current-session setting still works when storage is unavailable. */ }
}
export function focusDrawingCanvas() { document.querySelector('canvas.drawing-canvas')?.focus(); }

export function closeLaunchedSyncTab() {
  window.close();
  return window.closed === true;
}

export function closeLaunchedSyncTab() {
  window.close();
  return window.closed === true;
}

export function clearSyncLaunchQuery() {
  window.history.replaceState(window.history.state, "", window.location.pathname + window.location.hash);
}

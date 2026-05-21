export function markDimensionInputCollectionToSkipNextCommit(inputs) {
  if (!inputs) {
    return;
  }

  for (const input of inputs.values()) {
    input.dataset.skipNextBlurCommit = "true";
    input.dataset.skipNextChangeCommit = "true";
  }
}

export function clearDimensionInputSkipNextCommit(input) {
  if (!input || !input.dataset) {
    return;
  }

  input.dataset.skipNextBlurCommit = "false";
  input.dataset.skipNextChangeCommit = "false";
}

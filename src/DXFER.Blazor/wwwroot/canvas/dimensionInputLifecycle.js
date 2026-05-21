export function markDimensionInputCollectionToSkipNextCommit(inputs) {
  if (!inputs) {
    return;
  }

  for (const input of inputs.values()) {
    input.dataset.skipNextBlurCommit = "true";
    input.dataset.skipNextChangeCommit = "true";
  }
}

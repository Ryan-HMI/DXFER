import assert from "node:assert/strict";
import test from "node:test";
import {
  markDimensionInputCollectionToSkipNextCommit
} from "../../src/DXFER.Blazor/wwwroot/canvas/dimensionInputLifecycle.js";

test("dimension input lifecycle marks every input to skip blur and change commits", () => {
  const lengthInput = { dataset: {} };
  const heightInput = { dataset: {} };

  markDimensionInputCollectionToSkipNextCommit(new Map([
    ["length", lengthInput],
    ["height", heightInput]
  ]));

  assert.deepEqual(lengthInput.dataset, {
    skipNextBlurCommit: "true",
    skipNextChangeCommit: "true"
  });
  assert.deepEqual(heightInput.dataset, {
    skipNextBlurCommit: "true",
    skipNextChangeCommit: "true"
  });
});

test("dimension input lifecycle tolerates missing collections", () => {
  assert.doesNotThrow(() => markDimensionInputCollectionToSkipNextCommit(null));
  assert.doesNotThrow(() => markDimensionInputCollectionToSkipNextCommit(undefined));
});

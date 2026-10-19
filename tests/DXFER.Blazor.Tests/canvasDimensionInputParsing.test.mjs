import assert from "node:assert/strict";
import test from "node:test";

import {
  getPersistentDimensionCommitValue,
  parseDimensionInputNumber
} from "../../src/DXFER.Blazor/wwwroot/canvas/dimensionInputParsing.js";
import { getDimensionDisplayText } from "../../src/DXFER.Blazor/wwwroot/canvas/dimensionPresentation.js";

test("dimension input parser accepts plain values and displayed prefixes", () => {
  assert.equal(parseDimensionInputNumber("12.5"), 12.5);
  assert.equal(parseDimensionInputNumber("R3.5"), 3.5);
  assert.equal(parseDimensionInputNumber("\u23006"), 6);
  assert.equal(parseDimensionInputNumber(" -2.25 mm"), -2.25);
  assert.equal(Number.isNaN(parseDimensionInputNumber("R")), true);
});

test("persistent dimension commit policy rejects empty zero and negative edits", () => {
  assert.deepEqual(getPersistentDimensionCommitValue("", 20), { shouldCommit: false, value: 20 });
  assert.deepEqual(getPersistentDimensionCommitValue("0", 20), { shouldCommit: false, value: 20 });
  assert.deepEqual(getPersistentDimensionCommitValue("-2", 20), { shouldCommit: false, value: 20 });
  assert.deepEqual(getPersistentDimensionCommitValue("12.5", 20), { shouldCommit: true, value: 12.5 });
  assert.deepEqual(getPersistentDimensionCommitValue("R3.5", 1), { shouldCommit: true, value: 3.5 });
  assert.deepEqual(getPersistentDimensionCommitValue("bogus", "fallback"), { shouldCommit: false, value: 0 });
});

for (const kind of ["offset", "radius", "diameter", "lineardistance", "angle"]) {
  for (const value of [1.000000323445391, 4.000000323445391, 6.1254]) {
    test(`unedited formatted ${kind} ${value} preserves full precision on commit`, () => {
      const text = getDimensionDisplayText({ kind, value });
      assert.deepEqual(getPersistentDimensionCommitValue(text, String(value)), { shouldCommit: false, value });
    });
  }
}

test("deliberately typing the rounded value remains a real edit", () => {
  assert.deepEqual(getPersistentDimensionCommitValue("1", 1.000000323445391, true), { shouldCommit: true, value: 1 });
  assert.deepEqual(getPersistentDimensionCommitValue("R4", 4.000000323445391, true), { shouldCommit: true, value: 4 });
});

test("unchanged exact value does not send redundant edits", () => {
  assert.deepEqual(getPersistentDimensionCommitValue("4", 4), { shouldCommit: false, value: 4 });
  assert.deepEqual(getPersistentDimensionCommitValue("4.000", 4, true), { shouldCommit: false, value: 4 });
});

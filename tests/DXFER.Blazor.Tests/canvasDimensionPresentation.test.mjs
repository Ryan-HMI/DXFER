import assert from "node:assert/strict";
import test from "node:test";

import {
  formatDimensionValue,
  getDimensionDisplayText,
  getDimensionInputClassName,
  getDimensionLabel,
  getDimensionRenderStyle,
  shouldAutoSelectDimensionInputValue,
  shouldCommitDimensionInputOnBlur,
  shouldCommitDimensionInputOnChange,
  shouldRefreshDimensionInputValue
} from "../../src/DXFER.Blazor/wwwroot/canvas/dimensionPresentation.js";

test("dimension value and display text formatting stays stable", () => {
  assert.equal(formatDimensionValue(6), "6");
  assert.equal(formatDimensionValue(6.1254), "6.125");
  assert.equal(formatDimensionValue(Number.NaN), "");
  assert.equal(getDimensionDisplayText({ kind: "diameter", value: 6 }), "\u23006");
  assert.equal(getDimensionDisplayText({ kind: "radius", value: 3 }), "R3");
  assert.equal(getDimensionDisplayText({ kind: "count", value: 2 }), "3");
  assert.equal(getDimensionDisplayText({ kind: "count", value: 80 }), "64");
});

test("dimension labels name the supported persistent dimension kinds", () => {
  assert.equal(getDimensionLabel("radius"), "Radius");
  assert.equal(getDimensionLabel("diameter"), "Diameter");
  assert.equal(getDimensionLabel("horizontaldistance"), "Horizontal distance");
  assert.equal(getDimensionLabel("verticaldistance"), "Vertical distance");
  assert.equal(getDimensionLabel("pointtolinedistance"), "Point-to-line distance");
  assert.equal(getDimensionLabel("count"), "Sides");
  assert.equal(getDimensionLabel("lineardistance"), "Distance");
});

test("dimension render styles cover selected active preview and failed states", () => {
  const normal = getDimensionRenderStyle(false);
  const preview = getDimensionRenderStyle(true);
  const selected = getDimensionRenderStyle(false, { selected: true });
  const active = getDimensionRenderStyle(false, { active: true });
  const unsatisfied = getDimensionRenderStyle(false, { unsatisfied: true });

  assert.notEqual(normal.strokeStyle, preview.strokeStyle);
  assert.equal(selected.glow, true);
  assert.equal(active.strokeStyle, "#bae6fd");
  assert.equal(unsatisfied.strokeStyle, "#f87171");
  assert.equal(unsatisfied.textStyle, "#fecaca");
});

test("dimension input class names preserve edit selection and failed-state flags", () => {
  const className = getDimensionInputClassName({
    persistent: true,
    selected: true,
    active: true,
    editing: false,
    unsatisfied: true
  });

  assert.equal(className.includes("drawing-persistent-dimension-input"), true);
  assert.equal(className.includes("drawing-persistent-dimension-input-selected"), true);
  assert.equal(className.includes("drawing-persistent-dimension-input-active"), true);
  assert.equal(className.includes("drawing-persistent-dimension-input-unsatisfied"), true);
  assert.equal(className.includes("drawing-dimension-input-active"), false);
});

test("dimension input update and commit predicates remain focused-edit aware", () => {
  assert.equal(shouldRefreshDimensionInputValue(true, true), false);
  assert.equal(shouldRefreshDimensionInputValue(true, false), true);
  assert.equal(shouldRefreshDimensionInputValue(true, false, true), false);
  assert.equal(shouldAutoSelectDimensionInputValue(true, false, false, true), true);
  assert.equal(shouldAutoSelectDimensionInputValue(true, false, false, false), false);
  assert.equal(shouldCommitDimensionInputOnBlur(false, false, true), true);
  assert.equal(shouldCommitDimensionInputOnBlur(true, false, true), false);
  assert.equal(shouldCommitDimensionInputOnChange(false, true), true);
  assert.equal(shouldCommitDimensionInputOnChange(true, true), false);
});

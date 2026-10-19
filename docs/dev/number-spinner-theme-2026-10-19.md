# Native Number Control Theme

## Layout Contract

Preserve existing Pattern and corner-editor dimensions, two-column alignment,
wrapping, padding and minimum input heights. Native spinner hit targets and
keyboard stepping remain browser-owned; do not hide the controls or replace
them with text. Preserve decimal typing and min/step validation. No canvas,
palette, geometry or document changes. Desktop browser scope.

## Cause and Fix

Custom dark input backgrounds did not declare a corresponding CSS color
scheme, leaving native spinner chrome in the browser's light default.
The workbench must inherit a control color-scheme token derived from the
actual panel input surface, not the independently configurable canvas color.
Foundry and Dark grey use dark native controls; light custom panels use light
controls. Keep this scoped to the workbench, including nested editors.

Use the existing appearance normalization and luminance helper. Bump the
appearance module and direct scoped stylesheet URLs for existing browsers.
No new widget or design-system dependency is needed.

## Verification

Ryan's screenshot shows the white native spinner strip on the radial Instances
field. Public baseline `5fbc681` reproduced the same appearance with computed
color-scheme `normal`. The new theme regression failed before implementation.

All 1,114 JavaScript tests pass; Web build has zero warnings/errors. Isolated
Chromium passes Foundry, Dark grey and Custom light panels over a dark canvas.
Actual native arrow clicks and ArrowUp/Down preserve minimum count2, decimal
field values and identical 133.109375x30 input dimensions. Cancel retains the
original document and no Undo entry. These tests set decimal values with fill;
they are not a new independent acceptance of every sequential typing path.

Before/after spinner screenshots inspected. Evidence:
`/tmp/dxfer-number-spinner-baseline/`, `/tmp/dxfer-number-spinner-final/`,
`/tmp/number-spinner-final.log`, `/tmp/dxfer-number-spinner-js.log`.
No saved drawings or Sync records need repair; runtime scope is DXFer Dev only.

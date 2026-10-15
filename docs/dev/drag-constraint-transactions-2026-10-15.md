# Constraint-safe drag transactions

## Contract

Keep the existing canvas, independent palettes, grip targets and full-width
wrapping feedback unchanged. No new controls, widths, overflow rules or
breakpoints. A rejected drag must preserve geometry, dimensions, constraint
intent and history, both in the saved document and the visible canvas. The
existing status region explains the rejection. A delayed response must not
overwrite a newer canvas document.

## Reproduction

On Dev20b645c, two circles of radius3 have Equal and the second has Fix.
Dragging the first quadrant from(3,0) to(5,0) commits radius5 beside radius3
and reports success. A focused core regression fails, and the installed browser
reproduces it in a disposable native DXF. No saved Engineering file was edited.

The generic drag path validates constraints but ignores the resulting failure;
specialized dimension-aware paths already reject invalid candidates. Adding
the missing generic rejection fixes the exported document but exposes a second
gap: the unchanged server document does not replace the optimistic canvas.
The JS interop needs an explicit accepted/rejected result, not only exception
rollback. Canvas preview must also preserve valid geometry on failed intent.

## Coverage In Progress

Core rejection: Equal circle/Fix, Equal line/Fix, Midpoint line/fixed point,
and suppressed relation still permitting free movement. Existing successful
peer propagation and all51 drag tests pass; full811-test suite passes.
Local browser confirms rejected document/history unchanged; preview/canvas
recovery is still being implemented and is not yet deployed.

The shared guard uses the existing constraint validator for every generic
entity path. Line, polyline/polygon, circle, arc, ellipse, spline and point
remain limited to their existing supported grips/relations. This does not add
new constraint kinds or a general DOF solver. Dimension-label and constraint
glyph repositioning are separate from geometry dragging and remain unchanged.
Previously saved invalid drawings require Undo or a verified original; no
automatic drawing repair is attempted.

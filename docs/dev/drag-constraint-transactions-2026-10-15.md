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

## Verification

Core rejection: Equal circle/Fix, Equal line/Fix, Midpoint line/fixed point,
and suppressed relation still permitting free movement. Existing successful
peer propagation and all51 drag tests pass; full814-test suite passes including
three interop acceptance tests. The integrated preview patch passes304 JS tests,
including31 constraint/rollback cases. Web build has zero warnings/errors.

Real local browser repeats the installed-Dev failure fixture: rejected preview
and released canvas retain both radius3 circles, native export preserves exact
entities/dimensions/constraints, and Undo remains disabled. Hover at the original
quadrant resolves to the original grip, confirming the visible canvas is not
stale. With Fix removed, both radii become5 and Undo/Redo restore the correct
documents. Screenshots inspected, no browser errors. QA script:
`/tmp/dxfer-drag-live-qa.cjs`; fixture generator `/tmp/dxfer-drag-fixtures`.
Deployed Dev only at e94f6ee on2026-10-15. Both browser cases pass against the
installed public URL; screenshots inspected. Authentication, disabled Sync
callbacks and10 runtime settings preserved, all four assemblies match Release,
health200/anonymous302, Production/Test PIDs unchanged. No user acceptance claimed.

The shared guard uses the existing constraint validator for every generic
entity path. Line, polyline/polygon, circle, arc, ellipse, spline and point
remain limited to their existing supported grips/relations. This does not add
new constraint kinds or a general DOF solver. Dimension-label and constraint
glyph repositioning are separate from geometry dragging and remain unchanged.
Previously saved invalid drawings require Undo or a verified original; no
automatic drawing repair is attempted.

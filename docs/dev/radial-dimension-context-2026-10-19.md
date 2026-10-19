# Radius/diameter context choices

## Layout and behavior contract

Reuse the existing dimension right-click dialog and radio-row styling. Add a Display group with Radius (R) and Diameter (diameter symbol) only for radial dimensions, below Driving/Driven. Preserve 240px menu width, 32px minimum row targets, viewport clamping and bounded scrolling, keyboard focus, Escape/outside dismissal and locked/reference-only disabling. No palette or canvas layout change.

Changing display kind preserves geometry, dimension identity/references/anchor, driving/driven state and other intent. Driving values convert by two or one-half without solving or replacing an unresolved requested value. Driven dimensions report the current measurement. Undo/Redo and native reopen must retain the chosen kind. Existing drawings need no migration.

## Verification

- Shared conversion service supports existing circle/arc/polygon radial dimensions and rejects unsupported, invalid or protected targets. Failed driving values remain failed intent, scaled without solving. Existing drawings need no migration or record repair.
- Focused core coverage: 62 passed, including 55 new radial-kind cases. Full core suite: 2002 passed. JavaScript suite: 944 passed. Web build: zero warnings/errors.
- Local browser: all five scenarios pass with zero browser errors. Driving/driven conversions preserve exact geometry, IDs, references and anchors; Undo/Redo, reopen and subsequent radius editing pass. Arc sweep stays unchanged; locked controls disable; nonradial dimensions have no Display choices. Screenshot inspected at 1188x761; menu bounds also verified at 1100x705.
- Evidence: `/tmp/dxfer-radial-context-local/`; 31 exports with valid native checksums and zero DXF audit errors/fixes.
- Dev deployment/public replay pending. Production/Test unchanged.

# Palette minimum and tangent investigation

Labeled palette resize minimum must be intrinsic single-line content width, including icon, gap, button padding, header controls and an extra 8px margin. Preserve independent docking, label toggle, saved preferred widths and desktop viewport safety clamp. Measure existing scoped DOM rather than estimating text characters. Pointer and keyboard must use the same measured minimum; restored too-small widths must become readable. Icon-only sizing remains unchanged.

Tangent investigation is separate: reproduce preview/commit disagreement before modifying solver behavior. Do not mark an unsatisfied constraint satisfied just to hide a failure. Preserve geometry and driving intent on refusal.

## Verification

- 1,549 .NET and 752 JavaScript tests pass; build and diff checks pass.
- Actual browser checks every palette at its own measured minimum, single-line labels, pointer/keyboard resizing, independent persistence, docking/collapse and desktop viewport clamp. Screenshot: `/tmp/dxfer-palette-width-evidence/content-minimums.png`.
- New Tangent replay tests pass circle/line and circle/circle application in both reference orders, followed by center drag. Actual browser passes new Tangent application on a diameter15 circle and dimensioned rectangle, unchanged peers, Undo/Redo, held drag/release and native reopen. Evidence: `/tmp/dxfer-tangent-application-evidence`.
- Ryan's reported Tangent broken-state case is NOT reproduced or fixed by this change. Exact drawing/pair requested; Flux assigned independent testing only. No solver change or existing drawing repair performed. Keep this separate from accepted palette sizing verification.

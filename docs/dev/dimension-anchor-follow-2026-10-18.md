# Repositioned dimension following

## Reproduction and cause

Dev a9d15a1: move the length-10 label on the horizontal line in
`tests/DXFER.Blazor.Tests/fixtures/corner-driving10.dxf` to (6,2), then drag
the line body by (2,1). Saved anchor is correctly (8,3); held preview and
released label remain at (6,2). The canvas anchor override created during
manual label dragging takes precedence over document anchors indefinitely.
Existing pruning only removes overrides for deleted dimensions.

## Interaction contract

No palette, CSS, alignment, wrapping or target-size changes. Dimension labels
keep their established viewport clamping and styling. A held label drag may
temporarily override its own anchor. A completed/cancelled drag must not mask
the authoritative anchor on geometry edits, Undo/Redo, or document replacement.
Pointer cancellation restores the committed location. New/open must not reuse
an override merely because the new drawing has the same dimension ID.
Verify the actual input position during held geometry preview and after release,
not merely exported coordinates. Desktop browser only; Dev deployment only.

## Verification

Original owner browser failure: `/tmp/dimension-anchor-before.log`. Shared
canvas document updates now retire completed anchor overrides while retaining
the currently held label gesture. New/open, pointer cancellation and Escape
clear temporary anchor state. Core geometry/anchor-following algorithms are
unchanged; no saved geometry or anchor corruption was found in this case.

- 1,712 .NET tests and 867 JavaScript tests pass; Web build passes.
- Three lifecycle regressions cover authoritative updates/history, held gesture
  preservation, deletion and new/open same-ID reset.
- Actual browser regression `dimensionAnchorFollow.browser.mjs` passed locally:
  held/released geometry following after label movement, two-step Undo/Redo,
  native reopen, cancelled label drag by pointercancel/Escape with no history,
  then another geometry edit. The saved original label is viewport-clamped;
  Undo verifies both that prior visible position and its exact exported anchor.
- Seven generated DXFs audit with zero errors/fixes. Held screenshot inspected.
  Evidence `/tmp/dimension-anchor-local`, `/tmp/dimension-anchor-local.log`.

No record or drawing repair is required for this display-state defect. Core
anchors were already correct in exports; refreshing/reopening restores them.
This is a minimized linear-dimension reproduction, not an assertion that every
dimension type and every related solver behavior is independently accepted.

## Deployment

Installed `adf544c7f754355b51796a38680ddd2cff10e505` on DXFer Dev at
2026-10-18T19:20Z. Clean pushed candidate, four installed assembly hashes match,
ten runtime settings unchanged, health200/anonymous302. Normal sign-in and
disabled Sync callbacks retained; Production/Test PIDs unchanged.

Public browser replay passes with the rendered `adf544c` build, including
both held/released following, history, reopen/edit again and cancellation.
Seven public DXFs audit0/0; screenshot inspected. Evidence:
`/tmp/dimension-anchor-dev` and `/tmp/dimension-anchor-dev.log`.
Independent Flux/Ryan acceptance pending. No live drawing changed and no
automatic repair required. Temporary local QA process stopped.

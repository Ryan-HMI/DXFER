# Pattern draft visibility context menu

## Layout and interaction contract

Reuse the existing compact drawing context menu, viewport clamping, icon rows,
focus/dismiss behavior and desktop dimensions. During a pattern edit, right-click
on the canvas (including the seed or preview instances) exposes only Show/Hide
pattern controls and Close. No geometry-edit commands may replace the draft.
Hiding never cancels the edit, changes its values, removes the preview, changes
selection or adds history. The sidebar retains Confirm/Cancel while hidden.
Direction picking and an in-flight center drag still block visibility toggling.
No geometry, solver or document-format changes are required.

## Cause

The canvas context-menu guard rejected every request during pattern editing,
despite the workbench visibility toggle already supporting draft preservation.
The corrected route opens a view-only menu for the active draft and retains the
existing guards on actual geometry mutation.

## Verification

Full core suite: 3,149 passed. Full JavaScript suite: 1,794 passed, including
the active-draft callback and unchanged selection. All 46 focused pattern and
context component cases pass, including geometry-command refusal during a draft.
Real-browser replay passes all four linear/radial active-draft cases at
1440x1000 and 1100x705: source, preview-member and blank-canvas right-click
menus; only Show/Hide and Close exposed; preserved draft, selection, preview and
history; Confirm commits one transaction and hidden Cancel adds no history.
Existing 15 layout checks and 18 held-center samples also pass. Browser tests
wait for the menu to close and mirrored input/preview updates before comparing
state, rather than treating an intermediate render as final. No browser errors.
Evidence: `/tmp/dxfer-pattern-draft-context-targeted` (four completed cases) and
`/tmp/dxfer-pattern-draft-context-qa-r3` (existing layout/center checks).
Owner screenshot review confirms the compact two-row menu. Deployment pending.

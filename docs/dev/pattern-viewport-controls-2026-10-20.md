# Pattern viewport controls

## Correction: geometry-anchored controls (2026-10-21)

The floating 224px parameter card below is superseded, not accepted as the
requested viewport interaction. Use independently anchored compact controls on
the pattern's pitch/radial guides, without an enclosing card. Circular patterns
have an exact-center drag handle, count input, and a Full circle / Angle between
instances segmented control. Center dragging stages the existing pattern edit;
Confirm/Cancel, validation, layer protection and one-step Undo remain authoritative.
Show/Hide from any instance context menu hides both handles and annotations.
Controls must follow pan/zoom, avoid palettes and each other, retain decimal key
entry, and remain readable at 1440x1000 and 1100x705 desktop sizes. The center
handle stays at the actual center, never relocated to simulate an on-screen point.
No solver, document-format or existing-record migration is included.

Verification: 3,061 core tests and 1,783 JavaScript tests passed, including
coalesced/ordered center events, protected geometry, stale callbacks, cancellation
and one-step commit history. A follow-up held-drag Hide guard passed 18 focused
component tests. Isolated PlaneGCS browser replay passed 15 control-layout checks
at 1440x1000 and 1100x705, 18 held-movement samples (eight circle pixel probes
per sample), exact native source preservation, mode/count/decimal editing,
Confirm/Cancel, Undo/Redo, native reopen/edit and instance Show/Hide. Owner
screenshots inspected; no browser errors. Evidence:
`/tmp/dxfer-pattern-viewport/pattern-viewport-results.json`.
This is owner verification, not Flux independent or Ryan visual acceptance.

Installed on DXFer Dev as `9a1976c` at 2026-10-21T18:19:36Z. The full
public-endpoint replay also passed all 15 layout checks and 18 held-drag samples,
with no browser errors. Screenshots/native fixtures are in
`/tmp/dxfer-pattern-handles-installed`. Four assemblies embed the approved SHA;
six served CSS/JS hashes match the publication. Google authentication and
PlaneGCS remain enabled, Sync integration remains disabled, and Production/Test
PIDs are unchanged. Rollback: `/home/ryan/dxfer-runtime/dev-publish-before-9a1976c`.
The isolated QA host has been stopped. Independent Flux acceptance remains open.

## Layout contract

- Context-menu follow-up: add Show/Hide pattern controls alongside Edit pattern
  using existing menu rows/icons, wrapping and viewport clamping. The toggle
  affects canvas widgets only, never pattern parameters, draft or Undo history.
  A shown pattern stays available without selecting it; hiding is per pattern
  for the open drawing. New/Open resets visibility preferences. One widget is
  displayed at a time; an active edit retains priority over other patterns.

- DXFer desktop only, following the installed compact Foundry control styles.
- A 224px, two-column parameter widget is anchored to the selected pattern's
  source geometry. Canvas guides show its pitch axes or radial center/step.
- It appears for the active pattern draft or a selected existing pattern; no
  all-pattern annotation clutter. The sidebar remains available.
- Controls use existing theme tokens, 28px minimum inputs, readable labels and
  predictable tab order. No font scaling or oversized tiles.
- Position follows pan/zoom/resize and stays within the canvas, clear of tool
  palettes and dimension inputs. It never changes canvas layout dimensions.
- Input drafts remain browser-owned until change/Enter so partial decimal entry
  is not overwritten by hover rendering. Enter applies the field, not the entire
  pattern; explicit Confirm/Cancel retain existing transaction/history behavior.
- Editing a radial step angle selects partial-circle mode; Full circle restores
  count-derived spacing. Invalid values keep the editor open and block Confirm.
- Pattern parameters remain authoritative: these widgets do not add independent
  solver dimensions or duplicate the pattern's equations.

## Implementation and verification

- The viewport edits the same PatternDraft and uses the existing preview,
  validation, commit, cancellation and history path as the sidebar. Selected
  saved patterns expose an Edit pattern action, guarded by layer editability.
- Rectangular/staggered controls expose columns, rows and X/Y pitch; radial
  controls expose count, step angle and Full circle. Rotation and radial-center
  coordinates remain in the existing sidebar.
- Native pattern persistence is unchanged. Existing linked patterns gain the
  widget when selected; no document migration or record repair is necessary.
- Local verification: 3,043 core tests and 1,780 JavaScript tests pass.
- Playwright tests use real individual decimal key events, field synchronization,
  invalid-count refusal/recovery, Confirm/Cancel, Undo/Redo, native DXF reopen and
  edit again. All three pattern kinds pass. Canvas wheel zoom and middle-button
  pan keep controls visible; 1440x1000 and 1100x705 screenshots confirm bounds
  and palette clearance. Evidence: /tmp/dxfer-pattern-viewport.
- This adds parameter widgets, not drag-to-change pitch handles. It does not
  resolve the separately tracked large-pattern PlaneGCS performance limit.

## Installed Dev verification

- Installed 5ae1ca5 on 2026-10-20 at
  https://dxferdev.sync.harrisonmetals.com. Browser tooltip independently read
  `DXFER build 5ae1ca5; sketch solver: PlaneGCS` before replay.
- The complete patternViewport.browser.mjs replay passes against the public
  Dev endpoint. Installed screenshots and native fixtures:
  /tmp/dxfer-pattern-viewport-installed; log:
  /tmp/dxfer-pattern-widget-installed.log.
- Normal Google sign-in remains configured; anonymous root returns 302.
  Sync callbacks remain disabled. Production/Test were not changed.
- Rollback publish: /home/ryan/dxfer-runtime/dev-publish-before-5ae1ca5.
- Flux independent acceptance is pending; owner browser verification is not
  represented as independent QA.

## Context-menu follow-up

- Right-click a source or generated instance to Show/Hide pattern controls.
  Explicit Show pins the widget without adding selection. Hide survives
  reselecting that pattern. New/Open resets these view-only preferences.
- Editing a pattern explicitly reveals its widget. Hiding during an edit keeps
  the draft and sidebar available; it does not cancel or commit the operation.
- Two WorkbenchPatternControlsTests cover visibility, untouched document/history,
  draft preservation, active-edit priority and lifecycle reset.
- Expanded Playwright replay passes rectangular and radial generated-member
  context toggles, reselect-hidden behavior, and the prior pattern edit/history/
  roundtrip checks. Local screenshots: /tmp/dxfer-pattern-context.
- No solver or document-format change, and no existing-data repair is required.
- Installed follow-up: Dev 45ce09f, 2026-10-20. Tooltip-verified public-endpoint
  browser replay passes all prior widget checks plus rectangular/radial instance
  Show/Hide. Full core suite: 3,045 passed. Installed screenshots/fixtures:
  /tmp/dxfer-pattern-context-installed; log:
  /tmp/dxfer-pattern-context-installed.log. Google sign-in retained; anonymous
  root returns 302. Production/Test unchanged. Rollback:
  /home/ryan/dxfer-runtime/dev-publish-before-45ce09f.

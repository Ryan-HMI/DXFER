# Pattern viewport controls

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

# DXFer UI implementation contract

## Target

Ryan's scope clarification (Slack delivery `5a5e8e2c96d4f5f655fedbb6`): DXFer
targets PC browsers, not phones. Prioritize desktop/laptop mouse and keyboard
workflows, resizable windows and display scaling. Phone/touch usability is not
a development requirement or acceptance gate. Prior narrow-screen screenshots
are historical checks, not a mobile-support commitment. Preserve working layout
behavior; do not spend implementation effort adding mobile-specific controls.

A precise, viewport-first 2D editing instrument: blue-black drawing space, quiet neutral panels, consistent line icons, sentence-case labels, explicit selections and trustworthy edit feedback. Cyan identifies the current operation or selected geometry; it does not decorate every border. Amber identifies protected/excluded geometry. No glow, ornamental patterns, large dashboard cards or invented workflow.

The target SVG shows an illustrative fillet operation. Values and geometry are visual examples, not computed model results. `Keep original edges`, corner mode, layer display, command search, and new preview/apply affordances are **proposals**: render them only when the real operation supports them, or implement/test them in a separately reviewed behavior change. Never expose fake controls or imply a solver result is valid before the solver says so.

## Priority order and bounded change sets

1. **Foundation:** tokens, icon component, typography and semantic control states. Replace existing icons one family at a time while retaining handlers. Keep screenshots of the old and new UI.
2. **Workspace composition:** document header; independently dockable tool palettes; supplementary active-operation inspector; stable viewport controls; bounded status regions. Keep all existing tools, cleanup and grain commands reachable.
3. **Feedback:** textual constraint conflicts, input validation, explicit selection types and safe cancellation. This may require new state plumbing; it is not a CSS-only fix.
4. **Polish and evidence:** narrow-window reflow, keyboard focus, tooltip placement, saved-layout recovery, contrast and DPI checks. Ship only after running screenshots and behavioral tests.

Do not bundle solver rewrites, persistence changes or new CAD capabilities into the icon migration. An improved visual warning never substitutes for a geometry invariant.

## Layout contract

**Mandatory architecture: preserve multiple independently dockable tool palettes.** Every existing palette retains its stable identity, command membership, independent docking/undocking, movement, collapse/expand state, visibility, and saved layout. Do not replace them with one fixed shelf, one combined Tools palette, or one mandatory drawer. The active-operation inspector supplements these palettes; it never replaces them. Apply the new icons, labels, spacing and colors inside each existing palette.

The 1440 × 900 target illustrates one user-arranged layout: header 48 px, a 200 px left dock zone containing several independent palettes, a 296 px active-operation inspector, and footer 32 px. These are example dimensions, not fixed architecture. The viewport occupies the remaining unobscured space. Sketch, Modify, Transform, Pattern, Constraints, Cleanup, Grain and View remain separate palettes wherever those identities already exist. Match actual palette IDs and names rather than renaming or regrouping by this illustration. The View palette is shown floating to demonstrate that it can move independently while others remain docked.

Each palette has its own labeled header, drag grip, dock/undock control, collapse control and body. A dock zone is only a container: dragging one header moves that palette alone. Docked and floating states share the same visual language, command handlers and tool state. Use compact icon + label grids or rows per palette; offer density/label choices without combining palettes. Preserve all commands and shape variants in their existing palette; optional labeled flyouts must not remove existing direct access without a separately approved change. Do not collapse unrelated palettes automatically when a tool activates.

At smaller windows, preserve identities and user choice. Let users individually collapse, hide, relocate or redock palettes through their own controls and a Palettes menu listing every palette and Reset layout. A dock zone may scroll its palette collection independently; it is not a merged tool palette. Keep the active tool's required fields, warning and commit/cancel visible. Do not force all tools into one drawer, move all palettes as a unit, discard saved positions, or change visibility merely because a breakpoint was crossed. Any optional compact layout must be user-selected and restore the previous arrangement. Below the supported minimum, show a clear size limitation rather than pretending the docking workflow has been verified on mobile.

Clamp each floating palette after resize and layout restoration, keeping its header and dock/collapse controls reachable. Preserve each palette's last chosen position separately. Provide Reset layout as explicit recovery, never automatic reset. Fit uses the actual unobscured area after dock zones and floating palettes. Numeric editor fields and the operation footer may use independent scrolling regions for optional details, but not require scrolling to commit or cancel the common path.

### Palette acceptance gates

- Inventory and preserve every existing palette ID, membership, shortcut, visibility and docking capability before styling.
- Float Sketch and View independently; move one and assert the other's position and state do not change.
- Dock, undock, reorder and collapse each palette separately. Preserve the active tool, typed values, selections and undo history throughout.
- Reload with mixed docked/floating/collapsed palettes and verify each saved identity, position, visibility and collapse state.
- Resize through baseline and smallest supported dimensions; every floating header/control stays reachable and no palette is silently consolidated, hidden or reset.
- Keyboard-accessible per-palette dock/collapse controls and the Palettes menu provide alternatives to dragging. Focus remains on the moved palette or its control.
- Capture before/after screenshots with at least two independently floating palettes and a mixed docked/floating layout. A single-sidebar screenshot is not evidence of preserved independent docking.

## Active-operation contract

- Entry: activate through the existing semantic action. Show the tool name and immediate next step.
- Selection: named, ordered slots describe required entity type; completed slots show entity names and a check. Hover/keyboard focus cross-highlights geometry. Invalid clicks provide a short reason.
- Preview: model changes are clearly distinguishable from committed geometry; the panel says Preview. Never generate a misleading illustrative preview in production.
- Numeric editing: visible unit; exact typing; validated range; existing step and reset semantics. Preserve invalid typed text long enough to correct it. Enter commits only a valid state; Esc cancels one level. Do not globally capture keyboard shortcuts while typing.
- Commit: one action, one existing undo transaction, truthful affected count and durable history entry. Cancel leaves geometry and relations unchanged.
- Existing immediate tools: preserve immediate behavior until a separately tested transaction interface is deliberately introduced. Do not show Apply on a tool that already committed.

## Constraint and status feedback

Required messages explain the operation, affected entity/relation and recovery. Example: “Vertical conflicts with Horizontal on Line 04. The operation was not applied. Remove Horizontal or choose another line.” This wording is allowed only after rollback/non-application is verified. If an existing invalid state is retained, say so explicitly and offer Undo/revert rather than falsely claiming safety.

Overlarge fillet: “Radius is too large for the selected edges. Choose a smaller radius or different edges.” Include a numeric bound only if the geometry engine supplies it. Never invent a maximum or report success on a no-op.

Selection, protected state, invalidity and preview use text/shape as well as hue:
- Selected: cyan edge and square endpoint handles; panel selection count/type
- Preview: dashed result boundary plus Preview label
- Protected: amber edge, lock marker and “Protected” name
- Invalid: broken red boundary, error marker and textual explanation
- Committed: neutral edge; selected objects may retain selection treatment

Separate ephemeral operation instructions from document/cut metrics. Instructions may occupy two bounded lines above the viewport bottom; they must not wrap below the window. Put persistent warnings in the inspector with `role="alert"` for new blocking errors. Routine updates use a polite live region, with throttling during pointer motion. Do not announce every hover or drag frame.

Display actual document units in header, fields, dimensions, bounds and cut total. Unknown units remain “Document units”/“Unspecified”, with an explicit resolution path; styling must not invent inch/mm assumptions. Export wording must match actual geometry/native-data persistence.

## Typography and states

Use installed Inter if already available, otherwise system UI/Segoe UI; this pack downloads no fonts. Main labels 13 px, control values 13 px tabular numerals, group labels 12 px semibold, helper text 12 px. Avoid tiny uppercase hierarchy. Icon sizes and hit targets are separate. Main text targets contrast 4.5:1; essential control boundaries/focus/geometry target 3:1 against adjacent background. `--dx-muted` is deliberately brighter than typical dim metadata for long-session readability.

Hover uses a neutral local fill. Pressed adds border contrast without movement. Selected uses a soft accent fill plus left indicator and `aria-pressed`. Keyboard focus uses a 2 px ring with 2 px separation. Disabled keeps its shape and a discoverable explanation; do not use disabled gray for available commands. Busy keeps label width stable and announces genuine pending work. Danger styling is isolated from Undo/Redo. Only one primary commit per tool.

## Verification gates

- Before/after captures at 1440×900 and 1280×720; then 1920×1080 and desktop display/browser scaling at 125/150/200%. Check resized PC windows, not phone or touch workflows. Verify no clipped footer, inaccessible inspector actions or offscreen saved floating palette.
- Open every tool family; compare action inventory and all IDs/shortcuts with the baseline. Disabled prerequisites remain accurate. No CSS class should determine which geometry command executes.
- Draw line/arc/ellipse/spline/slot; edit dimension with actual keyboard; trim, extend, fillet/chamfer; move/rotate/scale/mirror; both patterns; constraint apply/reject; Undo/Redo; cancel at each stage.
- Preserve exact geometry and relation results, selection kind, import/export semantics and dirty-document warnings. Preserve existing automated regression tests.
- Tab/Shift-Tab through each palette and inspector; Enter/Esc; command search returns all supported tools. Focus never disappears into a hidden palette or offscreen panel.
- Inspect light/dark, hover/selected/disabled and grayscale icon contact sheets at actual 18/20/24 px and 125–200% scale. Validate tool labels rather than relying on three-point/center-point icon differences.
- Run real UI checks, not only snapshot or unit tests. Artifact validation below does not establish production acceptance.

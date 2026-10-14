# DXFer UI implementation contract

## Target

A precise, viewport-first 2D editing instrument: blue-black drawing space, quiet neutral panels, consistent line icons, sentence-case labels, explicit selections and trustworthy edit feedback. Cyan identifies the current operation or selected geometry; it does not decorate every border. Amber identifies protected/excluded geometry. No glow, ornamental patterns, large dashboard cards or invented workflow.

The target SVG shows an illustrative fillet operation. Values and geometry are visual examples, not computed model results. `Keep original edges`, corner mode, layer display, command search, and new preview/apply affordances are **proposals**: render them only when the real operation supports them, or implement/test them in a separately reviewed behavior change. Never expose fake controls or imply a solver result is valid before the solver says so.

## Priority order and bounded change sets

1. **Foundation:** tokens, icon component, typography and semantic control states. Replace existing icons one family at a time while retaining handlers. Keep screenshots of the old and new UI.
2. **Workspace composition:** document header; labeled tool library; docked active-operation inspector; stable viewport controls; bounded status regions. Keep all existing tools, cleanup and grain commands reachable.
3. **Feedback:** textual constraint conflicts, input validation, explicit selection types and safe cancellation. This may require new state plumbing; it is not a CSS-only fix.
4. **Polish and evidence:** narrow-window reflow, keyboard focus, tooltip placement, saved-layout recovery, contrast and DPI checks. Ship only after running screenshots and behavioral tests.

Do not bundle solver rewrites, persistence changes or new CAD capabilities into the icon migration. An improved visual warning never substitutes for a geometry invariant.

## Layout contract

Baseline 1440 × 900: header 48 px; left tool shelf 200 px; right operation inspector 296 px; footer 32 px. Inspector range 280–320 px. Main viewport is the remaining space, not a fixed width. Do not put a large floating View palette over geometry by default.

The shelf is a stable tool library with short icon + label rows, grouped into Create, Modify, Transform/Pattern, Relations, and Preparation. Place shape variants behind explicit, labeled flyouts: Rectangle → corner/center/aligned; Circle → center/three-point; Arc → three-point/center/elliptical; Polygon → inscribed/circumscribed. Keep rare existing tools in those menus and command search. A flyout trigger needs a chevron and accessible expanded state. Do not silently delete, disable or combine distinct commands.

The target's broad Transform, Pattern, Constraints and Cleanup/Grain rows open complete labeled lists, not a single inferred operation. Move, Rotate, Scale, Mirror; linear/circular pattern; every relation; all cleanup and grain commands retain separate names, shortcuts and discoverability. A full command catalog remains available even with no selection, explaining prerequisites for unavailable actions.

At 1280 × 720: shorten shelf groups through explicit disclosures; do not shrink type. Active tool and its primary fields, warning, Cancel and Apply must remain visible with no core-path scrolling. Inspector layout is `header auto / content minmax(0,1fr) / footer auto`. Only optional relation/history details scroll. During an active tool, collapse unrelated shelf groups by default without relocating the tool or hiding its name. At widths under 1100 px, collapse the shelf into a labeled Tools drawer; keep a visible active-tool name and access to all commands. Below 760 px, use a bounded bottom tool sheet with a persistent commit/cancel row, never an offscreen floating card. A mobile composition needs a separate screenshot review before claiming support.

Preserve existing docking if supported. Clamp floating panels after viewport resize and at saved-layout restore. Minimum 44 px of the title/return controls must remain reachable, with Reset layout as recovery. Fit computes actual unobscured drawing area after docks and overlays, not the browser rectangle.

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

- Before/after captures at 1440×900 and 1280×720; then 1920×1080, 125/150/200% scale and smallest actually supported size. Verify no clipped footer, inaccessible inspector actions or offscreen saved floating palette.
- Open every tool family; compare action inventory and all IDs/shortcuts with the baseline. Disabled prerequisites remain accurate. No CSS class should determine which geometry command executes.
- Draw line/arc/ellipse/spline/slot; edit dimension with actual keyboard; trim, extend, fillet/chamfer; move/rotate/scale/mirror; both patterns; constraint apply/reject; Undo/Redo; cancel at each stage.
- Preserve exact geometry and relation results, selection kind, import/export semantics and dirty-document warnings. Preserve existing automated regression tests.
- Tab/Shift-Tab through shelf and inspector; Enter/Esc; command search returns all supported tools. Focus never disappears into a hidden drawer or offscreen panel.
- Inspect light/dark, hover/selected/disabled and grayscale icon contact sheets at actual 18/20/24 px and 125–200% scale. Validate tool labels rather than relying on three-point/center-point icon differences.
- Run real UI checks, not only snapshot or unit tests. Artifact validation below does not establish production acceptance.

# CAD Polish Implementation Passes

Date: 2026-05-06

This document tracks the staged implementation work for the constraint solver
and CAD polish plan. Keep it updated as each pass lands so partially finished
systems are visible from the repo.

## Licensing Boundary

Current behavior:

- DXFER had no explicit repo license file.
- Third-party licensing expectations were captured only in chat/design notes.

Target behavior:

- DXFER-owned code is MIT licensed.
- HMI-Sync can consume DXFER across a clear app/package boundary without being
  forced open-source by DXFER-owned code.
- GPL-only runtime solver dependencies are excluded.
- LGPL solver candidates, including PlaneGCS, must stay behind replaceable
  adapter boundaries and carry third-party notices.

Acceptance tests:

- Repo contains `LICENSE`.
- Repo contains `THIRD_PARTY_NOTICES.md`.
- Solver adapter APIs use DXFER-owned request/result types rather than leaking
  third-party objects into UI or Sync-facing code.

Known gaps:

- Legal review has not been performed.
- PlaneGCS notices must be expanded when the adapter ships real LGPL assets.

## Pass 1 - Solver Adapter Spike

Current behavior:

- `SketchConstraintService` and `SketchDimensionSolverService` apply constraints
  directly with DXFER-owned C# logic.
- There is no replaceable sketch-solver interface.

Target behavior:

- Add a DXFER-owned solver interface with request/result types.
- Add a fallback adapter that delegates to current services without changing
  behavior.
- Add a PlaneGCS adapter shell/proof that is isolated from UI and HMI-Sync.
- Do not replace the current solver until PlaneGCS proves equivalent or better.

Acceptance tests:

- Fallback adapter applies existing constraints and driving dimensions.
- PlaneGCS adapter is discoverable as optional/unavailable until WASM assets are
  wired.
- UI and document types do not reference PlaneGCS package types.

Known gaps:

- Full PlaneGCS WASM loading and solving is not complete until browser and node
  tests prove it can solve the required primitive/constraint set.

## Pass 2 - Constraint Command System

Current behavior:

- Constraint toolbar buttons are disabled until a valid preselection exists.
- Constraint creation is not a repeated modal post-selection workflow.
- Constraint hover filtering is limited to glyph visibility, not command
  eligibility.

Target behavior:

- Constraint commands support both preselection and post-selection.
- With no valid preselection, a constraint command enters a modal tool and
  gathers eligible references until the constraint can be applied.
- Hover targets are filtered by the active constraint kind.
- Created constraint glyphs are selectable and deletable.
- Creation tools emit logical persistent constraints for intentional geometry
  relationships and implicit snaps.

Acceptance tests:

- Each constraint kind rejects ineligible selections and highlights eligible
  hover targets in modal mode.
- Modal constraint tools repeat until Escape.
- Deleting selected constraint glyphs removes only those constraints.
- Rectangles, polygons, tangent-created geometry, and snap-created relations
  create the expected persistent constraints.

Known gaps:

- Solver over/under-constrained diagnostics remain future work unless the
  selected solver adapter exposes reliable status.

## Pass 3 - Dimension Styling And Behavior

Current behavior:

- Linear dimension styling is close to CAD norms.
- Arc, radial, diameter, and slot dimensions can place leaders or labels on top
  of geometry or point at low-legibility locations.

Target behavior:

- Angle dimensions draw from arc sweep extension lines, not center projection
  unless explicitly measuring vectors from the center.
- Radius and diameter dimensions choose inside/outside leader placement for
  legibility.
- Dimension text is plain until selected for edit; edit boxes appear only while
  editing.
- Slot arc dimensions reuse the same radial placement rules.

Acceptance tests:

- Angle dimension graphics use arc endpoints/extension lines for arc sweep
  dimensions.
- Radial and diameter graphics stay readable for inside, outside, and crowded
  anchor positions.
- Dragged dimension anchors remain stable under zoom/scroll and geometry drag.

Known gaps:

- Full automatic dimension collision avoidance is not part of this pass.

## Pass 4 - Power Trim And Curve Operations

Current behavior:

- Power trim/extend is line-only and click based.

Target behavior:

- Drag-crossing geometry trims every crossed section that can be safely split.
- Lines are supported first, followed by arcs/circles/polylines/polygons.
- Trimming a parametric polygon explodes it into regular constrained segments.

Acceptance tests:

- Drag-cross over one line segment trims the crossed span.
- Drag-cross over multiple line segments trims each crossed span.
- Existing click-to-extend behavior remains available where applicable.
- Unsupported entities fail with a clear status instead of silently doing
  nothing.

Known gaps:

- Spline trimming requires exact curve split support and is deferred until the
  spline model is corrected to fit-point behavior.

## Pass 5 - Workspace And Tooling UI

Current behavior:

- Tool groups are static side-panel sections.
- Grain/prep controls are mixed with general transform concepts.
- New file behavior still centers around the sample drawing.

Target behavior:

- Toolbars are dockable/collapsible with compact labels and tooltips.
- Cleanup/prep tools own grain, alignment, origin, rotate 90, duplicate cleanup,
  and flat-pattern preparation actions.
- File menu supports a true blank new canvas and a separate sample command.
- Custom SVG icons are cleaned up in the existing icon component rather than
  replaced by raster assets.

Acceptance tests:

- New File opens an empty document named `Untitled.dxf`.
- Sample remains explicitly available for demos/tests.
- Cleanup group contains grain/alignment/prep tools.
- Existing hotkeys and icon buttons remain functional.

Known gaps:

- Fully rearrangeable Adobe-style docking is a larger workspace system and may
  follow after this pass.

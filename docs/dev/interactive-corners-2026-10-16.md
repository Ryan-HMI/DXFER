# Interactive Fillet and Chamfer

## Interaction Contract

- Desktop operation inspector retains its current dock and compact numeric field.
- Selecting a corner stages geometry only. Typing or dragging the canvas size
  handle recomputes from the unchanged source through the existing corner service.
- Apply/Enter commits one history entry; Escape/Exit discards the preview.
- Invalid sizes and constraint failures remove the candidate and disable Apply.
- The canvas handle has a fixed screen-space target, independent of zoom. Horizontal
  dragging adjusts size in drawing units. Pointer release ends adjustment, not the command.
- Preview never changes saved geometry, dimensions, dirty status or Undo history.
- No new floating panel or overlap with existing palettes. Existing inspector
  wrapping and collapse behavior remains in use.

## Implementation

The selection callback now builds a separate candidate instead of committing.
The existing corner service and generated-dimension solver are shared between
preview and commit; generated Equal-chain state is restored after preview.
The source document remains the hit-test/export/history authority. A changed
source or leaving the tool invalidates the candidate. New unsatisfied driving
intent refuses rather than committing a misleading preview.

The existing operation inspector supplies key-in and Apply. Its collapsed
version and the canvas also accept Enter. A 12px handle with a 28px hit target
adjusts size horizontally in world units; pointer capture survives leaving the
handle. Release ends adjustment without committing. No existing drawings require
repair, and no solver-engine replacement or Tangent fix is claimed.

## Verification

- 1,553 .NET tests pass, including staged recalculation, invalid-size/refusal,
  cancellation, reference-dimension updates, one-entry commit, history/reopen.
- 752 JavaScript tests pass. The obsolete immediate-commit source assertion
  now verifies explicit candidate/commit wiring.
- `interactiveCorners.browser.mjs` passes on an isolated local app: both tools,
  separate lines and open/closed polylines; key-in; held/released handle drag;
  visible preview pixel changes; unchanged export/dirty state/history before
  Apply; invalid/oversized input and recovery; Escape; preselected corner;
  Apply and Enter from canvas/collapsed inspector; Undo/Redo/native reopen.
- Existing `operationInspector.browser.mjs` passes with explicit Apply:
  driving-dimension refusal, invalid input, 960-1440px desktop widths, docking,
  header states and save/reopen. Desktop screenshots inspected.
- Evidence: `/tmp/dxfer-interactive-corners-evidence` and
  `/tmp/dxfer-inspector-evidence`.

Scope: DXFer Dev only. Deployment and public replay recorded in the operational
acceptance ledger after installation; no Production/Test rollout authorized here.

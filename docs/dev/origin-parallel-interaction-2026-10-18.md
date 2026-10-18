# Origin snapping, parallel endpoint drag and Escape

Ryan delivery 7f7e8a9de07c9807d0823fd7 and screenshot F0C9ZUUHTNX.
Related requests: implicit snaps should persist, drags should not add selection,
and Escape should clear selection. Scope: DXFer Dev only.

## Shared causes and corrections

- Creation excluded the origin from snap picking and did not carry explicit
  origin-snap intent into constraint creation. It now does. Supported created
  point references at the snapped origin receive a persistent Coincident;
  dragged point references snapping there also retain Coincident. Merely loading
  geometry at zero does not pin it. No universal virtual-center or curve-contact
  inference is claimed; browser coverage is Line, Center circle and Point.
- Point-anchor closure previously handled only curve centers. A line endpoint
  Coincident to origin was not recognized as an anchored point for propagation.
  Closure now retains point-level immobility without fixing the entire entity.
  If the opposite endpoint is anchored, Parallel/Perpendicular propagation can
  rotate a movable peer while retaining its length. Existing full validation,
  dimension and lock guards still decide whether the candidate may commit.
- Escape returned early when the canvas was already in Select, and selection
  survived cancellation. Cancellation now clears both canvas/workbench tool and
  selection state, restores held-drag geometry, and focuses the canvas. Geometry
  drags no longer insert their targets into persistent selection.

## Reproduction and verification

Native fixtures: A=(-5,10)->(0,0), B=(-14,9)->(-9,-1), origin Coincident to A:end,
Parallel A/B. A:start failed before the correction in both reference orders in
core and preview. All three free endpoints now follow drag targets; A:end refuses.

1698 .NET and 863 JavaScript tests pass. Added coverage includes reference order,
Parallel/Perpendicular, fixed-peer refusal/removal recovery, and scales
0.00001/1/10000. Origin persistence, unsnapped creation, Escape in idle/active
tools, held-drag cancellation and preexisting selection preservation are covered.

Local actual-browser replay passes eight endpoint cases (including held preview,
release, Undo/Redo, native reopen/edit again), three origin-creation cases with
history/native reopen, idle-selection Escape and active-tool Escape. Eleven
exports audit with zero errors/fixes. Held-drag and final Escape screenshots
inspected. Browser harness: originParallelInteraction.browser.mjs.

This fixes shared behavior, not a particular saved drawing. Existing valid
constraints need no record repair; drawings previously snapped without a stored
origin relation need a deliberate Coincident or resnap, not automatic pinning.
Broader coupled-angle/rotation and fixed-midpoint limitations are separate.
Independent Flux replay and Ryan acceptance are not yet claimed.

## Installed verification

Installed f016d90732ff8cd9cb989552d7fdd84bf4820a6e at2026-10-18T18:51Z.
The public Dev browser independently read rendered f016d90 and passed the full
same interaction replay. Eleven public exports audit0/0; held-preview screenshot
inspected. Log: /tmp/dxfer-origin-dev-browser.log. Published/live assemblies match,
ten runtime settings are unchanged, health200/anonymous302, Production/Test PIDs
unchanged. Normal sign-in, isolated Dev storage and disabled Sync callbacks retained.
Rollback: /home/ryan/dxfer-runtime/dev-publish-before-f016d90.

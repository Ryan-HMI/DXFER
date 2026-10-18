# Creation Snap Intent

Ryan reports implicit snaps are still not consistently permanent. The shared
creation commit previously retained coordinates but not the picked reference.
This change carries typed snap intents through the existing commit, validates
them against current geometry and saves relations without repositioning peers.

Interaction/layout contract: unchanged canvas, palettes, sizes, wrapping,
overflow and desktop targets. Accepted snap intent survives intermediate clicks,
numeric completion and moving-click placement. Escape/New clear pending intent;
stale or incompatible explicit references refuse with visible feedback and no
history. No Fix substitution and no historical coordinate-based repair.

## Implemented Scope

- Creation commits carry the accepted source reference and relation type through
  intermediate clicks and final placement, instead of guessing only from coordinates.
- Persistent created endpoints/centers/Point entities can retain line-body and
  straight-polyline-segment PointOnLine, straight-line intersections (both lines),
  and acquired endpoint/center/Point-entity horizontal/vertical projections.
- Existing endpoint, center, origin and line/polyline midpoint intent also travels
  through this path. A numeric override away from a cue does not reuse its intent.
- Commit validation does not reposition existing peers or rewrite their driving
  values. Stale or incompatible represented references reject without history.
- Cancel clears pending intent; source IDs with shared prefixes resolve exactly.

## Verification

1740 core tests and919 JavaScript tests pass. New focused coverage includes the
typed factory, actual registered pointer commits, multi-click/cancellation,
source identity, workbench history/native serialization and stale-source refusal.
Web build passes. Local actual-browser checks cover edge creation and acquired
projection, unchanged source geometry, Undo/Redo, native download/reopen, then
source editing with the saved relation still satisfied. A real intersection
click retains both supporting lines through history/reopen. Six exported DXFs
audit with zero errors/fixes; the projection screenshot was inspected.
Local evidence: /tmp/creation-snap-local.log and /tmp/creation-snap-local/.

## Dev Installation

Installed b0b4b9ce28af03ce2fc61d3d3c8a974437f02d03 at2026-10-18T23:43Z.
Public actual-browser replay verifies the rendered b0b4b9c identifier and repeats
the passing edge/projection/intersection checks. Six public DXFs audit0/0; the
post-edit edge screenshot was inspected. Evidence: /tmp/creation-snap-dev.log
and /tmp/creation-snap-dev/. Four installed assemblies match publish, all ten
tracked runtime settings remain unchanged, health200/anonymous302, normal
Google sign-in and disabled Sync callbacks retained. Production/Test processes
are unchanged. Rollback: /home/ryan/dxfer-runtime/dev-publish-before-b0b4b9c.
Owner verification only; independent Flux replay/Ryan acceptance not inferred.

## Remaining, Not Complete

This is creation-path work, not full drag-snap capture. Quadrants, arc midpoints,
curved intersections, virtual construction points (including center-rectangle
centers and circumference sizing clicks), acquired virtual midpoints and polar
inference are not made persistent by this change. Explicit point intents are
applied only when the accepted position is a persistent point on the created
geometry. No Fix substitution, disabled snap families or unsupported geometric
approximation was introduced. Broader stable-reference work remains open.

The shared lost-provenance cause is corrected for the listed creation paths.
Previously saved drawings with coordinate-only snaps still need deliberate
resnapping/reapplication; missing intent cannot be safely reconstructed. No
historical files, manufacturing records or Production/Test settings are repaired
or changed. Dev installation and independent acceptance are recorded separately.

# PlaneGCS Arc Pattern Center Drag

## Reproduction

An unconstrained ARC at (3,2), radius 1, angles 10 to 110 degrees, is used
as the source of a four-member full radial pattern about (1,-2).
Dragging its center should translate the source and update the linked
members without changing the source radius or sweep.

The actual UI-created pattern on public Dev `5f116e4` changed the source
radius to 0.7228184316875316 after a center drag by (0.8,0.5).
The native baseline also reproduces before the affine-point optimization.
All hard pattern equations can still be satisfied by that distorted result:
shape stability during center dragging is a soft preference, not a driving
dimension. A shape-preserving solution is feasible in this fixture.

Before evidence: `/tmp/dxfer-arc-pattern-before-5f116e4`.

## Shared Correction

Pattern initialization now propagates the complete dependent arc guess:
center, radius, start/end angles using the existing full-turn differences,
and endpoint coordinates. Previously, moving only dependent centers left
arc endpoint rules inconsistent, so the candidate was rejected and native
hard initialization could deform the curve before soft preferences ran.

The candidate still must satisfy every existing hard residual and preserve
fixed references. Its transaction now snapshots arc scalar parameters as
well as points. A later failure restores all of them. No radius, angle or
pointer preference is promoted to a hard constraint, and no geometry is
reconstructed after solving. The authoritative result remains PlaneGCS.

## Verification And Data

The new real-browser `pattern-radial-arc-source-follow` case passes locally:
UI pattern creation, held center-drag preview, release with unchanged
radius/angles, Undo/Redo, native DXF reopen and another center drag.
Evidence: `/tmp/dxfer-arc-pattern-after`.

`planeGcsArcPatternDrag.test.mjs` adds 17 real-WASM regressions covering
two/four-member Radial, Rectangular and Staggered patterns without driving
dimensions, reversed order, later Fixed/locked members, radius/endpoint
editing and complete scalar/endpoint rollback. Native input observation
checks that soft weights, degrees of freedom and hard equation counts have
not been strengthened to force the desired result.
All 1,759 JavaScript and 3,023 Core tests pass. Logs:
`/tmp/dxfer-arc-pattern-js-final.log` and `/tmp/dxfer-arc-pattern-core.log`.
The 512-circle benchmark still passes in 2.14
seconds after this correction.

No existing drawings were modified. Previously saved distorted geometry
cannot be reconstructed reliably from current geometry alone; use Undo or
a verified original. There is no automatic historical repair.
Installed verification and final regression counts follow after deployment.

## Installed Verification

Deployed `4409521` to DXFer Dev on 2026-10-20 around 18:00 UTC. Public tooltip
and served solver/pattern hashes match this revision. All five installed
replays pass: rectangular-circle pattern, radial-circle pattern, radial-arc
pattern, native ellipse edit and native spline edit. The arc test preserves
radius 1, angles 10/110 and total cut length 6.981317 inches across four arcs,
including native reopen and another drag.
Evidence: `/tmp/dxfer-arc-pattern-installed-4409521`.

The published 512-circle host check still passes in 2,315 ms. Dev PID 2118600
retains PlaneGCS, full editing, disabled Sync callbacks and isolated Dev keys.
Anonymous root redirects to the expected Google HTTPS callback.
Production/Test PIDs 3584417/3584475 remain unchanged.
Rollback: `/home/ryan/dxfer-runtime/dev-publish-before-4409521`.
Constrained large-pattern timeout investigation remains separate and open.

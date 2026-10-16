# Dimensioned Spline References

Scope: DXFer Dev, DRAG-SPLINE-001. No replacement solver, visual changes, Sync
callbacks, manufacturing-file repair or Production/Test deployment.

## Reproduction And Cause

Both cubic-control and fit-point fixtures with points (0,0),(1,0),(3,1),(4,1)
and a driving distance1 between points0/1 accepted a point1 drag to(1,1).
The physical span became sqrt(2) while the dimension remained1/satisfied.
Two new core tests failed before editing. Core measurement and public reference
resolution fell back to coordinates saved in canvas keys. Preview already used
live points, but missing fit indices still fell back to saved coordinates.

## Shared Correction

Resolve supported spline endpoints, indexed fit/control points and tangent grips
from the current spline in both dimension measurement and public lookup. Native
control tangents use the existing quarter-vector grip and supported tangent
control index. Preview refuses nonexistent/unsupported identities rather than
accepting saved positions. The existing dimension-preserving translation fallback
now validates actual geometry; whole-spline Fix still prevents that fallback.
Unidentified spline sample keys cannot validate driving dimensions. The earlier
test allowing snapshot-only spline measurements is intentionally corrected;
ellipse behavior is not changed in this slice.

## Verification Scope

Core covers both spline representations, nonexistent/wrong-kind indices,
unsupported labels, tangent span preservation and Fixed refusal. Browser fixtures
are documented under tests/DXFER.Blazor.Tests/fixtures/drag-matrix. Native degree,
knots and driving intent must survive history and reopen; a further independent
point edit must work. The browser contract checks painted held-pointer geometry
as well as exported committed geometry.

All six local browser fixtures (fit/control/tangent, free/Fixed) pass: held-pointer
geometry, release, driving intent, valid history/reopen/reedit, and Fixed refusal
without Undo. 1,119 core and 408 JavaScript tests pass. Unclamped endpoint
resolution uses actual evaluated curve endpoints, not the outside control
vertices; unsupported unclamped tangent grips still refuse. An initial pixel check sampled a fit-spline
point outside the fitted viewport after translation; visible fit points now
provide the check, without weakening the exported-geometry assertions.

This is not a claim of arbitrary coupled spline solving or numeric resizing of
all spline dimensions. Unsupported identities fail closed; that does not prove
mathematical inconsistency. Previously damaged geometry is not reconstructed:
use Undo or verified originals and explicit review. No records were repaired.

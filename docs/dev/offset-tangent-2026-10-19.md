# Tangency on offset-dependent curves

## Root Cause

Applying Tangent to a derived outer circle positioned that circle independently.
Offset propagation only rebuilt dependents after source movement; it did not
carry the source along. Full-system validation then correctly rejected the
broken Offset relationship and restored geometry. Picking the original circle
worked because its movement already propagated outward.

## Correction

During one newly added active Tangent constraint, rigid dependent translation
may propagate back to unchanged Offset sources. The original relation must be
valid, the entire dependent shape must match a pure translation, and every
source must pass existing Fixed guards. Source updates are staged atomically;
normal propagation then updates chains and siblings. Complete constraints,
dimensions and protected-layer commit checks still decide acceptance.

This is not general inverse-offset reconstruction: resizing, rotation and
topology edits are not inferred. Dimension edits, ordinary dragging, mixed
dimension/constraint requests and replay of saved constraints do not enable
reverse propagation. In particular, editing a line's offset gap still moves
only its dependent instead of translating the source to preserve the old gap.

## Verification and Scope

- Detached baseline f7d0f08: 16 failures / 16 passing controls. Evidence:
  `/tmp/dxfer-offset-tangent-tests/offset-tangent-baseline.trx`.
- Candidate: 2082 core tests and 1008 JavaScript tests pass. Forty Tangent
  cases cover both pick orders, source/derived rectangle edges, chains,
  siblings, relation order, native persistence, Fixed/center/origin guards,
  locked sibling commit protection and mixed/replayed-request guards.
- Browser replay: `tests/DXFER.Blazor.Tests/offsetTangent.browser.mjs` uses
  actual Offset and Tangent controls with original and offset rectangle edges,
  both pick orders, unchanged radii/gaps/target, Undo/Redo and native reopen.
- No UI layout or styling changes. Existing canvas picking, relation glyphs,
  dimension fields and status feedback are retained.
- DXFer Dev only; Production/Test, sign-in and Sync callbacks stay unchanged.

## Installed Verification

Dev `f18760764de147dd51e06eac564fd05c2ce1a624` verified at
2026-10-19T15:15Z. Public-URL browser replay passes all four original/offset
rectangle edge and pick-order combinations, including history and native
reopen. Rendered build tooltip matches; screenshots inspected and no browser
errors. Evidence: `/tmp/offset-tangent-dev`, `/tmp/offset-tangent-dev-browser.log`.
The replay hides origin axes for unambiguous selection and picks clear of the
existing gap dimension; earlier harness failures selected the dimension, not
the edge, and are not solver failures.

Four installed assembly hashes match staging; ten runtime settings preserved;
health 200, anonymous root 302 and Production/Test PIDs unchanged. No new
service warnings. Rollback: `/home/ryan/dxfer-runtime/dev-publish-before-f187607`.

## Existing Drawings

No saved file or business record is rewritten. Old failed Tangent intent is
retained intentionally: delete the failed Tangent and apply it again to retry
with this correction. Previously conflicting generic gap dimensions are a
separate issue and must be removed deliberately. Broader solver convergence
and alternatives with anchored families/free peers are not claimed solved.

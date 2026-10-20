# PlaneGCS Pattern Initialization

## Cause And Fix

Dragging a pattern seed initialized the source at its proposed location but
left generated members at their old coordinates. For large radial patterns,
native hard-constraint initialization spent its deadline correcting that
inconsistent starting guess before reaching the soft pointer solve.

The mapper now records each member's existing affine point transform and
tries propagating the source guess before native insertion. It accepts the
candidate only if fixed coordinates stay exact and the complete existing
geometry/hard-residual validator passes. Otherwise every proposed point
change is restored, including changes made before a later member fails.
The optimization runs only for drags. Unsupported or incomplete curve
propagation falls back to the original guesses.

This does not change the native equations, unknowns, parameter values,
pointer priority, solver algorithm, iteration budget or five-second host
deadline. PlaneGCS still solves the complete system, and final native
diagnostics and hard residuals remain authoritative. No post-solve analytic
regeneration or Legacy fallback is introduced.

## Evidence

The isolated investigation compared nine baseline/optimized negative pairs
at 8, 128 and 256 members, including early/later Fixed members and a driving
distance. Each rejected candidate restored its original guesses; complete
result hashes matched the baseline.

Fresh-process positive benchmarks pass from 16 through 512 members for
Rectangular and Radial patterns. The benchmark checks every member position,
radius, identity and request immutability, not just the returned status.
The integrated radial-512 sample took 2.12 seconds alone and 2.81 seconds
while Core/browser verification was also running. The previous baseline
exceeded five seconds. These are single-source circle benchmarks, not a
guaranteed bound for arbitrary drawings or concurrent load.

Final suites: 1,742 JavaScript and 3,023 Core tests pass. The new 38-case
actual-WASM regression set covers early/later Fixed and locked references,
complete rollback, all three pattern kinds, line/circle/arc/ellipse seeds,
mixed source sets, zero-origin auxiliary mapping, order and Offset coupling.
Arc fixtures specify driving radius/sweep; they do not establish preservation
of unconstrained arc shape under soft preferences.

All 35 local browser workflows pass, including held pattern-source drag,
Undo/Redo and native reopen/edit again. The local assembly tooltip predates
this change; its served solver asset hash was checked against the changed
source. Installed-revision verification is still separate.
Browser evidence: `/tmp/dxfer-pattern-seed-browser`.
Logs: `/tmp/dxfer-pattern-seed-all-js-final.log`,
`/tmp/dxfer-pattern-seed-core.log`, and
`/tmp/dxfer-pattern-seed-benchmark.log`.
Isolated evidence and the baseline comparison are in
`/tmp/dxfer-planegcs-affine-seed-review-20261020`.

## Remaining Limits

Constrained radial-512 negative fixtures still reach the original deadline
after the candidate is rejected. They remain unchanged; this is not a
successful solve or proof that their hard constraints are inconsistent.
Large-pattern interactive responsiveness remains limited even when the
request finishes inside five seconds.

No saved drawings or application records are rewritten. The reproduced
timeout refused unchanged, so no data repair is required for this case.
Deployment and final browser evidence are recorded below when verified.

## Dev Deployment

Installed `5f116e4` on 2026-10-20 at approximately 17:52 UTC. The public tooltip
reports that revision and PlaneGCS. Installed rectangle/radial pattern-source
drag workflows pass, along with the actual-created rectangle corner and
redundant-peer Horizontal circle regression. Evidence:
`/tmp/dxfer-pattern-seed-installed-5f116e4`.

The published Node/WASM host passes the 512-circle radial fixture in 2,227 ms,
including process startup and framed output, with every output member checked.
Public solver/pattern/offset asset hashes match source. Dev PID 2110950 keeps
PlaneGCS, full editing, disabled Sync callbacks and isolated Dev keys.
Anonymous root redirects to Google with the correct HTTPS Dev callback;
auth status returns 200. This is not an interactive Google-login test.
Production/Test PIDs 3584417/3584475 are unchanged.
Rollback: `/home/ryan/dxfer-runtime/dev-publish-before-5f116e4`.

Additional baseline issue found during testing: a free arc's center drag
inside a radial pattern can change its radius/sweep despite a feasible pure
translation. This reproduces before this optimization; dimension-constrained
arc tests do not close that separate defect. It is being investigated with
an unconstrained regression rather than accepted as normal dragging behavior.

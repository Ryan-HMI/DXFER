# Isolated Native Pointer Fast Path

2026-10-20 investigation only. No application source, runtime timeout,
native algorithm, publication, or deployment changes. All experimental
solver edits are in `/tmp/dxfer-planegcs-pointer-fastpath-20261020/`.
The evidence archive retains those isolated sources.

## Result

**Yes for this compatible radial-circle drag, if the pointer's coordinates
become native fixed parameters. Simply adding hard coordinate equations
is not fast. The fallback strategy is not yet safely bounded.**

Latest measurements include Main's fresh-GcsSystem-per-request lifecycle
update and fixed-parameter wrapper changes. The module remains initialized
as before. Each timing sample ran in a fresh Node process; therefore the
earlier measurements also never relied on native state reuse.

| Approach | Radial 128 | Radial 256 | Radial 512 |
| --- | --- | --- | --- |
| Native fixed pointer, weak goals omitted, latest lifecycle, median of three | 38.55 ms | 111.96 ms | 530.94 ms |
| Hard coordinate equations, weak goals omitted, median of three before lifecycle update | 185.78 ms | 1267.20 ms | 10202.94 ms |
| Hard coordinate equations, latest lifecycle control | not rerun | not rerun | 10150.09 ms |
| Original mixed-priority solve, latest lifecycle control | not rerun | not rerun | 11358.90 ms |

Latest fixed-pointer ranges: 37.00-38.70 / 104.30-115.63 /
530.31-580.09 ms. All nine fixed-pointer samples completed under the
unchanged **five-second external test deadline**. Slow comparison controls
had a separate 30-second diagnostic deadline, not an application timeout
change. Module initialization, excluded from the table, was about 10-13 ms.
Results are from a shared development host, not a concurrency capacity test.

Every compatible fixed-pointer sample returned:

- Pointer exactly at (31,21).
- Maximum radial relationship residual 3.69e-9, below the mapper's 1e-6
  tolerance.
- No radius change in any circle.
- Original request unchanged.
- Peak RSS at 512 of about 155-160 MiB in the lifecycle-updated runs.

The original mixed solve placed the pointer within 1.4e-13 of that same
target and radial relationships within 4.5e-14, with unchanged radii.
The fast result therefore matches this fixture geometrically within the
existing tolerance, not bit-for-bit.

## What Was Tested

Both experimental paths identify exactly two temporary pointer-coordinate
goals, with source labels `Center drag target` or `Drag target`. All other
temporary goals are omitted for the first attempt. In this fixture the
omitted goal is the weak radius stability preference.

The equation variant only changes those two goals to non-temporary native
constraints. It leaves the center coordinates in the native unknown list.

The fixed-parameter variant requires both coordinate goals to refer to the
same native point. It sets that point's native `fixed` flag and target
coordinates, then removes the two now-unnecessary coordinate equations.
It never changes application-level Fix metadata. An already-fixed point
with a different target is rejected before native insertion.

The performance distinction is explained by native system construction:
`GcsWrapper.push_point` passes the fixed flag into native parameters;
`GcsSystem::solve_system` omits fixed parameters from the unknown list.
`planegcs/GCS.cpp` builds component edges only for unknown parameters.
Fixing the seed center lets each radial member's center equations separate
into small components. Adding coordinate equations does not eliminate
the shared seed unknowns and leaves the large component coupled.
No analytic reconstruction or custom solver was introduced.

## Fallback Findings

On a rejected fast attempt, the diagnostic harness passes the **unchanged
original request** to a newly created original mixed-priority solver.
It uses another module instance for clarity of isolation; this is not a
proposed production module/resource lifecycle.

Latest lifecycle-updated results under a five-second whole-process deadline:

| Incompatible target | First attempt | Final original fallback |
| --- | --- | --- |
| 8 circles, source center already fixed | Rejected before native solve | Solved; total 28 ms |
| 512 circles, source center already fixed | Rejected before native solve | Solved; total 560 ms |
| 8 circles, first generated member center fixed | Native conflict detected | Solved; total 31 ms |
| 512 circles, first generated member center fixed | Did not return before deadline | **Not reached; process timed out** |

Successful fallbacks preserve the fixed geometry and match standalone
baseline results. The pointer remains away from its requested target,
which is correct when that target conflicts with hard geometry.

The large indirectly incompatible case is a significant blocker:
before the lifecycle update, an extended **30-second** diagnostic also
expired before the speculative native solve returned. A standalone
original mixed solve for that same case completed in **4392 ms**.
The lifecycle-updated fast strategy again exceeded five seconds before
returning from its first solve. This is not the clear_data reuse defect.

Therefore an unbounded "try fast, then fallback on failure" sequence can
regress a case that the original solver completes inside the existing
deadline. Node timers cannot interrupt a synchronous WASM native call in
that same process. Any production experiment would need an external
sub-deadline/termination boundary for the speculative attempt and one
aggregate request budget, preserving enough time/resources for fallback.
A case already near five seconds may have no affordable speculative
budget. No such watchdog or scheduling change was implemented here.

## Semantics And Adoption Warnings

- This is only a point-target/circle-center fixture. Radius grips, tangent
  handles, ellipse edits, axis-projected gestures, multi-point translations,
  snap relations, offsets and general constraints were not qualified.
- Omitting weak shape preferences can select a different size or solution
  branch in underconstrained geometry. The unchanged radii here do not
  establish general semantic equivalence. Keeping preferences on independent
  components might help, but that variant was not measured.
- A hard pointer target changes the optimization objective. Exact-target
  success is not by itself proof that the original least-squares/preference
  result would be identical.
- Source-label matching is diagnostic scaffolding. A production gate would
  need explicit typed intent metadata, not display-label matching or an
  assumption that every temporary relation is a pointer target.
- Do not omit non-pointer temporary snap or other semantic relations just
  because this fixture has only a radius preference. Eligibility must
  preserve all relevant intent, or reject the fast attempt.
- Never overwrite existing fixed coordinates, dimensions, or constraints.
  Independently validate original hard geometry and the promoted pointer
  target before accepting a candidate; status alone is insufficient.
- Native diagnostic DOF changes from 3 to 1 when the pointer is fixed in
  this fixture. Transient attempt diagnostics must not be represented as
  permanent drawing DOF/redundancy facts.
- A failed attempt must not seed fallback with partially solved geometry
  or retained GcsSystem state. The lifecycle update and complete immutable
  request must remain in force.
- Even the successful roughly 0.53-second native request is not a
  frame-rate drag solve and the current server bridge remains synchronous.
  This investigation makes no UI responsiveness or complete-capacity claim.

Recommendation: the native fixed-parameter variant merits a separately
scoped implementation experiment with strict eligibility, result checks,
and bounded fallback. The coordinate-equation variant does not solve the
512 performance problem. Do not integrate this diagnostic patch as-is.

## Reproduction

`evidence/pointer-fastpath-isolated-sources.tar.gz` contains two isolated
source snapshots, excluding the vendor directory:

- `runtime/`: snapshot before the lifecycle update, with diagnostic variants.
- `runtime-lifecycle/`: updated snapshot, both diagnostic variants, and
  `planeGcsSolver.original.js` preserving Main's unmodified source.

Extract into a disposable directory and copy the unchanged, rebuilt
`wwwroot/vendor/planegcs/dist` into each snapshot's
`vendor/planegcs/dist`. Do not replace application files.

```sh
timeout --kill-after=1s 5s node --max-old-space-size=128 \
  --wasm-max-mem-pages=4096 --experimental-default-type=module \
  docs/dev/planegcs-runtime/rebuild/pointer-fastpath-benchmark.mjs \
  /absolute/disposable/runtime-lifecycle 512 fixed-fast radial
```

Modes: `fixed-fast`, `fixed-strategy`, `fast` (coordinate equations),
`strategy` (equation attempt then fallback), and `baseline`.
Fixtures: `radial`, `fixed-center`, and `fixed-member`.
Use a separate 30-second diagnostic ceiling only when reproducing the slow
controls; do not infer an application timeout change.

Current evidence:

- `evidence/pointer-fixed-lifecycle-three-rounds.jsonl`
- `evidence/pointer-lifecycle-controls512.jsonl`
- `evidence/pointer-fixed-lifecycle-fallback.jsonl`
- `evidence/pointer-fixed-lifecycle-isolated.patch`
- `evidence/pointer-fixed-incompatible512-diagnostic.jsonl` (pre-update
  30-second failure)
- Other `pointer-*.jsonl` files retain the earlier comparisons.

Only investigation evidence was added to the worktree. The application
performance/fallback problem remains **unfixed**. No business records were
modified or identified as needing repair.

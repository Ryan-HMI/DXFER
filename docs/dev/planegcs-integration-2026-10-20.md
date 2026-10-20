# PlaneGCS Integration

Ryan's exclusive implementation priority is PlaneGCS. Flux owns independent
testing, not design or implementation. Unrelated feature work is paused.

## Current State

The integration is locally verified and awaiting Dev rollout and independent
acceptance. The previous Dev deployment still uses the procedural solver.

Local verification has progressed beyond the smoke test:

- Current integrated Core run: **2,874 passed, none failed or skipped**.
  Full JavaScript suite: **1,605 passed**; the focused actual-WASM subset has
  **443 passing tests**. All **16 actual Blazor browser workflows pass** after
  the source-preserving Offset correction.
- Sustained browser motion produced 41 accepted previews during 1.87 seconds
  of dragging. Release committed the latest pointer; Escape rejected pending
  stale output. History and native reopening passed. Evidence:
  `/tmp/dxfer-planegcs-sustained-drag-final/results.json` and
  `/tmp/dxfer-planegcs-browser-full-final`. The full local browser run used
  LongPolling after a WebSocket handshake 503; no zero-console-error claim.
- Native-system lifetime is per complete request, while the WASM module remains
  loaded. Reusing a cleared GcsSystem reproduced success followed by failures on
  the identical joined-outline drag; a fresh system fixes the repeated cases.
- Whole-Fix curve scalars use fixed native parameters, not approximate equality
  equations. Direct Offset gap edits hold the source exactly. Contour joins and
  directed normal equations retain closure and side. Validation canonicalizes
  endpoint roundoff only in a read-only topology copy, never in actual geometry.
- Polygon pattern mappings, explicit drag snaps with AutoOFF evidence retention,
  and dimension-label following are integrated. The full Core run includes
  the unchanged legacy anchor regression and the new offset regressions.

Earlier milestones (historical subsets, not additional totals):

- 2,662 Core tests pass after introducing the explicit injected adapter.
  Most are existing regression tests, not independent PlaneGCS acceptance.
- Actual WASM suites include eight retained-offset and fifteen retained-pattern
  cases. They test real engine execution, not mocked responses.
- The first actual Blazor browser run exposed a synchronization-context deadlock
  in the synchronous runtime bridge. The asynchronous pipe operations now avoid
  capturing that context. Six actual Blazor browser integration cases now pass,
  covering creation, drag preview/release, fixed protection, midpoint/origin
  relations, dimensions, history and reopening.
- Retained-pattern equation tests cover rectangular, staggered and radial
  source following, driving radius edits, member references, locked peers,
  malformed ownership, arcs, ellipses and rational splines. Browser integration
  remains separate.
- Native ellipse/spline mapping has bounded real-engine coverage. Fit/Hermite,
  crossed axes and gesture parity are still under integration verification.

## Architecture

- Pin `@salusoft89/planegcs@1.3.0`, retain license and corresponding source.
- Keep a neutral full-document contract, not the visibility-filtered canvas DTO.
- Map geometry, stable references, constraints and dimensions into actual
  PlaneGCS equations. Read-only display measurements are not driving equations.
- Use an isolated per-scope Node host for the pinned WASM on the server.
  Requests and replies are bounded and identified; timeouts kill the child.
  The child does not inherit application credentials. No network service or
  remote solver is involved.
- Route both preview and committed edits through the same engine. Coalesce
  pointer updates and reject stale preview responses after movement, cancellation,
  document replacement or release.
- Keep transient drag goals separate from permanent sketch intent.
- Preserve IDs, metadata, layers, fixed geometry and driving intent. Apply
  accepted candidates atomically; never use legacy propagation to repair a
  PlaneGCS result.
- Unknown or unsupported mappings refuse explicitly. Runtime failure never
  selects Legacy automatically.

`Dxfer:SketchSolverEngine=PlaneGCS` is explicit opt-in for isolated verification.
The default remains Legacy until the replacement is ready for an authorized
Dev rollout. This preserves other deployments while implementation is incomplete.
Selecting an unknown engine is a configuration error, not a silent fallback.

## Acceptance Gates

- [x] Actual pinned WASM loads and solves an isolated smoke fixture.
- [x] Full snapshot mapping and residual/protection checks for supported geometry.
- [x] Initial bounded real-engine core fixtures, with no mocked or fallback success.
- [x] Constraint creation, dimension edit and deletion recovery integration.
- [x] Live preview/release equivalence and stale-response rejection.
- [x] Offset, pattern and curve support mapped or explicitly scoped as unfinished.
- [x] Undo/Redo, native reopen and repeated edit.
- [x] Fixed/driving/locked-layer failure and recovery.
- [ ] Installed Dev version and runtime verification.
- [ ] Independent Flux acceptance of stated scope.

The first tests target coupled Parallel/Perpendicular rotation, fixed-midpoint
length changes, tangent free radii, free rectangle corner movement, and atomic
refusal. A few passing examples are not proof of complete solver replacement.

## Native Primitive Regression

The pinned runtime returns an incorrect result for a `proportional` equation
between two free coordinate parameters combined with a driving coordinate:
`b.x = 0.309 * a.x`, `a.x = 4` returns `a.x = b.x = 4` with success.
The same equation works when `a` is a fixed point. Independent residual checks
caught this while testing radial patterns.

The pattern mapper uses native equal-length and angle relations for nondegenerate
radial points. At a zero-length radial locus, it expresses affine multiplication
with fixed-slope point-on-line equations and coordinate equalities, all solved by PlaneGCS.
It does not post-correct geometry or call the procedural solver. Non-axis radial
patterns and inverse member-reference movement are covered by the actual-WASM
regressions. The exact upstream cause has not been established; no vendored
solver algorithm has been patched. The memory-only rebuild below supersedes
the original npm binary.

The first all-affine radial mapping exhausted native memory at 64 instances.
Reducing auxiliary variables to the native polar equations corrected that
reproduction. A fresh-process benchmark now solves 16/64/128 circle instances for
rectangular and radial patterns. On this host, 128 rectangular instances took
about 57 ms and 128 radial instances about 211 ms, excluding browser transport.
These are bounded smoke measurements, not a sustained frame-rate guarantee.
Larger patterns and continuous pointer load remain separate performance gates.

The original capacity probe failed: 256 rectangular circles exhausted the npm
binary's fixed 337-page memory. A reproducible bounded memory-growth rebuild
from the pinned source corrects that cause: 16 MiB initial memory, 256 MiB hard
maximum. Two builds were byte-identical, the second offline. Rectangular 256/512
and Radial 256 solve. Radial 512 remains CPU-bound at about 11 seconds in the
diagnostic run; the application retains its five-second timeout and
unchanged-geometry refusal. See `planegcs-runtime/rebuild/README.md` and
`planegcs-runtime/rebuild/RADIAL-CAPACITY.md`. This performance limit is unresolved.
The speculative fixed-pointer fast path is not integrated: although its radial
512 positive case took about 531 ms, incompatible targets could stall before
fallback. See `planegcs-runtime/rebuild/POINTER-FASTPATH.md`. Omitting soft goals
also needs broader equivalence testing; neither a timeout increase nor an
unverified solver shortcut is part of this rollout.

## Existing Drawings

No data repair or native-file migration has been performed. Existing documents
must keep their current geometry and intent; unsupported dependencies cannot be
discarded to make the new solver appear successful.

## Workbench Mutation Boundaries

The current opt-in Workbench is not at complete engine parity. Its former
document-wide `CanApplyPlaneGcsDirectGeometryEdit` guard has been replaced by
`SketchDirectEditValidationService.TryValidate`. The commands below produce pure
geometry candidates, then validate the final snapshot before the shared
single-history commit. Unrelated geometry is no longer refused just because
the document contains constraints, dimensions, or a pattern. Core and Workbench
regressions pass in the combined Core suite.

| Commands | Candidate entry points |
| --- | --- |
| Translate, Rotate, Scale, Mirror | `TryApplyModifyTool` |
| Add Spline Point | `TryApplyModifyTool`, `OnAddSplinePointRequested` |
| Power Trim / Extend, including crossing-path trim | `OnPowerTrimRequested`, `OnPowerTrimCrossingRequested` |
| Split at Point: line, circle, arc | `SplitLineAtPoint`, `SplitCircleAtPoint`, `SplitArcAtPoint` |
| Auto Cleanup | `ApplyAutoCleanup` |
| Bounds to Origin, Point to Origin | `MoveBoundsToOrigin`, `MoveSelectedPointToOrigin` |
| Vector to X, Vector to Y | `AlignSelectedVector` |
| Rotate 90 Clockwise, Rotate 90 Counterclockwise | `RotateDocumentAboutBoundsCenter` |

Following reported Core runtime passes, the blanket pattern and retained-offset
creation guards are removed. Pattern commits and offset creation pass generated
geometry through PlaneGCS; offset previews also solve the full document. The
common PlaneGCS commit boundary performs read-only pattern validation, never
legacy reconciliation. Source drag/dimension changes must regenerate dependent
members through the mapped equations, not through a post-solve geometry repair.
Workbench/Canvas boundary tests include rejection of a source-only pattern
movement whose generated members were not solved. Full browser acceptance
remains separate from these bounded tests.

Pure geometry generation is not itself a forbidden solver fallback. Contour
offset generation, initial pattern expansion/upsert, trimming, splitting, and
rigid transforms may produce candidates. Constraint application/propagation to
repair those candidates must use PlaneGCS, not the Legacy engine.

The validation-only acceptance path compares both the original and candidate:

- Preserve original fixed reference positions and protected geometry, not just
  the candidate's current fixed declarations.
- Preserve constraint IDs, kinds, states, and stable references, plus dimension
  IDs, kinds, driving modes/values, and stable references. Topology operations
  currently refuse geometry changes on any referenced entity; no new lineage
  remapping is claimed. Unreferenced topology edits can pass.
- Check driving residuals and constraint satisfaction without applying or
  propagating constraints. Preserve failed intent rather than erasing it.
- Validate complete pattern ownership/member geometry and retained-offset
  relationships. A stale generated member must fail or go through the mapped
  engine equations.
- Preserve metadata, layers, and user-positioned labels, and commit atomically
  with one undo entry only after acceptance.

Validation never invokes a solver or changes candidate geometry. Untouched
failed intent retains its IDs/states and does not block unrelated edits.
Changed geometry with failed affected intent is refused. Pattern snapshots
must already be consistent; this validator does not regenerate members.
Unsupported or ambiguous affected topology remains a rollout limitation.
Retained-offset and pattern equation paths remain separate from this read-only
direct-edit acceptance path.

The existing brand tooltip now includes the actual injected adapter identity
(`PlaneGCS` or `Legacy`) beside the build identity. This is an installed-QA
observation point, not proof that every mapped operation is implemented.

### Drag Presentation and Snap Limits

`SketchDimensionAnchorService.FollowDimensionAnchors(original, candidate)`
extracts the existing label-following behavior into a pure presentation service.
The Legacy call is preserved. PlaneGCS must invoke it after accepted drag
geometry so a manually repositioned dimension label follows subsequent
translation/rotation. Dedicated parity tests pass, including the original
Legacy anchor-follow regression.

The explicit drag-snap implementation is integrated: Core validates a
normalized Coincident/Midpoint relation, sends it as a hard equation in the same
solve as the pointer goal, and only retains the new relation when automatic
constraints are enabled. Preview/commit transport, actual-runtime regressions
and browser endpoint/midpoint cases with Auto ON/OFF pass. Removing temporary
OFF intent preserves native solve evidence. This does not claim every possible
implicit snap class has independent browser coverage.

### Final Workbench Boundary Audit

Source review of the current shared tree:

- Creation builds a complete candidate from `_document`, including existing
  dimensions, constraints, metadata and hidden geometry, then calls the injected
  PlaneGCS solver. Failed creation does not commit geometry or history.
- Corner-size input and handle edits regenerate a preview through
  `BuildCornerEdit`. Explicit and default-size branches both call PlaneGCS;
  Apply regenerates and checks the solve again. Generated dimensions and equal
  chains are not accepted by consuming a failed result's `Document`.
- Corner topology helpers now construct/remap candidates and perform read-only
  checks of original fixed references, constraints and driving values. No
  legacy apply/propagation call was found in `DrawingModifyService`,
  `SelectionOffsetService`, or the prep/normalization path. Corner remapping is
  specialized; it is not evidence of general topology-edit parity.
- Translate/rotate/scale/mirror/prep and trim/split/spline-point commands use
  original-versus-candidate validation before history mutation. Referenced
  topology with unproven lineage, affected failed intent, and stale generated
  dependencies remain explicit refusals. Pure validation cannot solve these.
- Constraint/dimension changes and deletion recovery use the injected solver.
  Legacy deletion recovery and pattern reconciliation remain behind the
  non-PlaneGCS branch. Label placement, radial unit conversion, and
  driving/driven mode changes use measurement/intent-only helpers; they do not
  apply legacy geometry constraints.
- Canvas uses `FullDocument` for PlaneGCS drag and offset previews. Pattern and
  offset guards permit mapped operations, but actual browser acceptance and
  unsupported native-curve equation combinations remain separate QA gates.

Boundary regressions now cover the actual corner-size binding and default
preview on an unavailable runtime, and verify hidden geometry/fixed references
remain in creation/corner requests. These assertions pass in the combined run.

Polygon Count edits have a bounded discrete staging exception: the Workbench
clones the editable polygon with `WithSideCount`, preserving its center, radius,
rotation, identity and all other entities/intent, then solves the full candidate
through PlaneGCS with the edited Count dimension. No legacy `ApplyCount` is
called. Whole-polygon Fix, protected/reference-only geometry, linked pattern
sources/members, and attached vertex/edge references refuse before staging.
Center references may remain; unprovable topology lineage is not remapped.
Success uses the normal single-history commit. A failed dimension solve keeps
the edited intent visible on the original geometry, never the staged topology.
Eight handler-level regression cases pass in the combined run.

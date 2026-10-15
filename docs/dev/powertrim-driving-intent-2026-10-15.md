# PowerTrim and split driving-intent protection

Base: `7f9c1b5`, isolated branch `fix/powertrim-driving-intent-20261015`.
Core implementation and automated verification complete; installed-browser
verification, integration, and deployment remain parent-owned.

## Root cause and bounded fix

PowerTrim's shared refresh helper measured affected dimensions and replaced
their values regardless of `IsDriving`, silently dropping unresolved dimensions.
Middle-span splits bypassed the common replacement helper but did the same
refresh. Target deletion and polygon explosion removed all attached intent.
Standalone line/arc splits retained dimensions without verifying satisfaction
or refreshing reference measurements.

All PowerTrim candidates now pass one guard before becoming observable, as do
standalone line/circle/arc split candidates. It reuses the corner dimension/Fix
finalizer; the old unconditional rewrite/drop helper is removed. Geometry
generation, intersection algorithms, exact spline trimming, and solvers are
unchanged. Rejected edits return the original document object.

- Driving values, flags, IDs and anchors remain design intent, not measurements.
- Reference values and resolvable canvas-point coordinates refresh, including
  zero horizontal/vertical measurements and elliptical-arc endpoints.
- Missing or incompatible references refuse the entire edit, including deletion
  or polygon explosion that would discard attached dimensions/constraints.
- Previously satisfied constraints cannot be broken. Fix compares original and
  candidate geometry; unaffected constraints are retained. No solving or forced
  remote-geometry movement is introduced.
- Batch crossing retains existing per-pick semantics: valid picks commit, refused
  picks contribute neither geometry changes nor the applied count. Earlier and
  later valid picks remain; an entirely refused batch returns the original.

## Applicability and safe limitations

| Target | Behavior |
| --- | --- |
| Line | Trim, middle-span split, extend, deletion and standalone split guarded. |
| Polyline | Only in-place shortening of existing end segments can retain attached intent. Vertex-count/closure changes, reordered interior vertices, reversed or relocated segments refuse because indexed topology remapping is unavailable. Unattached edits retain existing behavior. |
| Polygon | Existing explosion works without attached intent. Attached intent refuses rather than disappearing; no virtual polygon/corner model added. |
| Circle | Trim to arc and standalone split retain compatible radius intent. Whole-entity Fix refuses changed geometry. |
| Arc | Trim, extend, split and deletion guarded. Compatible radius intent remains valid; incompatible endpoint/chord intent refuses. |
| Ellipse | Trim, extend and open-arc deletion guarded. Endpoint coordinates use the existing analytic evaluator; compatible axis intent remains supported. |
| Spline | Exact trimming and supported degree-1 extension unchanged. Endpoint intent can be refreshed/validated. Attached internal control/fit/tangent canvas references refuse rather than silently rebinding after knot insertion. Higher-degree extension remains unsupported. |
| Point | Deletion permitted only when it would not discard attached intent. |
| Dimension / constraint glyph | Not geometry targets; references are protected rather than deleted. |

These conservative refusals include reference-only intent that cannot be mapped
reliably. No inference of virtual original endpoints or reconstruction of lost
topology is attempted. Already-unsatisfied constraints are not repaired by this
operation; they are retained rather than dropped.

This base has no `IsClosed` property: closed polylines repeat the first vertex.
The guard compares that closure state as well as vertex order and retained
segment position/direction, using the existing segment projection helpers.
Four red/green finalizer regressions prove that a same-count rotated rectangle
cannot rebind a bottom-edge length10 to an unrelated right-edge length10, for
both driving/reference intent and closed-to-open/open-to-open candidates. A
public rectangle trim regression also verifies atomic refusal with attached
bottom-edge intent. The direct finalizer test does not claim the existing raw
trim algorithm currently constructs that rotated candidate.

## Adapter handoff

The original bool API remains source-compatible. A bounded overload adds a
final `out string status` after `out DrawingDocument nextDocument`:

```csharp
DrawingModifyService.TryPowerTrimOrExtendLine(document, entityId, point,
    createEntityId, out var nextDocument, out var status);
```

Intent refusal reports: `Power trim/extend could not preserve dimensions,
constraints, or their references. Geometry unchanged.` Geometry-inapplicable
requests retain their separate generic explanation. Parent can assign this
status in `OnPowerTrimRequested`; this branch does not edit Razor, JS, or visual
files. Parent separately owns this wiring, standalone split refusal copy,
visible SplitAtPoint `_status`, and preserving preselection refusal feedback.
Existing generic unchanged feedback and history behavior are tested here.
The batch API still reports its existing applied count, not per-pick reasons.

## Browser fixtures and gestures

Native fixtures were generated with `DxfDocumentWriter` and individually
reopened/checked for dimension10 and the intended driving flag:
`/tmp/dxfer-powertrim-intent-20261015/{trim,split,extend}-{driving,reference}.dxf`.
Each has target `(0,0)->(10,0)`, LinearDistance10 on `target:start/end`, anchor
`(5,-2)`. Source geometry and matching tests are in `PowerTrimIntentTests` and
`WorkbenchPowerTrimIntentTests`.

1. Import `trim-driving.dxf`, activate PowerTrim, click `(2,0)`. Boundary x4
   spans y-3..3. Refuse unchanged10, native entities unchanged, Undo disabled.
   Reference variant succeeds with target `(4,0)->(10,0)`, value6, driving=false.
2. Import `split-driving.dxf`, click `(5,0)`. Boundaries x4 and x7 span y-3..3.
   Refuse unchanged10. Reference variant leaves `(0,0)->(4,0)` plus
   `(7,0)->(10,0)`, reference4 attached to the retained original-ID segment.
3. Import `extend-driving.dxf`, click just beyond the end at `(11,0)`. Boundary
   x14 spans y-3..3. Refuse unchanged10. Reference variant extends to14/value14.
4. For each reference success: Undo restores original10; Redo restores the edit;
   native download/reopen retains the geometry and false driving flag. Cutter
   geometry must remain unchanged throughout.

Parent independently reproduced installed Dev trim10->6 corruption before this
patch; screenshot `/tmp/dxfer-trim-before-driving.png`. No post-fix installed
browser result is claimed here.

## Automated evidence and existing drawings

- Initial new regressions: 25 failed / 9 passed before implementation.
- Same-count topology regression before strengthening: 4 failed; after: 4 passed.
- Final focused new suite: 58 passed (52 core, 6 headless workbench).
- Full `dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj`: 1029 passed.
- Earlier tests that explicitly endorsed driving rewriting or stale reference
  values were migrated to reference-refresh expectations; independent new tests
  require driving refusal. Polygon tests cover both intent refusal and unchanged
  unconstrained topology/constraint generation.
- Tests cover exact geometry versus unconstrained results, preserved unrelated
  intent, Fix/Equal/coincident constraints, removal, ambiguous topology, native
  reopen, failed history, successful Undo/Redo and mixed batch picks.
- No JS or UI files changed; no dependency or runtime configuration changes.

The shared cause is corrected for these operations. Previously saved corrupted
values cannot be reconstructed from the shortened value alone. Restoring such
drawings requires verified originals, undo history or backups and an explicit
compatible geometry/intent decision. No user drawings were repaired or deployed.

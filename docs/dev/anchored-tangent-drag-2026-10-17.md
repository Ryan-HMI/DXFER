# Tangent drag with an anchored center and free radius

Ryan delivery `17f9175668679e0ee72c1e7a`: dragging the smaller circle's center
or perimeter should resize the larger circle when only the latter's position
is constrained. Exact user fixture requested; reproduced with center-only Fix,
Coincident-to-origin and Concentric-to-origin, internal/external Tangent.

## Root causes

- Mobility checks saw only explicit Fix. Tangency tried moving an origin-anchored
  center instead of using its free radius, then validation rejected the edit.
- Internal tangency resizing used an absolute subtraction that could shrink the
  containing circle onto the wrong branch. The same formula affected applying
  Tangent to already-tangent geometry with an anchored center.

## Correction

Infer only anchored circle/arc centers through satisfied Coincident/Concentric
relationships to an immutable origin or Fixed peer. Do not fix their radii or
rewrite constraints. Repeat center closure to remove relation-order dependence.
Tangent drag preserves its original internal/external branch and which circle
contains the other; solve the appropriate positive radius. Core and held-preview
implement the same behavior. Driving dimensions and whole-entity Fix still guard
incompatible changes. Canvas module version updated to avoid stale preview code.

No UI redesign, production/test rollout, native-document migration or data repair.
Existing valid drawings can be edited again. Already incorrectly resized/saved
drawings need Undo or a verified original; no automatic geometric reconstruction.
General coupled-orientation and point-to-line limitations remain separate.

## Verification

Before changes, 20 of24 cases failed in both core and JS. Regression matrix covers
three anchors, internal/external tangency, center/perimeter and constraint order.
Additional core tests cover whole Fix/driving-radius refusal and recovery, a driving
small radius, and scale0.00001/1/10000 with a drag beyond the old branch boundary.
JS covers the matching branch-scale and driving-small-radius previews.

Browser fixture family: `tests/DXFER.Blazor.Tests/fixtures/drag-matrix/anchored-tangent-*.dxf`.
Runner: `tests/DXFER.Blazor.Tests/anchoredTangentDrag.browser.mjs`.
Twelve valid actual canvas drags include held screenshots, release, Undo/Redo,
native download/reopen and edit again. Two driving-large-radius cases refuse
unchanged with no history. Fourteen local exports audit0/0; held screenshot inspected.

## Installed verification

1653 .NET tests and826 JavaScript tests pass; Release publish passes.
Installed `2a127dd13a1222e5919b90f77abbade4422b61cf` on DXFer Dev at
2026-10-17T23:45Z. Public browser verified the rendered2a127dd build and replayed
all14 cases above. Fourteen public DXFs audit with zero errors/fixes; held
internal-tangency screenshots inspected.

Live assemblies match the published candidate; ten runtime settings unchanged,
health200 and anonymous root302. Normal Google auth and disabled Sync callbacks
retained. Production/Test PIDs unchanged. Rollback publish retained.
This does not claim arbitrary position-dimension chains are solved; Ryan's exact
native fixture was requested to distinguish that from the reproduced anchors.

## Independent acceptance

Flux delivery596164c17cd75eccb702f3d5 reports passing
2a127dd replay of the saved synthetic failure and six internal/external cases
with center Fix, origin Coincident and origin Concentric. Anchored centers stay
put; free radii adjust. Undo/Redo, native reopen and subsequent edits pass.
Both driving-large-radius negative controls refuse unchanged;42 exports audit
clean. This accepts those fixtures, not arbitrary coupled systems or Ryan's
original drawing. No saved drawings were repaired.

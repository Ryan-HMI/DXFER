# Drag preview constraint safety

Bounded JavaScript follow-up to the parent's authoritative core drag rollback.
Base: `8a42c3d`. No core solver, persistence, service or deployment changes here.

## Preserved interaction contract

Palette alignment, wrapping, dimensions, overflow, hit targets and responsive
layout are unchanged. Failed previews use the original document, not an earlier
optimistic frame. No failed constraint state, document history or dimension intent
is persisted by JavaScript. The core receives the release request even when the
preview declines it, and remains responsible for the result and explanation.

After existing propagation, validate all active constraints and driving dimensions.
Do not trust stored `satisfied` flags. This is a validation gate, not another solver.
An explicit `false` from `OnGeometryDragRequested`, or a rejected promise, restores
the original document, prunes stale point handles, refreshes debug attributes and
redraws. It does so only while the current document is still that request's
optimistic document. Late responses cannot overwrite newer state. Canvas backing
dimensions, CSS dimensions, pixel ratio and viewport transforms are preserved.

## Supported validation and conservative limits

- Coincident, Horizontal/Vertical, Parallel/Perpendicular, Equal, Concentric,
  Midpoint and Tangent use existing reference/geometry primitives and tolerances.
  Degenerate lines cannot satisfy directional or Equal-line constraints.
- Fix checks whole-entity preservation, individual points, or polyline segments
  against the drag-start document. A fixed endpoint does not freeze its free peer.
- Measurable driving dimensions are checked again after peer propagation. Axis
  distances measure their axis, not the diagonal. Suppressed constraints/dimensions
  and non-driving dimensions do not block valid previews.
- Symmetric, unknown kinds, missing references and unsupported entity/reference
  combinations decline the entire optimistic preview, even if unrelated to the
  dragged entity. Unknown/unmeasurable active driving dimensions also decline it.
  The core may accept on release; preview refusal is not proof of inconsistency.

Line and polyline-segment relations, circle/arc/polygon radius and center relations,
and point relations use the existing supported primitives. Ellipse center/Fix and
measurable dimensions remain available; ellipse Equal/Tangent are not inferred.
Spline free edits and whole-entity Fix remain available; unsupported spline
constraint references decline preview. Dimension anchors and constraint glyph
placement keep their separate existing handlers; this gate is for geometry drags.

## Evidence and remaining ownership

Red tests reproduced Equal + Fix radius/line corruption, mixed point/line relations,
post-propagation dimension violations, unsupported relations and explicit server
`false` retaining stale optimistic geometry. Green tests cover valid free-peer
propagation, failed-frame restoration/recovery, authoritative release, late-response
guards, exception rollback and high-DPI redraw without resizing/stretching.

Run `node --test tests/DXFER.Blazor.Tests/*.test.mjs`.
The older ellipse anchor-translation test now uses actual major-axis point references
instead of unresolvable `ellipse:start/end` driving references. Production code does
not silently ignore that invalid dimension anymore.

Parent owns core rollback, the Boolean interop contract, installed-browser checks
and deployment. No browser acceptance is claimed by these helper tests. Existing
damaged saved drawings are not repaired; recovery requires Undo or a verified original.

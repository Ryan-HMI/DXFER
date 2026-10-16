# Independent Ellipse Axis Drags

DXFer Dev-only continuation of DRAG-ELLIPSE-001 and mixed center weld coverage.

## Reproduced Causes

An ellipse with Fix center and a driving opposite diameter refused valid axis
edits. Core point resolution omitted ellipse centers, so the final fixed-point
check could not verify even an unchanged center. Preview already resolved that
center correctly. Core get/set and propagation enumeration now include it.

Major-axis dragging changed the stored vector but retained its ratio, scaling
both diameters. That broke an orthogonal driving diameter. An axis grip now
preserves the other axis length, matching existing dimension-input edits; this
is not a uniform scaling command. Core and preview recompute the ratio alike.
Existing undimensioned axis tests now assert the preserved minor length, not
the former scaling side effect. Both original core reproductions and the major
preview reproduction failed before correction.

## Verification Scope

- Free major/minor axis with opposite driving diameter and Fix center.
- Both axes driving with Fix center: refuse unchanged.
- Center weld to a line, with/without fixed peer, reversed constraint order.
- Exact/near-zero collapse refuses; negative-side valid retry preserves the
  other axis. Native axis identity remains stable when stored ratio exceeds1.
- 1143 core and424 JavaScript tests pass; Web build has no warnings/errors.
- Actual axis and welded-center canvas replay passes held-pointer pixels,
  release, refusal/no Undo, recovery, Undo/Redo, native reopen and edit again.
  Screenshots confirm visible unchanged center/orthogonal diameter and readable
  feedback. Public installation is recorded in the deployment handoff.

No UI markup/layout or solver-engine replacement. Existing status area is used.
No business records/manufacturing files repaired. Old scaled saved shapes are
not silently changed: use Undo or verified originals if correction is needed.
Arbitrary coupled ellipse systems and partial-ellipse endpoint editing are not
claimed accepted by these full-ellipse fixtures.

## Installed Evidence

Dev d72876b passed the full six-fixture public-browser replay with rendered
build tooltip verification, followed by retained clicked/corrupt-polyline cases.
Four assemblies and served app/scoped CSS/canvas module match publish. Ten
runtime settings retained, health200/anonymous302; Production/Test processes
unchanged, normal sign-in retained, Sync callbacks disabled. Independent Flux
replay requested with exact fixtures and gestures. No independent ellipse pass
or overall solver completion claimed. Next uncovered family: dimensioned
polygon translation and fixed-peer behavior.

# Closed-polyline vertex identity

Scope: DXFer Dev, DRAG-POLYLINE-001 and mixed-entity propagation. No changes to
Sync, manufacturing records, Production/Test or previously saved documents.

## Shared cause

DXF closed polylines are represented internally by a repeated first/last vertex.
Direct and solver edits wrote one array entry without updating its closing alias.
This opened an imported profile and let a Fix on the other alias be bypassed.
Six initial core cases and four canvas-preview cases reproduced the defect.
A separate paired regression reproduced failure to recognize the last alias
when recovering a partially dimensioned drag along its free direction.

`PolylineEntity.WithVertex` and its canvas counterpart preserve exact closure.
Shared point/line edits and vertex/segment dragging use those helpers. Fixed
reference sets expand either closing alias (including adjacent fixed segments)
to both; the same expansion is used for dimensions and constraint application,
not just drag rejection. Anchored dimension recovery recognizes either name.
Near-but-distinct endpoints are deliberately not welded.

## Bounded verification

- Full suites: 1,610 .NET and 794 JavaScript tests pass.
- Core/preview: first/last vertex and segment drags, opposing Fix, constraint and
  dimension refusal preserving geometry, mixed point weld in either reference
  order, partial width with Fixed peer and both closing aliases, near-open ends.
- Browser fixtures: `closed-alias-free`, `closed-alias-fixed`,
  `closed-alias-driving` in `tests/DXFER.Blazor.Tests/fixtures/drag-matrix`.
  Runner: `closedPolylineAlias.browser.mjs` with the normal QA environment.
- Fixture vertices: (0,0),(6,0),(4,3),(1,4),(0,0). Drag the closing corner
  (0,0) to (-1,1). Free moves both aliases. Fix on segment3:end refuses without
  Undo. Driving horizontal width6 with segment0:end Fixed permits (0,1).
- Verify held closing-edge pixels, one contour after release, unchanged other
  vertices, dimensions, Undo/Redo, native reopen and another edit. Saved open
  files are not automatically closed; recover from Undo or a verified original.

This closes only the reproduced alias cases, not every coupled polyline solve.
Independent Flux QA and Ryan acceptance remain separate from owner verification.

## Installed verification

Dev deployed `d8e284e3aa14a84b6beb61486fb7b85928dc3cb7` at
2026-10-17 18:36 UTC. The public browser independently reports build `d8e284e`.
All three fixtures pass against that endpoint, including held preview, release,
closure, history and native reopen/reedit (or Fixed refusal without Undo).
All five downloaded fixture files audit with zero DXF errors or fixes.
The eight spline-weld browser fixtures also pass on this installed build;
their twelve exported files audit with zero errors or fixes.
Public health returns 200 and anonymous root returns 302 to normal sign-in.
Sync callbacks remain disabled. Production/Test were not deployment targets.
Rollback publish is `dev-publish-before-d8e284e`.

## Independent QA

Flux reported bounded acceptance on d8e284e in Slack message
1792264456.968729: closing aliases and spline endpoint welds pass the tested
free/Fixed/driving cases, Undo/Redo and native reopen. Held-pointer preview was
not independently verified; its evidence remains owner-run only.

Separate OPEN solver limitation from the same report: line (0,0)-(10,0),
Midpoint to Point (5,0), Fix only the Point. Changing driving length10 to12
refuses with unchanged geometry, although (-1,0)-(11,0) is feasible. Removing
Fix allows12 and moves the marker to(6,0). Equal coupling is unnecessary.
This is a feasible-solve refusal, not proven inconsistent intent or corruption.
No correction or existing-document repair is claimed yet.

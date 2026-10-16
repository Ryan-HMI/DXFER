# Disposable Drag Fixtures

Native DXF files generated with `DxfDocumentWriter` and `IncludeNativeDocument: true`.
All coordinates and dimensions are inches. No manufacturing artifacts are used.

- `equal-rectangle`: closed polyline (0,0),(11,0),(11,12.5),(0,12.5),(0,0),
  H/V, closure, Equal/Parallel opposite short edges and Perpendicular adjacent
  edges. Bottom edge driving11. Add top driving11: redundant explanation.
  Change top to12: conflicting explanation. Both preserve geometry and intent
  through history and native reopen. Removing top permits original edits again.

- `line-free`: edge (0,0)-(10,0), driving length 10.
- `line-anchor`: same, Fix start. Drag end toward (13,3): rotate about start,
  preserving length 10. Toward (0,0) is undefined and must refuse unchanged.
- `line-fixed`: same length and Fix start, plus Horizontal. Drag toward (13,3)
  must refuse unchanged with no Undo entry.
- `poly-anchor`: vertices (0,0),(10,0),(14,5), driving horizontal distance 10
  across segment 0, Fix first and last vertices. Drag middle toward (13,3):
  result (10,3), fixed endpoints unchanged.
- `weld-free`: edge (0,0)-(10,0), driving length 10; polyline
  (10,0),(12,2),(14,2); point (10,0). Coincident edge:end -> poly:start,
  poly:start -> point. Drag edge:end toward (13,3): line translates to
  (3,3)-(13,3), both welded references follow to (13,3).
- `weld-fixed`: same, with Fix point. Drag edge:end toward (13,3) must refuse.
- `poly-clicks-corrupt`: saved vertices (0,0),(10,2),(10,6), driving length10
  with original clicked-vertex coordinates in its references. Reopen must show
  an unsatisfied dimension without changing geometry or driving intent.
- `mid-anchor`: line(0,0)-(10,0), Midpoint marker(5,0), Fix marker. End drag
  toward(8,2) gives start(2,-2), end(8,2), marker unchanged.
- `mid-locked`: same, plus Fix line:start. The same drag refuses unchanged.
- `equal-tangent-fixed`: moving circle(0,0) R3, Equal fixed circle(20,0) R3,
  moving circle Tangent to line(-5,3)-(5,3). Center motion is valid; radius5
  refuses while the equal peer remains fixed.
- `tangent-radius-free`: circle(0,0) R3 tangent to fixed line y3. Move center
  to(0,1): R2. Moving onto y3 refuses collapse; moving to(0,2) recovers with R1.
- `tangent-diameter-fixed`: same plus driving diameter6. Center motion along X
  is valid; Y displacement refuses without changing circle, line or diameter.
- `spline-control-free` / `spline-fit-free`: cubic controls or fit points
  (0,0),(1,0),(3,1),(4,1), driving distance1 between indexed points0/1.
  Drag point1 up1: supported dimension-preserving fallback translates the whole
  spline, not just point1. Reopen and drag point2 down0.5: points0/1 stay put.
- `spline-tangent-free`: same clamped cubic, driving distance0.25 from control0
  to start tangent handle. Select endpoint to expose the square handle, then
  drag it up0.5: whole spline translates while the driving span stays0.25.
- `spline-*-fixed`: same fixtures with Fix whole spline. Corresponding drags
  refuse without changing geometry, driving value or Undo history.

`dragMatrix.browser.mjs` drives actual mouse preview/release, compares preview
endpoints with exported native geometry, tests Undo/Redo, downloads/reopens,
and drags again. Run only against isolated local or DXFer Dev.
`PLAYWRIGHT_MODULE` must resolve `playwright/test`, not the base package.

Core/preview tests additionally cover reversed constraint order, near-zero
targets, negative-side targets, a failed pointer frame followed by recovery,
and removing a fixed peer before retrying. This is bounded coverage, not proof
that every coupled system has a supported solve.

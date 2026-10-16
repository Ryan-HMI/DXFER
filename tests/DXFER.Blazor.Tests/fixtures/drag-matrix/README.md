# Disposable Drag Fixtures

Native DXF files generated with `DxfDocumentWriter` and `IncludeNativeDocument: true`.
All coordinates and dimensions are inches. No manufacturing artifacts are used.

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

`dragMatrix.browser.mjs` drives actual mouse preview/release, compares preview
endpoints with exported native geometry, tests Undo/Redo, downloads/reopens,
and drags again. Run only against isolated local or DXFer Dev.
`PLAYWRIGHT_MODULE` must resolve `playwright/test`, not the base package.

Core/preview tests additionally cover reversed constraint order, near-zero
targets, negative-side targets, a failed pointer frame followed by recovery,
and removing a fixed peer before retrying. This is bounded coverage, not proof
that every coupled system has a supported solve.

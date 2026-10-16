# Coincident and Power Trim icon clarification

## Scope and contract

Ryan's bounded icon request only. References: the visual-overhaul handoff and
`docs/dev/compact-palette-density-2026-10-15.md`. Preserve the current 24-unit
SVG viewbox, 1.6-unit currentColor stroke, 20px glyph, centered 32px palette
button, and accessible command names (`Coincident`, `Power trim/extend`). No
layout, wrapping, overflow, responsive sizing, command mapping, solver, canvas,
workbench behavior, or dimension-anchor changes.

## Change and reproduction

The previous Coincident marker was offset from the line endpoints; the previous
Power Trim motif resembled scissors without a clear bounded removal. The new
focused structural tests reproduced these shortcomings before editing: 2 failed,
3 passed. Coincident now has two noncollinear segments ending at one ringed,
filled point. Power Trim shows two crossing boundaries, retained outer segment
ends, and a deletion cross in the missing middle span.

Changed the two CadIcon bodies and their shared design generator/master assets.
Regenerated standalone SVGs, sprite, contact sheet, and embedded design-preview
copies. Verified the HTML/SVG workspace preview diffs are exactly those two
artwork substitutions. No mapping change was needed. This corrects the shared
artwork definitions; no existing documents or records require repair.

## Verification

Run from the worktree root:

```sh
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --no-restore --filter FullyQualifiedName~CadIcon --verbosity minimal
node --test tests/DXFER.Blazor.Tests/workbenchIconCleanup.test.mjs
env PLAYWRIGHT_MODULE=/home/ryan/HMI-Sync/node_modules/playwright/index.mjs node tests/DXFER.Blazor.Tests/joinTrimIcons.browser.mjs
python3 docs/design/visual-overhaul-20261014/validate_assets.py
dotnet build src/DXFER.Web/DXFER.Web.csproj --no-restore --verbosity minimal
```

- 65 icon tests passed, including 5 new tests for geometry, master/runtime/sprite
  parity, bounds, existing accessible names, and compact dimensions.
- 8 existing JS icon checks passed.
- Isolated Chromium artwork review passed at device scales 1 and 2: eight
  buttons per render, 20px glyphs centered in 32px targets, nonblank/unclipped
  raster checks, no page errors. Dark, light, selected, and disabled states were
  captured; the actual-size 1x screenshot was visually inspected.
- Asset validation passed for 81 standalone masters, unique sprite IDs, valid
  visual SVGs, and no external/active master content.
- Web build passed with zero warnings/errors.

A final rebuild-based icon-test rerun encountered a concurrent parent-owned
compile error, `DimensionAnchorFollowTests.cs:16` / CS1729 (two-argument
`DrawingDocument` constructor). That file was left untouched and the error
reported to the parent. The earlier build-and-test run passed all 65 icon tests;
the final `--no-build --no-restore` rerun also passed all 65 against that built
assembly. This does not claim the parent's in-progress tests compile.

Evidence: `/tmp/dxfer-join-trim-evidence/{review.html,icons-1x.png,icons-2x.png,metrics-1x.json,metrics-2x.json}`.
The browser script reads the actual CadIcon artwork but uses an isolated static
review surface; this is not installed-app interaction or accessibility-tree
acceptance. The in-app browser service was unavailable; standalone Playwright
provided the render evidence. No phone QA, deployment, Slack, or behavior
acceptance is claimed. Parent-owned concurrent changes remain outside this commit.

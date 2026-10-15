# Visual integration, first pass

Flux handoff f18e5656 is retained under docs/design/visual-overhaul-20261014.
This pass adopts 45 directly mapped icon masters in the existing CadIcon
component, a 1.6 stroke, plus an explicit keyboard focus ring. Existing enlarged
glyph dimensions and seven explicitly requested cleanup/rotation motifs remain.
Existing enum IDs, command handlers, tooltips, labels, disabled/pressed states,
button sizes, docking and saved layout are unchanged. No runtime SVG injection,
external library, network dependency or proposed nonfunctional controls added.

## Layout contract

Glyphs stay centered within existing fixed button targets. Toolbar alignment,
wrapping, overflow and responsive behavior do not change. Focus uses an outline
without resizing controls. The canvas and palettes retain their prior bounds.
Keep the geometry-tested three-point circle/arc, centered arc and inscribed/
circumscribed polygon artwork, plus unmatched existing icons, until each is
reviewed for equivalent geometric meaning. Do not substitute ambiguous assets.

## Scope remaining

Verification: 648 .NET tests and all 265 JavaScript tests pass, including
master-artwork equivalence for every migrated icon and preservation of every
existing enum rendering and accessible button contract. Real-browser conflict,
Undo/Redo and icon-bound checks pass at 1440, 1280, 820 and 390px; desktop and
phone screenshots inspected. No browser errors. This does not claim the larger
composition or all CAD operations are fully tested.

This is not the complete workspace composition redesign. The corrected handoff
3bc680971f791700c635e1b30843f283f31b022e preserves separate independently dockable
palettes; the earlier fixed-shelf/mandatory-drawer wording is superseded.
Per-palette label/density styling, supplementary active-operation inspector,
document header and remaining composition/feedback polish are separate work.
Do not merge palettes or invent proposed controls with no implemented behavior.
Current Dev170e976 includes49 of81 icon masters; see
geometry-icon-integration-2026-10-15.md for deployed evidence and the32 remaining
dispositions. Solver and file persistence are not changed by this visual pass.
No user drawing or business record requires repair.

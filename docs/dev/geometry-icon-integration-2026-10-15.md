# Geometry Icon Integration

Follow-up: chrome-icon-integration-2026-10-15.md maps axes/menu/close, raising
the integrated count to52. The49-master counts below describe this earlier
geometry-only batch; the remaining13 retained and16 unmatched masters persist.

Base: 7ad15ab. Bounded follow-up to the 45-master integration. This batch maps
only these four existing CadIcon renderings to the reviewed icons.json bodies:

| Master | CadIconName | Existing command |
|---|---|---|
| circle-three-point | ThreePointCircle | ThreePointCircle |
| arc | Arc | ThreePointArc |
| center-arc | CenterPointArc | CenterPointArc |
| inscribed-polygon | InscribedPolygon | InscribedPolygon |

The integrated count becomes 49 of 81; 32 masters remain unmapped. No enum,
command, handler, CAD operation, document data or dependency changes.

## Layout Contract

Preserve the existing contracts in visual-integration-2026-10-14.md and
../design/visual-overhaul-20261014/IMPLEMENTATION.md. Glyphs remain centered in
the same 24-unit viewBox, with 1.9rem CSS glyph dimensions and existing 2.15rem
button targets. No changes to alignment, wrapping, overflow, labels, tooltips,
focus, disabled/pressed states, palette membership, independent docking,
visibility, saved positions, responsive behavior or canvas bounds. All seven
requested motifs, axes/chrome and the current circumscribed polygon are retained.

## Geometry Evidence

- Three-point circle: all three markers are on the radius-8 circumference
  within 0.0245 units; the established tolerance remains 0.05.
- Three-point arc: the actual relative SVG arc is a radius-8 upper semicircle;
  start, through and end picks match its endpoints/top, not its opposite half.
- Center-point arc: the actual relative radius-12 small positive-sweep arc has
  center (16,18), matching its marker; both endpoints lie at radius 12. The test
  checks coordinates numerically rather than searching for the old radius text.
- Inscribed polygon: relative moves, implicit line pairs and the absolute H
  edge are resolved before checking all five vertices against radius 9. Maximum
  radial error is 0.0444 units, still below the original 0.05 tolerance.

The polygon test reader accepts only closed M/m, L/l, H/h, V/v paths and rejects
unsupported commands. A separate reader regression checks the actual mixed
relative/absolute master path. It is test-only, not a new runtime SVG parser.
The existing circumscribed-polygon side-tangency assertion is unchanged.

## Remaining Master Audit

These are mappings of artwork, not proposals for new tools or controls.

| Masters | Disposition / existing target |
|---|---|
| axes | Safe candidate for OriginAxes; deliberately outside this batch. |
| menu | Existing application-menu button uses CSS bars; separate chrome wiring required. |
| close | Existing dialog-close/error-dismiss controls use text X; separate chrome wiring required. |
| construction, split, scale, linear-pattern, circular-pattern, rotate, text | Retain the seven Ryan-requested motifs (UT-ICON-002). Text also remains disabled/future. |
| circumscribed-polygon | Retain current corrected geometry: master radius 6.7 is not tangent to sides 8.091-8.100 from the center. Do not adopt the defective master. |
| rotate-90 | Retain distinct Rotate90Clockwise/Rotate90CounterClockwise signals; one master is not an equivalent pair. |
| auto-orient | Nearest target AutoCleanup; retain its cleanup-specific motif and combined rotate/origin meaning. |
| origin | Not a unique mapping: BoundsToOrigin and PointToOrigin are distinct operations. |
| grain | Not equivalent to separate GrainNone/GrainX/GrainY/GrainVector signals. |
| constraints | Nearest target ShowConstraints; retain the visibility-eye signal rather than imply constraint creation. |
| select, pan, zoom | Canvas behavior exists, but no corresponding dedicated toolbar control. |
| extend | Part of PowerTrim, not a separate command/control. |
| dimension-angle, dimension-radius, dimension-diameter | Dimension modes, not separate controls. |
| polygon | No generic control; existing commands distinguish inscribed/circumscribed. |
| grid, layers, search, help | No matching implemented command/control. |
| check, warning, conflict | No corresponding static icon bindings; not additional commands. |
| return-to-tools | No corresponding control; not equivalent to ReturnToSync. |

Remaining count: 3 safe existing-control candidates, 13 retained masters and
16 without dedicated targets. No axes/chrome work is included here.

## Verification

The four new CadIconHandoffTests equivalence cases failed against the old
artwork, then passed after the substitutions. Focused icon tests: 56 pass.
Full .NET suite: 850 pass. All 333 JavaScript regressions pass; no JavaScript
was edited. Commands:

```text
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --filter FullyQualifiedName~CadIcon
dotnet test tests/DXFER.Core.Tests/DXFER.Core.Tests.csproj --no-build --no-restore
node --test tests/DXFER.Blazor.Tests/*.test.mjs
```

Parent integration reran850 .NET tests and the actual local browser at
1440/820/390px. All four existing buttons activate their original tools; SVG
bounds stay within fixed targets, no page overflow or JS errors, and the canvas
remains nonblank. Imported geometry is unchanged after activation/cancellation.
Desktop and phone screenshots inspected under `/tmp/dxfer-geometry-icons-*.png`.
Repeatable check: `/tmp/dxfer-drag-live-qa.cjs --icons`.
Installed on Dev67ef64a and retained in current170e976. Actual installed browser
checks pass all four command mappings and fixed icon bounds at1440/820/390px;
canvas nonblank, no overflow or geometry changes. Parent rechecked the current
installed Blazor assembly against the reviewed Release build: SHA256
`a7e9b07c1a6f58377d602b33d467e771d9a3ced827f0cb5edae7d4ae1d890772`.
This is not full UI acceptance. The corrected independent-palette contract at
3bc680971f791700c635e1b30843f283f31b022e remains authoritative; asset delivery
does not establish implementation of its entire workspace composition.
Existing drawings/business records require no repair for these static glyphs.

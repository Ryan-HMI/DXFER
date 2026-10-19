# Circular Pattern Center Dimensions

## Report and Scope

Ryan reported that dimensioning between circular-pattern instances fails
(Slack delivery `c6201182761a19234c059ca2`). Investigated against Dev `5fbc681`.
This is separate from the completed pattern-center reference change.

## Reproduction

Seed circle: center `(4,0)`, radius `1`; full radial pattern with four instances
about `(0,0)`. Adjacent center distance is `4 * sqrt(2)`.

- Explicit center picks construct and place that measured distance, in either
  order, for seed/member and member/member pairs. Canonical and canvas point
  references both work.
- A reference dimension persists through native reopening and follows source
  movement and regenerated members.
- Changing the driving distance to `8` fails without changing the geometry,
  pattern membership, or previously accepted dimension value.
- Circle-body picks do not mean center picks: the existing body-pair handler
  expects a directional Offset relationship and refuses unrelated circles.

## Underlying Cause

The point-distance solver treats generated members as read-only. For a pair
of members it cannot move either point. For seed/member it tries moving the
seed relative to the old member position, but regeneration invalidates that
distance and the pattern dependency guard rejects the result.

The initial suspicion that the factory rejected explicit center pairs was
disproved: center references do not carry the whole-circle selection type.
No selection-path patch was made on that hypothesis.

## Unresolved Behavior

Asked Ryan whether this driving distance should change pitch-circle radius
while preserving the pattern center, count, angles, seed phase and hole sizes,
or instead change angular spacing (Slack `1792431189.081129`).

For the radius interpretation, a same-seed radial pair has
`R = distance / (2 * abs(sin(deltaAngle / 2)))`. That requires a pattern-aware
solve before final classification, with complete dependency validation.
Ambiguous ownership, multi-source families, degenerate angular separations,
Fixed/locked geometry and competing drivers need explicit protection.

Do not relax member guards, detach copies, or silently convert requested
driving dimensions to reference dimensions. No inverse solve is implemented
by this investigation. No saved drawings or Sync records were rewritten.

## Evidence

`PatternCenterDimensionTests`: 28 passing characterization cases, including
eight current-distance placements, eight reference/source-follow cases,
eight unsupported driving edits and four body-pick/Offset controls.
The refusal cases document a limitation, not feature acceptance.

Log: `/tmp/dxfer-pattern-center-dimension-characterization.log`.

Installed-browser characterization on rendered build `5fbc681` passed three
bounded cases: member/member, seed/member and body-pair refusal. For center
picks, changing `7.0710678118654755` to `10` visibly refused and retained the
exact previous native value and geometry. Driven conversion, Undo/Redo and
native reopening passed. There were no browser/circuit errors.

Script: `tests/DXFER.Blazor.Tests/patternCenterDimensions.browser.mjs`.
Log: `/tmp/pattern-center-dimensions-diagnosis.log`.
Screenshots/results: `/tmp/dxfer-pattern-center-dimensions-diagnosis/`.
The broader first run exposed an overly strict exact-equality assertion on a
remeasured reference value (difference below `1e-15`), not changed geometry;
the corrected comparison tolerates `1e-12` only for that measurement.
Preselection-entry variants repick after tool activation clears selection;
they must not be cited as native preselection acceptance.

Only tests and this investigation report were added. No runtime deployment.

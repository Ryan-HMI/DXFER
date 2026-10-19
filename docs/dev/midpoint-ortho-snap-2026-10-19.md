# Midpoint-Acquired Ortho Constraints

## Root Cause

Hover-acquired Horizontal/Vertical guides retained a persistent source key for
endpoints, but midpoint canvas targets had no persistent point identity. The
guide still positioned geometry while sending an empty relation list at commit.

## Correction

Acquired line and straight polyline-segment midpoints now use explicit `:mid`
references. They resolve from current source geometry rather than saved canvas
coordinates. Only referenced midpoints participate in constraint propagation;
ordinary point discovery and direct Midpoint snapping remain unchanged.

Midpoints are derived references, not independently writable grips. Source edits
can move the aligned counterpart. This change does not introduce a general
reverse solve that reshapes a source segment when its aligned counterpart moves.
Unsupported midpoint references refuse instead of guessing a chord point.

## Interaction Contract

Existing canvas, palettes, layout, snap tolerances, hover timing, and cue placement
are unchanged. Horizontal/Vertical cues use the existing persistent-relation
pipeline. The File > Settings automatic-constraint toggle retains its meaning:
OFF keeps snap positioning without adding relations. Direct midpoint placement
continues to create Midpoint, not a fixed-coordinate Coincident substitute.

## Verification

Four new JavaScript acquisition regressions failed before the correction: line
and polyline midpoint, each with Horizontal and Vertical guides. The focused
suite also checks held-preview source changes and existing direct midpoint paths.
- 2,105 core and 1,016 JavaScript tests pass; Web build has no warnings/errors.
- Twelve actual-browser cases pass: line/polyline midpoint to Point, line
  midpoint to Line start, Horizontal/Vertical, automatic constraints ON/OFF.
  Off-axis mouse clicks acquire exact guides; ON saves satisfied stable refs;
  OFF keeps placement without new relations. Undo/Redo, native reopen and source
  endpoint edits pass, including following existing relations while OFF.
- Evidence: `/tmp/midpoint-ortho-core.log`, `/tmp/midpoint-ortho-js.log`,
  `/tmp/midpoint-ortho-browser.log`, `/tmp/dxfer-midpoint-ortho-snap/`.
  Guide and following-geometry screenshots inspected; no browser/circuit errors.
- Early browser test coordinates exceeded the existing 360px guide range or
  acquired an unrelated crossing line. Corrected test picks avoid both; product
  snap tolerance and guide distance were not changed.
- Core negative controls cover Fixed/locked counterpart protection, unsupported
  midpoint references and safe refusal of reverse derived-midpoint movement.

## Existing Drawings

No drawings or business records are rewritten. Geometry created without a
relation cannot identify its intended source reliably; add the intended
constraint deliberately or recreate the snapped point after the correction.
Deployment is DXFer Dev only, retaining normal sign-in and disabled Sync callbacks.

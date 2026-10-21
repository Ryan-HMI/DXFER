# Linear pattern direction controls

## Scope and layout contract

Ryan requested geometry-anchored U/V instance counts, direction flips, linked
directions, and optional stagger controls. The pattern is not a floating form.
Keep independent compact control groups on the U/V pitch guides, with count and
pitch inputs and icon buttons for flip, pick and clear direction. Buttons have
tooltips and accessible names. Stagger has a checkbox and a conditional row-shift
fraction input. Reuse Foundry tokens and existing 28px controls, with no enclosing
card or canvas resize. Clamp labels clear of palettes and other control groups at
1440x1000 and 1100x705. Show/Hide via any instance context menu covers every
control and guide. Retain staged preview, Confirm/Cancel, decimal typing and
single-step Undo. Radial center/count/mode behavior is unchanged.

## Persistent behavior

- U references a straight line or polyline segment, falling back to the existing
  rotation angle. V references another straight edge or defaults perpendicular
  to U. The references survive native DXF save/reopen.
- Signed pitches implement direction flips without reversing seed geometry.
- Stagger offsets alternate rows by a fraction of U pitch, default 0.5; allowed
  fractions are zero through one. Disabling stagger retains the value for reuse.
- PlaneGCS maps linked direction vectors and translated members simultaneously,
  rather than fixing a copied angle. Reference-edge edits regenerate the pattern.
- Missing, degenerate and generated-member references are rejected. Repeating
  rows and columns cannot have collinear axes. Protected geometry remains
  authoritative; incompatible edits must not partially mutate the document.
- Existing patterns without direction references preserve their prior rotated
  orthogonal axes and half-pitch stagger. No existing-document repair is needed.

## Verification

- Full core suite: 3,149 passed. Full JavaScript suite: 1,793 passed. The latter
  includes actual PlaneGCS WASM equations, not mocked geometry results.
- Runtime adapter and native-DXF tests cover linked direction regeneration,
  signed pitches, independent/perpendicular axes, and protected-geometry refusal.
- Browser testing found two real integration faults: the pattern-edit guard
  swallowed canvas reference picks, and direct-point initialization discarded a
  direction edit before its dependent members could follow. A dedicated guarded
  reference-pick callback fixes the former. Transactional joint pattern seeding
  fixes the latter; hard intent is validated before accepting the initial guess.
  The free-edge regression failed before this correction and passes afterward,
  preserving the seed, opposite endpoint and other direction reference exactly.
- Actual Playwright canvas replay passes at 1440x1000 and 1100x705: line U and
  polyline-segment V picking, signed flips, stagger on/off and fraction, native
  decimal key entry, context Show/Hide, direction-source dragging, exact source
  preservation, Undo/Redo, native DXF reopen and another edit, Fixed-member
  refusal and locked-layer recovery. All 40 exported native fixtures retain
  checksums and the expected geometry/relationships. No browser errors.
  Evidence: `/tmp/dxfer-linear-directions-qa-r4-selection`.
  Owner inspected both desktop screenshots. These are owner/worker checks,
  not independent Flux or Ryan acceptance.

## Installed Dev

- Deployed `8987f1e21072b11a3071c55b292c7a41c635b033` at
  2026-10-21T18:47:17Z to https://dxferdev.sync.harrisonmetals.com only.
- Public-endpoint replay independently reads that tooltip revision and passes
  both desktop sizes, all 10 layout checks and 40 native exports, including
  reopen/edit and protected-geometry controls. No browser errors. Evidence:
  `/tmp/dxfer-linear-directions-installed/linear-pattern-directions-results.json`.
- The existing rectangular/radial control replay also passes all 15 layouts and
  18 held-center-movement samples on the public endpoint, including Cancel,
  Confirm, history, native reopen/edit and instance context Show/Hide. Evidence:
  `/tmp/dxfer-linear-directions-viewport-installed/pattern-viewport-results.json`.
- Four installed assemblies embed the approved SHA; six public CSS/JS hashes
  match the publication. PlaneGCS and full editing remain enabled, Sync callbacks
  disabled, Google sign-in configured and anonymous root challenged with 302.
- Production/Test PIDs remain 3584417/3584475. All Sync environments untouched.
- Rollback: `/home/ryan/dxfer-runtime/dev-publish-before-8987f1e`.
- Flux/Ryan independent acceptance remains pending. No existing-record repair.
- The isolated QA host on port 5038 has been stopped.

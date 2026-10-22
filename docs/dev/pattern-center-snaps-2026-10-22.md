# Pattern Center Snapping

## Root Cause

Explicit pattern-center constraint selection already exists. The separate DOM
handle's pointer-drag path converted screen coordinates directly to world
coordinates without using the canvas snap picker or transporting snap intent.

## Interaction Contract

Keep the center handle attached to the radial center. Apply ordinary point snaps
after correcting for the grab offset, excluding the pattern's own center and
generated members. Show the snap type and pending relation through the existing
canvas cue. Automatic constraints OFF keeps exact snap positioning without
adding persistent relations; ON stages accepted intent with the pattern preview.
Confirm commits geometry and intent together, while Cancel restores the prior
document without history. Existing Fixed/driving/locked protections remain.
The floating card is separate UI state and must not move the pattern center.

## Construction Display

Construction dashes retain an 8px stroke and increase the gap from 5px to 9px.
The shared display helper applies this consistently to committed geometry,
selection/hover and construction previews, including imported linetypes. This
is screen-space presentation only; geometry, constraints and export are unchanged.

## Verification

Core: 3,452 passing. JavaScript: 1,958 passing, including construction display
and native center-handle snap paths.

Dev c6aa442 deployed 2026-10-22T13:37:18Z. One uninterrupted public-URL
browser run passed all 18 cases with the rendered c6aa442 tooltip verified:
five snap targets with automatic constraints ON/OFF, Cancel in both modes,
own-center/member exclusion, Fixed-center refusal, floating-card behavior at
1440x1000 and 1100x705, and actual construction canvas strokes. Native exports
verify unchanged source/target geometry, committed relations, Undo/Redo and
lossless reopen. Evidence: /tmp/dxfer-pattern-snap-card-c6aa442-public/results.json.

Screenshot review identified a card-stacking gap despite the functional pass:
the far top-left placement could put its move header behind the View palette.
The follow-up gives only the floating card precedence over palettes and adds
an actual elementFromPoint header-reachability assertion to browser coverage.

The shared handle-to-snap pipeline is corrected, not patched per drawing.
No manufacturing records or saved drawings were modified. Previously committed
snap-only placements have no relation to recover automatically; re-snap them
with automatic constraints ON or add an explicit constraint when desired.
Construction spacing is display-only and needs no data repair. Production and
Test remain unchanged; normal Dev Google sign-in remains configured and Sync
callbacks remain disabled.

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
and native center-handle snap paths. Browser verification is in progress;
no deployment claimed yet.

# Ortho Snap Precedence

## Scope and Layout Contract

Remove the two-line snap information card entirely. Keep snap markers, guide
lines and accessible canvas descriptions; add no replacement overlay, hit target,
wrapping, width or responsive layout. Other parameter cards are unchanged.

## Reproduction and Cause

A Line endpoint accepts an acquired Horizontal/Vertical guide, then the shared
world-point conversion runs automatic polar snapping a second time relative to
the draft start. When the draft start is near the acquired axis, this changes
the accepted point. The intent collector correctly rejects the now-mismatched
target, so its relation is lost. Two regressions failed before the fix: expected
(5,2) became (5.000124999,2.05), and (0,7) became (0.05,7.000124999).

Public Dev 8ae31bb browser replay also reproduced a Horizontal guide at Y=5
committing at Y=5.03. A second Enter-only path parsed the untouched rounded
length readout as a keyed driving value, changing the endpoint again and dropping
the relation. That path is corrected by accepting an unedited snap without
inventing a keyed dimension. Explicitly edited or already locked values still
go through numeric validation and may override the snap.

## Correction

Accepted geometric and acquired-guide targets take precedence over automatic
polar adjustment. Free-space ortho snapping remains active. Explicit Shift-angle
overrides remain intentional and cannot attach stale target intent. Automatic
constraint ON/OFF retains its existing persistence contract. No solver changes
or automatic reconstruction of old drawings are included.

## Verification

Core: 3,452 passing. JavaScript: 1,962 passing. Candidate browser: 12/12
guide/free-axis, H/V, automatic ON/OFF, mouse/Enter cases; 4/4 deliberate keyed
override controls. Each covers unchanged source, native relation inspection,
Undo/Redo and native reopen. Screenshot verifies guide/marker and dimension
input remain with no snap card. Evidence directories under /tmp:
dxfer-ortho-baseline-20261022-b, dxfer-ortho-candidate-20261022-d,
dxfer-ortho-keyed-candidate-20261022.

Installed on DXFer Dev as a2f6e65 at 2026-10-22T13:58:54Z.
Public-browser replay passed all 16 cases, with the rendered build verified.
Evidence: `/tmp/dxfer-ortho-a2f6e65-public`. Production/Test unchanged.

Existing drawings are not rewritten. Previously missing intended relations
require deliberate reapplication or recreation; geometry alone cannot establish
which alignment the user intended. This is a concrete reproduced path, not
a claim that every possible reported ortho interaction has been reproduced.

# Selected curve centers and tangent continuation

## Contract

Dev-only follow-on to Ryan's circle/arc center and implicit-tangent reports.
Selecting a circle/arc leaves a visible center marker using the existing pick/drag
target. It follows preview geometry and disappears on deselection. No extra entity,
layout shift, palette or mobile work. Existing disabled/fixed movement rules remain.

Lines started on a circle/arc perimeter offer both normal and tangent inference.
Use the existing 8-pixel tolerance and guide/marker styles, with finite perimeter
and arc-sweep checks. Center/interior starts are not tangent contacts. Creation
must retain a real Tangent relation when the actual committed segment is tangent;
near misses and extension-only contact must not acquire a hidden constraint.

Existing inward tangent snapping stays supported. History/native reopen preserve
geometry and relation. No live manufacturing documents or machine actions.
Broader new-circle tangency and tangent glyph leader placement remain separate.

## Verification

- 1,416 .NET tests and 627 JavaScript tests pass. Clean Web build.
- Eleven new canvas tests verify selection-only center visibility, actual registered
  paint calls, both outgoing tangent directions, center/interior and out-of-sweep
  rejection, screen tolerance and retained normal inference.
- Actual Chromium on disposable native circle/arc fixtures: selected centers stay
  painted after pointer leave, follow held drag and commit correctly; Undo/Redo pass.
- Line from `(13,14)` off radius-5 curve centered `(10,10)` snaps outward, creates
  exactly one Satisfied Tangent, retains geometry through Undo/Redo/native reopen.
  Six exported DXFs audit with zero errors/fixes. Screenshots inspected at 1440x1000.
- The insertion factory has 51 focused tests including inward/outward/finite contact,
  arc sweep, near misses and duplicate prevention. See its companion audit.
- No existing sketches are backfilled. Previously missing intended Tangent relations
  require an explicit constraint or recreation, not proximity-based bulk repair.

## Rotation label follow-on

Flux supplied a fixed-start line `(0,0)-(10,0)`, driving length 10, label `(5,-2)`.
Reference-centroid movement alone did not preserve the label's rotated offset.
Linear distance anchors now preserve their offset in the measured line frame;
horizontal/vertical distance labels retain their axis-aligned placement rule.
Core/preview and actual held-browser regression rotate the endpoint to `(8,6)`:
length stays 10 and label moves to `(5.2,1.4)`. Release, Undo/Redo and native reopen
preserve that placement. Origin/partial-line cases were rerun successfully.

Evidence: `/tmp/dxfer-centers-tangent-evidence`, `/tmp/dxfer-origin-evidence`.
Deployment and independent Flux acceptance are not claimed by local checks.

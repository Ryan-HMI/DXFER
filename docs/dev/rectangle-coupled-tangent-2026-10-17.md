# Rectangle tangent to an already-tangent circle

Ryan Slack1792279459.530749 includes screenshot F0CA31KQ3T4. The automatic
text delivery omitted the attachment; original channel history and the image
were inspected. Flux reproduced the relationship pattern on319bdf9 and3404e37,
including reverse picks. It is separate from point-qualified quadrant picking.

Current2a127dd already fixes the reproduced pattern through anchored-center
mobility. No further runtime patch or deployment was required for this check.

Fixture: larger circle(0,0)R3 Coincident center-to-origin, smaller circle(-4,3)R2
externally Tangent, ordinary rectangle(-4,7)-(-1,10) with the creation factory's
four coincident corners, two Parallel, Perpendicular, Horizontal and Vertical.
Adding Tangent between bottom and smaller circle succeeds in both pick orders
on installed2a127dd. All previous constraints remain Satisfied; origin stays
fixed, small center moves to(-4,5) and large radius changes to sqrt(41)-2.
Rectangle stays intact. This is a reconstructed fixture, not Ryan's original DXF.

Two core regressions and installed browser tests pass. Browser asserts actual
body targets, all constraint states, Undo/Redo and native download/reopen.
Both exports audit zero errors/fixes; screenshot inspected. Fixtures and browser
runner are checked in under `tests/DXFER.Blazor.Tests`.

No drawings changed automatically. Existing failed intent requires a deliberate
re-solve/reapply; Ryan's original drawing remains for manual confirmation.
Independent Flux replay on2a127dd passed both pick orders and history/reopen
(delivery596164c17cd75eccb702f3d5). The result is tangent to the edge's extended
supporting line outside the finite segment: this accepts the solve, not visible
contact within the rectangle edge. Arbitrary coupled constraints remain outside
this acceptance. Parallel pick-order correction is separately documented.

Flux also retested empty default, added-layer, renamed-layer and one-line
downloads successfully on2a127dd. The single earlier3404e37 download exception
was not reproduced; its cause is unconfirmed, not claimed fixed.

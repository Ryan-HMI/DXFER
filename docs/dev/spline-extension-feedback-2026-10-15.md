# Unsupported Spline Extension Feedback

## Cause And Fix

The canvas already targets off-end spline clicks and sends the raw cursor to
`OnPowerTrimRequested`. The core's boolean candidate builder discarded the
unsupported-extension reason, leaving the existing workbench prompt generic.
The builder now carries that reason through the public status overload:

> Spline extension is not supported for higher-degree or fit-point splines. Geometry unchanged.

Off-end classification uses the existing projection primitive on the sampled
endpoint segments, matching canvas targeting. It accepts the lateral cursor
offset for refusal feedback only; geometry tolerances and exact trim math are
unchanged. Interior projected curve picks remain trim requests. No IDs, drawing
changes, or Undo entries are created by refusal.

## Scope And Limits

- Higher-degree and fit-point open splines: explicit off-end refusal.
- Degree1 control splines: existing supported extension remains available.
- Exact interior spline trim: unchanged, including degree/controls/knots.
- Invalid/unsupported exact spline representations and closed splines retain
  existing safe refusal paths; no new extension algorithm is introduced.
- Line, polyline, polygon, circle, arc, and ellipse operations are unchanged.
  Points, dimensions, and constraint glyphs are not spline-extension targets.
- No production canvas, Razor, markup, CSS, palette, or solver changes.
- Existing drawings need no repair for this feedback defect: geometry was
  already preserved. This does not repair older approximation defects.

## Evidence And Browser Handoff

- Before: six core refusal cases failed because the reason was generic.
- After: 40 focused .NET cases pass (new core/workbench cases plus exact spline
  fidelity coverage); full suite 1,051 passed. Full JS suite 360 passed.
- JS covers actual registered pointerdown/pointerup handlers dispatching one
  callback at each endpoint, plus raw picks at scales10/100/1000.
- Workbench handler tests cover cubic, quadratic, and fit splines at both ends:
  visible command prompt, unchanged native DXF, empty Undo/Redo history.
- Real installed-browser verification remains with parent; not deployed here.

Browser fixture: degree3 controls `(0,0),(1,0),(3,1),(4,1)`, knots
`0,0,0,0,1,1,1,1`. Activate PowerTrim and click about14px left of the start
or right of the end (3px lateral offset is also covered). Verify the specific
refusal, unchanged curve/native download, and disabled Undo on a fresh fixture.
Keep the pick within the existing18px extension hit radius. Repeat with a
degree1 spline `(0,0)->(4,0)` and vertical boundary atx6: off-end click extends
successfully. Existing cubic interior-trim browser fixture should remain exact.

## Parent integration and installed verification

Integrated as f11f71b, deployed to DXFer Dev only on 2026-10-15. Production/Test
unchanged, normal sign-in retained, Sync callbacks disabled. Integrated suite:
1,052 .NET / 363 JS passed. Installed browser verified explicit refusal at both
cubic endpoints with raw14px/3px-offset clicks, exact native geometry preservation
and disabled Undo. Degree1 extension reachesx6; the original cubic trim fixture
retains degree3 and controls [(2,-4),(2.75,-4.5),(3.5,-5),(4,-4)]. Undo/Redo and
native download/reopen preserve the expected results. Screenshots:
/tmp/dxfer-spline-refusal-14.png and /tmp/dxfer-spline-refusal--14.png.
No source drawings changed. Independent Flux QA remains a separate acceptance.

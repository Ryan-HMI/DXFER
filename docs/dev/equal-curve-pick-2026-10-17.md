# Equal curve-pick normalization

Flux reproduction: Slack1792266225.029429 on Dev d8e284e. Quarter arc centered
(10,10), R3, angles0-90 and circle centered(20,10), R3. Arc midpoint selection
generated an Equal with a canvas point reference; entity-body picks succeeded.

## Shared cause and correction

The command factory classified perimeter picks as circle-like but copied the
point key into Equal. Radius solving expects a curve reference, so the resulting
intent was invalid. Equal now reuses the existing radial-dimension conversion
to entity references. The conversion is local to Equal's circle-like branch:
Coincident/Fix and other point constraints keep their original point meaning.
Tangent and Concentric semantics are not changed by this patch.

## Verification

- Four factory regressions failed before the change for midpoint/endpoint and
  quadrant picks in both orders; they now pass with satisfied Equal and unchanged
  geometry. Point-based Coincident references remain intact.
- Unequal-radius cases verify one Fixed peer permits the free radius to change;
  two Fixed peers preserve geometry and retain unsatisfied intent.
- Full suites:1616 .NET and794 JavaScript tests pass; Web build passes.
- Actual browser picks the arc midpoint (asserted hovered target), then circle
  body, and reverse order. Both yield satisfied entity references with unchanged
  geometry; Undo/Redo and native download/reopen pass. Four exports audit0/0.
- Synthetic fixture: `fixtures/drag-matrix/equal-arc-pick.dxf`, runner
  `tests/DXFER.Blazor.Tests/equalCurvePick.browser.mjs`.

No historical drawing is rewritten. Previously saved point-qualified failed
Equal intent remains uncorrected; remove/reapply that constraint after updating.
This is not a fix for the separate coupled-orientation or placement failures.

## Installed verification

Dev-only 319bdf97b65688e705c5c48f2c470d87cfe9242a installed2026-10-17T23:16Z.
Public browser asserts the rendered319bdf9 build and passes both pick orders,
unchanged geometry, satisfied entity references, Undo/Redo and native reopen.
Four public downloads audit0/0. Candidate/live assemblies match; ten runtime
settings retained, health200 and anonymous302. Production/Test processes
unchanged; Sync callbacks disabled and normal sign-in retained.
Rollback: `dev-publish-before-319bdf9`.

## Independent acceptance

Flux delivery `24744668d719e7b6a8b70159` reports a visible319bdf9 retest:
exact R3 arc-midpoint/circle picks in both orders and body-pick control pass.
Equal retains whole-curve references, Satisfied status and unchanged geometry
through Undo/Redo and native reopen. Separate arc-center Fix retains its point
reference and blocks center dragging. Eight exports audit clean per Flux.
This closes the bounded Equal pick regression, not broader solver acceptance.
Previously saved failed Equal constraints still require deliberate remove/reapply.

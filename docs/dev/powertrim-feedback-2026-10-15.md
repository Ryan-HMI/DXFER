# PowerTrim And Split Feedback

## Layout And Interaction Contract

Keep the existing command bar, status region, widths, wrapping, target sizes,
palette membership and independent docking unchanged. PowerTrim uses the shared
core refusal result; Split at point exposes its existing operation status in
the same visible prompt rather than masking it with static instructions.
No new modal, toolbar, solver state or automatic intent conversion is added.
Messages report unchanged geometry only on the core false/no-commit path.
Location errors and intent conflicts are both possible for split; its message
must not falsely assert that every refused point is geometrically invalid.

Preselected line/point split attempts retain refusal feedback instead of
overwriting it with tool activation. No applicable preselection still activates
the normal modal split tool. Successful operations retain existing history.

## Baseline Evidence

Installed Dev170e976 browser reproduced a driving10 line changing to6 after
trimming and14 after extension; both silently replaced the driving value.
Standalone split at2 retained driving10 bound to the new2-inch segment.
Native snapshots captured these failures. Disposable fixtures only; no live
manufacturing file was modified.

Fixture: target line(0,0)->(10,0), driving LinearDistance10 on target:start/end.
Trim cutter x4 spanning y-3..3; click(2,0). Extend cutter x14; click(10.2,0).
Standalone split uses Split at point at(2,0). Reference variants use the same
fixture with IsDriving=false.

## Integrated Verification

Parent integrated corefd544e9 as759c951 alongside icon commitbdbacac. All1035
.NET tests pass, including visible split feedback and preselected refusal not
being overwritten by tool activation.352 JavaScript tests pass; no JS changes.
Web build has zero warnings/errors. Local actual-browser checks pass all six
driving/reference trim/extend/split cases: driving10 remains unchanged with no
Undo entry; references become6/14/2 (split within pointer precision). Undo/Redo
and native download/reopen preserve corresponding geometry/dimensions; boundary
geometry stays unchanged. Refusal screenshots inspected and fully readable.
The chrome/menu/hotkey-dialog browser suite also passes on the integrated build.

Evidence: /tmp/dxfer-intent-*.png, /tmp/dxfer-drag-live-qa.cjs --trim-driving
with optional --reference, --extend-intent or --split-intent. Synthetic fixtures
are generated through DxfDocumentWriter, not edited native-payload bytes.

The shared cause is fixed; existing corrupted drawings were not repaired.
Recover those from verified originals or Undo/history, not inferred old values.
Dev deployment and independent QA remain separate from this local verification.

## Installed Dev

Deployed0eca667 on2026-10-15, PID4188556. All four installed assemblies match
the reviewed Release candidate, ten runtime settings are preserved, health200
and anonymous root302 retain normal Google sign-in; Sync callbacks stay off.
Production/Test PIDs unchanged. Rollback publish: dev-publish-before-0eca667.
Installed browser repeats all six driving/reference cases, history/reopen and
readable failure screenshots. Axes/menu/close/hotkey-dialog suite passes;
previous ellipse6x8 analysis and cubic tangent checks also pass. No existing
drawings repaired; independent acceptance is pending, not implied by deployment.

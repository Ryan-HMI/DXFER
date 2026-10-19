# Line axis alignment and recovery

## Contract

Ryan reports that points work but line Horizontal/Vertical fails. The perpendicular line candidate currently projects to zero length; validation refuses it. Preserve ordinary projection when usable, but retain a valid segment's length when the projection would collapse it. Respect Fixed endpoints, driving values, layer locks and linked Offset intent. Point-based alignment semantics remain unchanged.

Deleting the opposite axis from the same line/segment may retry its existing failed H/V intent in the same Undo transaction. Do not retry unrelated failures or solve from the pre-deletion snapshot. If recovery cannot satisfy the whole system, keep the deliberate deletion but do not move geometry. Do not run the solver merely to enable a Delete button. General conflict recovery after arbitrary removals is not part of this bounded change.

## Layout contract

No markup/CSS or layout changes. Preserve desktop canvas size, existing glyph/pick targets, status placement/wrapping, toolbars and panel widths. Actual delete/axis actions use the current history and guarded commit paths. Existing Point operations must remain unchanged. Browser screenshots verify axis and deletion results; no phone scope.

## Verification

In progress. Installed Dev9692551 baseline reproduces four failures: each perpendicular line rotation and each opposite-axis deletion recovery. Four Fixed/driving negative controls already pass. Actual glyph deletion preserves the pending intent's ID, but on the baseline leaves it unsatisfied with unchanged geometry (`/tmp/dxfer-axis-recovery-before/results.json`).

The shared candidate fix reuses length-preserving direction editing only when axis projection is at or below the existing geometry tolerance; nearest orientation is chosen, with positive X/Y on exact angular ties. Ordinary projection and two-point alignment remain unchanged. The explicit recovery helper is called only from the actual delete command, not from selection availability queries. Unrelated failed constraints/unmet driving values skip recovery; complete solver and layer guards must pass before accepting a recovered candidate.

Combined focused coverage:80 cases pass, including open/closed polyline preservation and collapse fallback, exact/reversed/near-perpendicular/tiny/zero geometry, fixed endpoint variants, driving conflicts, protected layers, suppressed constraints, unrelated pending intent and post-deletion rollback. Full core1,932 and JavaScript942 pass; Web build zero warnings/errors. Owner independently reran all12 recovery tests. Browser verification remains pending. No existing saved drawing or business record is automatically rewritten.

Local browser completed: all10 scenarios pass with no page/circuit errors. Both directions cover standalone alignment, actual opposite-glyph deletion/recovery, deletion of failed intent instead, Fixed refusal and driving conflict. Undo/Redo and native reopen preserve each resulting document.60 DXFs audit with zero errors/fixes. Owner inspected the recovered Horizontal screenshot. Evidence: `/tmp/dxfer-axis-recovery-fixed/`. Public deployment/replay and independent Flux/Ryan acceptance remain pending.

## Deployment

Dev4551c418fbfa19edf64d3344341bd9422d96e3f1 installed2026-10-19T05:15Z at https://dxferdev.sync.harrisonmetals.com. Four installed assemblies match; ten runtime settings retained. Health200, anonymousroot302; normal sign-in and disabled Sync callbacks retained. Production/Test PIDs unchanged. Backup: `/home/ryan/dxfer-runtime/dev-publish-before-4551c41`. Public axis and prior polyline/Offset browser regressions are running; independent acceptance is not yet claimed.

Public verification completed on rendered4551c41: all10 axis cases and3 prior polyline/linked Offset regressions pass, no page/circuit errors.60 axis plus19 polyline exports audit with zero errors/fixes. Owner inspected installed recovered-axis screenshots; runtime warning journal remains empty. Evidence: `/tmp/axis-recovery-dev/` and `/tmp/axis-recovery-polyline-dev/`. These completed results supersede earlier pending notes. Independent Flux/Ryan acceptance remains pending.

## Existing drawings

The shared collapse candidate and same-line opposing-axis deletion recovery are fixed. No historical drawings or business records were rewritten. Previously saved failed intent remains until a deliberate retry or deletion of the opposing axis; no corrupt geometry was established by these reproductions and no automatic data repair is required. Unrelated unsatisfied intent still blocks recovery rather than being silently reapplied. General solver convergence and recovery after arbitrary constraint deletion are not claimed.

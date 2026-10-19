# Polyline axis constraints

## Report and scope

Ryan's Slack message1792381770.814259 and screenshot F0CABQTDKEG show a tall four-sided closed contour with a sloped top edge and failed Horizontal intent. Later clarification identifies an offset-generated polyline; hand-drawn lines and point constraints work. Screenshot alone does not establish committed geometry loss.

Investigate the shared Horizontal/Vertical edge write path: a polyline segment is represented temporarily as a LineEntity, but must be written back through its owning polyline, preserving type, ID, remaining vertices, closure, layer/style and unrelated intent. Test ordinary open/closed polylines, closing segments, Fixed/driving refusal, native persistence and actual canvas picks. With active Offset intent, incompatible independent changes must remain refused; deleting Offset makes the copy independent. No silent unlinking to force a solve.

Current editable PolylineEntity contains straight vertices, not DXF bulges. This change adds no curved-polyline import/solve behavior. Whole polygons, circles, arcs, ellipses, splines, points, dimensions and glyphs are not single straight-edge H/V targets; their existing point-based or supported entity-specific paths are unchanged. Ordinary standalone LINE is the working control. Direct 90-degree axis projection collapse and conflict-deletion recovery are tracked separately; do not claim this writeback correction solves them.

## Layout contract

No new UI or CSS. Preserve existing desktop canvas, constraint icons, pick targets and error/status placement. Verify actual edge picks, geometry and failed/satisfied glyphs in browser screenshots. Existing polyline behavior and protected geometry take precedence over cosmetic status changes.

## Evidence

The identified shared cause is direct assignment of the temporary segment LineEntity to the parent array slot in both Horizontal/Vertical endpoint paths. Correction uses existing SketchGeometryEditor.TrySetLine, preserving parent geometry and closure aliases. Eight core baseline cases reproduced the defect; all 38 new regression cases and the complete 1,890-test core suite pass. The 942 JavaScript tests pass, and Web build has zero warnings/errors. Browser and deployment verification remain in progress.

Coverage includes open/closed contours, repeated closing aliases, both movable endpoint branches, whole/segment/endpoint Fix, feasible driving length and conflicting driving values, already-aligned edges, construction/style/provenance and unrelated entities. No analogous raw proxy assignments remain in this class. Unsupported polygon edges and perpendicular-line collapse remain unchanged, not newly supported.

Real canvas baseline on installed Dev5a21cd4 reproduces both H and V refusal. Fixed local browser passes sloped/open/closed/already-aligned edges, protected Fixed/driving intent and Undo/Redo/native reopen/edit again. Linked Offset rejects the incompatible H change without changing geometry or Offset intent; actual glyph deletion then frees the copy for successful H alignment. The initial linked-test setup picked an overlapping dimension instead of the edge; the fixture now searches other interior points and asserts the exact segment target. Six initial passes plus the isolated linked replay produce 43 clean DXF audits, including setup exports. Public installed replay is still pending.

Existing drawings will not be rewritten. The reproduced solver failure retains the original geometry with failed intent; it needs deliberate reapplication, not geometric repair. If a separately damaged save exists, use Undo or a verified original rather than guessed reconstruction. No existing-record repair was performed or established necessary by this reproduction.

Final local replay: all seven browser scenarios pass with no page/circuit errors (`/tmp/dxfer-polyline-axis-final/results.json`). Owner inspected the applied-contour screenshot. Tests use disposable fixtures and actual pointer targets; rendered badge rectangles are observed only to locate the Offset glyph for a real click/delete. No application selection or document state is changed through browser evaluation.

## Deployment

Committed/pushed 969255146c5a322da253e28d43fe0578198f8fde and installed only at https://dxferdev.sync.harrisonmetals.com on 2026-10-19T05:06Z. Four installed assemblies match the candidate, ten runtime settings are unchanged, health is 200 and anonymous root redirects to normal sign-in (302). Sync callbacks remain disabled; Production/Test runtime PIDs are unchanged. Rollback directory: `/home/ryan/dxfer-runtime/dev-publish-before-9692551`. Public browser replay is running, not yet independently accepted by Flux/Ryan.

Public replay completed: all seven scenarios pass on rendered build9692551, no page/circuit errors. All 41 public exports audit with zero errors/fixes; the final local suite also has 41 clean exports. Owner inspected installed applied-contour and linked-refusal screenshots. Runtime warning journal is empty since deployment. Evidence: `/tmp/polyline-axis-dev/results.json`, matching DXFs and PNGs. This supersedes the in-progress notes above; independent Flux/Ryan acceptance remains pending.

## Separate recovery issue

Read-only investigation of AXIS-RECOVERY-20261019 found that a perpendicular bare line projects to zero length, so validation correctly refuses that candidate. Deleting the earlier opposing constraint does not re-solve the remaining failed intent. A follow-on needs a non-collapsing axis candidate and a deliberate deletion/recovery contract; automatic retry of arbitrary old failed intent must not be introduced casually. This deployment does not change those semantics.

Follow-on4551c41 fixes the bounded line-axis/deletion cases; see `axis-recovery-2026-10-19.md`.

## Recovered independent baseline

Flux delivery33b27e0accff23b1db533bd4 reports on older11d3f60: rectangle out1/in2, refused inset4 with correction, joined polygon/open-chain contours, original intent/layer, history and native reopen pass. That build lacks the later persistent Offset relation/driving gap. Already-horizontal polyline H reports failure text despite saved Satisfied state; slanted polyline H refuses unchanged/Unsatisfied while slanted standalone LINE works. Screenshot distortion was not reproduced. Dropped key entry is independently confirmed on that baseline.

Do not treat this as failure or acceptance of installed4551c41. Current retest must check both visible status and stored constraint state for already-aligned edges, not just geometry/native persistence. Current owner coverage and prior input fix remain recorded separately; independent current-build acceptance is pending.

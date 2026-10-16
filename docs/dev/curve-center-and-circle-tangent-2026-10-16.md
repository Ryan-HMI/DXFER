# Curve Centers and Circle Tangent Creation

## Interaction Contract

- Circle and arc centers are discoverable before selection: a small fixed-screen-size center cross/dot, muted when unselected and cyan when selected. Existing center pick tolerance and drag semantics stay unchanged.
- Centers follow geometry previews and never create exported point entities. No new panel, layout or canvas-space consumption.
- Center-radius circle creation offers a tangent contact snap on supported finite edges and circular curves. The visible snap carries the exact target reference into creation; only a geometrically validated snap intent becomes a persistent Tangent relation.
- Typed radius or polar modifiers that move the committed point away from the snap must not leave stale tangent intent. Ordinary proximity without taking the snap must not create a relation.
- Existing outgoing-line tangent behavior remains supported. Constraints survive Undo/Redo and native reopen; source geometry is not edited by creating a circle.

## Verification

- Reproduced root causes: center rendering was conditional on selection; center-radius creation had no dynamic tangent snap and no explicit snap-reference handoff to constraint creation. Existing outgoing-line tangent behavior still passed on public Dev 93ddec7.
- Full integrated suites: 1531 .NET and 738 JavaScript tests pass. Web build has zero warnings/errors. The new core regression failed 18 cases on baseline before implementation.
- Actual 1440x1000 browser checks: unselected circle/arc center visible, direct center drag and held preview, outgoing-line tangent retained, Undo/Redo and native reopen all pass. Screenshots inspected.
- New center-circle browser workflow against line, circle and arc peers shows tangent cue and retains one Satisfied relation without moving peers; Undo/Redo and native reopen pass. Ordinary edge clicks do not persist Tangent. Typed radius3 overriding a radius4 snap drops intent; typed matching radius4 retains it.
- Evidence: `/tmp/dxfer-centers-tangent-evidence` and `/tmp/dxfer-circle-creation-evidence`. Export audits report zero errors/fixes.
- Scope is DXFer Dev only. Shared rendering and creation paths are fixed; no drawing files or business records were repaired. Previously created circles with missing Tangent intent require explicit user intent, not proximity-based backfill. Other circle/arc construction modes are not newly covered by this center-radius implementation.

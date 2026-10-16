# Curve Centers and Circle Tangent Creation

## Interaction Contract

- Circle and arc centers are discoverable before selection: a small fixed-screen-size center cross/dot, muted when unselected and cyan when selected. Existing center pick tolerance and drag semantics stay unchanged.
- Centers follow geometry previews and never create exported point entities. No new panel, layout or canvas-space consumption.
- Center-radius circle creation offers a tangent contact snap on supported finite edges and circular curves. The visible snap carries the exact target reference into creation; only a geometrically validated snap intent becomes a persistent Tangent relation.
- Typed radius or polar modifiers that move the committed point away from the snap must not leave stale tangent intent. Ordinary proximity without taking the snap must not create a relation.
- Existing outgoing-line tangent behavior remains supported. Constraints survive Undo/Redo and native reopen; source geometry is not edited by creating a circle.

Verification pending.

# Constraint transactions

Ryan requested coherent persistent constraints, explicit overconstraint failure,
and an existing-tool verification pass. CypNest annotation integration is owned
by a separate HMI-Sync worker; Flux owns independent browser/tool verification.

## Scope and layout contract

Use the existing solver interface and propagation services. No new hand-written
solver engine or entity-specific rectangle workaround. Toolbar, canvas, dock,
targets, wrapping and responsive dimensions remain unchanged. Failure uses the
existing command-status area and constraint/dimension rendering.

## Reproduced causes and changes

- Constraint application validated only the newly added constraint, allowing an
  older one to retain a stale satisfied state. Final validation now checks all.
- Constraint changes skipped propagation through connected references and
  existing driving dimensions. The adapter now propagates changes and reapplies
  driving dimensions, with edited dimension IDs replacing their prior values.
- Failed solves returned partially changed geometry. The adapter retains the
  prior entities and metadata while keeping editable failed constraint/dimension
  intent and diagnostics. Undo removes that intent; redo restores it.
- A collapsed line could satisfy both horizontal and vertical. Directional
  validation now rejects a zero-length line or polyline segment.
- Workbench constraint/dimension commands discarded solve status. They now
  report unsuccessful solves instead of claiming an update succeeded.

## Entity coverage

Direct regressions cover line and polyline-segment directional conflicts, opposing
parallel/perpendicular constraints, conflicting lengths, same-ID dimension
updates, suppressed constraints, driving length plus horizontal, and a midpoint
move through a coincident corner. Shared final validation/rollback applies to all
entities supported by the existing solver. Existing full-suite coverage includes
circle/arc radius, polygon, ellipse, spline and point behavior; this does not
claim every constraint kind is supported on every entity. Dimension and constraint
references remain document intent, not manufacturing cut entities.

## Remaining work

### History feedback follow-up (2026-10-14)

Flux reproduced Redo restoring failed intent while replacing its explanation
with a generic success message. Undo and Redo now derive failure feedback from
the restored document's constraint and driving-dimension diagnostics. Empty
history actions leave the existing explanation intact. Undoing conflict removal
also restores the explanation, and redoing removal clears it. Layout and tool
handlers are unchanged; this is shared history feedback, not a geometry repair.
Regression coverage includes dimension-only failures. A real-browser conflict,
Undo and Redo check verifies unchanged ten-inch geometry and visible explanation
(`/tmp/dxfer-solver-redo.png`). No existing records require repair.

Verification: 601 .NET tests and 265 JavaScript tests pass. Scoped formatting and
Web build pass (the older lifecycle-test file retains preexisting whitespace
warnings outside this patch). Isolated Playwright canvas test imports a real
DXF, applies Horizontal then conflicting Vertical, retains the 10-inch line,
shows red geometry and the failure message, and preserves geometry through
Undo/Redo with no page errors. The browser caught the modal prompt masking the
failure; failure feedback now takes precedence. Evidence:
`/tmp/dxfer-solver-qa.cjs`, `/tmp/dxfer-solver-browser.log`, and
`/tmp/dxfer-solver-conflict.png`. Temporary QA process is shut down afterward.

The legacy adapter is still a sequential solver, not a DOF/rank-based general
constraint solver. Failure means this solver could not satisfy the combination;
it is not proof that every such system is mathematically inconsistent. PlaneGCS
remains an unavailable adapter, not an active engine. General drag coverage and
convergence need further work.

The original transaction patch did not preserve intent on reopen. The later
standalone full-editor native DXF snapshot now does; see
`native-constraint-persistence-2026-10-14.md`. Analysis sidecars and Sync-bound
manufacturing exports remain geometry/analysis only. Existing damaged drawings cannot be
automatically reconstructed; users need Undo or a verified original. No saved
Engineering artifacts, Sync records, machine programs, or Production/Test
deployments are changed by these source edits.

The generic drag path was not covered by the adapter transaction fix. The
2026-10-15 Equal/Fix browser reproduction and missing canvas rejection response
are tracked separately in `drag-constraint-transactions-2026-10-15.md`.

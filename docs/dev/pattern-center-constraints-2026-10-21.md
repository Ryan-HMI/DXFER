# Constrainable Radial Pattern Center

## Interaction Contract

Keep the existing 24px center handle, card dimensions and source-clearance placement. A click on a committed center selects a persistent construction point for the normal constraint/dimension tools; Shift preserves existing selection. A drag retains the staged center-edit workflow and Confirm/Cancel. Distinguish click from drag by a small screen-space threshold, not by server timing. Keyboard Enter/Space selects the center. Draft centers remain editable before commit but cannot acquire constraints against unsaved pattern geometry.

The center point is retained in native documents and participates in PlaneGCS pattern equations. Existing patterns gain the point on first deliberate selection, without moving geometry. It is construction geometry, not a new cut path. Fixed, driving and locked-layer protections must continue to apply. No new panel, color scheme or mobile layout is introduced.

## Implementation

An optional `PatternDefinition.CenterPointId` owns the construction-point reference. PlaneGCS maps the same point used by ordinary constraints into simultaneous radial transform equations; the origin is no longer an independently fixed solver parameter for exposed centers. Solved center coordinates are read back into metadata before validation, without regenerating or repairing solver output. Legacy unexposed patterns retain their existing behavior.

Explicit center edits, native IO and unlinking validate point identity, construction status, Fixed/driving intent and protected layers. Unlink retains the point and its remaining sketch intent. A selected pattern's controls remain available when a constraint tool clears ordinary selection.

## Verification

- 1,837 JavaScript tests and 3,196 core/component tests passed. Includes real NodeRuntime solve, native JSON/DXF roundtrip and another center edit after reopen.
- New solver cases cover origin/point coincidence, H/V and distance freedoms, Fixed/locked refusal, legacy patterns, invalid references and near-center radial geometry. Sixteen instances checked; larger live-center patterns were not benchmarked. Live-center affine equations have more overhead than the legacy fixed-center mapping.
- Isolated browser candidate passed all 13 cases: Coincident/Horizontal in both pick orders and both tool/selection workflows, driving center-position edits, Undo/Redo, native reopen/edit again, Fixed-center drag refusal, and valid center drag Confirm/Cancel. Six held-pointer samples tracked the current viewport with unchanged Fixed source geometry. Evidence: `/tmp/dxfer-pattern-center-qa-r3-final`. No browser errors.
- Screenshots inspected. No Flux/Ryan independent acceptance claimed.

This fixes the shared center-reference mapping rather than a specific drawing. No existing records require repair; old patterns gain the construction point on first deliberate center selection.

## Installed Dev

- Code `9a0678fcfa009814a57a08601277c84b4206becd`, installed 2026-10-22T01:03:02Z at `https://dxferdev.sync.harrisonmetals.com`.
- Public-browser replay independently read the rendered build tooltip and passed the same 13 cases with no browser errors. Evidence: `/tmp/dxfer-pattern-center-installed/results.json`; exported native fixtures and screenshots are beside it. This is owner-run verification, not Flux acceptance.
- Public `drawingCanvas.js`, `canvas/patternViewport.js` and `planeGcsPatterns.js` SHA-256 hashes matched source. Normal Google sign-in remains configured; Sync callbacks remain disabled.
- Only `dxfer-dev.service` restarted. Production/Test stayed active with unchanged PIDs 3584417/3584475. No HMI-Sync rollout or data writes.
- Rollback publish: `/home/ryan/dxfer-runtime/dev-publish-before-9a0678f`.

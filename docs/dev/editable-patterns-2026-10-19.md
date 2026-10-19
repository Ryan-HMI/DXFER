# Editable patterns

## Layout contract

- Reuse the compact Foundry operation inspector. No modal or new floating palette.
- Mode uses a select; counts and distances use labeled numeric fields in two stable columns.
- Fields have zero minimum width, labels wrap, and the inspector scrolls without covering its actions or the canvas footer.
- Confirm and Cancel remain explicit. Invalid displayed input clears the preview and disables Confirm.
- Rectangular and staggered modes share rows/columns and pitch; radial mode shows center and count/angle controls.
- Existing Linear/Circular pattern icons open these settings. A member's context menu offers Edit pattern.
- Desktop verification at 1188x761 and 1100x705, preserving independent docked palettes.

## Scope

DXFer Dev only. Rectangular rows/columns, alternate-row half-pitch stagger, and radial full-circle or angular-step layouts. Counts include the original seed; distances use document units. Rotation applies to the rectangular/staggered pitch axes; radial instances rotate around the specified center.

The existing Pattern palette and selection context menu open the staged inspector. Source/member context commands and the Patterns list reopen the same saved definition. Confirm is the only creation/update commit. Cancel/Escape leave the original drawing and history intact. Unlink requires explicit confirmation and keeps independent geometry.

## Shared behavior

- Structured native metadata owns source IDs, parameters and deterministic member identities. Surviving grid slots retain IDs after count/spacing edits.
- Source edits regenerate copies. Both canvas held-drag preview and release use the shared core. Independent member edits/deletion refuse with a source-edit/unlink explanation.
- Fixed, driving, other constraint and locked/hidden-layer dependencies are checked before accepting a change. Reducing a count refuses if it would orphan attached intent.
- Source layer/style and construction state propagate. Absent source styles explicitly resolve to layer0 instead of inheriting the current drawing layer.
- Reference measurements refresh; dimension anchors follow regenerated members during drags.
- Native reopen validates definitions against actual geometry. External edits with stale embedded metadata keep the externally edited geometry and discard stale links with a warning.
- Context lookup now resolves quadrant and other canvas point targets to their owning entity, fixing an empty-menu case shared with other contextual commands.

## Bounds

No nested patterns, independent member overrides, or automatic conversion of old geometry-only copies. The service limits each layout to 1024 instances, 128 seed entities, 4096 generated entities across a document, and 64 definitions. Exact seed-coincident copies and invalid/collapsed layouts refuse. Arbitrary whole-pattern transformations that cannot preserve the saved definition refuse; use pattern rotation/center controls or deliberately unlink first. Additional compatible coupled relationships may still safely refuse rather than being solved bidirectionally.

No existing drawings/business records are rewritten. Old independent copies remain independent; no historical data repair is required or claimed.

## Verification

- Core regression suite: 1852 passing, including 48 pattern cases across entity types, stable identity, malformed metadata, source/member edits, dependencies, expansion bounds, native roundtrip and external stale-metadata edits.
- JavaScript suite: 940 passing, including routing pattern drags through the coalesced shared preview path.
- Local browser: all three pattern modes, create/edit/cancel, invalid/blank inputs, held source-drag preview, source radius changes, dependent refusal, source/member context reopening, Undo/Redo, native reopen/edit again, unlink and subsequent independent edits. Staged clicks do not commit.
- Desktop 1188x761 and 1100x705 screenshots inspected; Confirm/Cancel verified by actual hit testing, not just bounding boxes. Inspector no longer clips pattern actions behind Layers. No phone QA requested.
- 69 local DXF exports independently audited: 0 errors, 0 fixes. No browser/circuit errors.
- Evidence: `/tmp/dxfer-editable-patterns-retry4/`; executable `tests/DXFER.Blazor.Tests/editablePatterns.browser.mjs`. Earlier failed runs retained and distinguished from final passing evidence.
- Final current-code replay `/tmp/editable-patterns-final/` also passes a 400-hole staggered grid, expansion-limit refusal, history and native reopen. 72 exports audit clean. Existing context/dimension workflows pass separately in `/tmp/pattern-context-regression/`.

Deployment verification will be recorded after the reviewed candidate is installed. Production/Test remain outside this change.

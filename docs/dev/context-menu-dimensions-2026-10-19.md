# Context Commands and Dimension Modes

## Layout Contract

- Reuse Foundry surface, border, text, focus and icon styles. No palette or canvas reflow.
- Pointer-anchored menu, 240px maximum width, 32px minimum action rows, 18px icons.
- Clamp to desktop viewport edges; long text wraps and long layer lists scroll within the menu.
- Preserve a multi-selection when the context target belongs to it; otherwise target only the clicked geometry.
- Corner actions reuse the staged key-in/preview/Confirm/Cancel workflow. Geometry commands reuse existing handlers.
- Escape/outside click dismiss; keyboard focus enters the menu and returns to canvas. Tab navigates controls.
- Dimension menu offers Driving/Driven. Driven measurements are parenthesized and read-only, but their labels remain movable.
- Mode changes retain IDs/anchors, participate in Undo/Redo and native persistence, and never move geometry.
- Reference-only documents and locked/hidden geometry must not be mutated through context actions.

## Verification

- 1,804 core tests and 938 JavaScript tests pass; Web build has zero warnings/errors.
- Real desktop browser at 1188x761 and 1100x705 verifies mode conversion, read-only parenthesized Driven values, measured updates after drag, promotion without geometry changes, Undo/Redo and native reopen.
- Actual context actions verify four-selected-edge Offset, corner Chamfer preview/Confirm, construction, move-to-layer, Undo/Redo, locked geometry, Escape/outside dismissal and keyboard focus.
- All 11 local browser exports audit with ezdxf: zero errors/fixes. Screenshots reviewed in `/tmp/context-commands-local/`.
- Root-cause fixes: reference measurements no longer compare against stale saved target values; the shared commit/DTO paths refresh measurements. Right-click cannot accidentally commit a focused dimension value. Programmatic canvas selection accepts validated subentity targets, allowing corner commands to retain their actual vertex.
- Mode changes are atomic and do not move geometry. Promotion uses the current measurement and existing redundancy/validity checks; this does not claim complete general solver rank analysis.
- No saved drawings, Sync artifacts or business records are rewritten. No historical repair required. Existing dimensions remain Driving unless deliberately changed.
- Context menu is available in selection mode; active creation tools retain their existing right-click gestures. Production/Test unchanged. Independent acceptance pending.
- Installed Dev `0937e6bace6c3824ed32611c1efef4060763e22d` at 2026-10-19 04:20 UTC. Candidate clean/pushed; four assembly hashes match, all ten tracked runtime settings retained, health200/anonymous302, Production/Test process IDs unchanged. Rollback: `/home/ryan/dxfer-runtime/dev-publish-before-0937e6b`.
- Public authenticated browser repeated the full bounded workflow and independently read build `0937e6b` from its rendered tooltip. Eleven public exports audit0/0; screenshots reviewed in `/tmp/context-commands-dev/`; no page/circuit or new service warning/error entries.
- Public replay initially outran the asynchronous selection-reset during fixture setup; the harness now waits for each actual deletion/count/selection result instead of a fixed delay. No application code changed after deployment for that synchronization correction.

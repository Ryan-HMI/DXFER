# Tangent Hotkeys

The hotkey allowlist omitted both Tangent and Tangent Arc. Tangent Arc also lacked
the direct command-to-tool mapping because it was only reached by chain hover.
Add both without assigning defaults or changing existing bindings. The same
allowlist controls editing and loading stored bindings, so both paths are fixed.

Layout contract: preserve the existing centered searchable hotkey popup, row
dimensions, key recorder, conflict feedback, focus handling and scroll behavior.
Use distinct searchable names: Tangent constraint and Tangent arc.

Verification: 35 focused hotkey tests passed. Isolated browser verified both
search entries, key recording, duplicate refusal, actual command activation,
reload persistence, retained Shift+A Line default and unchanged empty drawing/
Undo state. Zero browser errors. Screenshot inspected.
Evidence: `/tmp/dxfer-tangent-hotkeys-qa-r2/results.json`.

Dev `10a1310217b1d166a289b00145fc9a2825d07ca1` installed
`2026-10-22T02:12:31Z`. Public browser replay passed on tooltip-verified `10a1310`,
including both shortcuts after reload, conflict rejection, unchanged default
and no document/Undo changes. Zero browser errors; installed screenshot inspected.
Evidence: `/tmp/dxfer-tangent-hotkeys-installed/results.json`.

Production/Test unchanged; Google authentication retained and Sync callbacks
disabled. QA5038 stopped. No drawings or existing records require repair;
users can assign the newly available bindings normally.

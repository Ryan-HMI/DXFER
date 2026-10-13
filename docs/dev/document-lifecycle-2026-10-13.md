# Document lifecycle

Scope: shared New/Open/sample replacement and unsaved-document protection;
deploy to the standalone full-editor Dev playground only. Production/Test and
live Sync callbacks stay unchanged.

Layout contract: use the existing File submenu, dimensions, typography and
wrapping. Add New only in full-editing mode; no new permanent panel, toolbar,
or canvas overlay. Native discard and leave-page confirmations take focus;
Cancel preserves document, selection/history and Sync ownership. Existing menu
and command-bar boundaries must remain usable at1440,820,390 and320px.
Browser verification exposed the existing sideways submenu clipping at320px:
below480px submenus expand within the menu column, with bounded vertical scroll
and wrapping labels. The operation readout wraps and scrolls within4.5rem instead
of clipping long import errors. The full canvas remains the primary surface.

The current document must remain intact until a candidate import is valid.
Successful standalone New/Open/sample clears prior Sync callback ownership,
including the header action and stale launch query. Downloads establish a local
save point; they must not claim a connected Sync artifact was updated. Failed
saves and edits made while a save is in flight remain unsaved. Undo/redo back to
the saved document should clear the warning. New blank documents start clean.

Applies to every document entity without rewriting geometry: line, polyline,
polygon, circle, arc, ellipse, spline, point, dimension and constraint/reference.
Entity editing, solver fixes and unsupported import formats are separate work.

## Verification

Four ownership/failed-import regressions failed before the fix and pass after.
591 .NET tests and258 JavaScript tests pass. Actual isolated browser verified
New/Open cancel, reload cancel, downloaded save point, undo to imported state,
empty-import preservation, repeated same-file selection, and menus at1440/820/
390/320px. Screenshots inspected at desktop and320px; no horizontal overflow.
The existing readout now displays idle operation results/errors instead of
always masking them with generic selection instructions.

Mock-only Sync browser tests verified that New and local Open remove the
callback, header action and launch query without sending data; failed callbacks
retain unsaved work; replacement is refused during a save; and newer edits made
during a successful callback stay unsaved instead of closing the tab. Two calls
went only to the loopback mock. No live artifacts, database or machine accessed.
QA evidence: /tmp/dxfer-lifecycle-qa/evidence.json and menu-*.png; disposable app
and mock server stopped. No existing files or business records need repair.

Dev Sync integration remains disabled. These tests do not claim that full Dev
Sync callback integration or the historical geometry/solver backlog is complete.

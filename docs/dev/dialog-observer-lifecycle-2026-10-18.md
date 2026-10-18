# Dialog Observer Lifecycle

Flux delivery3aa001dbb56d438c470bdda3 reports circuit termination after closing
Appearances with custom canvas/panel colors. Dev journal23:35:47.646UTC identifies
ObjectDisposedException in CanvasAppearance.OnAfterRenderAsync line59, followed
by a second exception in component disposal. The observer field still points to
the object while asynchronous cleanup is in progress, allowing another render
to dispose it again. ToolHotkeyOptions has the same ownership pattern.

Contract: preserve centered desktop dialogs, widths, alignment, wrapping,
scrolling, controls, stored colors/hotkeys, focus and Escape behavior. Change
observer lifetime only. No geometry, native files, or historical record repair.
Focused lifecycle tests precede the fix; browser checks must include console
circuit errors, not only pageerror events.

## Fix and Verification

Both dialogs now detach the observer reference before awaiting cleanup. A second
render or component teardown cannot reuse that reference. Teardown also detaches
the module, is idempotent, releases references even on disconnection, and prevents
new post-disposal render work. No blanket ObjectDisposedException suppression.

Four deterministic pre-fix tests reproduce the exact exception; all four pass
afterward. Two additional disconnection/idempotence tests pass. Full suite:
1746 core tests,919 JavaScript tests; Web build passes.

Actual local browser repeats the reported saved-empty-drawing sequence eight
times, immediately reopens the dialog and downloads after every close. It also
repeats Hotkeys search/close plus Appearance reopen six times. No page or console
circuit errors, File menu stays live, saved Custom values survive reload. The
existing gradient/preset/contrast/focus/desktop-layout/native-invariance checks
also pass; custom-dialog screenshot inspected. Evidence:
/tmp/dialog-lifecycle-local.log and /tmp/dialog-lifecycle-local/.

This fixes the identified shared disposal race. Existing drawing files and
preferences need no repair; a previously disconnected tab requires reload.
Production/Test remain outside scope. Installed verification recorded below.

## Installed Verification

Dev4e6f2056d892635fce0418599736c268d5d61a22 installed2026-10-18T23:52Z.
Public browser verifies that rendered revision and passes the same eight custom
color cycles, six Hotkeys cycles and full appearance regression.13 downloaded
DXFs audit0/0;1100px dialog screenshot inspected. Server journal from23:52:30UTC
through replay completion contains no warning/error entries. Four assemblies
match publish; ten runtime settings retained, health200/anonymous302, normal
Google sign-in and disabled Sync callbacks preserved. Production/Test processes
unchanged. Rollback:/home/ryan/dxfer-runtime/dev-publish-before-4e6f205.
Evidence:/tmp/dialog-lifecycle-dev.log and /tmp/dialog-lifecycle-dev/.
Owner verification complete; Flux independent retest remains pending. Existing
geometry acceptance is unaffected; no broad appearance acceptance inferred.

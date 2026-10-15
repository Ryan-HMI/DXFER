# Axes, Menu And Close Icons

## Layout Contract

Adopt the three remaining direct-control masters from the corrected visual
handoff, preserving independent palettes and all existing command handlers.
Axes retains its existing 24-unit viewBox and 1.9rem glyph. Menu remains a
2rem square; dialog close targets retain their existing dimensions. Chrome
glyphs are centered at 1.25rem so they cannot overflow smaller close targets.
Accessible names, title tooltips, keyboard activation and focus styling remain.
No labels, palette membership, docking, saved layout, wrapping, viewport bounds
or document geometry change. PC browser resizing/scaling is the target, not
new phone behavior. The fatal-error dismiss control keeps Blazor's dismiss
class and native listener, including when the circuit is unavailable.

This raises mapped masters from 49 to 52 of 81. The other 29 remain deliberately
unmapped as documented in geometry-icon-integration-2026-10-15.md: 13 retained
motifs and 16 without dedicated controls. It is not the full visual redesign.

## Verification

Baseline browser testing exposed a preexisting missing layout notification:
the Tool hotkeys event set its flag without rerendering the parent, so the
dialog appeared only after an unrelated parent event. OpenHotkeyOptions now
requests a renderer update, following the existing SyncDocumentChanged pattern.
The browser regression requires immediate opening without that workaround.

Four new master/binding assertions failed before implementation. All974 .NET
and352 JavaScript tests pass afterward; Web builds without warnings/errors.
Actual local browser verifies axes toggling, menu keyboard activation, immediate
hotkey dialog opening and keyboard close, fixed glyph bounds, and unchanged
native geometry/dimensions. Desktop screenshots at1440x900/1280x720 and1920x1080
with125/150/200percent CSS scaling retained; baseline/after1440 and200percent
screenshots inspected. This is glyph/target verification, not full workspace
accessibility acceptance at200percent. Synthetic error banner verifies icon
rendering only; no live circuit failure was induced. Sync-connect remains
disabled on Dev and is covered by the shared icon/binding contract.

Evidence: /tmp/dxfer-chrome-*.png and /tmp/dxfer-drag-live-qa.cjs --chrome.
No user drawings or business records need repair. Deployed with0eca667 to Dev
only on2026-10-15; installed browser repeats the checks above. Four assembly
hashes and ten runtime settings match; normal sign-in retained, Sync callbacks
off, Production/Test unchanged.52 of81 masters are now installed. The remaining
composition work is separate; no blanket visual acceptance is claimed.

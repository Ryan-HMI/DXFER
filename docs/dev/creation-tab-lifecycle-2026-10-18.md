# Creation Tab Lifecycle

## Reproduction

On Dev adf544c, create a rectangle with keyed width 6 and height 4.
After commitment, focus the canvas and press Tab repeatedly. The browser
cycles forever between the two saved dimension inputs. The browser regression
`creationTabLifecycle.browser.mjs` reproduced eight alternating saved-input focuses.

## Interaction Contract

- Draft Tab/Shift+Tab continues cycling through creation-time numeric fields.
- Committing the shape ends that cycle. Saved dimension overlays are not ordinary
  sequential Tab stops and do not intercept Tab to loop among themselves.
- Deliberate double-click/programmatic dimension editing remains available;
  Enter commits, Escape exits, and Tab follows normal browser focus traversal.
- Preserve all overlay alignment, dimensions, wrapping, overflow, hit targets,
  desktop resizing and independent palette layout. No styling changes.
- Geometry, driving values, Undo/Redo and native save/reopen remain unchanged.

## Cause And Scope

Saved dimension inputs were default tab stops and had their own modulo-based
Tab handler. This is a shared canvas keyboard issue, not corrupted drawing data.
No existing records require repair.

## Verification

Saved inputs now have tabIndex -1 and the persistent-input Tab loop is removed.
The draft handler is unchanged. The canvas module URL is versioned for reload.

- 1,712 .NET and 867 JavaScript tests pass; Web build passes.
- Actual local browser regression passes: draft Tab/Shift+Tab, keyed 6x4 creation,
  eight post-commit Tabs without saved-field capture, deliberate double-click
  editing to 7 and Tab-to-commit, Undo, creation Undo/Redo and native reopen.
- Seven generated DXFs audit with zero errors/fixes. Screenshot inspected;
  geometry, dimension overlays and the operational layout are unchanged.
- Evidence: `/tmp/creation-tab-before.log`, `/tmp/creation-tab-local.log` and
  `/tmp/creation-tab-local`. Public Dev replay pending deployment.

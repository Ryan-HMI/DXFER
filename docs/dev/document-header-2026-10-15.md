# Document Header

## Layout Contract (Before Implementation)

- Preserve the existing48px appbar and viewport origin; add no row or wrapping.
- Keep the menu target, brand, handlers and menu overflow unchanged. Never clip
  the header container, which would clip its existing popup menus.
- Document metadata occupies remaining inline width with `min-width: 0`.
  Filename alone flexes and ellipsizes; its title retains the complete name.
- Units, modification state and read-only state have fixed90/88/76px slots.
  The read-only slot remains reserved when empty to prevent filename movement.
  Text is13px with zero letter spacing; long unit labels ellipsize with a title.
- All metadata stays on the same center-aligned row. Desktop window resizing
  reduces filename space, not menu targets or the48px viewport offset.
- No document displays `No document`, without claiming units or edit state.
  Unknown units display `Unspecified`; never assume inches or millimeters.
- `Modified`/`Unchanged` describe caller-supplied document state only, never
  server persistence. Read-only is independent, so it cannot conceal dirty state.
- Actual assembly informational version supplies the brand build tooltip.
  No hardcoded revision, new button, icon or workflow is introduced.

## Integration Contract

Parent owns DrawingWorkbench state publication and installed-browser checks.
This work owns only the service snapshot/event, layout rendering and scoped CSS.
All entity types share this document-level presentation; no geometry or solver
semantics change, and no existing saved documents require migration or repair.

```csharp
public void SetDocumentState(string fileName, string units,
    bool hasUnsavedChanges, bool hasDocument, bool isReferenceOnly);
```

`DocumentState` is a privately-set property containing an immutable sealed
`WorkbenchDocumentState` record with `FileName`, `Units`, `HasUnsavedChanges`,
`HasDocument`, and `IsReferenceOnly`. `DocumentStateChanged` is an `Action` event.
Publication installs the complete snapshot before notification. Equal values
preserve the existing snapshot and send no event. This state is independent of
`HasSyncDocument`; neither Sync attachment nor download implies persisted data.

The header subscribes/unsubscribes with its component lifecycle and schedules
render through `InvokeAsync(StateHasChanged)`. Build title uses Web assembly
informational metadata, shortening an actual hex git revision to seven digits;
without a revision it displays the informational version, never an invented SHA.

## Verification

- Focused service/layout tests:29 passed. Full .NET suite:1,062 passed.
- Web build passes; source assertions cover header markup, lifecycle/dispatcher,
  truthfulness, fixed slots and the existing48px viewport contract.
- Service tests cover default/clear, every field change, no-op equality,
  immutable prior snapshots and independence from Sync connectivity.
- No installed-browser verification or deployment performed by this worker.
  Parent must verify publication on open/new/edit/Undo/Redo/download and reference
  load, long filenames and unknown units, both menu actions and 48px header bounds
  at desktop window widths/scaling. Download must not claim server save.

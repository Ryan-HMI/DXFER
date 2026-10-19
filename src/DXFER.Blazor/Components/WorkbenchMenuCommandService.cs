using Microsoft.AspNetCore.Components.Forms;

namespace DXFER.Blazor.Components;

public sealed record WorkbenchDocumentState(
    string FileName,
    string Units,
    bool HasUnsavedChanges,
    bool HasDocument,
    bool IsReferenceOnly);

public sealed class WorkbenchMenuCommandService
{
    public event Func<WorkbenchCommandId, Task>? CommandRequested;
    public event Func<IBrowserFile, Task>? FileOpenRequested;
    public event Action? HotkeyOptionsRequested;
    public event Action? SyncDocumentChanged;
    public event Action? DocumentStateChanged;

    public bool HasSyncDocument { get; private set; }
    public bool AutomaticSnapConstraints { get; set; } = true;
    public WorkbenchDocumentState DocumentState { get; private set; } = new(string.Empty, string.Empty, false, false, false);

    public void SetDocumentState(string fileName, string units, bool hasUnsavedChanges, bool hasDocument, bool isReferenceOnly)
    {
        var next = new WorkbenchDocumentState(fileName, units, hasUnsavedChanges, hasDocument, isReferenceOnly);
        if (DocumentState == next) return;
        DocumentState = next;
        DocumentStateChanged?.Invoke();
    }

    public void SetSyncDocument(bool connected)
    {
        if (HasSyncDocument == connected) return;
        HasSyncDocument = connected;
        SyncDocumentChanged?.Invoke();
    }

    public async Task InvokeAsync(WorkbenchCommandId commandId)
    {
        if (CommandRequested is not { } handler)
        {
            return;
        }

        await handler(commandId);
    }

    public async Task OpenFileAsync(IBrowserFile file)
    {
        if (FileOpenRequested is not { } handler)
        {
            return;
        }

        await handler(file);
    }

    public void OpenHotkeyOptions() => HotkeyOptionsRequested?.Invoke();
}

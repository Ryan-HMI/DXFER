using Microsoft.AspNetCore.Components.Forms;

namespace DXFER.Blazor.Components;

public sealed class WorkbenchMenuCommandService
{
    public event Func<WorkbenchCommandId, Task>? CommandRequested;
    public event Func<IBrowserFile, Task>? FileOpenRequested;
    public event Action? HotkeyOptionsRequested;
    public event Action? SyncDocumentChanged;

    public bool HasSyncDocument { get; private set; }

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

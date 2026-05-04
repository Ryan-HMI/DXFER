using Microsoft.AspNetCore.Components.Forms;

namespace DXFER.Blazor.Components;

public sealed class WorkbenchMenuCommandService
{
    public event Func<WorkbenchCommandId, Task>? CommandRequested;
    public event Func<IBrowserFile, Task>? FileOpenRequested;

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
}

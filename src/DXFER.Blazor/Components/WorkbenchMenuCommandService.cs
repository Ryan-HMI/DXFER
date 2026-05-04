namespace DXFER.Blazor.Components;

public sealed class WorkbenchMenuCommandService
{
    public event Func<WorkbenchCommandId, Task>? CommandRequested;

    public async Task InvokeAsync(WorkbenchCommandId commandId)
    {
        if (CommandRequested is not { } handler)
        {
            return;
        }

        await handler(commandId);
    }
}

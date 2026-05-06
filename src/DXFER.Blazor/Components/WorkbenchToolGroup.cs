namespace DXFER.Blazor.Components;

public sealed record WorkbenchToolGroup(
    string Name,
    IReadOnlyList<WorkbenchToolCommand> Commands,
    string? Label = null,
    bool InitiallyOpen = true);

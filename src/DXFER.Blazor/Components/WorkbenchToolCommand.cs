namespace DXFER.Blazor.Components;

public sealed record WorkbenchToolCommand(
    WorkbenchCommandId Id,
    WorkbenchTool? Tool,
    CadIconName Icon,
    string Label,
    bool Disabled = false,
    bool Pressed = false,
    bool IsFuture = false);

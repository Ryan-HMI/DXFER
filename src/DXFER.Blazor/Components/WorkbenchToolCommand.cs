namespace DXFER.Blazor.Components;

public sealed record WorkbenchToolCommand(
    WorkbenchCommandId Id,
    WorkbenchTool? Tool,
    CadIconName Icon,
    string Label,
    bool Disabled = false,
    bool? Pressed = null,
    bool IsFuture = false,
    bool IsConfirmedWorking = false,
    string? Tooltip = null,
    string? Hotkey = null)
{
    public string TooltipText => string.IsNullOrWhiteSpace(Tooltip)
        ? Label
        : Tooltip;
}

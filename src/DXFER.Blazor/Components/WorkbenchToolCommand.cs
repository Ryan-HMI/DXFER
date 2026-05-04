namespace DXFER.Blazor.Components;

public sealed record WorkbenchToolCommand(
    WorkbenchCommandId Id,
    WorkbenchTool? Tool,
    CadIconName Icon,
    string Label,
    bool Disabled = false,
    bool? Pressed = null,
    bool IsFuture = false,
    string? Tooltip = null)
{
    public string TooltipText => string.IsNullOrWhiteSpace(Tooltip)
        ? Label
        : Tooltip;
}

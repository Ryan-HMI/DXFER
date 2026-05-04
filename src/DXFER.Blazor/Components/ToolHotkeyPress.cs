namespace DXFER.Blazor.Components;

public sealed record ToolHotkeyPress(
    string Key,
    bool CtrlKey = false,
    bool AltKey = false,
    bool ShiftKey = false,
    bool MetaKey = false,
    bool IsEditableTarget = false)
{
    public static ToolHotkeyPress Plain(string key) => new(key);
}

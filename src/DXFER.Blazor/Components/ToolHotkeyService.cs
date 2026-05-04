namespace DXFER.Blazor.Components;

public sealed class ToolHotkeyService
{
    private IReadOnlyList<ToolHotkeyBinding> _bindings = ToolHotkeyResolver.GetDefaultBindings();

    public event Action? BindingsChanged;

    public IReadOnlyList<ToolHotkeyBinding> Bindings => _bindings;

    public void Load(IEnumerable<ToolHotkeyBinding>? bindings)
    {
        if (bindings is null)
        {
            return;
        }

        _bindings = bindings
            .Where(binding => ToolHotkeyResolver.ToolCommandIds.Contains(binding.CommandId))
            .Where(binding => ToolHotkeyResolver.NormalizeKey(binding.Key) is not null)
            .Select(binding => new ToolHotkeyBinding(binding.CommandId, ToolHotkeyResolver.NormalizeKey(binding.Key)!))
            .GroupBy(binding => binding.Key, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToArray();
        BindingsChanged?.Invoke();
    }

    public void SetBinding(WorkbenchCommandId commandId, string key)
    {
        _bindings = ToolHotkeyResolver.UpdateBinding(_bindings, commandId, key);
        BindingsChanged?.Invoke();
    }

    public void ResetToDefaults()
    {
        _bindings = ToolHotkeyResolver.GetDefaultBindings();
        BindingsChanged?.Invoke();
    }

    public bool TryResolve(ToolHotkeyPress press, out WorkbenchCommandId commandId) =>
        ToolHotkeyResolver.TryResolve(_bindings, press, out commandId);

    public string? GetKey(WorkbenchCommandId commandId) =>
        _bindings.FirstOrDefault(binding => binding.CommandId == commandId)?.Key;
}

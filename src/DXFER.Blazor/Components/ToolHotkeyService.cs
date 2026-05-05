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

        var loaded = bindings
            .Where(binding => ToolHotkeyResolver.ToolCommandIds.Contains(binding.CommandId))
            .Select(NormalizeStoredBinding)
            .OfType<ToolHotkeyBinding>()
            .GroupBy(binding => binding.CommandId)
            .Select(group => group.Last())
            .ToList();

        var duplicateKeys = loaded
            .Where(binding => !string.IsNullOrWhiteSpace(binding.Key))
            .GroupBy(binding => binding.Key, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

        loaded = loaded
            .Where(binding => string.IsNullOrWhiteSpace(binding.Key) || !duplicateKeys.Contains(binding.Key))
            .ToList();

        foreach (var defaultBinding in ToolHotkeyResolver.GetDefaultBindings())
        {
            var hasStoredCommand = loaded.Any(binding => binding.CommandId == defaultBinding.CommandId);
            var hasStoredKeyConflict = loaded.Any(binding =>
                !string.IsNullOrWhiteSpace(binding.Key)
                && StringComparer.Ordinal.Equals(binding.Key, defaultBinding.Key));
            if (!hasStoredCommand && !hasStoredKeyConflict)
            {
                loaded.Add(defaultBinding);
            }
        }

        _bindings = ToolHotkeyResolver.OrderBindings(loaded);
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

    private static ToolHotkeyBinding? NormalizeStoredBinding(ToolHotkeyBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.Key))
        {
            return new ToolHotkeyBinding(binding.CommandId, string.Empty);
        }

        var normalized = ToolHotkeyResolver.NormalizeKey(binding.Key);
        return normalized is null
            ? null
            : new ToolHotkeyBinding(binding.CommandId, normalized);
    }
}

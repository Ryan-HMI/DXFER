using System.Globalization;

namespace DXFER.Blazor.Components;

public static class ToolHotkeyResolver
{
    public static IReadOnlyList<WorkbenchCommandId> ToolCommandIds { get; } = new[]
    {
        WorkbenchCommandId.Measure,
        WorkbenchCommandId.Line,
        WorkbenchCommandId.MidpointLine,
        WorkbenchCommandId.TwoPointRectangle,
        WorkbenchCommandId.CenterCircle
    };

    public static IReadOnlyList<ToolHotkeyBinding> GetDefaultBindings() => new[]
    {
        new ToolHotkeyBinding(WorkbenchCommandId.Measure, "Q"),
        new ToolHotkeyBinding(WorkbenchCommandId.Line, "L"),
        new ToolHotkeyBinding(WorkbenchCommandId.MidpointLine, "M"),
        new ToolHotkeyBinding(WorkbenchCommandId.TwoPointRectangle, "R"),
        new ToolHotkeyBinding(WorkbenchCommandId.CenterCircle, "C")
    };

    public static bool TryResolve(
        IEnumerable<ToolHotkeyBinding> bindings,
        ToolHotkeyPress press,
        out WorkbenchCommandId commandId)
    {
        commandId = default;

        if (press.IsEditableTarget || press.CtrlKey || press.AltKey || press.MetaKey)
        {
            return false;
        }

        var normalizedKey = NormalizeKey(press.Key);
        if (normalizedKey is null)
        {
            return false;
        }

        foreach (var binding in bindings)
        {
            if (StringComparer.Ordinal.Equals(binding.Key, normalizedKey))
            {
                commandId = binding.CommandId;
                return true;
            }
        }

        return false;
    }

    public static IReadOnlyList<ToolHotkeyBinding> UpdateBinding(
        IEnumerable<ToolHotkeyBinding> bindings,
        WorkbenchCommandId commandId,
        string key)
    {
        var normalizedKey = NormalizeKey(key);
        var updated = bindings
            .Where(binding => binding.CommandId != commandId)
            .ToList();

        if (normalizedKey is null)
        {
            return updated;
        }

        var conflict = updated.FirstOrDefault(binding =>
            StringComparer.Ordinal.Equals(binding.Key, normalizedKey));
        if (conflict is not null)
        {
            throw new InvalidOperationException(
                $"{normalizedKey} is already assigned to {FormatCommandName(conflict.CommandId)}.");
        }

        updated.Add(new ToolHotkeyBinding(commandId, normalizedKey));
        return OrderBindings(updated);
    }

    public static string? NormalizeKey(string? key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var trimmed = key.Trim();
        if (trimmed.Length != 1)
        {
            return null;
        }

        var character = trimmed[0];
        return char.IsLetterOrDigit(character)
            ? char.ToUpper(character, CultureInfo.InvariantCulture).ToString()
            : null;
    }

    public static string FormatCommandName(WorkbenchCommandId commandId) => commandId switch
    {
        WorkbenchCommandId.MidpointLine => "Midpoint line",
        WorkbenchCommandId.TwoPointRectangle => "Two-point rectangle",
        WorkbenchCommandId.CenterCircle => "Center circle",
        _ => commandId.ToString()
    };

    private static IReadOnlyList<ToolHotkeyBinding> OrderBindings(IEnumerable<ToolHotkeyBinding> bindings)
    {
        var commandOrder = ToolCommandIds
            .Select((commandId, index) => new { commandId, index })
            .ToDictionary(item => item.commandId, item => item.index);

        return bindings
            .OrderBy(binding => commandOrder.GetValueOrDefault(binding.CommandId, int.MaxValue))
            .ThenBy(binding => binding.CommandId)
            .ToArray();
    }
}

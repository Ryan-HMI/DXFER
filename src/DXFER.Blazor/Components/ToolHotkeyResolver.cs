using System.Globalization;

namespace DXFER.Blazor.Components;

public static class ToolHotkeyResolver
{
    private const string CtrlModifier = "Ctrl";
    private const string AltModifier = "Alt";
    private const string ShiftModifier = "Shift";
    private const string MetaModifier = "Meta";

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
        new ToolHotkeyBinding(WorkbenchCommandId.Line, "Shift+A"),
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

        if (press.IsEditableTarget)
        {
            return false;
        }

        var normalizedKey = NormalizePress(press);
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

        var parts = key
            .Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            return null;
        }

        var ctrlKey = false;
        var altKey = false;
        var shiftKey = false;
        var metaKey = false;
        string? baseKey = null;

        foreach (var part in parts)
        {
            if (TryApplyModifier(part, ref ctrlKey, ref altKey, ref shiftKey, ref metaKey))
            {
                continue;
            }

            if (baseKey is not null)
            {
                return null;
            }

            baseKey = NormalizeBaseKey(part);
            if (baseKey is null)
            {
                return null;
            }
        }

        return baseKey is null
            ? null
            : FormatChord(baseKey, ctrlKey, altKey, shiftKey, metaKey);
    }

    public static string? NormalizePress(ToolHotkeyPress press)
    {
        var baseKey = NormalizeBaseKey(press.Key);
        return baseKey is null
            ? null
            : FormatChord(baseKey, press.CtrlKey, press.AltKey, press.ShiftKey, press.MetaKey);
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

    private static string? NormalizeBaseKey(string? key)
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

    private static bool TryApplyModifier(
        string value,
        ref bool ctrlKey,
        ref bool altKey,
        ref bool shiftKey,
        ref bool metaKey)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(value, CtrlModifier)
            || StringComparer.OrdinalIgnoreCase.Equals(value, "Control"))
        {
            ctrlKey = true;
            return true;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(value, AltModifier))
        {
            altKey = true;
            return true;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(value, ShiftModifier))
        {
            shiftKey = true;
            return true;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(value, MetaModifier)
            || StringComparer.OrdinalIgnoreCase.Equals(value, "Cmd")
            || StringComparer.OrdinalIgnoreCase.Equals(value, "Command")
            || StringComparer.OrdinalIgnoreCase.Equals(value, "Win"))
        {
            metaKey = true;
            return true;
        }

        return false;
    }

    private static string FormatChord(string baseKey, bool ctrlKey, bool altKey, bool shiftKey, bool metaKey)
    {
        var parts = new List<string>();
        if (ctrlKey)
        {
            parts.Add(CtrlModifier);
        }

        if (altKey)
        {
            parts.Add(AltModifier);
        }

        if (shiftKey)
        {
            parts.Add(ShiftModifier);
        }

        if (metaKey)
        {
            parts.Add(MetaModifier);
        }

        parts.Add(baseKey);
        return string.Join("+", parts);
    }
}

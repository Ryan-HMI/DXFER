using System.Globalization;

namespace DXFER.Blazor.Components;

public static class ToolHotkeyResolver
{
    private const string CtrlModifier = "Ctrl";
    private const string AltModifier = "Alt";
    private const string ShiftModifier = "Shift";
    private const string MetaModifier = "Meta";
    private static readonly IReadOnlyDictionary<string, string> NamedBaseKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["Backspace"] = "Backspace",
        ["Delete"] = "Delete",
        ["Del"] = "Delete",
        ["Insert"] = "Insert",
        ["Ins"] = "Insert",
        ["Enter"] = "Enter",
        ["Return"] = "Enter",
        ["Escape"] = "Escape",
        ["Esc"] = "Escape",
        ["Tab"] = "Tab",
        ["Space"] = "Space",
        ["Spacebar"] = "Space",
        ["Home"] = "Home",
        ["End"] = "End",
        ["PageUp"] = "PageUp",
        ["PgUp"] = "PageUp",
        ["PageDown"] = "PageDown",
        ["PgDn"] = "PageDown",
        ["ArrowLeft"] = "ArrowLeft",
        ["Left"] = "ArrowLeft",
        ["ArrowRight"] = "ArrowRight",
        ["Right"] = "ArrowRight",
        ["ArrowUp"] = "ArrowUp",
        ["Up"] = "ArrowUp",
        ["ArrowDown"] = "ArrowDown",
        ["Down"] = "ArrowDown",
        ["PrintScreen"] = "PrintScreen",
        ["ScrollLock"] = "ScrollLock",
        ["Pause"] = "Pause",
        ["CapsLock"] = "CapsLock",
        ["ContextMenu"] = "ContextMenu",
        ["Plus"] = "Plus",
        ["Add"] = "Plus",
        ["Minus"] = "Minus",
        ["Hyphen"] = "Minus",
        ["Dash"] = "Minus",
        ["Equal"] = "Equal",
        ["Equals"] = "Equal",
        ["Comma"] = "Comma",
        ["Period"] = "Period",
        ["Dot"] = "Period",
        ["Slash"] = "Slash",
        ["ForwardSlash"] = "Slash",
        ["Backslash"] = "Backslash",
        ["Semicolon"] = "Semicolon",
        ["Quote"] = "Quote",
        ["Apostrophe"] = "Quote",
        ["Backquote"] = "Backquote",
        ["Grave"] = "Backquote",
        ["BracketLeft"] = "BracketLeft",
        ["LeftBracket"] = "BracketLeft",
        ["BracketRight"] = "BracketRight",
        ["RightBracket"] = "BracketRight"
    };

    private static readonly IReadOnlyDictionary<char, string> PunctuationBaseKeys = new Dictionary<char, string>
    {
        [' '] = "Space",
        ['+'] = "Plus",
        ['-'] = "Minus",
        ['='] = "Equal",
        [','] = "Comma",
        ['.'] = "Period",
        ['/'] = "Slash",
        ['\\'] = "Backslash",
        [';'] = "Semicolon",
        ['\''] = "Quote",
        ['`'] = "Backquote",
        ['['] = "BracketLeft",
        [']'] = "BracketRight"
    };

    public static IReadOnlyList<WorkbenchCommandId> ToolCommandIds { get; } = new[]
    {
        WorkbenchCommandId.Undo,
        WorkbenchCommandId.Redo,
        WorkbenchCommandId.Measure,
        WorkbenchCommandId.FitExtents,
        WorkbenchCommandId.OriginAxes,
        WorkbenchCommandId.Line,
        WorkbenchCommandId.MidpointLine,
        WorkbenchCommandId.TwoPointRectangle,
        WorkbenchCommandId.CenterRectangle,
        WorkbenchCommandId.AlignedRectangle,
        WorkbenchCommandId.CenterCircle,
        WorkbenchCommandId.ThreePointCircle,
        WorkbenchCommandId.ThreePointArc,
        WorkbenchCommandId.CenterPointArc,
        WorkbenchCommandId.Point,
        WorkbenchCommandId.Construction,
        WorkbenchCommandId.DeleteSelection,
        WorkbenchCommandId.PowerTrim,
        WorkbenchCommandId.SplitAtPoint,
        WorkbenchCommandId.AddSplinePoint,
        WorkbenchCommandId.Offset,
        WorkbenchCommandId.Fillet,
        WorkbenchCommandId.Chamfer,
        WorkbenchCommandId.Dimension,
        WorkbenchCommandId.Translate,
        WorkbenchCommandId.Rotate,
        WorkbenchCommandId.Rotate90Clockwise,
        WorkbenchCommandId.Rotate90CounterClockwise,
        WorkbenchCommandId.Scale,
        WorkbenchCommandId.Mirror,
        WorkbenchCommandId.BoundsToOrigin,
        WorkbenchCommandId.PointToOrigin,
        WorkbenchCommandId.VectorToX,
        WorkbenchCommandId.VectorToY,
        WorkbenchCommandId.LinearPattern,
        WorkbenchCommandId.CircularPattern,
        WorkbenchCommandId.Coincident,
        WorkbenchCommandId.Concentric,
        WorkbenchCommandId.Parallel,
        WorkbenchCommandId.Horizontal,
        WorkbenchCommandId.Vertical,
        WorkbenchCommandId.Perpendicular,
        WorkbenchCommandId.Equal,
        WorkbenchCommandId.Midpoint,
        WorkbenchCommandId.Fix
    };

    public static IReadOnlyList<ToolHotkeyBinding> GetDefaultBindings() => new[]
    {
        new ToolHotkeyBinding(WorkbenchCommandId.Undo, "Ctrl+Z"),
        new ToolHotkeyBinding(WorkbenchCommandId.Redo, "Ctrl+Shift+Z"),
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
            updated.Add(new ToolHotkeyBinding(commandId, string.Empty));
            return OrderBindings(updated);
        }

        var conflict = updated.FirstOrDefault(binding =>
            !string.IsNullOrEmpty(binding.Key)
            && StringComparer.Ordinal.Equals(binding.Key, normalizedKey));
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
        WorkbenchCommandId.FitExtents => "Fit extents",
        WorkbenchCommandId.OriginAxes => "Origin axes",
        WorkbenchCommandId.MidpointLine => "Midpoint line",
        WorkbenchCommandId.TwoPointRectangle => "Two-point rectangle",
        WorkbenchCommandId.CenterRectangle => "Center rectangle",
        WorkbenchCommandId.AlignedRectangle => "Aligned rectangle",
        WorkbenchCommandId.CenterCircle => "Center circle",
        WorkbenchCommandId.ThreePointCircle => "Three-point circle",
        WorkbenchCommandId.ThreePointArc => "Three-point arc",
        WorkbenchCommandId.TangentArc => "Tangent arc",
        WorkbenchCommandId.CenterPointArc => "Center point arc",
        WorkbenchCommandId.DeleteSelection => "Delete selected geometry",
        WorkbenchCommandId.PowerTrim => "Power trim/extend",
        WorkbenchCommandId.SplitAtPoint => "Split at point",
        WorkbenchCommandId.AddSplinePoint => "Add spline point",
        WorkbenchCommandId.LinearPattern => "Linear pattern",
        WorkbenchCommandId.CircularPattern => "Circular pattern",
        WorkbenchCommandId.Rotate90Clockwise => "Rotate 90 CW",
        WorkbenchCommandId.Rotate90CounterClockwise => "Rotate 90 CCW",
        WorkbenchCommandId.BoundsToOrigin => "Bounds to origin",
        WorkbenchCommandId.PointToOrigin => "Point to origin",
        WorkbenchCommandId.VectorToX => "Vector to X",
        WorkbenchCommandId.VectorToY => "Vector to Y",
        _ => commandId.ToString()
    };

    internal static IReadOnlyList<ToolHotkeyBinding> OrderBindings(IEnumerable<ToolHotkeyBinding> bindings)
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
        if (trimmed.Length == 1)
        {
            var character = trimmed[0];
            if (char.IsLetterOrDigit(character))
            {
                return char.ToUpper(character, CultureInfo.InvariantCulture).ToString();
            }

            return PunctuationBaseKeys.TryGetValue(character, out var punctuationKey)
                ? punctuationKey
                : null;
        }

        if (TryNormalizeFunctionKey(trimmed, out var functionKey))
        {
            return functionKey;
        }

        if (NamedBaseKeys.TryGetValue(trimmed, out var namedKey))
        {
            return namedKey;
        }

        return IsModifierOnlyBaseKey(trimmed) || IsInvalidBrowserBaseKey(trimmed)
            ? null
            : trimmed.ToUpper(CultureInfo.InvariantCulture);
    }

    private static bool TryNormalizeFunctionKey(string key, out string normalized)
    {
        normalized = string.Empty;
        if (key.Length < 2
            || key[0] is not ('f' or 'F')
            || !int.TryParse(key[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index)
            || index is < 1 or > 24)
        {
            return false;
        }

        normalized = $"F{index.ToString(CultureInfo.InvariantCulture)}";
        return true;
    }

    private static bool IsModifierOnlyBaseKey(string key) =>
        StringComparer.OrdinalIgnoreCase.Equals(key, CtrlModifier)
        || StringComparer.OrdinalIgnoreCase.Equals(key, "Control")
        || StringComparer.OrdinalIgnoreCase.Equals(key, AltModifier)
        || StringComparer.OrdinalIgnoreCase.Equals(key, ShiftModifier)
        || StringComparer.OrdinalIgnoreCase.Equals(key, MetaModifier)
        || StringComparer.OrdinalIgnoreCase.Equals(key, "Cmd")
        || StringComparer.OrdinalIgnoreCase.Equals(key, "Command")
        || StringComparer.OrdinalIgnoreCase.Equals(key, "Win")
        || StringComparer.OrdinalIgnoreCase.Equals(key, "OS");

    private static bool IsInvalidBrowserBaseKey(string key) =>
        StringComparer.OrdinalIgnoreCase.Equals(key, "Dead")
        || StringComparer.OrdinalIgnoreCase.Equals(key, "Unidentified")
        || StringComparer.OrdinalIgnoreCase.Equals(key, "Process")
        || key.Any(char.IsWhiteSpace)
        || key.Contains('+', StringComparison.Ordinal);

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

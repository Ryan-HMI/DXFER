namespace DXFER.Core.Sketching;

public readonly record struct SketchReference
{
    private const string CanvasPointSeparator = "|point|";

    public SketchReference(string entityId, SketchReferenceTarget target)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);

        EntityId = entityId;
        Target = target;
    }

    public string EntityId { get; }

    public SketchReferenceTarget Target { get; }

    public bool IsEntity => Target == SketchReferenceTarget.Entity;

    public static bool TryParse(string key, out SketchReference reference)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            reference = default;
            return false;
        }

        var trimmed = key.Trim();
        var canvasPointIndex = trimmed.IndexOf(CanvasPointSeparator, StringComparison.Ordinal);
        if (canvasPointIndex >= 0)
        {
            return TryParseCanvasPointReference(trimmed, canvasPointIndex, out reference);
        }

        var targetSeparatorIndex = trimmed.LastIndexOf(':');
        if (targetSeparatorIndex > 0 && targetSeparatorIndex < trimmed.Length - 1)
        {
            var entityId = trimmed[..targetSeparatorIndex];
            var targetText = trimmed[(targetSeparatorIndex + 1)..];
            if (TryParsePointTarget(targetText, out var target))
            {
                reference = new SketchReference(entityId, target);
                return true;
            }

            reference = default;
            return false;
        }

        reference = new SketchReference(trimmed, SketchReferenceTarget.Entity);
        return true;
    }

    public static bool TryNormalize(string key, out string normalized)
    {
        if (TryParse(key, out var reference))
        {
            normalized = reference.ToString();
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    public override string ToString() =>
        Target switch
        {
            SketchReferenceTarget.Start => $"{EntityId}:start",
            SketchReferenceTarget.End => $"{EntityId}:end",
            SketchReferenceTarget.Center => $"{EntityId}:center",
            _ => EntityId
        };

    private static bool TryParseCanvasPointReference(
        string key,
        int separatorIndex,
        out SketchReference reference)
    {
        var entityId = key[..separatorIndex];
        if (string.IsNullOrWhiteSpace(entityId))
        {
            reference = default;
            return false;
        }

        var tail = key[(separatorIndex + CanvasPointSeparator.Length)..];
        var parts = tail.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || !TryParsePointTarget(parts[0], out var target))
        {
            reference = default;
            return false;
        }

        reference = new SketchReference(entityId, target);
        return true;
    }

    private static bool TryParsePointTarget(string value, out SketchReferenceTarget target)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(value, "start"))
        {
            target = SketchReferenceTarget.Start;
            return true;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(value, "end"))
        {
            target = SketchReferenceTarget.End;
            return true;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(value, "center"))
        {
            target = SketchReferenceTarget.Center;
            return true;
        }

        target = default;
        return false;
    }
}

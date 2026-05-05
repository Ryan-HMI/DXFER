using System.Globalization;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

public readonly record struct SketchReference
{
    private const string CanvasPointSeparator = "|point|";
    private const string SegmentSeparator = "|segment|";

    public SketchReference(string entityId, SketchReferenceTarget target)
        : this(entityId, target, null)
    {
    }

    public SketchReference(string entityId, SketchReferenceTarget target, int? segmentIndex)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityId);
        if (segmentIndex.HasValue && segmentIndex.Value < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentIndex), "Segment index cannot be negative.");
        }

        EntityId = entityId;
        Target = target;
        SegmentIndex = segmentIndex;
    }

    public string EntityId { get; }

    public SketchReferenceTarget Target { get; }

    public int? SegmentIndex { get; }

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
            var entityPart = trimmed[..targetSeparatorIndex];
            var targetText = trimmed[(targetSeparatorIndex + 1)..];
            if (TryParsePointTarget(targetText, out var target))
            {
                reference = TryParseSegmentReferenceBase(entityPart, out var entityId, out var segmentIndex)
                    ? new SketchReference(entityId, target, segmentIndex)
                    : new SketchReference(entityPart, target);
                return true;
            }

            reference = default;
            return false;
        }

        reference = TryParseSegmentReferenceBase(trimmed, out var segmentEntityId, out var segmentReferenceIndex)
            ? new SketchReference(segmentEntityId, SketchReferenceTarget.Entity, segmentReferenceIndex)
            : new SketchReference(trimmed, SketchReferenceTarget.Entity);
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

    public static bool TryParseCanvasPointCoordinates(
        string key,
        out string entityId,
        out string label,
        out Point2 point)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            entityId = string.Empty;
            label = string.Empty;
            point = default;
            return false;
        }

        var trimmed = key.Trim();
        var separatorIndex = trimmed.IndexOf(CanvasPointSeparator, StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            entityId = string.Empty;
            label = string.Empty;
            point = default;
            return false;
        }

        entityId = trimmed[..separatorIndex];
        var tail = trimmed[(separatorIndex + CanvasPointSeparator.Length)..];
        var parts = tail.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3
            || !double.TryParse(parts[^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            entityId = string.Empty;
            label = string.Empty;
            point = default;
            return false;
        }

        label = string.Join("|", parts[..^2]);
        point = new Point2(x, y);
        return true;
    }

    public override string ToString()
    {
        var baseKey = SegmentIndex.HasValue
            ? $"{EntityId}{SegmentSeparator}{SegmentIndex.Value.ToString(CultureInfo.InvariantCulture)}"
            : EntityId;

        return Target switch
        {
            SketchReferenceTarget.Start => $"{baseKey}:start",
            SketchReferenceTarget.End => $"{baseKey}:end",
            SketchReferenceTarget.Center => $"{baseKey}:center",
            _ => baseKey
        };
    }

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

    private static bool TryParseSegmentReferenceBase(
        string value,
        out string entityId,
        out int segmentIndex)
    {
        var separatorIndex = value.IndexOf(SegmentSeparator, StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            entityId = string.Empty;
            segmentIndex = default;
            return false;
        }

        entityId = value[..separatorIndex];
        var segmentText = value[(separatorIndex + SegmentSeparator.Length)..];
        if (string.IsNullOrWhiteSpace(entityId)
            || !int.TryParse(segmentText, NumberStyles.Integer, CultureInfo.InvariantCulture, out segmentIndex)
            || segmentIndex < 0)
        {
            entityId = string.Empty;
            segmentIndex = default;
            return false;
        }

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

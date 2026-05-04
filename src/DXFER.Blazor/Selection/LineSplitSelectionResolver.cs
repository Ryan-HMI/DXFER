using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Blazor.Selection;

public static class LineSplitSelectionResolver
{
    private const string SegmentKeySeparator = "|segment|";
    private const string PointKeySeparator = "|point|";

    public static bool TryResolveLineAndPoint(
        DrawingDocument document,
        IEnumerable<string> selectionKeys,
        out string lineEntityId,
        out Point2 point)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectionKeys);

        var keys = selectionKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var lineIds = keys
            .Where(key => !key.Contains(PointKeySeparator, StringComparison.Ordinal)
                && !key.Contains(SegmentKeySeparator, StringComparison.Ordinal)
                && FindEntity(document, key) is LineEntity)
            .ToArray();

        if (lineIds.Length != 1)
        {
            lineEntityId = string.Empty;
            point = default;
            return false;
        }

        var points = new List<Point2>();
        foreach (var key in keys)
        {
            if (TryGetPointFromSelectionKey(key, out var selectedPoint))
            {
                points.Add(selectedPoint);
                continue;
            }

            if (!key.Contains(SegmentKeySeparator, StringComparison.Ordinal)
                && FindEntity(document, key) is PointEntity pointEntity)
            {
                points.Add(pointEntity.Location);
            }
        }

        if (points.Count != 1)
        {
            lineEntityId = string.Empty;
            point = default;
            return false;
        }

        lineEntityId = lineIds[0];
        point = points[0];
        return true;
    }

    private static DrawingEntity? FindEntity(DrawingDocument document, string entityId) =>
        document.Entities.FirstOrDefault(entity =>
            StringComparer.Ordinal.Equals(entity.Id.Value, entityId));

    private static bool TryGetPointFromSelectionKey(string selectionKey, out Point2 point)
    {
        var separatorIndex = selectionKey.IndexOf(PointKeySeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            point = default;
            return false;
        }

        var tail = selectionKey[(separatorIndex + PointKeySeparator.Length)..];
        var parts = tail.Split('|', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3
            || !double.TryParse(parts[^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var x)
            || !double.TryParse(parts[^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var y))
        {
            point = default;
            return false;
        }

        point = new Point2(x, y);
        return true;
    }
}

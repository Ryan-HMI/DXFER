using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Blazor.Selection;

public static class SelectionPointResolver
{
    private const string PointKeySeparator = "|point|";

    public static bool TryGetPointToOriginReference(
        DrawingDocument document,
        IEnumerable<string> selectionKeys,
        out Point2 point)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectionKeys);

        var keys = selectionKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (keys.Length != 1)
        {
            point = default;
            return false;
        }

        var selectionKey = keys[0];
        if (TryGetPointFromSelectionKey(selectionKey, out point))
        {
            return true;
        }

        var entity = document.Entities.FirstOrDefault(candidate =>
            StringComparer.Ordinal.Equals(candidate.Id.Value, selectionKey));

        switch (entity)
        {
            case CircleEntity circle:
                point = circle.Center;
                return true;
            case ArcEntity arc:
                point = arc.Center;
                return true;
            default:
                point = default;
                return false;
        }
    }

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

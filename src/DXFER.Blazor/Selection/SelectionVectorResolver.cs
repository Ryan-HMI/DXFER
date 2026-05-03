using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Blazor.Selection;

public static class SelectionVectorResolver
{
    private const string SegmentKeySeparator = "|segment|";
    private const string PointKeySeparator = "|point|";
    private const double VectorTolerance = 0.000001;

    public static bool TryGetAlignmentVector(
        DrawingDocument document,
        IEnumerable<string> selectionKeys,
        out Point2 start,
        out Point2 end)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectionKeys);

        var keys = selectionKeys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (keys.Length == 2
            && TryGetPointFromSelectionKey(keys[0], out start)
            && TryGetPointFromSelectionKey(keys[1], out end))
        {
            return HasUsableLength(start, end);
        }

        if (keys.Length != 1)
        {
            start = default;
            end = default;
            return false;
        }

        var selectionKey = keys[0];
        if (TryParseSegmentSelectionKey(selectionKey, out var entityId, out var segmentIndex))
        {
            return TryGetPolylineSegmentVector(document, entityId, segmentIndex, out start, out end);
        }

        if (selectionKey.Contains(PointKeySeparator, StringComparison.Ordinal))
        {
            start = default;
            end = default;
            return false;
        }

        if (document.Entities.FirstOrDefault(entity =>
                StringComparer.Ordinal.Equals(entity.Id.Value, selectionKey)) is LineEntity line)
        {
            start = line.Start;
            end = line.End;
            return HasUsableLength(start, end);
        }

        start = default;
        end = default;
        return false;
    }

    private static bool TryGetPolylineSegmentVector(
        DrawingDocument document,
        string entityId,
        int segmentIndex,
        out Point2 start,
        out Point2 end)
    {
        if (document.Entities.FirstOrDefault(entity =>
                StringComparer.Ordinal.Equals(entity.Id.Value, entityId)) is PolylineEntity polyline
            && segmentIndex >= 0
            && segmentIndex < polyline.Vertices.Count - 1)
        {
            start = polyline.Vertices[segmentIndex];
            end = polyline.Vertices[segmentIndex + 1];
            return HasUsableLength(start, end);
        }

        start = default;
        end = default;
        return false;
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

    private static bool TryParseSegmentSelectionKey(string selectionKey, out string entityId, out int segmentIndex)
    {
        var separatorIndex = selectionKey.IndexOf(SegmentKeySeparator, StringComparison.Ordinal);
        if (separatorIndex < 0)
        {
            entityId = string.Empty;
            segmentIndex = default;
            return false;
        }

        entityId = selectionKey[..separatorIndex];
        return int.TryParse(
            selectionKey[(separatorIndex + SegmentKeySeparator.Length)..],
            NumberStyles.Integer,
            CultureInfo.InvariantCulture,
            out segmentIndex);
    }

    private static bool HasUsableLength(Point2 start, Point2 end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY) > VectorTolerance;
    }
}

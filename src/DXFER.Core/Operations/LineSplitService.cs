using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Operations;

public static class LineSplitService
{
    private const double GeometryTolerance = 0.000001;

    public static bool TrySplitLineAtPoint(
        DrawingDocument document,
        string lineEntityId,
        Point2 point,
        EntityId newLineId,
        out DrawingDocument nextDocument)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(lineEntityId))
        {
            nextDocument = document;
            return false;
        }

        var nextEntities = new List<DrawingEntity>();
        var split = false;

        foreach (var entity in document.Entities)
        {
            if (!split
                && entity is LineEntity line
                && StringComparer.Ordinal.Equals(line.Id.Value, lineEntityId)
                && TryGetInteriorSplitPoint(line, point, out var splitPoint))
            {
                nextEntities.Add(new LineEntity(line.Id, line.Start, splitPoint, line.IsConstruction));
                nextEntities.Add(new LineEntity(newLineId, splitPoint, line.End, line.IsConstruction));
                split = true;
                continue;
            }

            nextEntities.Add(entity);
        }

        nextDocument = split
            ? new DrawingDocument(nextEntities, document.Dimensions, document.Constraints, document.Metadata)
            : document;
        return split;
    }

    private static bool TryGetInteriorSplitPoint(LineEntity line, Point2 point, out Point2 splitPoint)
    {
        var deltaX = line.End.X - line.Start.X;
        var deltaY = line.End.Y - line.Start.Y;
        var lengthSquared = (deltaX * deltaX) + (deltaY * deltaY);
        if (lengthSquared <= GeometryTolerance * GeometryTolerance)
        {
            splitPoint = default;
            return false;
        }

        var parameter = (((point.X - line.Start.X) * deltaX) + ((point.Y - line.Start.Y) * deltaY)) / lengthSquared;
        if (parameter <= GeometryTolerance || parameter >= 1.0 - GeometryTolerance)
        {
            splitPoint = default;
            return false;
        }

        var projected = new Point2(
            line.Start.X + (deltaX * parameter),
            line.Start.Y + (deltaY * parameter));
        var distanceX = point.X - projected.X;
        var distanceY = point.Y - projected.Y;
        var length = Math.Sqrt(lengthSquared);
        var tolerance = Math.Max(GeometryTolerance, length * 0.000001);
        if (Math.Sqrt((distanceX * distanceX) + (distanceY * distanceY)) > tolerance)
        {
            splitPoint = default;
            return false;
        }

        splitPoint = projected;
        return true;
    }
}

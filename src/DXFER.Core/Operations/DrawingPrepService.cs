using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Operations;

public static class DrawingPrepService
{
    public static DrawingDocument MoveBoundsMinimumToOrigin(DrawingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var bounds = document.GetBounds();
        return Transform(document, Transform2.Translation(-bounds.MinX, -bounds.MinY));
    }

    public static DrawingDocument MovePointToOrigin(DrawingDocument document, Point2 point)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Transform(document, Transform2.Translation(-point.X, -point.Y));
    }

    public static DrawingDocument Transform(DrawingDocument document, Transform2 transform)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new DrawingDocument(document.Entities.Select(entity => entity.Transform(transform)));
    }

    public static DrawingDocument TransformSelected(
        DrawingDocument document,
        IEnumerable<string> selectedEntityIds,
        Transform2 transform)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedEntityIds);

        var selected = selectedEntityIds.ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            return document;
        }

        return new DrawingDocument(
            document.Entities.Select(entity =>
                selected.Contains(entity.Id.Value) ? entity.Transform(transform) : entity));
    }

    public static DrawingDocument AlignVectorToAxis(
        DrawingDocument document,
        string vectorEntityId,
        AxisDirection axis)
    {
        ArgumentNullException.ThrowIfNull(document);

        var vectorEntity = document.Entities.FirstOrDefault(entity =>
            StringComparer.Ordinal.Equals(entity.Id.Value, vectorEntityId));

        if (vectorEntity is null || !TryGetVectorAngleDegrees(vectorEntity, out var vectorAngle))
        {
            return document;
        }

        return AlignVectorToAxis(document, vectorAngle, axis);
    }

    public static DrawingDocument AlignVectorToAxis(
        DrawingDocument document,
        Point2 vectorStart,
        Point2 vectorEnd,
        AxisDirection axis)
    {
        ArgumentNullException.ThrowIfNull(document);

        var deltaX = vectorEnd.X - vectorStart.X;
        var deltaY = vectorEnd.Y - vectorStart.Y;
        if (Math.Sqrt(deltaX * deltaX + deltaY * deltaY) <= 0.000001)
        {
            return document;
        }

        var vectorAngle = Math.Atan2(deltaY, deltaX) * 180.0 / Math.PI;
        return AlignVectorToAxis(document, vectorAngle, axis);
    }

    public static DrawingDocument OrientLongBoundsAxis(DrawingDocument document, AxisDirection axis)
    {
        ArgumentNullException.ThrowIfNull(document);

        var bounds = document.GetBounds();
        var longAxisIsX = bounds.Width >= bounds.Height;
        var targetLongAxisIsX = axis == AxisDirection.X;
        if (longAxisIsX == targetLongAxisIsX)
        {
            return document;
        }

        var center = new Point2(
            bounds.MinX + bounds.Width / 2.0,
            bounds.MinY + bounds.Height / 2.0);

        return Transform(document, Transform2.RotationDegreesAbout(90, center));
    }

    public static bool TryGetFirstPoint(DrawingDocument document, IEnumerable<string> selectedEntityIds, out Point2 point)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedEntityIds);

        var selected = selectedEntityIds.ToHashSet(StringComparer.Ordinal);
        foreach (var entity in document.Entities)
        {
            if (selected.Count > 0 && !selected.Contains(entity.Id.Value))
            {
                continue;
            }

            if (TryGetFirstPoint(entity, out point))
            {
                return true;
            }
        }

        point = default;
        return false;
    }

    public static bool TryGetMeasurement(DrawingDocument document, IEnumerable<string> selectedEntityIds, out MeasurementResult measurement)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedEntityIds);

        var selected = selectedEntityIds.ToHashSet(StringComparer.Ordinal);
        foreach (var entity in document.Entities)
        {
            if (selected.Count > 0 && !selected.Contains(entity.Id.Value))
            {
                continue;
            }

            if (MeasurementService.TryMeasureEntity(entity, out measurement))
            {
                return true;
            }
        }

        measurement = default;
        return false;
    }

    private static bool TryGetVectorAngleDegrees(DrawingEntity entity, out double angle)
    {
        if (TryGetVector(entity, out var start, out var end))
        {
            angle = Math.Atan2(end.Y - start.Y, end.X - start.X) * 180.0 / Math.PI;
            return true;
        }

        angle = default;
        return false;
    }

    private static DrawingDocument AlignVectorToAxis(
        DrawingDocument document,
        double vectorAngle,
        AxisDirection axis)
    {
        var targetAngle = axis == AxisDirection.X ? 0.0 : 90.0;
        var rotation = targetAngle - vectorAngle;
        var bounds = document.GetBounds();
        var center = new Point2(
            bounds.MinX + bounds.Width / 2.0,
            bounds.MinY + bounds.Height / 2.0);

        return Transform(document, Transform2.RotationDegreesAbout(rotation, center));
    }

    private static bool TryGetVector(DrawingEntity entity, out Point2 start, out Point2 end)
    {
        switch (entity)
        {
            case LineEntity line:
                start = line.Start;
                end = line.End;
                return true;
            case PolylineEntity polyline:
                for (var index = 1; index < polyline.Vertices.Count; index++)
                {
                    var candidateStart = polyline.Vertices[index - 1];
                    var candidateEnd = polyline.Vertices[index];
                    var deltaX = candidateEnd.X - candidateStart.X;
                    var deltaY = candidateEnd.Y - candidateStart.Y;
                    if (Math.Sqrt(deltaX * deltaX + deltaY * deltaY) > 0.000001)
                    {
                        start = candidateStart;
                        end = candidateEnd;
                        return true;
                    }
                }

                break;
        }

        start = default;
        end = default;
        return false;
    }

    private static bool TryGetFirstPoint(DrawingEntity entity, out Point2 point)
    {
        switch (entity)
        {
            case LineEntity line:
                point = line.Start;
                return true;
            case PolylineEntity polyline:
                point = polyline.Vertices[0];
                return true;
            case CircleEntity circle:
                point = circle.Center;
                return true;
            case ArcEntity arc:
                point = arc.GetSamplePoints(1)[0];
                return true;
            default:
                point = default;
                return false;
        }
    }
}

using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Core.Operations;

public static class DrawingPrepService
{
    private const double ParallelAngleToleranceDegrees = 0.001;

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

        var transformedEntityIds = document.Entities
            .Select(entity => entity.Id.Value)
            .ToHashSet(StringComparer.Ordinal);
        return new DrawingDocument(
            document.Entities.Select(entity => entity.Transform(transform)),
            TransformDimensions(document.Dimensions, transformedEntityIds, transform),
            document.Constraints,
            document.Metadata);
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
                selected.Contains(entity.Id.Value) ? entity.Transform(transform) : entity),
            TransformDimensions(document.Dimensions, selected, transform),
            document.Constraints,
            document.Metadata);
    }

    private static IReadOnlyList<SketchDimension> TransformDimensions(
        IReadOnlyList<SketchDimension> dimensions,
        IReadOnlySet<string> transformedEntityIds,
        Transform2 transform)
    {
        if (dimensions.Count == 0 || transformedEntityIds.Count == 0)
        {
            return dimensions;
        }

        var changed = false;
        var nextDimensions = new SketchDimension[dimensions.Count];
        for (var index = 0; index < dimensions.Count; index++)
        {
            var dimension = dimensions[index];
            var referenceKeys = TransformDimensionReferenceKeys(
                dimension.ReferenceKeys,
                transformedEntityIds,
                transform,
                out var referenceKeysChanged);
            var anchor = dimension.Anchor;
            if (anchor.HasValue && ShouldTransformDimensionAnchor(dimension, transformedEntityIds))
            {
                anchor = anchor.Value.Transform(transform);
            }

            if (!referenceKeysChanged && anchor == dimension.Anchor)
            {
                nextDimensions[index] = dimension;
                continue;
            }

            changed = true;
            nextDimensions[index] = new SketchDimension(
                dimension.Id,
                dimension.Kind,
                referenceKeys,
                dimension.Value,
                anchor,
                dimension.IsDriving);
        }

        return changed ? nextDimensions : dimensions;
    }

    private static IReadOnlyList<string> TransformDimensionReferenceKeys(
        IReadOnlyList<string> referenceKeys,
        IReadOnlySet<string> transformedEntityIds,
        Transform2 transform,
        out bool changed)
    {
        changed = false;
        if (referenceKeys.Count == 0)
        {
            return referenceKeys;
        }

        var nextKeys = new string[referenceKeys.Count];
        for (var index = 0; index < referenceKeys.Count; index++)
        {
            var key = referenceKeys[index];
            if (TryTransformCanvasPointReferenceKey(key, transformedEntityIds, transform, out var transformedKey))
            {
                nextKeys[index] = transformedKey;
                changed = true;
                continue;
            }

            nextKeys[index] = key;
        }

        return changed ? nextKeys : referenceKeys;
    }

    private static bool TryTransformCanvasPointReferenceKey(
        string referenceKey,
        IReadOnlySet<string> transformedEntityIds,
        Transform2 transform,
        out string transformedKey)
    {
        if (!SketchReference.TryParseCanvasPointCoordinates(referenceKey, out var entityId, out var label, out var point)
            || !transformedEntityIds.Contains(entityId))
        {
            transformedKey = string.Empty;
            return false;
        }

        var transformedPoint = point.Transform(transform);
        transformedKey = $"{entityId}|point|{label}|{FormatReferenceNumber(transformedPoint.X)}|{FormatReferenceNumber(transformedPoint.Y)}";
        return true;
    }

    private static bool ShouldTransformDimensionAnchor(
        SketchDimension dimension,
        IReadOnlySet<string> transformedEntityIds)
    {
        if (dimension.ReferenceKeys.Count == 0)
        {
            return false;
        }

        var referencedEntityIds = dimension.ReferenceKeys
            .Select(GetReferenceEntityId)
            .Where(entityId => !string.IsNullOrWhiteSpace(entityId))
            .ToArray();
        return referencedEntityIds.Length > 0
            && referencedEntityIds.All(transformedEntityIds.Contains);
    }

    private static string GetReferenceEntityId(string referenceKey)
    {
        if (SketchReference.TryParseCanvasPointCoordinates(referenceKey, out var canvasEntityId, out _, out _))
        {
            return canvasEntityId;
        }

        return SketchReference.TryParse(referenceKey, out var reference)
            ? reference.EntityId
            : string.Empty;
    }

    private static string FormatReferenceNumber(double value) =>
        CleanNearZero(value).ToString("0.######", CultureInfo.InvariantCulture);

    private static double CleanNearZero(double value) =>
        Math.Abs(value) <= 0.000000001 ? 0 : value;

    public static DrawingDocument RotateAboutBoundsCenter(DrawingDocument document, double degrees)
    {
        ArgumentNullException.ThrowIfNull(document);

        var bounds = document.GetBounds();
        var center = new Point2(
            bounds.MinX + bounds.Width / 2.0,
            bounds.MinY + bounds.Height / 2.0);

        return Transform(document, Transform2.RotationDegreesAbout(degrees, center));
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
        if (selected.Count > 1)
        {
            var selectedEntities = document.Entities
                .Where(entity => selected.Contains(entity.Id.Value))
                .ToArray();
            if (selectedEntities.Length > 1)
            {
                measurement = MeasurementService.MeasureBounds(selectedEntities);
                return true;
            }
        }

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
        var rotation = IsParallelToAxis(vectorAngle, targetAngle)
            ? 180.0
            : targetAngle - vectorAngle;
        var bounds = document.GetBounds();
        var center = new Point2(
            bounds.MinX + bounds.Width / 2.0,
            bounds.MinY + bounds.Height / 2.0);

        return Transform(document, Transform2.RotationDegreesAbout(rotation, center));
    }

    private static bool IsParallelToAxis(double vectorAngle, double targetAngle)
    {
        var delta = Math.Abs(NormalizeSignedDegrees(vectorAngle - targetAngle));
        return delta <= ParallelAngleToleranceDegrees
            || Math.Abs(delta - 180.0) <= ParallelAngleToleranceDegrees;
    }

    private static double NormalizeSignedDegrees(double angle)
    {
        var normalized = angle % 360.0;
        if (normalized > 180.0)
        {
            normalized -= 360.0;
        }
        else if (normalized <= -180.0)
        {
            normalized += 360.0;
        }

        return normalized;
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
            case PointEntity pointEntity:
                point = pointEntity.Location;
                return true;
            default:
                point = default;
                return false;
        }
    }
}

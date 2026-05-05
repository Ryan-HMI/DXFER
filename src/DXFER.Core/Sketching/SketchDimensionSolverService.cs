using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

public static class SketchDimensionSolverService
{
    public static DrawingDocument ApplyDimensions(
        DrawingDocument document,
        IEnumerable<SketchDimension> dimensions)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(dimensions);

        var solved = document;
        foreach (var dimension in dimensions)
        {
            solved = ApplyDimension(solved, dimension);
        }

        return solved;
    }

    public static DrawingDocument ApplyDimension(DrawingDocument document, SketchDimension dimension)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(dimension);

        var entities = document.Entities.ToArray();
        if (dimension.IsDriving)
        {
            ApplyDimensionGeometry(entities, document.Constraints, dimension);
        }

        return new DrawingDocument(
            entities,
            UpsertDimension(document.Dimensions, dimension),
            document.Constraints);
    }

    private static void ApplyDimensionGeometry(
        DrawingEntity[] entities,
        IEnumerable<SketchConstraint> constraints,
        SketchDimension dimension)
    {
        var fixedReferences = SketchFixedReferences.FromConstraints(constraints);

        switch (dimension.Kind)
        {
            case SketchDimensionKind.LinearDistance:
                ApplyPointDistance(entities, fixedReferences, dimension);
                break;
            case SketchDimensionKind.HorizontalDistance:
                ApplyAxisDistance(entities, fixedReferences, dimension, isHorizontal: true);
                break;
            case SketchDimensionKind.VerticalDistance:
                ApplyAxisDistance(entities, fixedReferences, dimension, isHorizontal: false);
                break;
            case SketchDimensionKind.PointToLineDistance:
                ApplyPointToLineDistance(entities, fixedReferences, dimension);
                break;
            case SketchDimensionKind.Radius:
                ApplyRadius(entities, fixedReferences, dimension, dimension.Value);
                break;
            case SketchDimensionKind.Diameter:
                ApplyRadius(entities, fixedReferences, dimension, dimension.Value / 2.0);
                break;
            case SketchDimensionKind.Angle:
                ApplyAngle(entities, fixedReferences, dimension);
                break;
        }
    }

    private static void ApplyPointDistance(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchDimension dimension)
    {
        if (!TryGetTwoPointReferences(dimension, out var firstReference, out var secondReference)
            || !SketchGeometryEditor.TryGetPoint(entities, firstReference, out var firstPoint)
            || !SketchGeometryEditor.TryGetPoint(entities, secondReference, out var secondPoint))
        {
            return;
        }

        var distance = Math.Abs(dimension.Value);
        if (fixedReferences.CanMovePoint(secondReference))
        {
            SketchGeometryEditor.TrySetPoint(
                entities,
                secondReference,
                PointAtDistance(firstPoint, secondPoint, distance));
            return;
        }

        if (fixedReferences.CanMovePoint(firstReference))
        {
            SketchGeometryEditor.TrySetPoint(
                entities,
                firstReference,
                PointAtDistance(secondPoint, firstPoint, distance));
        }
    }

    private static void ApplyAxisDistance(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchDimension dimension,
        bool isHorizontal)
    {
        if (!TryGetTwoPointReferences(dimension, out var firstReference, out var secondReference)
            || !SketchGeometryEditor.TryGetPoint(entities, firstReference, out var firstPoint)
            || !SketchGeometryEditor.TryGetPoint(entities, secondReference, out var secondPoint))
        {
            return;
        }

        var distance = Math.Abs(dimension.Value);
        if (fixedReferences.CanMovePoint(secondReference))
        {
            SketchGeometryEditor.TrySetPoint(
                entities,
                secondReference,
                PointAtAxisDistance(firstPoint, secondPoint, distance, isHorizontal));
            return;
        }

        if (fixedReferences.CanMovePoint(firstReference))
        {
            SketchGeometryEditor.TrySetPoint(
                entities,
                firstReference,
                PointAtAxisDistance(secondPoint, firstPoint, distance, isHorizontal));
        }
    }

    private static void ApplyPointToLineDistance(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchDimension dimension)
    {
        if (dimension.ReferenceKeys.Count < 2
            || !SketchReference.TryParse(dimension.ReferenceKeys[0], out var firstReference)
            || !SketchReference.TryParse(dimension.ReferenceKeys[1], out var secondReference))
        {
            return;
        }

        var value = Math.Abs(dimension.Value);
        var firstIsLine = SketchGeometryEditor.TryGetLine(entities, firstReference, out _, out var firstLine);
        var secondIsLine = SketchGeometryEditor.TryGetLine(entities, secondReference, out _, out var secondLine);
        var firstIsPoint = SketchGeometryEditor.TryGetPoint(entities, firstReference, out var firstPoint);
        var secondIsPoint = SketchGeometryEditor.TryGetPoint(entities, secondReference, out var secondPoint);

        if (firstIsLine && secondIsPoint)
        {
            if (fixedReferences.CanMovePoint(secondReference))
            {
                TryMovePointToLineDistance(entities, secondReference, secondPoint, firstLine, value);
            }
            else if (fixedReferences.CanMoveWholeLine(firstReference))
            {
                TryMoveLineToPointDistance(entities, firstReference, firstLine, secondPoint, value);
            }

            return;
        }

        if (firstIsPoint && secondIsLine)
        {
            if (fixedReferences.CanMoveWholeLine(secondReference))
            {
                TryMoveLineToPointDistance(entities, secondReference, secondLine, firstPoint, value);
            }
            else if (fixedReferences.CanMovePoint(firstReference))
            {
                TryMovePointToLineDistance(entities, firstReference, firstPoint, secondLine, value);
            }
        }
    }

    private static void ApplyRadius(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchDimension dimension,
        double radius)
    {
        if (dimension.ReferenceKeys.Count == 0
            || !SketchReference.TryParse(dimension.ReferenceKeys[0], out var reference)
            || !fixedReferences.CanChangeCircleLikeRadius(reference))
        {
            return;
        }

        SketchGeometryEditor.TrySetCircleLikeRadius(entities, reference, Math.Abs(radius));
    }

    private static void ApplyAngle(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchDimension dimension)
    {
        if (dimension.ReferenceKeys.Count < 2
            || !SketchReference.TryParse(dimension.ReferenceKeys[0], out var firstReference)
            || !SketchReference.TryParse(dimension.ReferenceKeys[1], out var secondReference)
            || !SketchGeometryEditor.TryGetLine(entities, firstReference, out _, out var firstLine)
            || !SketchGeometryEditor.TryGetLine(entities, secondReference, out _, out var secondLine))
        {
            return;
        }

        var firstAngle = GetLineAngleDegrees(firstLine);
        var secondAngle = GetLineAngleDegrees(secondLine);

        if (TryApplyLineDirection(
            entities,
            secondReference,
            GetLineAngleTargetDegrees(firstAngle, secondAngle, dimension.Value),
            fixedReferences))
        {
            return;
        }

        TryApplyLineDirection(
            entities,
            firstReference,
            GetLineAngleTargetDegrees(secondAngle, firstAngle, dimension.Value),
            fixedReferences);
    }

    private static double GetLineAngleTargetDegrees(
        double referenceAngleDegrees,
        double currentDrivenAngleDegrees,
        double dimensionValue)
    {
        var signedDelta = SketchGeometryEditor.NormalizeSignedDegrees(currentDrivenAngleDegrees - referenceAngleDegrees);
        var sign = signedDelta < 0 ? -1.0 : 1.0;
        var axisDelta = Math.Abs(signedDelta);
        var targetAxisAngle = Math.Abs(dimensionValue);
        var targetDelta = axisDelta > 90.0
            ? sign * (180.0 - targetAxisAngle)
            : sign * targetAxisAngle;

        return referenceAngleDegrees + targetDelta;
    }

    internal static bool TryApplyLineDirection(
        DrawingEntity[] entities,
        SketchReference lineReference,
        double angleDegrees,
        SketchFixedReferences fixedReferences)
    {
        if (fixedReferences.IsWholeEntityFixed(lineReference)
            || !SketchGeometryEditor.TryGetLine(entities, lineReference, out _, out var line)
            || !SketchGeometryEditor.TryGetLineDirection(line, out _, out _, out var length))
        {
            return false;
        }

        var radians = angleDegrees * Math.PI / 180.0;
        var delta = new Point2(CleanNearZero(Math.Cos(radians) * length), CleanNearZero(Math.Sin(radians) * length));

        if (fixedReferences.CanChangeLineEndpoint(lineReference, SketchReferenceTarget.End))
        {
            return SketchGeometryEditor.TrySetLine(
                entities,
                lineReference,
                line with
                {
                    End = new Point2(line.Start.X + delta.X, line.Start.Y + delta.Y)
                });
        }

        if (fixedReferences.CanChangeLineEndpoint(lineReference, SketchReferenceTarget.Start))
        {
            return SketchGeometryEditor.TrySetLine(
                entities,
                lineReference,
                line with
                {
                    Start = new Point2(line.End.X - delta.X, line.End.Y - delta.Y)
                });
        }

        return false;
    }

    internal static bool TryApplyLineLength(
        DrawingEntity[] entities,
        SketchReference lineReference,
        double length,
        SketchFixedReferences fixedReferences)
    {
        if (fixedReferences.IsWholeEntityFixed(lineReference)
            || !SketchGeometryEditor.TryGetLine(entities, lineReference, out _, out var line)
            || !SketchGeometryEditor.TryGetLineDirection(line, out var unitX, out var unitY, out _))
        {
            return false;
        }

        if (fixedReferences.CanChangeLineEndpoint(lineReference, SketchReferenceTarget.End))
        {
            return SketchGeometryEditor.TrySetLine(
                entities,
                lineReference,
                line with
                {
                    End = new Point2(line.Start.X + unitX * length, line.Start.Y + unitY * length)
                });
        }

        if (fixedReferences.CanChangeLineEndpoint(lineReference, SketchReferenceTarget.Start))
        {
            return SketchGeometryEditor.TrySetLine(
                entities,
                lineReference,
                line with
                {
                    Start = new Point2(line.End.X - unitX * length, line.End.Y - unitY * length)
                });
        }

        return false;
    }

    private static bool TryMovePointToLineDistance(
        DrawingEntity[] entities,
        SketchReference pointReference,
        Point2 point,
        LineEntity line,
        double distance)
    {
        if (!TryGetLineNormal(line, out var normalX, out var normalY))
        {
            return false;
        }

        var signedDistance = SignedPointLineDistance(point, line, normalX, normalY);
        var sign = signedDistance < 0 ? -1.0 : 1.0;
        var targetSignedDistance = sign * distance;
        var offset = targetSignedDistance - signedDistance;

        return SketchGeometryEditor.TrySetPoint(
            entities,
            pointReference,
            new Point2(point.X + normalX * offset, point.Y + normalY * offset));
    }

    private static bool TryMoveLineToPointDistance(
        DrawingEntity[] entities,
        SketchReference lineReference,
        LineEntity line,
        Point2 point,
        double distance)
    {
        if (!TryGetLineNormal(line, out var normalX, out var normalY))
        {
            return false;
        }

        var signedDistance = SignedPointLineDistance(point, line, normalX, normalY);
        var sign = signedDistance < 0 ? -1.0 : 1.0;
        var targetSignedDistance = sign * distance;
        var offset = signedDistance - targetSignedDistance;

        var moved = line with
        {
            Start = new Point2(line.Start.X + normalX * offset, line.Start.Y + normalY * offset),
            End = new Point2(line.End.X + normalX * offset, line.End.Y + normalY * offset)
        };

        return SketchGeometryEditor.TrySetLine(entities, lineReference, moved);
    }

    private static bool TryGetLineNormal(LineEntity line, out double normalX, out double normalY)
    {
        if (!SketchGeometryEditor.TryGetLineDirection(line, out var unitX, out var unitY, out _))
        {
            normalX = default;
            normalY = default;
            return false;
        }

        normalX = -unitY;
        normalY = unitX;
        return true;
    }

    private static double SignedPointLineDistance(
        Point2 point,
        LineEntity line,
        double normalX,
        double normalY) =>
        (point.X - line.Start.X) * normalX + (point.Y - line.Start.Y) * normalY;

    private static Point2 PointAtDistance(Point2 anchor, Point2 moving, double distance)
    {
        var deltaX = moving.X - anchor.X;
        var deltaY = moving.Y - anchor.Y;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (length <= SketchGeometryEditor.Tolerance)
        {
            return new Point2(anchor.X + distance, anchor.Y);
        }

        return new Point2(
            anchor.X + deltaX / length * distance,
            anchor.Y + deltaY / length * distance);
    }

    private static Point2 PointAtAxisDistance(
        Point2 anchor,
        Point2 moving,
        double distance,
        bool isHorizontal)
    {
        if (isHorizontal)
        {
            var sign = moving.X < anchor.X ? -1.0 : 1.0;
            return new Point2(anchor.X + sign * distance, moving.Y);
        }

        var verticalSign = moving.Y < anchor.Y ? -1.0 : 1.0;
        return new Point2(moving.X, anchor.Y + verticalSign * distance);
    }

    private static bool TryGetTwoPointReferences(
        SketchDimension dimension,
        out SketchReference firstReference,
        out SketchReference secondReference)
    {
        if (dimension.ReferenceKeys.Count >= 2
            && SketchReference.TryParse(dimension.ReferenceKeys[0], out firstReference)
            && SketchReference.TryParse(dimension.ReferenceKeys[1], out secondReference))
        {
            return true;
        }

        firstReference = default;
        secondReference = default;
        return false;
    }

    private static double GetLineAngleDegrees(LineEntity line) =>
        Math.Atan2(line.End.Y - line.Start.Y, line.End.X - line.Start.X) * 180.0 / Math.PI;

    private static double CleanNearZero(double value) =>
        Math.Abs(value) <= SketchGeometryEditor.Tolerance ? 0 : value;

    private static IReadOnlyList<SketchDimension> UpsertDimension(
        IReadOnlyList<SketchDimension> dimensions,
        SketchDimension dimension)
    {
        var nextDimensions = dimensions.ToArray();
        for (var index = 0; index < nextDimensions.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(nextDimensions[index].Id, dimension.Id))
            {
                nextDimensions[index] = dimension;
                return nextDimensions;
            }
        }

        return nextDimensions.Concat(new[] { dimension }).ToArray();
    }
}

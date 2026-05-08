using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

public static class SketchDimensionSolverService
{
    private const double DimensionValueTolerance = 0.001;

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

        var effectiveDimension = NormalizeDimension(dimension);
        var originalEntities = document.Entities.ToArray();
        var entities = document.Entities.ToArray();
        if (effectiveDimension.IsDriving)
        {
            ApplyDimensionGeometry(entities, document.Constraints, effectiveDimension);
            SketchConstraintPropagationService.PropagateFromChanges(
                originalEntities,
                entities,
                document.Constraints,
                SketchFixedReferences.FromConstraints(document.Constraints));
        }

        var dimensions = UpsertDimension(document.Dimensions, effectiveDimension);
        var solvedDocument = new DrawingDocument(entities, dimensions, document.Constraints, document.Metadata);
        return new DrawingDocument(
            entities,
            dimensions,
            SketchConstraintPropagationService.ValidateConstraints(solvedDocument, document.Constraints),
            document.Metadata);
    }

    public static SketchConstraintState GetDimensionState(
        DrawingDocument document,
        SketchDimension dimension)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(dimension);

        return IsDimensionSatisfied(document, dimension)
            ? SketchConstraintState.Satisfied
            : SketchConstraintState.Unsatisfied;
    }

    public static bool IsDimensionSatisfied(
        DrawingDocument document,
        SketchDimension dimension)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(dimension);

        if (!TryGetDimensionMeasurement(document, dimension, out var actualValue))
        {
            return false;
        }

        var expectedValue = dimension.Kind == SketchDimensionKind.Count
            ? PolygonEntity.NormalizeSideCount(dimension.Value)
            : Math.Abs(dimension.Value);
        return Math.Abs(actualValue - expectedValue) <= DimensionValueTolerance;
    }

    public static bool TryGetDimensionMeasurement(
        DrawingDocument document,
        SketchDimension dimension,
        out double value)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(dimension);

        return TryGetDimensionMeasurement(document.Entities, dimension, out value);
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
            case SketchDimensionKind.Count:
                ApplyCount(entities, fixedReferences, dimension);
                break;
        }
    }

    private static bool TryGetDimensionMeasurement(
        IReadOnlyList<DrawingEntity> entities,
        SketchDimension dimension,
        out double value)
    {
        value = default;
        switch (dimension.Kind)
        {
            case SketchDimensionKind.LinearDistance:
                return TryGetTwoPointDimensionPoints(entities, dimension, out var linearFirstPoint, out var linearSecondPoint)
                    && TryAssign(Distance(linearFirstPoint, linearSecondPoint), out value);
            case SketchDimensionKind.HorizontalDistance:
                return TryGetTwoPointDimensionPoints(entities, dimension, out var horizontalFirstPoint, out var horizontalSecondPoint)
                    && TryAssign(Math.Abs(horizontalSecondPoint.X - horizontalFirstPoint.X), out value);
            case SketchDimensionKind.VerticalDistance:
                return TryGetTwoPointDimensionPoints(entities, dimension, out var verticalFirstPoint, out var verticalSecondPoint)
                    && TryAssign(Math.Abs(verticalSecondPoint.Y - verticalFirstPoint.Y), out value);
            case SketchDimensionKind.PointToLineDistance:
                return TryGetPointToLineDimensionMeasurement(entities, dimension, out value);
            case SketchDimensionKind.Radius:
                return TryGetCircleLikeDimensionMeasurement(entities, dimension, diameter: false, out value);
            case SketchDimensionKind.Diameter:
                return TryGetCircleLikeDimensionMeasurement(entities, dimension, diameter: true, out value);
            case SketchDimensionKind.Angle:
                return TryGetAngleDimensionMeasurement(entities, dimension, out value);
            case SketchDimensionKind.Count:
                return TryGetCountDimensionMeasurement(entities, dimension, out value);
            default:
                return false;
        }
    }

    private static bool TryGetTwoPointDimensionPoints(
        IReadOnlyList<DrawingEntity> entities,
        SketchDimension dimension,
        out Point2 firstPoint,
        out Point2 secondPoint)
    {
        if (dimension.ReferenceKeys.Count < 2
            || !TryResolveDimensionPoint(entities, dimension.ReferenceKeys[0], out firstPoint)
            || !TryResolveDimensionPoint(entities, dimension.ReferenceKeys[1], out secondPoint))
        {
            firstPoint = default;
            secondPoint = default;
            return false;
        }

        return true;
    }

    private static bool TryResolveDimensionPoint(
        IReadOnlyList<DrawingEntity> entities,
        string referenceKey,
        out Point2 point)
    {
        if (SketchReference.TryParse(referenceKey, out var reference)
            && SketchGeometryEditor.TryGetPoint(entities, reference, out point))
        {
            return true;
        }

        if (!SketchReference.TryParseCanvasPointCoordinates(referenceKey, out var entityId, out var label, out var storedPoint)
            || !SketchGeometryEditor.TryFindEntity(entities, entityId, out _, out var entity))
        {
            point = default;
            return false;
        }

        if (entity is EllipseEntity ellipse
            && TryGetEllipseCanvasPoint(ellipse, label, out point))
        {
            return true;
        }

        point = storedPoint;
        return true;
    }

    private static bool TryGetEllipseCanvasPoint(EllipseEntity ellipse, string label, out Point2 point)
    {
        if (StringComparer.OrdinalIgnoreCase.Equals(label, "major-start"))
        {
            point = new Point2(
                ellipse.Center.X - ellipse.MajorAxisEndPoint.X,
                ellipse.Center.Y - ellipse.MajorAxisEndPoint.Y);
            return true;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(label, "major-end"))
        {
            point = new Point2(
                ellipse.Center.X + ellipse.MajorAxisEndPoint.X,
                ellipse.Center.Y + ellipse.MajorAxisEndPoint.Y);
            return true;
        }

        if (StringComparer.OrdinalIgnoreCase.Equals(label, "minor-start")
            || StringComparer.OrdinalIgnoreCase.Equals(label, "minor-end"))
        {
            var majorLength = Distance(ellipse.Center, new Point2(
                ellipse.Center.X + ellipse.MajorAxisEndPoint.X,
                ellipse.Center.Y + ellipse.MajorAxisEndPoint.Y));
            if (majorLength <= SketchGeometryEditor.Tolerance)
            {
                point = default;
                return false;
            }

            var minorLength = majorLength * ellipse.MinorRadiusRatio;
            var minorUnit = new Point2(
                -ellipse.MajorAxisEndPoint.Y / majorLength,
                ellipse.MajorAxisEndPoint.X / majorLength);
            var direction = StringComparer.OrdinalIgnoreCase.Equals(label, "minor-start") ? -1.0 : 1.0;
            point = new Point2(
                ellipse.Center.X + (minorUnit.X * minorLength * direction),
                ellipse.Center.Y + (minorUnit.Y * minorLength * direction));
            return true;
        }

        point = default;
        return false;
    }

    private static bool TryGetPointToLineDimensionMeasurement(
        IReadOnlyList<DrawingEntity> entities,
        SketchDimension dimension,
        out double value)
    {
        value = default;
        if (dimension.ReferenceKeys.Count < 2
            || !SketchReference.TryParse(dimension.ReferenceKeys[0], out var firstReference)
            || !SketchReference.TryParse(dimension.ReferenceKeys[1], out var secondReference))
        {
            return false;
        }

        var firstIsLine = SketchGeometryEditor.TryGetLine(entities, firstReference, out _, out var firstLine);
        var secondIsLine = SketchGeometryEditor.TryGetLine(entities, secondReference, out _, out var secondLine);
        var firstIsPoint = SketchGeometryEditor.TryGetPoint(entities, firstReference, out var firstPoint);
        var secondIsPoint = SketchGeometryEditor.TryGetPoint(entities, secondReference, out var secondPoint);

        if (firstIsLine && secondIsLine)
        {
            return AreLinesParallel(firstLine, secondLine)
                && TryAssign(DistancePointToLine(SketchGeometryEditor.Midpoint(secondLine), firstLine), out value);
        }

        if (firstIsLine && secondIsPoint)
        {
            return TryAssign(DistancePointToLine(secondPoint, firstLine), out value);
        }

        if (firstIsPoint && secondIsLine)
        {
            return TryAssign(DistancePointToLine(firstPoint, secondLine), out value);
        }

        return false;
    }

    private static bool TryGetCircleLikeDimensionMeasurement(
        IReadOnlyList<DrawingEntity> entities,
        SketchDimension dimension,
        bool diameter,
        out double value)
    {
        value = default;
        if (dimension.ReferenceKeys.Count == 0
            || !SketchReference.TryParse(dimension.ReferenceKeys[0], out var reference)
            || !SketchGeometryEditor.TryGetCircleLike(entities, reference, out _, out _, out var radius))
        {
            return false;
        }

        return TryAssign(diameter ? radius * 2.0 : radius, out value);
    }

    private static bool TryGetAngleDimensionMeasurement(
        IReadOnlyList<DrawingEntity> entities,
        SketchDimension dimension,
        out double value)
    {
        value = default;
        if (dimension.ReferenceKeys.Count == 1
            && SketchReference.TryParse(dimension.ReferenceKeys[0], out var arcReference)
            && SketchGeometryEditor.TryGetEntity(entities, arcReference, out _, out var entity)
            && entity is ArcEntity arc)
        {
            return TryAssign(GetPositiveSweepDegrees(arc.StartAngleDegrees, arc.EndAngleDegrees), out value);
        }

        if (dimension.ReferenceKeys.Count < 2
            || !SketchReference.TryParse(dimension.ReferenceKeys[0], out var firstReference)
            || !SketchReference.TryParse(dimension.ReferenceKeys[1], out var secondReference)
            || !SketchGeometryEditor.TryGetLine(entities, firstReference, out _, out var firstLine)
            || !SketchGeometryEditor.TryGetLine(entities, secondReference, out _, out var secondLine))
        {
            return false;
        }

        var delta = Math.Abs(SketchGeometryEditor.NormalizeSignedDegrees(GetLineAngleDegrees(secondLine) - GetLineAngleDegrees(firstLine)));
        return TryAssign(delta > 90.0 ? 180.0 - delta : delta, out value);
    }

    private static bool TryGetCountDimensionMeasurement(
        IReadOnlyList<DrawingEntity> entities,
        SketchDimension dimension,
        out double value)
    {
        value = default;
        if (dimension.ReferenceKeys.Count == 0
            || !SketchReference.TryParse(dimension.ReferenceKeys[0], out var reference)
            || !SketchGeometryEditor.TryGetEntity(entities, reference, out _, out var entity)
            || entity is not PolygonEntity polygon)
        {
            return false;
        }

        return TryAssign(polygon.NormalizedSideCount, out value);
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

        if (firstIsLine && secondIsLine)
        {
            if (fixedReferences.CanMoveWholeLine(secondReference))
            {
                TryMoveParallelLineToLineDistance(entities, secondReference, secondLine, firstLine, value);
            }
            else if (fixedReferences.CanMoveWholeLine(firstReference))
            {
                TryMoveParallelLineToLineDistance(entities, firstReference, firstLine, secondLine, value);
            }

            return;
        }

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

    private static void ApplyCount(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchDimension dimension)
    {
        if (dimension.ReferenceKeys.Count == 0
            || !SketchReference.TryParse(dimension.ReferenceKeys[0], out var reference)
            || fixedReferences.IsWholeEntityFixed(reference))
        {
            return;
        }

        SketchGeometryEditor.TrySetPolygonSideCount(
            entities,
            reference,
            PolygonEntity.NormalizeSideCount(dimension.Value));
    }

    private static void ApplyAngle(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchDimension dimension)
    {
        if (TryApplyArcSweepAngle(entities, fixedReferences, dimension))
        {
            return;
        }

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

    private static bool TryApplyArcSweepAngle(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchDimension dimension)
    {
        if (dimension.ReferenceKeys.Count != 1
            || !SketchReference.TryParse(dimension.ReferenceKeys[0], out var reference)
            || fixedReferences.IsWholeEntityFixed(reference)
            || !SketchGeometryEditor.TryGetEntity(entities, reference, out var index, out var entity)
            || entity is not ArcEntity arc)
        {
            return false;
        }

        var sweep = Math.Abs(dimension.Value);
        if (sweep <= SketchGeometryEditor.Tolerance)
        {
            return false;
        }

        entities[index] = arc with
        {
            EndAngleDegrees = arc.StartAngleDegrees + Math.Min(sweep, 360.0 - SketchGeometryEditor.Tolerance)
        };
        return true;
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

    private static bool TryMoveParallelLineToLineDistance(
        DrawingEntity[] entities,
        SketchReference movingReference,
        LineEntity movingLine,
        LineEntity anchorLine,
        double distance)
    {
        if (!AreLinesParallel(movingLine, anchorLine)
            || !TryGetLineNormal(anchorLine, out var normalX, out var normalY))
        {
            return false;
        }

        var signedDistance = SignedPointLineDistance(SketchGeometryEditor.Midpoint(movingLine), anchorLine, normalX, normalY);
        var sign = signedDistance < 0 ? -1.0 : 1.0;
        var targetSignedDistance = sign * distance;
        var offset = targetSignedDistance - signedDistance;
        var moved = movingLine with
        {
            Start = new Point2(movingLine.Start.X + normalX * offset, movingLine.Start.Y + normalY * offset),
            End = new Point2(movingLine.End.X + normalX * offset, movingLine.End.Y + normalY * offset)
        };

        return SketchGeometryEditor.TrySetLine(entities, movingReference, moved);
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

    private static bool AreLinesParallel(LineEntity first, LineEntity second)
    {
        if (!SketchGeometryEditor.TryGetLineDirection(first, out var firstX, out var firstY, out _)
            || !SketchGeometryEditor.TryGetLineDirection(second, out var secondX, out var secondY, out _))
        {
            return false;
        }

        return Math.Abs(firstX * secondY - firstY * secondX) <= SketchGeometryEditor.Tolerance;
    }

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

    private static double Distance(Point2 first, Point2 second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static double DistancePointToLine(Point2 point, LineEntity line)
    {
        var deltaX = line.End.X - line.Start.X;
        var deltaY = line.End.Y - line.Start.Y;
        var denominator = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (denominator <= SketchGeometryEditor.Tolerance)
        {
            return Distance(point, line.Start);
        }

        return Math.Abs((deltaY * point.X) - (deltaX * point.Y) + (line.End.X * line.Start.Y) - (line.End.Y * line.Start.X)) / denominator;
    }

    private static double GetPositiveSweepDegrees(double startAngleDegrees, double endAngleDegrees)
    {
        var sweep = (endAngleDegrees - startAngleDegrees) % 360.0;
        if (sweep < 0)
        {
            sweep += 360.0;
        }

        return sweep;
    }

    private static bool TryAssign(double source, out double value)
    {
        value = source;
        return double.IsFinite(source);
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

    private static SketchDimension NormalizeDimension(SketchDimension dimension)
    {
        if (dimension.Kind != SketchDimensionKind.Count)
        {
            return dimension;
        }

        return new SketchDimension(
            dimension.Id,
            dimension.Kind,
            dimension.ReferenceKeys,
            PolygonEntity.NormalizeSideCount(dimension.Value),
            dimension.Anchor,
            dimension.IsDriving);
    }
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

public static class SketchConstraintService
{
    public static DrawingDocument ApplyConstraints(
        DrawingDocument document,
        IEnumerable<SketchConstraint> constraints)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(constraints);

        var solved = document;
        foreach (var constraint in constraints)
        {
            solved = ApplyConstraint(solved, constraint);
        }

        return solved;
    }

    public static DrawingDocument ApplyConstraint(DrawingDocument document, SketchConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(constraint);

        if (constraint.State == SketchConstraintState.Suppressed)
        {
            return new DrawingDocument(
                document.Entities,
                document.Dimensions,
                UpsertConstraint(document.Constraints, constraint),
                document.Metadata);
        }

        var entities = document.Entities.ToArray();
        if (constraint.Kind != SketchConstraintKind.Fix)
        {
            ApplyConstraintGeometry(entities, document.Constraints, constraint);
        }

        var validated = constraint.Kind == SketchConstraintKind.Fix
            ? WithState(constraint, SketchConstraintState.Satisfied)
            : ValidateConstraint(
                new DrawingDocument(entities, document.Dimensions, document.Constraints, document.Metadata),
                constraint);

        return new DrawingDocument(
            entities,
            document.Dimensions,
            UpsertConstraint(document.Constraints, validated),
            document.Metadata);
    }

    public static SketchConstraint ValidateConstraint(DrawingDocument document, SketchConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(constraint);

        if (constraint.State == SketchConstraintState.Suppressed)
        {
            return constraint;
        }

        var satisfied = constraint.Kind switch
        {
            SketchConstraintKind.Coincident => IsCoincident(document.Entities, constraint),
            SketchConstraintKind.Horizontal => IsAxisAligned(document.Entities, constraint, isHorizontal: true),
            SketchConstraintKind.Vertical => IsAxisAligned(document.Entities, constraint, isHorizontal: false),
            SketchConstraintKind.Parallel => IsParallel(document.Entities, constraint),
            SketchConstraintKind.Perpendicular => IsPerpendicular(document.Entities, constraint),
            SketchConstraintKind.Tangent => IsTangent(document.Entities, constraint),
            SketchConstraintKind.Concentric => IsConcentric(document.Entities, constraint),
            SketchConstraintKind.Equal => IsEqual(document.Entities, constraint),
            SketchConstraintKind.Midpoint => IsMidpoint(document.Entities, constraint),
            SketchConstraintKind.Fix => true,
            _ => false
        };

        return WithState(
            constraint,
            satisfied ? SketchConstraintState.Satisfied : SketchConstraintState.Unsatisfied);
    }

    private static void ApplyConstraintGeometry(
        DrawingEntity[] entities,
        IEnumerable<SketchConstraint> constraints,
        SketchConstraint constraint)
    {
        var fixedReferences = SketchFixedReferences.FromConstraints(constraints);

        switch (constraint.Kind)
        {
            case SketchConstraintKind.Coincident:
                ApplyCoincident(entities, fixedReferences, constraint);
                break;
            case SketchConstraintKind.Horizontal:
                ApplyAxisAlignment(entities, fixedReferences, constraint, isHorizontal: true);
                break;
            case SketchConstraintKind.Vertical:
                ApplyAxisAlignment(entities, fixedReferences, constraint, isHorizontal: false);
                break;
            case SketchConstraintKind.Parallel:
                ApplyLinePairDirection(entities, fixedReferences, constraint, perpendicular: false);
                break;
            case SketchConstraintKind.Perpendicular:
                ApplyLinePairDirection(entities, fixedReferences, constraint, perpendicular: true);
                break;
            case SketchConstraintKind.Tangent:
                break;
            case SketchConstraintKind.Concentric:
                ApplyConcentric(entities, fixedReferences, constraint);
                break;
            case SketchConstraintKind.Equal:
                ApplyEqual(entities, fixedReferences, constraint);
                break;
            case SketchConstraintKind.Midpoint:
                ApplyMidpoint(entities, fixedReferences, constraint);
                break;
        }
    }

    private static void ApplyCoincident(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchConstraint constraint)
    {
        if (!TryGetTwoPointReferences(constraint, out var firstReference, out var secondReference)
            || !SketchGeometryEditor.TryGetPoint(entities, firstReference, out var firstPoint)
            || !SketchGeometryEditor.TryGetPoint(entities, secondReference, out var secondPoint))
        {
            return;
        }

        if (SketchGeometryEditor.AreClose(firstPoint, secondPoint))
        {
            return;
        }

        if (fixedReferences.CanMovePoint(secondReference))
        {
            SketchGeometryEditor.TrySetPoint(entities, secondReference, firstPoint);
        }
        else if (fixedReferences.CanMovePoint(firstReference))
        {
            SketchGeometryEditor.TrySetPoint(entities, firstReference, secondPoint);
        }
    }

    private static void ApplyAxisAlignment(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchConstraint constraint,
        bool isHorizontal)
    {
        if (constraint.ReferenceKeys.Count == 1
            && SketchReference.TryParse(constraint.ReferenceKeys[0], out var lineReference)
            && SketchGeometryEditor.TryGetLine(entities, lineReference, out var index, out var line))
        {
            ApplyLineAxisAlignment(entities, fixedReferences, lineReference, index, line, isHorizontal);
            return;
        }

        if (!TryGetTwoPointReferences(constraint, out var firstReference, out var secondReference)
            || !SketchGeometryEditor.TryGetPoint(entities, firstReference, out var firstPoint)
            || !SketchGeometryEditor.TryGetPoint(entities, secondReference, out var secondPoint))
        {
            return;
        }

        if (fixedReferences.CanMovePoint(secondReference))
        {
            SketchGeometryEditor.TrySetPoint(
                entities,
                secondReference,
                AlignPointToAxis(secondPoint, firstPoint, isHorizontal));
        }
        else if (fixedReferences.CanMovePoint(firstReference))
        {
            SketchGeometryEditor.TrySetPoint(
                entities,
                firstReference,
                AlignPointToAxis(firstPoint, secondPoint, isHorizontal));
        }
    }

    private static void ApplyLineAxisAlignment(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchReference lineReference,
        int index,
        LineEntity line,
        bool isHorizontal)
    {
        if (fixedReferences.IsWholeEntityFixed(lineReference))
        {
            return;
        }

        if (fixedReferences.CanChangeLineEndpoint(lineReference, SketchReferenceTarget.End))
        {
            entities[index] = isHorizontal
                ? line with { End = new Point2(line.End.X, line.Start.Y) }
                : line with { End = new Point2(line.Start.X, line.End.Y) };
            return;
        }

        if (fixedReferences.CanChangeLineEndpoint(lineReference, SketchReferenceTarget.Start))
        {
            entities[index] = isHorizontal
                ? line with { Start = new Point2(line.Start.X, line.End.Y) }
                : line with { Start = new Point2(line.End.X, line.Start.Y) };
        }
    }

    private static void ApplyLinePairDirection(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchConstraint constraint,
        bool perpendicular)
    {
        if (!TryGetTwoLineReferences(
                entities,
                constraint,
                out var firstReference,
                out var firstLine,
                out var secondReference,
                out var secondLine))
        {
            return;
        }

        var firstAngle = GetLineAngleDegrees(firstLine);
        var secondAngle = GetLineAngleDegrees(secondLine);
        var targetSecondAngle = GetClosestLineRelationAngle(
            firstAngle,
            secondAngle,
            perpendicular);

        if (SketchDimensionSolverService.TryApplyLineDirection(
                entities,
                secondReference,
                targetSecondAngle,
                fixedReferences))
        {
            return;
        }

        var targetFirstAngle = GetClosestLineRelationAngle(
            secondAngle,
            firstAngle,
            perpendicular);
        SketchDimensionSolverService.TryApplyLineDirection(
            entities,
            firstReference,
            targetFirstAngle,
            fixedReferences);
    }

    private static double GetClosestLineRelationAngle(
        double referenceAngleDegrees,
        double currentDrivenAngleDegrees,
        bool perpendicular)
    {
        var firstCandidate = perpendicular
            ? referenceAngleDegrees + 90.0
            : referenceAngleDegrees;
        var secondCandidate = perpendicular
            ? referenceAngleDegrees - 90.0
            : referenceAngleDegrees + 180.0;
        var firstDelta = Math.Abs(SketchGeometryEditor.NormalizeSignedDegrees(firstCandidate - currentDrivenAngleDegrees));
        var secondDelta = Math.Abs(SketchGeometryEditor.NormalizeSignedDegrees(secondCandidate - currentDrivenAngleDegrees));
        return secondDelta < firstDelta ? secondCandidate : firstCandidate;
    }

    private static void ApplyConcentric(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchConstraint constraint)
    {
        if (!TryGetTwoCircleLikeReferences(
                entities,
                constraint,
                out var firstReference,
                out var firstCenter,
                out _,
                out var secondReference,
                out var secondCenter,
                out _))
        {
            return;
        }

        if (fixedReferences.CanMoveCircleLikeCenter(secondReference))
        {
            SketchGeometryEditor.TrySetCircleLikeCenter(entities, secondReference, firstCenter);
        }
        else if (fixedReferences.CanMoveCircleLikeCenter(firstReference))
        {
            SketchGeometryEditor.TrySetCircleLikeCenter(entities, firstReference, secondCenter);
        }
    }

    private static void ApplyEqual(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchConstraint constraint)
    {
        if (TryGetTwoLineReferences(
                entities,
                constraint,
                out var firstLineReference,
                out var firstLine,
                out var secondLineReference,
                out var secondLine)
            && SketchGeometryEditor.TryGetLineDirection(firstLine, out _, out _, out var firstLength)
            && SketchGeometryEditor.TryGetLineDirection(secondLine, out _, out _, out var secondLength))
        {
            if (SketchDimensionSolverService.TryApplyLineLength(
                    entities,
                    secondLineReference,
                    firstLength,
                    fixedReferences))
            {
                return;
            }

            SketchDimensionSolverService.TryApplyLineLength(
                entities,
                firstLineReference,
                secondLength,
                fixedReferences);
            return;
        }

        if (!TryGetTwoCircleLikeReferences(
                entities,
                constraint,
                out var firstReference,
                out _,
                out var firstRadius,
                out var secondReference,
                out _,
                out var secondRadius))
        {
            return;
        }

        if (fixedReferences.CanChangeCircleLikeRadius(secondReference))
        {
            SketchGeometryEditor.TrySetCircleLikeRadius(entities, secondReference, firstRadius);
        }
        else if (fixedReferences.CanChangeCircleLikeRadius(firstReference))
        {
            SketchGeometryEditor.TrySetCircleLikeRadius(entities, firstReference, secondRadius);
        }
    }

    private static void ApplyMidpoint(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchConstraint constraint)
    {
        if (!TryGetPointAndLineReferences(
                entities,
                constraint,
                out var pointReference,
                out var point,
                out var lineReference,
                out var line,
                out var pointIsLastReference))
        {
            return;
        }

        var midpoint = SketchGeometryEditor.Midpoint(line);
        if (pointIsLastReference && fixedReferences.CanMovePoint(pointReference))
        {
            SketchGeometryEditor.TrySetPoint(entities, pointReference, midpoint);
            return;
        }

        if (!pointIsLastReference && fixedReferences.CanMoveWholeLine(lineReference))
        {
            TranslateLineToMidpoint(entities, lineReference, line, point);
            return;
        }

        if (fixedReferences.CanMovePoint(pointReference))
        {
            SketchGeometryEditor.TrySetPoint(entities, pointReference, midpoint);
        }
        else if (fixedReferences.CanMoveWholeLine(lineReference))
        {
            TranslateLineToMidpoint(entities, lineReference, line, point);
        }
    }

    private static void TranslateLineToMidpoint(
        DrawingEntity[] entities,
        SketchReference lineReference,
        LineEntity line,
        Point2 targetMidpoint)
    {
        var midpoint = SketchGeometryEditor.Midpoint(line);
        var offsetX = targetMidpoint.X - midpoint.X;
        var offsetY = targetMidpoint.Y - midpoint.Y;
        var moved = line with
        {
            Start = new Point2(line.Start.X + offsetX, line.Start.Y + offsetY),
            End = new Point2(line.End.X + offsetX, line.End.Y + offsetY)
        };

        SketchGeometryEditor.TrySetLine(entities, lineReference, moved);
    }

    private static bool IsCoincident(IReadOnlyList<DrawingEntity> entities, SketchConstraint constraint)
    {
        if (!TryGetTwoPointReferences(constraint, out var firstReference, out var secondReference)
            || !SketchGeometryEditor.TryGetPoint(entities, firstReference, out var firstPoint)
            || !SketchGeometryEditor.TryGetPoint(entities, secondReference, out var secondPoint))
        {
            return false;
        }

        return SketchGeometryEditor.AreClose(firstPoint, secondPoint);
    }

    private static bool IsAxisAligned(
        IReadOnlyList<DrawingEntity> entities,
        SketchConstraint constraint,
        bool isHorizontal)
    {
        if (constraint.ReferenceKeys.Count == 1
            && SketchReference.TryParse(constraint.ReferenceKeys[0], out var lineReference)
            && SketchGeometryEditor.TryGetLine(entities, lineReference, out _, out var line))
        {
            return isHorizontal
                ? SketchGeometryEditor.AreClose(line.Start.Y, line.End.Y)
                : SketchGeometryEditor.AreClose(line.Start.X, line.End.X);
        }

        if (!TryGetTwoPointReferences(constraint, out var firstReference, out var secondReference)
            || !SketchGeometryEditor.TryGetPoint(entities, firstReference, out var firstPoint)
            || !SketchGeometryEditor.TryGetPoint(entities, secondReference, out var secondPoint))
        {
            return false;
        }

        return isHorizontal
            ? SketchGeometryEditor.AreClose(firstPoint.Y, secondPoint.Y)
            : SketchGeometryEditor.AreClose(firstPoint.X, secondPoint.X);
    }

    private static bool IsParallel(IReadOnlyList<DrawingEntity> entities, SketchConstraint constraint)
    {
        if (!TryGetTwoLineReferences(
                entities,
                constraint,
                out _,
                out var firstLine,
                out _,
                out var secondLine)
            || !SketchGeometryEditor.TryGetLineDirection(firstLine, out var firstX, out var firstY, out _)
            || !SketchGeometryEditor.TryGetLineDirection(secondLine, out var secondX, out var secondY, out _))
        {
            return false;
        }

        return Math.Abs(firstX * secondY - firstY * secondX) <= SketchGeometryEditor.Tolerance;
    }

    private static bool IsPerpendicular(IReadOnlyList<DrawingEntity> entities, SketchConstraint constraint)
    {
        if (!TryGetTwoLineReferences(
                entities,
                constraint,
                out _,
                out var firstLine,
                out _,
                out var secondLine)
            || !SketchGeometryEditor.TryGetLineDirection(firstLine, out var firstX, out var firstY, out _)
            || !SketchGeometryEditor.TryGetLineDirection(secondLine, out var secondX, out var secondY, out _))
        {
            return false;
        }

        return Math.Abs(firstX * secondX + firstY * secondY) <= SketchGeometryEditor.Tolerance;
    }

    private static bool IsTangent(IReadOnlyList<DrawingEntity> entities, SketchConstraint constraint)
    {
        if (TryGetLineAndCircleLikeReferences(
                entities,
                constraint,
                out _,
                out var line,
                out _,
                out var center,
                out var radius))
        {
            return SketchGeometryEditor.AreClose(DistancePointToLine(center, line), radius);
        }

        if (!TryGetTwoCircleLikeReferences(
                entities,
                constraint,
                out _,
                out var firstCenter,
                out var firstRadius,
                out _,
                out var secondCenter,
                out var secondRadius))
        {
            return false;
        }

        var centerDistance = Distance(firstCenter, secondCenter);
        return SketchGeometryEditor.AreClose(centerDistance, firstRadius + secondRadius)
            || SketchGeometryEditor.AreClose(centerDistance, Math.Abs(firstRadius - secondRadius));
    }

    private static bool IsConcentric(IReadOnlyList<DrawingEntity> entities, SketchConstraint constraint)
    {
        if (!TryGetTwoCircleLikeReferences(
                entities,
                constraint,
                out _,
                out var firstCenter,
                out _,
                out _,
                out var secondCenter,
                out _))
        {
            return false;
        }

        return SketchGeometryEditor.AreClose(firstCenter, secondCenter);
    }

    private static bool IsEqual(IReadOnlyList<DrawingEntity> entities, SketchConstraint constraint)
    {
        if (TryGetTwoLineReferences(
                entities,
                constraint,
                out _,
                out var firstLine,
                out _,
                out var secondLine)
            && SketchGeometryEditor.TryGetLineDirection(firstLine, out _, out _, out var firstLength)
            && SketchGeometryEditor.TryGetLineDirection(secondLine, out _, out _, out var secondLength))
        {
            return SketchGeometryEditor.AreClose(firstLength, secondLength);
        }

        if (TryGetTwoCircleLikeReferences(
                entities,
                constraint,
                out _,
                out _,
                out var firstRadius,
                out _,
                out _,
                out var secondRadius))
        {
            return SketchGeometryEditor.AreClose(firstRadius, secondRadius);
        }

        return false;
    }

    private static bool IsMidpoint(IReadOnlyList<DrawingEntity> entities, SketchConstraint constraint)
    {
        if (!TryGetPointAndLineReferences(
                entities,
                constraint,
                out _,
                out var point,
                out _,
                out var line,
                out _))
        {
            return false;
        }

        return SketchGeometryEditor.AreClose(point, SketchGeometryEditor.Midpoint(line));
    }

    private static Point2 AlignPointToAxis(Point2 moving, Point2 anchor, bool isHorizontal) =>
        isHorizontal
            ? new Point2(moving.X, anchor.Y)
            : new Point2(anchor.X, moving.Y);

    private static bool TryGetTwoPointReferences(
        SketchConstraint constraint,
        out SketchReference firstReference,
        out SketchReference secondReference)
    {
        if (constraint.ReferenceKeys.Count >= 2
            && SketchReference.TryParse(constraint.ReferenceKeys[0], out firstReference)
            && SketchReference.TryParse(constraint.ReferenceKeys[1], out secondReference))
        {
            return true;
        }

        firstReference = default;
        secondReference = default;
        return false;
    }

    private static bool TryGetTwoLineReferences(
        IReadOnlyList<DrawingEntity> entities,
        SketchConstraint constraint,
        out SketchReference firstReference,
        out LineEntity firstLine,
        out SketchReference secondReference,
        out LineEntity secondLine)
    {
        if (constraint.ReferenceKeys.Count >= 2
            && SketchReference.TryParse(constraint.ReferenceKeys[0], out firstReference)
            && SketchReference.TryParse(constraint.ReferenceKeys[1], out secondReference)
            && SketchGeometryEditor.TryGetLine(entities, firstReference, out _, out firstLine)
            && SketchGeometryEditor.TryGetLine(entities, secondReference, out _, out secondLine))
        {
            return true;
        }

        firstReference = default;
        firstLine = default!;
        secondReference = default;
        secondLine = default!;
        return false;
    }

    private static bool TryGetTwoCircleLikeReferences(
        IReadOnlyList<DrawingEntity> entities,
        SketchConstraint constraint,
        out SketchReference firstReference,
        out Point2 firstCenter,
        out double firstRadius,
        out SketchReference secondReference,
        out Point2 secondCenter,
        out double secondRadius)
    {
        if (constraint.ReferenceKeys.Count >= 2
            && SketchReference.TryParse(constraint.ReferenceKeys[0], out firstReference)
            && SketchReference.TryParse(constraint.ReferenceKeys[1], out secondReference)
            && SketchGeometryEditor.TryGetCircleLike(entities, firstReference, out _, out firstCenter, out firstRadius)
            && SketchGeometryEditor.TryGetCircleLike(entities, secondReference, out _, out secondCenter, out secondRadius))
        {
            return true;
        }

        firstReference = default;
        firstCenter = default;
        firstRadius = default;
        secondReference = default;
        secondCenter = default;
        secondRadius = default;
        return false;
    }

    private static bool TryGetLineAndCircleLikeReferences(
        IReadOnlyList<DrawingEntity> entities,
        SketchConstraint constraint,
        out SketchReference lineReference,
        out LineEntity line,
        out SketchReference circleReference,
        out Point2 center,
        out double radius)
    {
        lineReference = default;
        line = default!;
        circleReference = default;
        center = default;
        radius = default;

        if (constraint.ReferenceKeys.Count < 2
            || !SketchReference.TryParse(constraint.ReferenceKeys[0], out var firstReference)
            || !SketchReference.TryParse(constraint.ReferenceKeys[1], out var secondReference))
        {
            return false;
        }

        var firstIsLine = SketchGeometryEditor.TryGetLine(entities, firstReference, out _, out var firstLine);
        var secondIsLine = SketchGeometryEditor.TryGetLine(entities, secondReference, out _, out var secondLine);
        var firstIsCircleLike = SketchGeometryEditor.TryGetCircleLike(entities, firstReference, out _, out var firstCenter, out var firstRadius);
        var secondIsCircleLike = SketchGeometryEditor.TryGetCircleLike(entities, secondReference, out _, out var secondCenter, out var secondRadius);

        if (firstIsLine && secondIsCircleLike)
        {
            lineReference = firstReference;
            line = firstLine;
            circleReference = secondReference;
            center = secondCenter;
            radius = secondRadius;
            return true;
        }

        if (firstIsCircleLike && secondIsLine)
        {
            lineReference = secondReference;
            line = secondLine;
            circleReference = firstReference;
            center = firstCenter;
            radius = firstRadius;
            return true;
        }

        return false;
    }

    private static bool TryGetPointAndLineReferences(
        IReadOnlyList<DrawingEntity> entities,
        SketchConstraint constraint,
        out SketchReference pointReference,
        out Point2 point,
        out SketchReference lineReference,
        out LineEntity line,
        out bool pointIsLastReference)
    {
        pointReference = default;
        point = default;
        lineReference = default;
        line = default!;
        pointIsLastReference = false;

        if (constraint.ReferenceKeys.Count < 2
            || !SketchReference.TryParse(constraint.ReferenceKeys[0], out var firstReference)
            || !SketchReference.TryParse(constraint.ReferenceKeys[1], out var secondReference))
        {
            return false;
        }

        var firstIsPoint = SketchGeometryEditor.TryGetPoint(entities, firstReference, out var firstPoint);
        var firstIsLine = SketchGeometryEditor.TryGetLine(entities, firstReference, out _, out var firstLine);
        var secondIsPoint = SketchGeometryEditor.TryGetPoint(entities, secondReference, out var secondPoint);
        var secondIsLine = SketchGeometryEditor.TryGetLine(entities, secondReference, out _, out var secondLine);

        if (firstIsLine && secondIsPoint)
        {
            pointReference = secondReference;
            point = secondPoint;
            lineReference = firstReference;
            line = firstLine;
            pointIsLastReference = true;
            return true;
        }

        if (firstIsPoint && secondIsLine)
        {
            pointReference = firstReference;
            point = firstPoint;
            lineReference = secondReference;
            line = secondLine;
            pointIsLastReference = false;
            return true;
        }

        return false;
    }

    private static double GetLineAngleDegrees(LineEntity line) =>
        Math.Atan2(line.End.Y - line.Start.Y, line.End.X - line.Start.X) * 180.0 / Math.PI;

    private static double Distance(Point2 first, Point2 second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
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

    private static SketchConstraint WithState(SketchConstraint constraint, SketchConstraintState state) =>
        new(constraint.Id, constraint.Kind, constraint.ReferenceKeys, state);

    private static IReadOnlyList<SketchConstraint> UpsertConstraint(
        IReadOnlyList<SketchConstraint> constraints,
        SketchConstraint constraint)
    {
        var nextConstraints = constraints.ToArray();
        for (var index = 0; index < nextConstraints.Length; index++)
        {
            if (StringComparer.Ordinal.Equals(nextConstraints[index].Id, constraint.Id))
            {
                nextConstraints[index] = constraint;
                return nextConstraints;
            }
        }

        return nextConstraints.Concat(new[] { constraint }).ToArray();
    }
}

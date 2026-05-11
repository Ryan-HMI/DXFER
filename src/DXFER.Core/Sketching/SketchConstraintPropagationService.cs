using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

internal static class SketchConstraintPropagationService
{
    public static void PropagateFromChanges(
        IReadOnlyList<DrawingEntity> originalEntities,
        DrawingEntity[] entities,
        IReadOnlyList<SketchConstraint> constraints,
        SketchFixedReferences fixedReferences)
    {
        var previousEntities = originalEntities.ToArray();
        var guardLimit = Math.Max(128, constraints.Count * 32);
        for (var guard = 0; guard < guardLimit; guard++)
        {
            var beforeIteration = entities.ToArray();
            var changed = PropagatePointConstraints(previousEntities, entities, constraints, fixedReferences);
            changed |= PropagateDrivenConstraintGeometry(originalEntities, entities, constraints, fixedReferences);
            if (!changed)
            {
                return;
            }

            previousEntities = beforeIteration;
        }
    }

    private static bool PropagatePointConstraints(
        IReadOnlyList<DrawingEntity> previousEntities,
        DrawingEntity[] entities,
        IReadOnlyList<SketchConstraint> constraints,
        SketchFixedReferences fixedReferences)
    {
        var changed = false;
        var queue = new Queue<SketchReference>(GetChangedPointReferences(previousEntities, entities));
        var queued = new HashSet<string>(queue.Select(reference => reference.ToString()), StringComparer.Ordinal);
        var guard = 0;
        var guardLimit = Math.Max(128, constraints.Count * 32);

        while (queue.Count > 0 && guard++ < guardLimit)
        {
            var changedReference = queue.Dequeue();
            queued.Remove(changedReference.ToString());
            if (!SketchGeometryEditor.TryGetPoint(entities, changedReference, out var changedPoint))
            {
                continue;
            }

            foreach (var constraint in constraints)
            {
                if (constraint.State == SketchConstraintState.Suppressed)
                {
                    continue;
                }

                switch (constraint.Kind)
                {
                    case SketchConstraintKind.Coincident:
                        changed |= PropagateCoincident(entities, fixedReferences, constraint, changedReference, changedPoint, queue, queued);
                        break;
                    case SketchConstraintKind.Horizontal:
                        changed |= PropagateAxis(entities, fixedReferences, constraint, changedReference, changedPoint, isHorizontal: true, queue, queued);
                        break;
                    case SketchConstraintKind.Vertical:
                        changed |= PropagateAxis(entities, fixedReferences, constraint, changedReference, changedPoint, isHorizontal: false, queue, queued);
                        break;
                    case SketchConstraintKind.Parallel:
                        changed |= PropagateLineRelation(entities, fixedReferences, constraints, constraint, changedReference, perpendicular: false, queue, queued);
                        break;
                    case SketchConstraintKind.Perpendicular:
                        changed |= PropagateLineRelation(entities, fixedReferences, constraints, constraint, changedReference, perpendicular: true, queue, queued);
                        break;
                }
            }
        }

        return changed;
    }

    public static IReadOnlyList<SketchConstraint> ValidateConstraints(
        DrawingDocument document,
        IReadOnlyList<SketchConstraint> constraints) =>
        constraints
            .Select(constraint => SketchConstraintService.ValidateConstraint(document, constraint))
            .ToArray();

    private static bool PropagateDrivenConstraintGeometry(
        IReadOnlyList<DrawingEntity> originalEntities,
        DrawingEntity[] entities,
        IReadOnlyList<SketchConstraint> constraints,
        SketchFixedReferences fixedReferences)
    {
        var changed = false;
        foreach (var constraint in constraints)
        {
            if (constraint.State == SketchConstraintState.Suppressed)
            {
                continue;
            }

            changed |= constraint.Kind switch
            {
                SketchConstraintKind.Equal => PropagateEqual(originalEntities, entities, fixedReferences, constraint),
                SketchConstraintKind.Concentric => PropagateConcentric(originalEntities, entities, fixedReferences, constraint),
                SketchConstraintKind.Midpoint => PropagateMidpoint(originalEntities, entities, fixedReferences, constraint),
                SketchConstraintKind.Tangent => PropagateTangent(originalEntities, entities, fixedReferences, constraint),
                _ => false
            };
        }

        return changed;
    }

    private static bool PropagateEqual(
        IReadOnlyList<DrawingEntity> originalEntities,
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchConstraint constraint)
    {
        if (TryGetTwoLineReferences(constraint, out var firstLineReference, out var secondLineReference)
            && SketchGeometryEditor.TryGetLine(entities, firstLineReference, out _, out var firstLine)
            && SketchGeometryEditor.TryGetLine(entities, secondLineReference, out _, out var secondLine)
            && SketchGeometryEditor.TryGetLineDirection(firstLine, out _, out _, out var firstLength)
            && SketchGeometryEditor.TryGetLineDirection(secondLine, out _, out _, out var secondLength))
        {
            var firstChanged = HasLineLengthChanged(originalEntities, entities, firstLineReference);
            var secondChanged = HasLineLengthChanged(originalEntities, entities, secondLineReference);
            if (firstChanged && !secondChanged)
            {
                return TryApplyLineLengthAndReport(entities, secondLineReference, firstLength, fixedReferences);
            }

            if (secondChanged && !firstChanged)
            {
                return TryApplyLineLengthAndReport(entities, firstLineReference, secondLength, fixedReferences);
            }

            return false;
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
            return false;
        }

        var firstRadiusChanged = HasCircleLikeRadiusChanged(originalEntities, entities, firstReference);
        var secondRadiusChanged = HasCircleLikeRadiusChanged(originalEntities, entities, secondReference);
        if (firstRadiusChanged && !secondRadiusChanged && fixedReferences.CanChangeCircleLikeRadius(secondReference))
        {
            return TrySetCircleLikeRadiusAndReport(entities, secondReference, firstRadius);
        }

        if (secondRadiusChanged && !firstRadiusChanged && fixedReferences.CanChangeCircleLikeRadius(firstReference))
        {
            return TrySetCircleLikeRadiusAndReport(entities, firstReference, secondRadius);
        }

        return false;
    }

    private static bool PropagateConcentric(
        IReadOnlyList<DrawingEntity> originalEntities,
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
            return false;
        }

        var firstCenterChanged = HasCircleLikeCenterChanged(originalEntities, entities, firstReference);
        var secondCenterChanged = HasCircleLikeCenterChanged(originalEntities, entities, secondReference);
        if (firstCenterChanged && !secondCenterChanged && fixedReferences.CanMoveCircleLikeCenter(secondReference))
        {
            return TrySetCircleLikeCenterAndReport(entities, secondReference, firstCenter);
        }

        if (secondCenterChanged && !firstCenterChanged && fixedReferences.CanMoveCircleLikeCenter(firstReference))
        {
            return TrySetCircleLikeCenterAndReport(entities, firstReference, secondCenter);
        }

        return false;
    }

    private static bool PropagateMidpoint(
        IReadOnlyList<DrawingEntity> originalEntities,
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
                out var line))
        {
            return false;
        }

        var pointChanged = HasPointChanged(originalEntities, entities, pointReference);
        var lineChanged = HasLineGeometryChanged(originalEntities, entities, lineReference);
        if (lineChanged && !pointChanged && fixedReferences.CanMovePoint(pointReference))
        {
            return TrySetPointAndReport(entities, pointReference, SketchGeometryEditor.Midpoint(line));
        }

        if (pointChanged && !lineChanged && fixedReferences.CanMoveWholeLine(lineReference))
        {
            return TryTranslateLineToMidpointAndReport(entities, lineReference, line, point);
        }

        return false;
    }

    private static bool PropagateTangent(
        IReadOnlyList<DrawingEntity> originalEntities,
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchConstraint constraint)
    {
        if (TryGetLineAndCircleLikeReferences(
                entities,
                constraint,
                out var lineReference,
                out var line,
                out var circleReference,
                out var center,
                out var radius))
        {
            var lineChanged = HasLineGeometryChanged(originalEntities, entities, lineReference);
            var circleChanged = HasCircleLikeGeometryChanged(originalEntities, entities, circleReference);
            if (circleChanged && !lineChanged)
            {
                if (fixedReferences.CanMoveWholeLine(lineReference)
                    && TryMoveLineTangentToCircleAndReport(entities, lineReference, line, center, radius))
                {
                    return true;
                }

                if (fixedReferences.CanChangeCircleLikeRadius(circleReference))
                {
                    return TrySetCircleLikeRadiusAndReport(entities, circleReference, DistancePointToLine(center, line));
                }
            }

            if (lineChanged && !circleChanged)
            {
                if (fixedReferences.CanMoveCircleLikeCenter(circleReference)
                    && TryMoveCircleLikeCenterTangentToLineAndReport(entities, circleReference, center, radius, line))
                {
                    return true;
                }

                if (fixedReferences.CanChangeCircleLikeRadius(circleReference))
                {
                    return TrySetCircleLikeRadiusAndReport(entities, circleReference, DistancePointToLine(center, line));
                }
            }

            return false;
        }

        if (!TryGetTwoCircleLikeReferences(
                entities,
                constraint,
                out var firstReference,
                out var firstCenter,
                out var firstRadius,
                out var secondReference,
                out var secondCenter,
                out var secondRadius))
        {
            return false;
        }

        var firstChanged = HasCircleLikeGeometryChanged(originalEntities, entities, firstReference);
        var secondChanged = HasCircleLikeGeometryChanged(originalEntities, entities, secondReference);
        if (firstChanged && !secondChanged)
        {
            return TryMoveOrResizeCircleLikeForTangency(
                entities,
                fixedReferences,
                movingReference: secondReference,
                movingCenter: secondCenter,
                movingRadius: secondRadius,
                anchorCenter: firstCenter,
                anchorRadius: firstRadius);
        }

        if (secondChanged && !firstChanged)
        {
            return TryMoveOrResizeCircleLikeForTangency(
                entities,
                fixedReferences,
                movingReference: firstReference,
                movingCenter: firstCenter,
                movingRadius: firstRadius,
                anchorCenter: secondCenter,
                anchorRadius: secondRadius);
        }

        return false;
    }

    private static bool PropagateCoincident(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchConstraint constraint,
        SketchReference changedReference,
        Point2 changedPoint,
        Queue<SketchReference> queue,
        ISet<string> queued)
    {
        if (!TryGetTwoPointReferences(constraint, out var firstReference, out var secondReference))
        {
            return false;
        }

        var otherReference = ReferenceEquals(firstReference, changedReference)
            ? secondReference
            : ReferenceEquals(secondReference, changedReference)
                ? firstReference
                : (SketchReference?)null;
        if (!otherReference.HasValue || !fixedReferences.CanMovePoint(otherReference.Value))
        {
            return false;
        }

        return TrySetAndQueuePoint(entities, otherReference.Value, changedPoint, queue, queued);
    }

    private static bool PropagateAxis(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchConstraint constraint,
        SketchReference changedReference,
        Point2 changedPoint,
        bool isHorizontal,
        Queue<SketchReference> queue,
        ISet<string> queued)
    {
        if (constraint.ReferenceKeys.Count == 1
            && SketchReference.TryParse(constraint.ReferenceKeys[0], out var lineReference)
            && ReferencesSameLine(lineReference, changedReference)
            && TryGetOppositeEndpointReference(lineReference, changedReference, out var otherReference)
            && SketchGeometryEditor.TryGetPoint(entities, otherReference, out var otherPoint)
            && fixedReferences.CanMovePoint(otherReference))
        {
            var aligned = isHorizontal
                ? new Point2(otherPoint.X, changedPoint.Y)
                : new Point2(changedPoint.X, otherPoint.Y);
            return TrySetAndQueuePoint(entities, otherReference, aligned, queue, queued);
        }

        if (!TryGetTwoPointReferences(constraint, out var firstReference, out var secondReference))
        {
            return false;
        }

        if (ReferenceEquals(firstReference, changedReference)
            && SketchGeometryEditor.TryGetPoint(entities, secondReference, out var secondPoint)
            && fixedReferences.CanMovePoint(secondReference))
        {
            var aligned = isHorizontal
                ? new Point2(secondPoint.X, changedPoint.Y)
                : new Point2(changedPoint.X, secondPoint.Y);
            return TrySetAndQueuePoint(entities, secondReference, aligned, queue, queued);
        }

        if (ReferenceEquals(secondReference, changedReference)
            && SketchGeometryEditor.TryGetPoint(entities, firstReference, out var firstPoint)
            && fixedReferences.CanMovePoint(firstReference))
        {
            var aligned = isHorizontal
                ? new Point2(firstPoint.X, changedPoint.Y)
                : new Point2(changedPoint.X, firstPoint.Y);
            return TrySetAndQueuePoint(entities, firstReference, aligned, queue, queued);
        }

        return false;
    }

    private static bool PropagateLineRelation(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        IReadOnlyList<SketchConstraint> constraints,
        SketchConstraint constraint,
        SketchReference changedReference,
        bool perpendicular,
        Queue<SketchReference> queue,
        ISet<string> queued)
    {
        if (!TryGetTwoLineReferences(constraint, out var firstReference, out var secondReference)
            || !TryGetLineReferenceForPoint(changedReference, out var changedLineReference))
        {
            return false;
        }

        if (AreLineAxesAlreadyConstrained(constraints, firstReference, secondReference, perpendicular))
        {
            return false;
        }

        if (ReferencesEqual(changedLineReference, firstReference)
            && SketchGeometryEditor.TryGetLine(entities, secondReference, out _, out var secondLine))
        {
            return TryApplyLineRelationPreservingPoint(
                entities,
                fixedReferences,
                firstReference,
                changedReference,
                secondLine,
                perpendicular,
                queue,
                queued);
        }
        else if (ReferencesEqual(changedLineReference, secondReference)
            && SketchGeometryEditor.TryGetLine(entities, firstReference, out _, out var firstLine))
        {
            return TryApplyLineRelationPreservingPoint(
                entities,
                fixedReferences,
                secondReference,
                changedReference,
                firstLine,
                perpendicular,
                queue,
                queued);
        }

        return false;
    }

    private static bool TryApplyLineRelationPreservingPoint(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchReference lineReference,
        SketchReference changedReference,
        LineEntity referenceLine,
        bool perpendicular,
        Queue<SketchReference> queue,
        ISet<string> queued)
    {
        if (!TryGetOppositeEndpointReference(lineReference, changedReference, out var otherReference)
            || !SketchGeometryEditor.TryGetLineDirection(referenceLine, out _, out _, out _)
            || !SketchGeometryEditor.TryGetPoint(entities, changedReference, out var changedPoint)
            || !SketchGeometryEditor.TryGetPoint(entities, otherReference, out var otherPoint)
            || !fixedReferences.CanMovePoint(otherReference))
        {
            return false;
        }

        var referenceAngle = GetLineAngleDegrees(referenceLine);
        var currentAngle = Math.Atan2(otherPoint.Y - changedPoint.Y, otherPoint.X - changedPoint.X) * 180.0 / Math.PI;
        var targetAngle = GetClosestLineRelationAngle(referenceAngle, currentAngle, perpendicular);
        var radians = targetAngle * Math.PI / 180.0;
        var unit = new Point2(CleanNearZero(Math.Cos(radians)), CleanNearZero(Math.Sin(radians)));
        var scalar = ((otherPoint.X - changedPoint.X) * unit.X) + ((otherPoint.Y - changedPoint.Y) * unit.Y);
        var nextOtherPoint = new Point2(
            changedPoint.X + unit.X * scalar,
            changedPoint.Y + unit.Y * scalar);

        return TrySetAndQueuePoint(entities, otherReference, nextOtherPoint, queue, queued);
    }

    private static bool AreLineAxesAlreadyConstrained(
        IReadOnlyList<SketchConstraint> constraints,
        SketchReference firstReference,
        SketchReference secondReference,
        bool perpendicular)
    {
        if (!TryGetAxisConstraint(constraints, firstReference, out var firstAxis)
            || !TryGetAxisConstraint(constraints, secondReference, out var secondAxis))
        {
            return false;
        }

        return perpendicular
            ? firstAxis != secondAxis
            : firstAxis == secondAxis;
    }

    private static bool TryGetAxisConstraint(
        IReadOnlyList<SketchConstraint> constraints,
        SketchReference lineReference,
        out SketchConstraintKind kind)
    {
        foreach (var constraint in constraints)
        {
            if (constraint.State == SketchConstraintState.Suppressed
                || constraint.Kind is not (SketchConstraintKind.Horizontal or SketchConstraintKind.Vertical)
                || constraint.ReferenceKeys.Count != 1
                || !SketchReference.TryParse(constraint.ReferenceKeys[0], out var constrainedReference)
                || !ReferencesEqual(constrainedReference, lineReference))
            {
                continue;
            }

            kind = constraint.Kind;
            return true;
        }

        kind = default;
        return false;
    }

    private static bool TrySetAndQueuePoint(
        DrawingEntity[] entities,
        SketchReference reference,
        Point2 point,
        Queue<SketchReference> queue,
        ISet<string> queued)
    {
        if (!SketchGeometryEditor.TryGetPoint(entities, reference, out var currentPoint)
            || SketchGeometryEditor.AreClose(currentPoint, point)
            || !SketchGeometryEditor.TrySetPoint(entities, reference, point))
        {
            return false;
        }

        var key = reference.ToString();
        if (queued.Add(key))
        {
            queue.Enqueue(reference);
        }

        return true;
    }

    private static bool TrySetPointAndReport(DrawingEntity[] entities, SketchReference reference, Point2 point)
    {
        if (!SketchGeometryEditor.TryGetPoint(entities, reference, out var currentPoint)
            || SketchGeometryEditor.AreClose(currentPoint, point)
            || !SketchGeometryEditor.TrySetPoint(entities, reference, point))
        {
            return false;
        }

        return true;
    }

    private static bool TryApplyLineLengthAndReport(
        DrawingEntity[] entities,
        SketchReference reference,
        double length,
        SketchFixedReferences fixedReferences)
    {
        if (!SketchGeometryEditor.TryGetLine(entities, reference, out _, out var before)
            || !SketchDimensionSolverService.TryApplyLineLength(entities, reference, length, fixedReferences)
            || !SketchGeometryEditor.TryGetLine(entities, reference, out _, out var after))
        {
            return false;
        }

        return !SketchGeometryEditor.AreClose(before.Start, after.Start)
            || !SketchGeometryEditor.AreClose(before.End, after.End);
    }

    private static bool TrySetCircleLikeCenterAndReport(DrawingEntity[] entities, SketchReference reference, Point2 center)
    {
        if (!SketchGeometryEditor.TryGetCircleLike(entities, reference, out _, out var currentCenter, out _)
            || SketchGeometryEditor.AreClose(currentCenter, center)
            || !SketchGeometryEditor.TrySetCircleLikeCenter(entities, reference, center))
        {
            return false;
        }

        return true;
    }

    private static bool TrySetCircleLikeRadiusAndReport(DrawingEntity[] entities, SketchReference reference, double radius)
    {
        if (!SketchGeometryEditor.TryGetCircleLike(entities, reference, out _, out _, out var currentRadius)
            || SketchGeometryEditor.AreClose(currentRadius, radius)
            || !SketchGeometryEditor.TrySetCircleLikeRadius(entities, reference, Math.Abs(radius)))
        {
            return false;
        }

        return true;
    }

    private static bool TryTranslateLineToMidpointAndReport(
        DrawingEntity[] entities,
        SketchReference reference,
        LineEntity line,
        Point2 targetMidpoint)
    {
        var midpoint = SketchGeometryEditor.Midpoint(line);
        var delta = new Point2(targetMidpoint.X - midpoint.X, targetMidpoint.Y - midpoint.Y);
        if (SketchGeometryEditor.Distance(new Point2(0, 0), delta) <= SketchGeometryEditor.Tolerance)
        {
            return false;
        }

        return SketchGeometryEditor.TrySetLine(
            entities,
            reference,
            line with
            {
                Start = new Point2(line.Start.X + delta.X, line.Start.Y + delta.Y),
                End = new Point2(line.End.X + delta.X, line.End.Y + delta.Y)
            });
    }

    private static bool TryMoveLineTangentToCircleAndReport(
        DrawingEntity[] entities,
        SketchReference lineReference,
        LineEntity line,
        Point2 center,
        double radius)
    {
        if (!TryGetLineNormal(line, out var normalX, out var normalY))
        {
            return false;
        }

        var signedDistance = SignedPointLineDistance(center, line, normalX, normalY);
        var sign = signedDistance < 0 ? -1.0 : 1.0;
        var targetSignedDistance = sign * Math.Abs(radius);
        var offset = signedDistance - targetSignedDistance;
        if (Math.Abs(offset) <= SketchGeometryEditor.Tolerance)
        {
            return false;
        }

        return SketchGeometryEditor.TrySetLine(
            entities,
            lineReference,
            line with
            {
                Start = new Point2(line.Start.X + normalX * offset, line.Start.Y + normalY * offset),
                End = new Point2(line.End.X + normalX * offset, line.End.Y + normalY * offset)
            });
    }

    private static bool TryMoveCircleLikeCenterTangentToLineAndReport(
        DrawingEntity[] entities,
        SketchReference circleReference,
        Point2 center,
        double radius,
        LineEntity line)
    {
        if (!TryGetLineNormal(line, out var normalX, out var normalY))
        {
            return false;
        }

        var signedDistance = SignedPointLineDistance(center, line, normalX, normalY);
        var sign = signedDistance < 0 ? -1.0 : 1.0;
        var targetSignedDistance = sign * Math.Abs(radius);
        var offset = targetSignedDistance - signedDistance;
        if (Math.Abs(offset) <= SketchGeometryEditor.Tolerance)
        {
            return false;
        }

        return TrySetCircleLikeCenterAndReport(
            entities,
            circleReference,
            new Point2(center.X + normalX * offset, center.Y + normalY * offset));
    }

    private static bool TryMoveOrResizeCircleLikeForTangency(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchReference movingReference,
        Point2 movingCenter,
        double movingRadius,
        Point2 anchorCenter,
        double anchorRadius)
    {
        var centerDistance = SketchGeometryEditor.Distance(anchorCenter, movingCenter);
        var externalDistance = Math.Abs(anchorRadius) + Math.Abs(movingRadius);
        var internalDistance = Math.Abs(Math.Abs(anchorRadius) - Math.Abs(movingRadius));
        var useInternal = Math.Abs(centerDistance - internalDistance) < Math.Abs(centerDistance - externalDistance);
        var targetDistance = useInternal ? internalDistance : externalDistance;

        if (fixedReferences.CanMoveCircleLikeCenter(movingReference)
            && targetDistance > SketchGeometryEditor.Tolerance)
        {
            var unit = UnitVector(anchorCenter, movingCenter);
            return TrySetCircleLikeCenterAndReport(
                entities,
                movingReference,
                new Point2(anchorCenter.X + unit.X * targetDistance, anchorCenter.Y + unit.Y * targetDistance));
        }

        if (!fixedReferences.CanChangeCircleLikeRadius(movingReference))
        {
            return false;
        }

        var nextRadius = useInternal
            ? Math.Abs(centerDistance - Math.Abs(anchorRadius))
            : centerDistance - Math.Abs(anchorRadius);
        return nextRadius > SketchGeometryEditor.Tolerance
            && TrySetCircleLikeRadiusAndReport(entities, movingReference, nextRadius);
    }

    private static bool HasPointChanged(
        IReadOnlyList<DrawingEntity> originalEntities,
        IReadOnlyList<DrawingEntity> entities,
        SketchReference reference) =>
        SketchGeometryEditor.TryGetPoint(originalEntities, reference, out var before)
        && SketchGeometryEditor.TryGetPoint(entities, reference, out var after)
        && !SketchGeometryEditor.AreClose(before, after);

    private static bool HasLineGeometryChanged(
        IReadOnlyList<DrawingEntity> originalEntities,
        IReadOnlyList<DrawingEntity> entities,
        SketchReference reference) =>
        SketchGeometryEditor.TryGetLine(originalEntities, reference, out _, out var before)
        && SketchGeometryEditor.TryGetLine(entities, reference, out _, out var after)
        && (!SketchGeometryEditor.AreClose(before.Start, after.Start)
            || !SketchGeometryEditor.AreClose(before.End, after.End));

    private static bool HasLineLengthChanged(
        IReadOnlyList<DrawingEntity> originalEntities,
        IReadOnlyList<DrawingEntity> entities,
        SketchReference reference) =>
        SketchGeometryEditor.TryGetLine(originalEntities, reference, out _, out var before)
        && SketchGeometryEditor.TryGetLine(entities, reference, out _, out var after)
        && SketchGeometryEditor.TryGetLineDirection(before, out _, out _, out var beforeLength)
        && SketchGeometryEditor.TryGetLineDirection(after, out _, out _, out var afterLength)
        && !SketchGeometryEditor.AreClose(beforeLength, afterLength);

    private static bool HasCircleLikeGeometryChanged(
        IReadOnlyList<DrawingEntity> originalEntities,
        IReadOnlyList<DrawingEntity> entities,
        SketchReference reference) =>
        HasCircleLikeCenterChanged(originalEntities, entities, reference)
        || HasCircleLikeRadiusChanged(originalEntities, entities, reference);

    private static bool HasCircleLikeCenterChanged(
        IReadOnlyList<DrawingEntity> originalEntities,
        IReadOnlyList<DrawingEntity> entities,
        SketchReference reference) =>
        SketchGeometryEditor.TryGetCircleLike(originalEntities, reference, out _, out var beforeCenter, out _)
        && SketchGeometryEditor.TryGetCircleLike(entities, reference, out _, out var afterCenter, out _)
        && !SketchGeometryEditor.AreClose(beforeCenter, afterCenter);

    private static bool HasCircleLikeRadiusChanged(
        IReadOnlyList<DrawingEntity> originalEntities,
        IReadOnlyList<DrawingEntity> entities,
        SketchReference reference) =>
        SketchGeometryEditor.TryGetCircleLike(originalEntities, reference, out _, out _, out var beforeRadius)
        && SketchGeometryEditor.TryGetCircleLike(entities, reference, out _, out _, out var afterRadius)
        && !SketchGeometryEditor.AreClose(beforeRadius, afterRadius);

    private static IReadOnlyList<SketchReference> GetChangedPointReferences(
        IReadOnlyList<DrawingEntity> originalEntities,
        IReadOnlyList<DrawingEntity> entities)
    {
        var changedReferences = new List<SketchReference>();
        foreach (var reference in EnumeratePointReferences(originalEntities))
        {
            if (SketchGeometryEditor.TryGetPoint(originalEntities, reference, out var before)
                && SketchGeometryEditor.TryGetPoint(entities, reference, out var after)
                && !SketchGeometryEditor.AreClose(before, after))
            {
                changedReferences.Add(reference);
            }
        }

        return changedReferences;
    }

    private static IEnumerable<SketchReference> EnumeratePointReferences(IReadOnlyList<DrawingEntity> entities)
    {
        foreach (var entity in entities)
        {
            switch (entity)
            {
                case LineEntity line:
                    yield return new SketchReference(line.Id.Value, SketchReferenceTarget.Start);
                    yield return new SketchReference(line.Id.Value, SketchReferenceTarget.End);
                    break;
                case PolylineEntity polyline:
                    for (var index = 0; index < polyline.Vertices.Count - 1; index++)
                    {
                        yield return new SketchReference(polyline.Id.Value, SketchReferenceTarget.Start, index);
                        yield return new SketchReference(polyline.Id.Value, SketchReferenceTarget.End, index);
                    }

                    break;
                case CircleEntity circle:
                    yield return new SketchReference(circle.Id.Value, SketchReferenceTarget.Center);
                    break;
                case ArcEntity arc:
                    yield return new SketchReference(arc.Id.Value, SketchReferenceTarget.Start);
                    yield return new SketchReference(arc.Id.Value, SketchReferenceTarget.End);
                    yield return new SketchReference(arc.Id.Value, SketchReferenceTarget.Center);
                    break;
                case PolygonEntity polygon:
                    yield return new SketchReference(polygon.Id.Value, SketchReferenceTarget.Center);
                    break;
                case PointEntity point:
                    yield return new SketchReference(point.Id.Value, SketchReferenceTarget.Entity);
                    break;
            }
        }
    }

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
        SketchConstraint constraint,
        out SketchReference firstReference,
        out SketchReference secondReference)
    {
        if (constraint.ReferenceKeys.Count >= 2
            && SketchReference.TryParse(constraint.ReferenceKeys[0], out firstReference)
            && SketchReference.TryParse(constraint.ReferenceKeys[1], out secondReference)
            && firstReference.Target == SketchReferenceTarget.Entity
            && secondReference.Target == SketchReferenceTarget.Entity)
        {
            return true;
        }

        firstReference = default;
        secondReference = default;
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
        out LineEntity line)
    {
        pointReference = default;
        point = default;
        lineReference = default;
        line = default!;

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
            return true;
        }

        if (firstIsPoint && secondIsLine)
        {
            pointReference = firstReference;
            point = firstPoint;
            lineReference = secondReference;
            line = secondLine;
            return true;
        }

        return false;
    }

    private static bool TryGetLineReferenceForPoint(SketchReference pointReference, out SketchReference lineReference)
    {
        if (pointReference.Target is SketchReferenceTarget.Start or SketchReferenceTarget.End)
        {
            lineReference = new SketchReference(
                pointReference.EntityId,
                SketchReferenceTarget.Entity,
                pointReference.SegmentIndex);
            return true;
        }

        lineReference = default;
        return false;
    }

    private static bool TryGetOppositeEndpointReference(
        SketchReference lineReference,
        SketchReference changedReference,
        out SketchReference otherReference)
    {
        if (!ReferencesSameLine(lineReference, changedReference)
            || changedReference.Target is not (SketchReferenceTarget.Start or SketchReferenceTarget.End))
        {
            otherReference = default;
            return false;
        }

        otherReference = new SketchReference(
            lineReference.EntityId,
            changedReference.Target == SketchReferenceTarget.Start
                ? SketchReferenceTarget.End
                : SketchReferenceTarget.Start,
            lineReference.SegmentIndex);
        return true;
    }

    private static bool ReferencesSameLine(SketchReference lineReference, SketchReference pointReference) =>
        StringComparer.Ordinal.Equals(lineReference.EntityId, pointReference.EntityId)
        && lineReference.SegmentIndex == pointReference.SegmentIndex;

    private static bool ReferencesEqual(SketchReference first, SketchReference second) =>
        StringComparer.Ordinal.Equals(first.ToString(), second.ToString());

    private static bool ReferenceEquals(SketchReference first, SketchReference second) =>
        StringComparer.Ordinal.Equals(first.ToString(), second.ToString());

    private static Point2 UnitVector(Point2 start, Point2 end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        return length <= SketchGeometryEditor.Tolerance
            ? new Point2(1, 0)
            : new Point2(deltaX / length, deltaY / length);
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

    private static double DistancePointToLine(Point2 point, LineEntity line)
    {
        var deltaX = line.End.X - line.Start.X;
        var deltaY = line.End.Y - line.Start.Y;
        var denominator = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (denominator <= SketchGeometryEditor.Tolerance)
        {
            return SketchGeometryEditor.Distance(point, line.Start);
        }

        return Math.Abs((deltaY * point.X) - (deltaX * point.Y) + (line.End.X * line.Start.Y) - (line.End.Y * line.Start.X)) / denominator;
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

    private static double GetLineAngleDegrees(LineEntity line) =>
        Math.Atan2(line.End.Y - line.Start.Y, line.End.X - line.Start.X) * 180.0 / Math.PI;

    private static double CleanNearZero(double value) =>
        Math.Abs(value) <= SketchGeometryEditor.Tolerance ? 0 : value;
}

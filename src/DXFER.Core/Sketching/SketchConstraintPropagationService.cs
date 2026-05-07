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
        var queue = new Queue<SketchReference>(GetChangedPointReferences(originalEntities, entities));
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
                        PropagateCoincident(entities, fixedReferences, constraint, changedReference, changedPoint, queue, queued);
                        break;
                    case SketchConstraintKind.Horizontal:
                        PropagateAxis(entities, fixedReferences, constraint, changedReference, changedPoint, isHorizontal: true, queue, queued);
                        break;
                    case SketchConstraintKind.Vertical:
                        PropagateAxis(entities, fixedReferences, constraint, changedReference, changedPoint, isHorizontal: false, queue, queued);
                        break;
                    case SketchConstraintKind.Parallel:
                        PropagateLineRelation(entities, fixedReferences, constraint, changedReference, perpendicular: false, queue, queued);
                        break;
                    case SketchConstraintKind.Perpendicular:
                        PropagateLineRelation(entities, fixedReferences, constraint, changedReference, perpendicular: true, queue, queued);
                        break;
                }
            }
        }
    }

    public static IReadOnlyList<SketchConstraint> ValidateConstraints(
        DrawingDocument document,
        IReadOnlyList<SketchConstraint> constraints) =>
        constraints
            .Select(constraint => SketchConstraintService.ValidateConstraint(document, constraint))
            .ToArray();

    private static void PropagateCoincident(
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
            return;
        }

        var otherReference = ReferenceEquals(firstReference, changedReference)
            ? secondReference
            : ReferenceEquals(secondReference, changedReference)
                ? firstReference
                : (SketchReference?)null;
        if (!otherReference.HasValue || !fixedReferences.CanMovePoint(otherReference.Value))
        {
            return;
        }

        TrySetAndQueuePoint(entities, otherReference.Value, changedPoint, queue, queued);
    }

    private static void PropagateAxis(
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
            TrySetAndQueuePoint(entities, otherReference, aligned, queue, queued);
            return;
        }

        if (!TryGetTwoPointReferences(constraint, out var firstReference, out var secondReference))
        {
            return;
        }

        if (ReferenceEquals(firstReference, changedReference)
            && SketchGeometryEditor.TryGetPoint(entities, secondReference, out var secondPoint)
            && fixedReferences.CanMovePoint(secondReference))
        {
            var aligned = isHorizontal
                ? new Point2(secondPoint.X, changedPoint.Y)
                : new Point2(changedPoint.X, secondPoint.Y);
            TrySetAndQueuePoint(entities, secondReference, aligned, queue, queued);
        }
        else if (ReferenceEquals(secondReference, changedReference)
            && SketchGeometryEditor.TryGetPoint(entities, firstReference, out var firstPoint)
            && fixedReferences.CanMovePoint(firstReference))
        {
            var aligned = isHorizontal
                ? new Point2(firstPoint.X, changedPoint.Y)
                : new Point2(changedPoint.X, firstPoint.Y);
            TrySetAndQueuePoint(entities, firstReference, aligned, queue, queued);
        }
    }

    private static void PropagateLineRelation(
        DrawingEntity[] entities,
        SketchFixedReferences fixedReferences,
        SketchConstraint constraint,
        SketchReference changedReference,
        bool perpendicular,
        Queue<SketchReference> queue,
        ISet<string> queued)
    {
        if (!TryGetTwoLineReferences(constraint, out var firstReference, out var secondReference)
            || !TryGetLineReferenceForPoint(changedReference, out var changedLineReference))
        {
            return;
        }

        if (ReferencesEqual(changedLineReference, firstReference)
            && SketchGeometryEditor.TryGetLine(entities, secondReference, out _, out var secondLine))
        {
            TryApplyLineRelationPreservingPoint(
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
            TryApplyLineRelationPreservingPoint(
                entities,
                fixedReferences,
                secondReference,
                changedReference,
                firstLine,
                perpendicular,
                queue,
                queued);
        }
    }

    private static void TryApplyLineRelationPreservingPoint(
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
            return;
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

        TrySetAndQueuePoint(entities, otherReference, nextOtherPoint, queue, queued);
    }

    private static void TrySetAndQueuePoint(
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
            return;
        }

        var key = reference.ToString();
        if (queued.Add(key))
        {
            queue.Enqueue(reference);
        }
    }

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

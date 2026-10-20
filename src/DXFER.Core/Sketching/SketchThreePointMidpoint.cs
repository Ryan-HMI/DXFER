using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

internal static class SketchThreePointMidpoint
{
    public static bool IsSatisfied(IReadOnlyList<DrawingEntity> entities, SketchConstraint constraint) =>
        TryResolve(entities, constraint, out _, out var points)
        && SketchGeometryEditor.AreClose(points[0], Average(points[1], points[2]));

    public static bool Apply(DrawingEntity[] entities, SketchFixedReferences fixedReferences,
        SketchConstraint constraint, IReadOnlyList<DrawingEntity>? original = null)
    {
        if (!TryResolve(entities, constraint, out var references, out var points)
            || SketchGeometryEditor.AreClose(points[0], Average(points[1], points[2])))
            return false;

        var targets = points.ToArray();
        var middleMoved = false;
        var firstMoved = false;
        var secondMoved = false;
        if (original is not null)
        {
            if (!TryResolve(original, constraint, out _, out var before)) return false;
            middleMoved = !SketchGeometryEditor.AreClose(before[0], points[0]);
            firstMoved = !SketchGeometryEditor.AreClose(before[1], points[1]);
            secondMoved = !SketchGeometryEditor.AreClose(before[2], points[2]);
            // Coupled edits do not identify a unique driver. Final validation decides.
            if (middleMoved && (firstMoved || secondMoved)
                || !middleMoved && !firstMoved && !secondMoved) return false;
        }

        if (original is null || !middleMoved)
        {
            if (CanMovePoint(entities, fixedReferences, references[0]))
            {
                targets[0] = Average(points[1], points[2]);
                return TryCommit(entities, fixedReferences, constraint, references, points, targets);
            }
            if (!IsFixedPoint(entities, fixedReferences, references[0])) return false;

            if (original is not null)
            {
                // Keep an edited outer point as the driver about a fixed middle.
                if (firstMoved == secondMoved) return false;
                var peer = firstMoved ? 2 : 1;
                if (!CanMovePoint(entities, fixedReferences, references[peer])) return false;
                targets[peer] = Reflect(points[0], points[firstMoved ? 1 : 2]);
                return TryCommit(entities, fixedReferences, constraint, references, points, targets);
            }
        }

        var canMoveFirst = CanMovePoint(entities, fixedReferences, references[1]);
        var canMoveSecond = CanMovePoint(entities, fixedReferences, references[2]);
        if (canMoveFirst && canMoveSecond)
        {
            var midpoint = Average(points[1], points[2]);
            var delta = new Point2(points[0].X - midpoint.X, points[0].Y - midpoint.Y);
            targets[1] = new Point2(points[1].X + delta.X, points[1].Y + delta.Y);
            targets[2] = new Point2(points[2].X + delta.X, points[2].Y + delta.Y);
        }
        else if (canMoveFirst)
            targets[1] = Reflect(points[0], points[2]);
        else if (canMoveSecond)
            targets[2] = Reflect(points[0], points[1]);
        else
            return false;

        return TryCommit(entities, fixedReferences, constraint, references, points, targets);
    }

    private static bool TryCommit(DrawingEntity[] entities, SketchFixedReferences fixedReferences,
        SketchConstraint constraint, SketchReference[] references, Point2[] points, Point2[] targets)
    {
        if (targets.Any(point => !Finite(point))) return false;
        var candidate = entities.ToArray();
        for (var index = 0; index < references.Length; index++)
        {
            if (SketchGeometryEditor.AreClose(points[index], targets[index])) continue;
            if (!CanMovePoint(entities, fixedReferences, references[index])
                || !SketchGeometryEditor.TrySetPoint(candidate, references[index], targets[index]))
                return false;
        }

        // Shared vertices and curve endpoints can affect other references while being
        // written. Check all requested positions and fixed aliases before committing.
        if (!IsSatisfied(candidate, constraint)) return false;
        for (var index = 0; index < references.Length; index++)
        {
            if (!SketchGeometryEditor.TryGetPoint(candidate, references[index], out var actual)
                || !SketchGeometryEditor.AreClose(actual, targets[index])) return false;
        }
        for (var index = 0; index < entities.Length; index++)
        {
            if (ReferenceEquals(entities[index], candidate[index])) continue;
            var entityReference = new SketchReference(entities[index].Id.Value, SketchReferenceTarget.Entity);
            if (fixedReferences.IsWholeEntityFixed(entityReference)) return false;
            foreach (var reference in GetAffectedReferences(entities[index]))
            {
                if (!fixedReferences.IsFixed(reference)) continue;
                if (reference.IsEntity)
                {
                    if (!SketchGeometryEditor.TryGetLine(entities, reference, out _, out var beforeLine)
                        || !SketchGeometryEditor.TryGetLine(candidate, reference, out _, out var afterLine)
                        || !SketchGeometryEditor.AreClose(beforeLine.Start, afterLine.Start)
                        || !SketchGeometryEditor.AreClose(beforeLine.End, afterLine.End)) return false;
                }
                else if (!SketchGeometryEditor.TryGetPoint(entities, reference, out var beforePoint)
                    || !SketchGeometryEditor.TryGetPoint(candidate, reference, out var afterPoint)
                    || !SketchGeometryEditor.AreClose(beforePoint, afterPoint)) return false;
            }
        }
        Array.Copy(candidate, entities, entities.Length);
        return true;
    }

    private static bool CanMovePoint(IReadOnlyList<DrawingEntity> entities,
        SketchFixedReferences fixedReferences, SketchReference reference) =>
        fixedReferences.CanMovePoint(reference) && !IsFixedPoint(entities, fixedReferences, reference);

    private static bool IsFixedPoint(IReadOnlyList<DrawingEntity> entities,
        SketchFixedReferences fixedReferences, SketchReference reference)
    {
        if (fixedReferences.IsFixed(reference)) return true;
        if (!SketchGeometryEditor.TryGetEntity(entities, reference, out _, out var entity)) return false;
        var identity = PointIdentity(entities, reference);
        return GetAffectedReferences(entity).Any(alias => !alias.IsEntity
            && fixedReferences.IsFixed(alias) && PointIdentity(entities, alias) == identity);
    }

    private static IEnumerable<SketchReference> GetAffectedReferences(DrawingEntity entity)
    {
        var targets = new[] { SketchReferenceTarget.Start, SketchReferenceTarget.End,
            SketchReferenceTarget.Center, SketchReferenceTarget.Midpoint };
        foreach (var target in targets)
            yield return new SketchReference(entity.Id.Value, target);
        if (entity is not PolylineEntity polyline) yield break;
        for (var segment = 0; segment < polyline.Vertices.Count - 1; segment++)
        {
            yield return new SketchReference(entity.Id.Value, SketchReferenceTarget.Entity, segment);
            yield return new SketchReference(entity.Id.Value, SketchReferenceTarget.Start, segment);
            yield return new SketchReference(entity.Id.Value, SketchReferenceTarget.End, segment);
            yield return new SketchReference(entity.Id.Value, SketchReferenceTarget.Midpoint, segment);
        }
    }

    private static bool TryResolve(IReadOnlyList<DrawingEntity> entities, SketchConstraint constraint,
        out SketchReference[] references, out Point2[] points)
    {
        references = new SketchReference[3];
        points = new Point2[3];
        if (constraint.Kind != SketchConstraintKind.Midpoint || constraint.ReferenceKeys.Count != 3) return false;
        var identities = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < 3; index++)
        {
            if (!SketchReference.TryParse(constraint.ReferenceKeys[index], out references[index])
                || !IsSupportedPointReference(entities, references[index])
                || !SketchGeometryEditor.TryGetPoint(entities, references[index], out points[index])
                || !Finite(points[index])
                || !identities.Add(PointIdentity(entities, references[index]))) return false;
        }
        return true;
    }

    private static bool IsSupportedPointReference(IReadOnlyList<DrawingEntity> entities, SketchReference reference)
    {
        if (reference.IsOrigin) return true;
        if (!SketchGeometryEditor.TryGetEntity(entities, reference, out _, out var entity)) return false;
        // Segment qualifiers belong only to real polyline segments. The shared
        // point resolver accepts some non-polyline targets without checking them.
        if (reference.SegmentIndex is { } segment)
            return entity is PolylineEntity polyline && segment >= 0 && segment < polyline.Vertices.Count - 1
                && reference.Target is SketchReferenceTarget.Start or SketchReferenceTarget.End or SketchReferenceTarget.Midpoint;

        return entity switch
        {
            LineEntity => reference.Target is SketchReferenceTarget.Start or SketchReferenceTarget.End or SketchReferenceTarget.Midpoint,
            ArcEntity => reference.Target is SketchReferenceTarget.Start or SketchReferenceTarget.End or SketchReferenceTarget.Center,
            CircleEntity or EllipseEntity or PolygonEntity => reference.Target == SketchReferenceTarget.Center,
            SplineEntity => reference.Target is SketchReferenceTarget.Start or SketchReferenceTarget.End,
            PointEntity => reference.Target is SketchReferenceTarget.Entity or SketchReferenceTarget.Center,
            _ => false
        };
    }

    private static string PointIdentity(IReadOnlyList<DrawingEntity> entities, SketchReference reference)
    {
        if (!SketchGeometryEditor.TryGetEntity(entities, reference, out _, out var entity))
            return reference.ToString();
        if (entity is PointEntity)
            return new SketchReference(reference.EntityId, SketchReferenceTarget.Center).ToString();
        if (entity is PolylineEntity polyline && reference.SegmentIndex is { } segment
            && reference.Target is SketchReferenceTarget.Start or SketchReferenceTarget.End)
        {
            var vertex = segment + (reference.Target == SketchReferenceTarget.End ? 1 : 0);
            if (vertex == polyline.Vertices.Count - 1 && polyline.Vertices[0] == polyline.Vertices[^1])
                vertex = 0;
            return new SketchReference(reference.EntityId, SketchReferenceTarget.Start, vertex).ToString();
        }
        return reference.ToString();
    }

    private static Point2 Average(Point2 first, Point2 second) =>
        new(first.X / 2 + second.X / 2, first.Y / 2 + second.Y / 2);

    private static Point2 Reflect(Point2 middle, Point2 outer) =>
        new(middle.X + (middle.X - outer.X), middle.Y + (middle.Y - outer.Y));

    private static bool Finite(Point2 point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
}

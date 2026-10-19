using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

internal static class SketchCircleIncidence
{
    public static bool IsSatisfied(IReadOnlyList<DrawingEntity> entities, SketchConstraint constraint) =>
        TryGetPair(entities, constraint, out _, out var point, out _, out var circle)
        && Math.Abs(SketchGeometryEditor.Distance(point, circle.Center) - circle.Radius) <= SketchGeometryEditor.Tolerance;

    public static bool Apply(DrawingEntity[] entities, SketchFixedReferences fixedReferences,
        SketchConstraint constraint, IReadOnlyList<DrawingEntity>? original = null)
    {
        if (IsSatisfied(entities, constraint)
            || !TryGetPair(entities, constraint, out var pointRef, out var point, out var circleRef, out var circle)) return false;
        var dx = point.X - circle.Center.X;
        var dy = point.Y - circle.Center.Y;
        var distance = Math.Sqrt(dx * dx + dy * dy);
        var pointMoved = original is not null && SketchGeometryEditor.TryGetPoint(original, pointRef, out var oldPoint)
            && !SketchGeometryEditor.AreClose(oldPoint, point);
        var circleMoved = original is not null && TryGetCircle(original, circleRef, out var oldCircle)
            && (oldCircle.Center != circle.Center || oldCircle.Radius != circle.Radius);

        // At the exact center, retain the previous radial direction if available.
        // A new concentric pair has no preferred direction; use positive X.
        if (distance <= SketchGeometryEditor.Tolerance)
        {
            dx = 1; dy = 0;
            if (original is not null && SketchGeometryEditor.TryGetPoint(original, pointRef, out var priorPoint)
                && TryGetCircle(original, circleRef, out var priorCircle))
            {
                var priorDistance = SketchGeometryEditor.Distance(priorPoint, priorCircle.Center);
                if (priorDistance > SketchGeometryEditor.Tolerance)
                {
                    dx = (priorPoint.X - priorCircle.Center.X) / priorDistance;
                    dy = (priorPoint.Y - priorCircle.Center.Y) / priorDistance;
                }
            }
        }
        else { dx /= distance; dy /= distance; }

        var projection = new Point2(circle.Center.X + dx * circle.Radius, circle.Center.Y + dy * circle.Radius);
        if (!Finite(projection)) return false;
        if ((pointMoved && !circleMoved || !fixedReferences.CanMovePoint(pointRef))
            && fixedReferences.CanMoveCircleLikeCenter(circleRef))
        {
            var center = new Point2(point.X - dx * circle.Radius, point.Y - dy * circle.Radius);
            return Finite(center) && SketchGeometryEditor.TrySetCircleLikeCenter(entities, circleRef, center);
        }
        return fixedReferences.CanMovePoint(pointRef) && SketchGeometryEditor.TrySetPoint(entities, pointRef, projection);
    }

    private static bool TryGetPair(IReadOnlyList<DrawingEntity> entities, SketchConstraint constraint,
        out SketchReference pointRef, out Point2 point, out SketchReference circleRef, out CircleEntity circle)
    {
        pointRef = circleRef = default; point = default; circle = default!;
        if (constraint.Kind != SketchConstraintKind.PointOnCircle || constraint.ReferenceKeys.Count != 2
            || !SketchReference.TryParse(constraint.ReferenceKeys[0], out var first)
            || !SketchReference.TryParse(constraint.ReferenceKeys[1], out var second)
            || first.EntityId == second.EntityId) return false;
        pointRef = first; circleRef = second;
        if (SketchGeometryEditor.TryGetPoint(entities, first, out point) && Finite(point)
            && TryGetCircle(entities, second, out circle)) return true;
        pointRef = second; circleRef = first;
        return SketchGeometryEditor.TryGetPoint(entities, second, out point) && Finite(point)
            && TryGetCircle(entities, first, out circle);
    }

    private static bool TryGetCircle(IReadOnlyList<DrawingEntity> entities, SketchReference reference, out CircleEntity circle)
    {
        circle = default!;
        if (!reference.IsEntity || reference.SegmentIndex.HasValue
            || !SketchGeometryEditor.TryGetEntity(entities, reference, out _, out var entity)
            || entity is not CircleEntity found || !Finite(found.Center)
            || !double.IsFinite(found.Radius) || found.Radius <= SketchGeometryEditor.Tolerance) return false;
        circle = found;
        return true;
    }

    private static bool Finite(Point2 point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
}

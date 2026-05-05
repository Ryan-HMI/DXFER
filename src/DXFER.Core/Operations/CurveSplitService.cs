using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Operations;

public static class CurveSplitService
{
    private const double GeometryTolerance = 0.000001;
    private const double FullCircleDegrees = 360.0;

    public static bool TrySplitCircleAtPoints(
        DrawingDocument document,
        string circleEntityId,
        Point2 firstPoint,
        Point2 secondPoint,
        EntityId secondArcId,
        out DrawingDocument nextDocument)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(circleEntityId))
        {
            nextDocument = document;
            return false;
        }

        var nextEntities = new List<DrawingEntity>();
        var split = false;
        foreach (var entity in document.Entities)
        {
            if (!split
                && entity is CircleEntity circle
                && StringComparer.Ordinal.Equals(circle.Id.Value, circleEntityId)
                && TryGetPointAngleOnCircle(circle.Center, circle.Radius, firstPoint, out var firstAngle)
                && TryGetPointAngleOnCircle(circle.Center, circle.Radius, secondPoint, out var secondAngle)
                && !AnglesAreClose(firstAngle, secondAngle))
            {
                nextEntities.Add(new ArcEntity(circle.Id, circle.Center, circle.Radius, firstAngle, secondAngle, circle.IsConstruction));
                nextEntities.Add(new ArcEntity(secondArcId, circle.Center, circle.Radius, secondAngle, firstAngle + FullCircleDegrees, circle.IsConstruction));
                split = true;
                continue;
            }

            nextEntities.Add(entity);
        }

        nextDocument = split
            ? new DrawingDocument(nextEntities, document.Dimensions, document.Constraints)
            : document;
        return split;
    }

    public static bool TrySplitArcAtPoint(
        DrawingDocument document,
        string arcEntityId,
        Point2 point,
        EntityId secondArcId,
        out DrawingDocument nextDocument)
    {
        ArgumentNullException.ThrowIfNull(document);

        if (string.IsNullOrWhiteSpace(arcEntityId))
        {
            nextDocument = document;
            return false;
        }

        var nextEntities = new List<DrawingEntity>();
        var split = false;
        foreach (var entity in document.Entities)
        {
            if (!split
                && entity is ArcEntity arc
                && StringComparer.Ordinal.Equals(arc.Id.Value, arcEntityId)
                && TryGetInteriorPointAngleOnArc(arc, point, out var splitAngle))
            {
                nextEntities.Add(arc with { EndAngleDegrees = splitAngle });
                nextEntities.Add(new ArcEntity(secondArcId, arc.Center, arc.Radius, splitAngle, arc.EndAngleDegrees, arc.IsConstruction));
                split = true;
                continue;
            }

            nextEntities.Add(entity);
        }

        nextDocument = split
            ? new DrawingDocument(nextEntities, document.Dimensions, document.Constraints)
            : document;
        return split;
    }

    private static bool TryGetInteriorPointAngleOnArc(ArcEntity arc, Point2 point, out double angle)
    {
        if (!TryGetPointAngleOnCircle(arc.Center, arc.Radius, point, out var rawAngle))
        {
            angle = default;
            return false;
        }

        var splitDelta = GetCounterClockwiseDeltaDegrees(arc.StartAngleDegrees, rawAngle);
        var sweep = GetCounterClockwiseDeltaDegrees(arc.StartAngleDegrees, arc.EndAngleDegrees);
        if (splitDelta <= GeometryTolerance || splitDelta >= sweep - GeometryTolerance)
        {
            angle = default;
            return false;
        }

        angle = arc.StartAngleDegrees + splitDelta;
        return true;
    }

    private static bool TryGetPointAngleOnCircle(Point2 center, double radius, Point2 point, out double angle)
    {
        if (radius <= GeometryTolerance)
        {
            angle = default;
            return false;
        }

        var distance = Distance(center, point);
        var tolerance = Math.Max(GeometryTolerance, radius * 0.000001);
        if (Math.Abs(distance - radius) > tolerance)
        {
            angle = default;
            return false;
        }

        angle = Math.Atan2(point.Y - center.Y, point.X - center.X) * 180.0 / Math.PI;
        return true;
    }

    private static bool AnglesAreClose(double first, double second)
    {
        var delta = GetCounterClockwiseDeltaDegrees(first, second);
        return delta <= GeometryTolerance || Math.Abs(delta - FullCircleDegrees) <= GeometryTolerance;
    }

    private static double GetCounterClockwiseDeltaDegrees(double startAngleDegrees, double angleDegrees)
    {
        var delta = (angleDegrees - startAngleDegrees) % FullCircleDegrees;
        return delta < 0 ? delta + FullCircleDegrees : delta;
    }

    private static double Distance(Point2 first, Point2 second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }
}

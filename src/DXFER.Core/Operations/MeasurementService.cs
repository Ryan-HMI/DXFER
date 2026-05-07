using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Operations;

public static class MeasurementService
{
    private const double GeometryTolerance = 0.000001;

    public static MeasurementResult Measure(Point2 start, Point2 end)
    {
        var deltaX = end.X - start.X;
        var deltaY = end.Y - start.Y;

        return new MeasurementResult(deltaX, deltaY, Math.Sqrt(deltaX * deltaX + deltaY * deltaY));
    }

    public static MeasurementResult MeasureBounds(IEnumerable<DrawingEntity> entities)
    {
        ArgumentNullException.ThrowIfNull(entities);

        using var enumerator = entities.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return default;
        }

        var bounds = enumerator.Current.GetBounds();
        while (enumerator.MoveNext())
        {
            bounds = bounds.Union(enumerator.Current.GetBounds());
        }

        return MeasureBounds(bounds);
    }

    public static MeasurementResult MeasureBounds(Bounds2 bounds)
    {
        var width = bounds.Width;
        var height = bounds.Height;
        return new MeasurementResult(width, height, Math.Sqrt((width * width) + (height * height)));
    }

    public static bool TryMeasureEntity(DrawingEntity entity, out MeasurementResult measurement)
    {
        switch (entity)
        {
            case LineEntity line:
                measurement = Measure(line.Start, line.End);
                return true;
            case PolylineEntity { Vertices.Count: >= 2 } polyline:
                measurement = MeasurePath(polyline.Vertices, closed: false);
                return true;
            case CircleEntity circle:
                measurement = new MeasurementResult(circle.Radius * 2, circle.Radius * 2, circle.Radius * 2);
                return true;
            case ArcEntity arc:
                measurement = MeasureArc(arc);
                return true;
            case EllipseEntity ellipse:
                measurement = MeasureSampledPath(ellipse.GetSamplePoints(), IsFullSweep(ellipse.StartParameterDegrees, ellipse.EndParameterDegrees));
                return true;
            case PolygonEntity polygon:
                measurement = MeasurePath(polygon.GetVertices(), closed: true);
                return true;
            case SplineEntity spline:
                measurement = MeasureSampledPath(spline.GetSamplePoints(), closed: false);
                return true;
            case PointEntity point:
                measurement = new MeasurementResult(point.Location.X, point.Location.Y, 0);
                return true;
            default:
                measurement = default;
                return false;
        }
    }

    public static bool TryMeasureShortestDistance(
        DrawingEntity first,
        DrawingEntity second,
        out MeasurementResult measurement)
    {
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(second);

        if (TryMeasureExactShortestDistance(first, second, out measurement))
        {
            return true;
        }

        var firstSegments = GetDistanceSegments(first).ToArray();
        var secondSegments = GetDistanceSegments(second).ToArray();
        if (firstSegments.Length == 0 || secondSegments.Length == 0)
        {
            measurement = default;
            return false;
        }

        var best = default(ClosestPointPair);
        var bestDistance = double.PositiveInfinity;
        foreach (var firstSegment in firstSegments)
        {
            foreach (var secondSegment in secondSegments)
            {
                var candidate = GetClosestPoints(firstSegment, secondSegment);
                var distance = Measure(candidate.First, candidate.Second).Distance;
                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
            }
        }

        measurement = Measure(best.First, best.Second);
        return double.IsFinite(bestDistance);
    }

    private static MeasurementResult MeasureArc(ArcEntity arc)
    {
        var sweep = GetPositiveSweepDegrees(arc.StartAngleDegrees, arc.EndAngleDegrees);
        var length = Math.Abs(arc.Radius) * sweep * Math.PI / 180.0;
        if (sweep >= 360.0 - GeometryTolerance)
        {
            return new MeasurementResult(Math.Abs(arc.Radius) * 2, Math.Abs(arc.Radius) * 2, length);
        }

        var start = PointOnCircle(arc.Center, arc.Radius, arc.StartAngleDegrees);
        var end = PointOnCircle(arc.Center, arc.Radius, arc.StartAngleDegrees + sweep);
        return new MeasurementResult(end.X - start.X, end.Y - start.Y, length);
    }

    private static MeasurementResult MeasureSampledPath(IReadOnlyList<Point2> points, bool closed)
    {
        if (points.Count == 0)
        {
            return default;
        }

        return MeasurePath(points, closed || IsClosedPath(points));
    }

    private static MeasurementResult MeasurePath(IReadOnlyList<Point2> points, bool closed)
    {
        if (points.Count == 0)
        {
            return default;
        }

        if (points.Count == 1)
        {
            return new MeasurementResult(points[0].X, points[0].Y, 0);
        }

        var length = 0d;
        for (var index = 1; index < points.Count; index++)
        {
            length += Measure(points[index - 1], points[index]).Distance;
        }

        if (closed)
        {
            if (!SamePoint(points[0], points[^1]))
            {
                length += Measure(points[^1], points[0]).Distance;
            }

            var bounds = Bounds2.FromPoints(points);
            return new MeasurementResult(bounds.Width, bounds.Height, length);
        }

        return new MeasurementResult(
            points[^1].X - points[0].X,
            points[^1].Y - points[0].Y,
            length);
    }

    private static bool IsClosedPath(IReadOnlyList<Point2> points) =>
        points.Count > 2 && SamePoint(points[0], points[^1]);

    private static bool SamePoint(Point2 first, Point2 second) =>
        Math.Abs(first.X - second.X) <= GeometryTolerance
        && Math.Abs(first.Y - second.Y) <= GeometryTolerance;

    private static Point2 PointOnCircle(Point2 center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point2(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }

    private static bool IsFullSweep(double startAngleDegrees, double endAngleDegrees) =>
        GetPositiveSweepDegrees(startAngleDegrees, endAngleDegrees) >= 360.0 - GeometryTolerance;

    private static double GetPositiveSweepDegrees(double startAngleDegrees, double endAngleDegrees)
    {
        var sweep = (endAngleDegrees - startAngleDegrees) % 360.0;
        if (sweep < 0)
        {
            sweep += 360.0;
        }

        return sweep <= GeometryTolerance ? 360.0 : sweep;
    }

    private static bool TryMeasureExactShortestDistance(
        DrawingEntity first,
        DrawingEntity second,
        out MeasurementResult measurement)
    {
        if (first is PointEntity firstPoint && second is PointEntity secondPoint)
        {
            measurement = Measure(firstPoint.Location, secondPoint.Location);
            return true;
        }

        if (first is PointEntity point && second is CircleEntity circle)
        {
            measurement = MeasurePointToCircle(point.Location, circle, invert: false);
            return true;
        }

        if (first is CircleEntity circleFirst && second is PointEntity pointSecond)
        {
            measurement = MeasurePointToCircle(pointSecond.Location, circleFirst, invert: true);
            return true;
        }

        if (first is CircleEntity firstCircle && second is CircleEntity secondCircle)
        {
            measurement = MeasureCircleToCircle(firstCircle, secondCircle);
            return true;
        }

        if (first is LineEntity firstLine && second is CircleEntity secondCircleForLine)
        {
            measurement = MeasureLineToCircle(firstLine.Start, firstLine.End, secondCircleForLine, invert: false);
            return true;
        }

        if (first is CircleEntity firstCircleForLine && second is LineEntity secondLine)
        {
            measurement = MeasureLineToCircle(secondLine.Start, secondLine.End, firstCircleForLine, invert: true);
            return true;
        }

        measurement = default;
        return false;
    }

    private static MeasurementResult MeasurePointToCircle(Point2 point, CircleEntity circle, bool invert)
    {
        var circlePoint = ClosestPointOnCircle(point, circle);
        return invert ? Measure(circlePoint, point) : Measure(point, circlePoint);
    }

    private static MeasurementResult MeasureCircleToCircle(CircleEntity first, CircleEntity second)
    {
        var dx = second.Center.X - first.Center.X;
        var dy = second.Center.Y - first.Center.Y;
        var centerDistance = Math.Sqrt((dx * dx) + (dy * dy));
        if (centerDistance <= GeometryTolerance)
        {
            var firstPoint = new Point2(first.Center.X + first.Radius, first.Center.Y);
            var secondPoint = new Point2(second.Center.X + second.Radius, second.Center.Y);
            return Measure(firstPoint, secondPoint);
        }

        var unitX = dx / centerDistance;
        var unitY = dy / centerDistance;
        var externalGap = centerDistance - first.Radius - second.Radius;
        if (externalGap >= 0)
        {
            return Measure(
                new Point2(first.Center.X + unitX * first.Radius, first.Center.Y + unitY * first.Radius),
                new Point2(second.Center.X - unitX * second.Radius, second.Center.Y - unitY * second.Radius));
        }

        var innerGap = Math.Abs(first.Radius - second.Radius) - centerDistance;
        if (innerGap >= 0)
        {
            var firstSign = first.Radius >= second.Radius ? 1.0 : -1.0;
            return Measure(
                new Point2(first.Center.X + unitX * first.Radius * firstSign, first.Center.Y + unitY * first.Radius * firstSign),
                new Point2(second.Center.X + unitX * second.Radius * firstSign, second.Center.Y + unitY * second.Radius * firstSign));
        }

        return default;
    }

    private static MeasurementResult MeasureLineToCircle(Point2 start, Point2 end, CircleEntity circle, bool invert)
    {
        var linePoint = ClosestPointOnSegment(circle.Center, new DistanceSegment(start, end));
        var centerDistance = Measure(circle.Center, linePoint).Distance;
        if (centerDistance <= circle.Radius + GeometryTolerance)
        {
            return default;
        }

        var circlePoint = ClosestPointOnCircle(linePoint, circle);
        return invert ? Measure(circlePoint, linePoint) : Measure(linePoint, circlePoint);
    }

    private static Point2 ClosestPointOnCircle(Point2 point, CircleEntity circle)
    {
        var dx = point.X - circle.Center.X;
        var dy = point.Y - circle.Center.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= GeometryTolerance)
        {
            return new Point2(circle.Center.X + circle.Radius, circle.Center.Y);
        }

        return new Point2(
            circle.Center.X + dx / length * circle.Radius,
            circle.Center.Y + dy / length * circle.Radius);
    }

    private static IEnumerable<DistanceSegment> GetDistanceSegments(DrawingEntity entity)
    {
        switch (entity)
        {
            case PointEntity point:
                yield return new DistanceSegment(point.Location, point.Location);
                break;
            case LineEntity line:
                yield return new DistanceSegment(line.Start, line.End);
                break;
            case PolylineEntity polyline:
                foreach (var segment in GetPathSegments(polyline.Vertices, closed: false))
                {
                    yield return segment;
                }

                break;
            case PolygonEntity polygon:
                foreach (var segment in GetPathSegments(polygon.GetVertices(), closed: true))
                {
                    yield return segment;
                }

                break;
            case CircleEntity circle:
                foreach (var segment in GetPathSegments(GetCircleSamplePoints(circle), closed: true))
                {
                    yield return segment;
                }

                break;
            case ArcEntity arc:
                foreach (var segment in GetPathSegments(arc.GetSamplePoints(64), closed: false))
                {
                    yield return segment;
                }

                break;
            case EllipseEntity ellipse:
                foreach (var segment in GetPathSegments(
                    ellipse.GetSamplePoints(),
                    IsFullSweep(ellipse.StartParameterDegrees, ellipse.EndParameterDegrees)))
                {
                    yield return segment;
                }

                break;
            case SplineEntity spline:
                foreach (var segment in GetPathSegments(spline.GetSamplePoints(), closed: false))
                {
                    yield return segment;
                }

                break;
        }
    }

    private static IEnumerable<DistanceSegment> GetPathSegments(IReadOnlyList<Point2> points, bool closed)
    {
        for (var index = 1; index < points.Count; index++)
        {
            yield return new DistanceSegment(points[index - 1], points[index]);
        }

        if (closed && points.Count > 1 && !SamePoint(points[0], points[^1]))
        {
            yield return new DistanceSegment(points[^1], points[0]);
        }
    }

    private static IReadOnlyList<Point2> GetCircleSamplePoints(CircleEntity circle)
    {
        const int sampleCount = 96;
        return Enumerable.Range(0, sampleCount)
            .Select(index => PointOnCircle(circle.Center, circle.Radius, index * 360.0 / sampleCount))
            .ToArray();
    }

    private static ClosestPointPair GetClosestPoints(DistanceSegment first, DistanceSegment second)
    {
        if (TryGetSegmentIntersection(first, second, out var intersection))
        {
            return new ClosestPointPair(intersection, intersection);
        }

        var candidates = new[]
        {
            new ClosestPointPair(first.Start, ClosestPointOnSegment(first.Start, second)),
            new ClosestPointPair(first.End, ClosestPointOnSegment(first.End, second)),
            new ClosestPointPair(ClosestPointOnSegment(second.Start, first), second.Start),
            new ClosestPointPair(ClosestPointOnSegment(second.End, first), second.End)
        };

        return candidates
            .OrderBy(candidate => Measure(candidate.First, candidate.Second).Distance)
            .First();
    }

    private static Point2 ClosestPointOnSegment(Point2 point, DistanceSegment segment)
    {
        var dx = segment.End.X - segment.Start.X;
        var dy = segment.End.Y - segment.Start.Y;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= GeometryTolerance)
        {
            return segment.Start;
        }

        var parameter = (((point.X - segment.Start.X) * dx) + ((point.Y - segment.Start.Y) * dy)) / lengthSquared;
        var clamped = Math.Clamp(parameter, 0, 1);
        return new Point2(segment.Start.X + dx * clamped, segment.Start.Y + dy * clamped);
    }

    private static bool TryGetSegmentIntersection(DistanceSegment first, DistanceSegment second, out Point2 point)
    {
        var r = new Point2(first.End.X - first.Start.X, first.End.Y - first.Start.Y);
        var s = new Point2(second.End.X - second.Start.X, second.End.Y - second.Start.Y);
        var denominator = Cross(r, s);
        var startDelta = new Point2(second.Start.X - first.Start.X, second.Start.Y - first.Start.Y);

        if (Math.Abs(denominator) <= GeometryTolerance)
        {
            if (Math.Abs(Cross(startDelta, r)) <= GeometryTolerance)
            {
                foreach (var candidate in new[] { first.Start, first.End, second.Start, second.End })
                {
                    if (IsPointOnSegment(candidate, first) && IsPointOnSegment(candidate, second))
                    {
                        point = candidate;
                        return true;
                    }
                }
            }

            point = default;
            return false;
        }

        var t = Cross(startDelta, s) / denominator;
        var u = Cross(startDelta, r) / denominator;
        if (t < -GeometryTolerance || t > 1 + GeometryTolerance || u < -GeometryTolerance || u > 1 + GeometryTolerance)
        {
            point = default;
            return false;
        }

        point = new Point2(first.Start.X + t * r.X, first.Start.Y + t * r.Y);
        return true;
    }

    private static bool IsPointOnSegment(Point2 point, DistanceSegment segment) =>
        Math.Abs(Cross(
            new Point2(segment.End.X - segment.Start.X, segment.End.Y - segment.Start.Y),
            new Point2(point.X - segment.Start.X, point.Y - segment.Start.Y))) <= GeometryTolerance
        && point.X >= Math.Min(segment.Start.X, segment.End.X) - GeometryTolerance
        && point.X <= Math.Max(segment.Start.X, segment.End.X) + GeometryTolerance
        && point.Y >= Math.Min(segment.Start.Y, segment.End.Y) - GeometryTolerance
        && point.Y <= Math.Max(segment.Start.Y, segment.End.Y) + GeometryTolerance;

    private static double Cross(Point2 first, Point2 second) =>
        (first.X * second.Y) - (first.Y * second.X);

    private readonly record struct DistanceSegment(Point2 Start, Point2 End);

    private readonly record struct ClosestPointPair(Point2 First, Point2 Second);
}

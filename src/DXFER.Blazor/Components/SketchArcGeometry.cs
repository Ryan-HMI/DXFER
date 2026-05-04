using DXFER.Core.Geometry;

namespace DXFER.Blazor.Components;

internal static class SketchArcGeometry
{
    private const double GeometryTolerance = 0.000001;
    private const double FullCircleDegrees = 360.0;

    public static (Point2 Center, double Radius)? GetThreePointCircle(Point2 first, Point2 second, Point2 third)
    {
        var determinant = 2 * (
            first.X * (second.Y - third.Y)
            + second.X * (third.Y - first.Y)
            + third.X * (first.Y - second.Y));
        if (Math.Abs(determinant) <= GeometryTolerance)
        {
            return null;
        }

        var firstSquared = (first.X * first.X) + (first.Y * first.Y);
        var secondSquared = (second.X * second.X) + (second.Y * second.Y);
        var thirdSquared = (third.X * third.X) + (third.Y * third.Y);
        var center = new Point2(
            (firstSquared * (second.Y - third.Y)
                + secondSquared * (third.Y - first.Y)
                + thirdSquared * (first.Y - second.Y)) / determinant,
            (firstSquared * (third.X - second.X)
                + secondSquared * (first.X - third.X)
                + thirdSquared * (second.X - first.X)) / determinant);
        var radius = Distance(center, first);
        return radius > GeometryTolerance ? (center, radius) : null;
    }

    public static (Point2 Center, double Radius, double StartAngleDegrees, double EndAngleDegrees)? GetThreePointArc(
        Point2 first,
        Point2 through,
        Point2 end)
    {
        var circle = GetThreePointCircle(first, through, end);
        if (circle is null)
        {
            return null;
        }

        var startAngle = GetPointAngleDegrees(circle.Value.Center, first);
        var throughAngle = GetPointAngleDegrees(circle.Value.Center, through);
        var endAngle = GetPointAngleDegrees(circle.Value.Center, end);
        var throughDelta = GetCounterClockwiseDeltaDegrees(startAngle, throughAngle);
        var endDelta = GetCounterClockwiseDeltaDegrees(startAngle, endAngle);

        if (throughDelta <= endDelta + GeometryTolerance)
        {
            return (circle.Value.Center, circle.Value.Radius, startAngle, startAngle + endDelta);
        }

        return (
            circle.Value.Center,
            circle.Value.Radius,
            endAngle,
            endAngle + GetCounterClockwiseDeltaDegrees(endAngle, startAngle));
    }

    public static (Point2 Center, double Radius, double StartAngleDegrees, double EndAngleDegrees)? GetCenterPointArc(
        Point2 center,
        Point2 startRadiusPoint,
        Point2 endAnglePoint)
    {
        var radius = Distance(center, startRadiusPoint);
        if (radius <= GeometryTolerance || Distance(center, endAnglePoint) <= GeometryTolerance)
        {
            return null;
        }

        var startAngle = GetPointAngleDegrees(center, startRadiusPoint);
        var endAngle = GetPointAngleDegrees(center, endAnglePoint);
        var sweep = GetShortestVisualSweep(startAngle, endAngle);
        return (center, radius, sweep.StartAngleDegrees, sweep.EndAngleDegrees);
    }

    public static (Point2 Center, double Radius, double StartAngleDegrees, double EndAngleDegrees)? GetTangentArc(
        Point2 start,
        Point2 tangentPoint,
        Point2 end)
    {
        var tangentLength = Distance(start, tangentPoint);
        var chordLength = Distance(start, end);
        if (tangentLength <= GeometryTolerance || chordLength <= GeometryTolerance)
        {
            return null;
        }

        var tangent = new Point2(
            (tangentPoint.X - start.X) / tangentLength,
            (tangentPoint.Y - start.Y) / tangentLength);
        var normal = new Point2(-tangent.Y, tangent.X);
        var chord = new Point2(start.X - end.X, start.Y - end.Y);
        var denominator = 2 * ((normal.X * chord.X) + (normal.Y * chord.Y));
        if (Math.Abs(denominator) <= GeometryTolerance)
        {
            return null;
        }

        var offset = -((chord.X * chord.X) + (chord.Y * chord.Y)) / denominator;
        var center = new Point2(
            start.X + normal.X * offset,
            start.Y + normal.Y * offset);
        var radius = Distance(center, start);
        if (radius <= GeometryTolerance)
        {
            return null;
        }

        var startAngle = GetPointAngleDegrees(center, start);
        var endAngle = GetPointAngleDegrees(center, end);
        var radiusVector = new Point2(start.X - center.X, start.Y - center.Y);
        var counterClockwiseTangent = new Point2(-radiusVector.Y / radius, radiusVector.X / radius);
        var tangentDot = (counterClockwiseTangent.X * tangent.X) + (counterClockwiseTangent.Y * tangent.Y);
        if (tangentDot >= 0)
        {
            return (center, radius, startAngle, startAngle + GetCounterClockwiseDeltaDegrees(startAngle, endAngle));
        }

        return (center, radius, endAngle, endAngle + GetCounterClockwiseDeltaDegrees(endAngle, startAngle));
    }

    private static double Distance(Point2 first, Point2 second) =>
        Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));

    private static double GetPointAngleDegrees(Point2 center, Point2 point)
    {
        var angle = Math.Atan2(point.Y - center.Y, point.X - center.X) * 180.0 / Math.PI;
        return angle < 0 ? angle + 360.0 : angle;
    }

    private static double GetCounterClockwiseDeltaDegrees(double startAngleDegrees, double angleDegrees)
    {
        var delta = (angleDegrees - startAngleDegrees) % FullCircleDegrees;
        return delta < 0 ? delta + FullCircleDegrees : delta;
    }

    private static (double StartAngleDegrees, double EndAngleDegrees) GetShortestVisualSweep(
        double startAngleDegrees,
        double endAngleDegrees)
    {
        var counterClockwiseDelta = GetCounterClockwiseDeltaDegrees(startAngleDegrees, endAngleDegrees);
        if (counterClockwiseDelta <= FullCircleDegrees / 2.0)
        {
            return (startAngleDegrees, startAngleDegrees + counterClockwiseDelta);
        }

        return (endAngleDegrees, endAngleDegrees + (FullCircleDegrees - counterClockwiseDelta));
    }
}

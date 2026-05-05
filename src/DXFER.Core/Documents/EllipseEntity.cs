using DXFER.Core.Geometry;

namespace DXFER.Core.Documents;

public sealed record EllipseEntity(
    EntityId Id,
    Point2 Center,
    Point2 MajorAxisEndPoint,
    double MinorRadiusRatio,
    double StartParameterDegrees = 0,
    double EndParameterDegrees = 360,
    bool IsConstruction = false) : DrawingEntity(Id, IsConstruction)
{
    private const int BoundsSampleSegments = 96;
    private const double GeometryTolerance = 0.000001;

    public override string Kind => "ellipse";

    public override Bounds2 GetBounds() => Bounds2.FromPoints(GetSamplePoints(BoundsSampleSegments));

    public override DrawingEntity Transform(Transform2 transform)
    {
        var transformedCenter = Center.Transform(transform);
        var transformedMajorEnd = new Point2(
            Center.X + MajorAxisEndPoint.X,
            Center.Y + MajorAxisEndPoint.Y).Transform(transform);

        return new EllipseEntity(
            Id,
            transformedCenter,
            new Point2(transformedMajorEnd.X - transformedCenter.X, transformedMajorEnd.Y - transformedCenter.Y),
            MinorRadiusRatio,
            StartParameterDegrees,
            EndParameterDegrees,
            IsConstruction);
    }

    public override DrawingEntity WithConstruction(bool isConstruction) =>
        this with { IsConstruction = isConstruction };

    public IReadOnlyList<Point2> GetSamplePoints(int segmentCount = BoundsSampleSegments)
    {
        var majorLength = Math.Sqrt((MajorAxisEndPoint.X * MajorAxisEndPoint.X) + (MajorAxisEndPoint.Y * MajorAxisEndPoint.Y));
        if (majorLength <= GeometryTolerance || MinorRadiusRatio <= GeometryTolerance)
        {
            return Array.Empty<Point2>();
        }

        var count = Math.Max(1, segmentCount);
        var sweep = GetPositiveSweepDegrees(StartParameterDegrees, EndParameterDegrees);
        var points = new Point2[count + 1];

        for (var index = 0; index <= count; index++)
        {
            var parameter = StartParameterDegrees + sweep * index / count;
            points[index] = PointAtParameterDegrees(parameter);
        }

        return points;
    }

    public Point2 PointAtParameterDegrees(double parameterDegrees)
    {
        var majorLength = Math.Sqrt((MajorAxisEndPoint.X * MajorAxisEndPoint.X) + (MajorAxisEndPoint.Y * MajorAxisEndPoint.Y));
        if (majorLength <= GeometryTolerance)
        {
            return Center;
        }

        var majorUnit = new Point2(MajorAxisEndPoint.X / majorLength, MajorAxisEndPoint.Y / majorLength);
        var minorUnit = new Point2(-majorUnit.Y, majorUnit.X);
        var minorLength = majorLength * MinorRadiusRatio;
        var radians = parameterDegrees * Math.PI / 180.0;

        return new Point2(
            Center.X + majorUnit.X * majorLength * Math.Cos(radians) + minorUnit.X * minorLength * Math.Sin(radians),
            Center.Y + majorUnit.Y * majorLength * Math.Cos(radians) + minorUnit.Y * minorLength * Math.Sin(radians));
    }

    private static double GetPositiveSweepDegrees(double startAngleDegrees, double endAngleDegrees)
    {
        var sweep = endAngleDegrees - startAngleDegrees;
        while (sweep < 0)
        {
            sweep += 360.0;
        }

        return sweep <= GeometryTolerance ? 360.0 : sweep;
    }
}

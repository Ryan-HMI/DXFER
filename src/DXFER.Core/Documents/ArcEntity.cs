using DXFER.Core.Geometry;

namespace DXFER.Core.Documents;

public sealed record ArcEntity(
    EntityId Id,
    Point2 Center,
    double Radius,
    double StartAngleDegrees,
    double EndAngleDegrees,
    bool IsConstruction = false) : DrawingEntity(Id, IsConstruction)
{
    private const int BoundsSampleSegments = 72;

    public override string Kind => "arc";

    public override Bounds2 GetBounds() => Bounds2.FromPoints(GetSamplePoints(BoundsSampleSegments));

    public override DrawingEntity Transform(Transform2 transform)
    {
        var angleOffset = transform.RotationDegreesComponent;

        return new ArcEntity(
            Id,
            Center.Transform(transform),
            Radius,
            StartAngleDegrees + angleOffset,
            EndAngleDegrees + angleOffset,
            IsConstruction);
    }

    public override DrawingEntity WithConstruction(bool isConstruction) =>
        this with { IsConstruction = isConstruction };

    public IReadOnlyList<Point2> GetSamplePoints(int segmentCount)
    {
        var count = Math.Max(1, segmentCount);
        var sweep = GetCounterClockwiseSweepDegrees(StartAngleDegrees, EndAngleDegrees);
        var points = new Point2[count + 1];

        for (var i = 0; i <= count; i++)
        {
            var angle = StartAngleDegrees + sweep * i / count;
            points[i] = PointAtDegrees(angle);
        }

        return points;
    }

    private Point2 PointAtDegrees(double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;

        return new Point2(
            Center.X + Radius * Math.Cos(radians),
            Center.Y + Radius * Math.Sin(radians));
    }

    private static double GetCounterClockwiseSweepDegrees(double startAngleDegrees, double endAngleDegrees)
    {
        var sweep = endAngleDegrees - startAngleDegrees;
        while (sweep < 0)
        {
            sweep += 360.0;
        }

        return sweep;
    }
}

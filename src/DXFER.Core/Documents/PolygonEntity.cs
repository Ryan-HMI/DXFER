using DXFER.Core.Geometry;

namespace DXFER.Core.Documents;

public sealed record PolygonEntity(
    EntityId Id,
    Point2 Center,
    double Radius,
    double RotationAngleDegrees,
    int SideCount,
    bool Circumscribed = false,
    bool IsConstruction = false) : DrawingEntity(Id, IsConstruction)
{
    public const int MinSideCount = 3;
    public const int MaxSideCount = 64;

    private const double GeometryTolerance = 0.000001;

    public override string Kind => "polygon";

    public int NormalizedSideCount => NormalizeSideCount(SideCount);

    public override Bounds2 GetBounds() => Bounds2.FromPoints(GetVertices());

    public override DrawingEntity Transform(Transform2 transform) =>
        FromCenterAndRadiusPoint(
            Id,
            Center.Transform(transform),
            GetRadiusPoint().Transform(transform),
            Circumscribed,
            NormalizedSideCount,
            IsConstruction);

    public override DrawingEntity WithConstruction(bool isConstruction) =>
        this with { IsConstruction = isConstruction, SideCount = NormalizedSideCount };

    public PolygonEntity WithRadius(double radius) =>
        this with { Radius = Math.Max(Math.Abs(radius), GeometryTolerance), SideCount = NormalizedSideCount };

    public PolygonEntity WithSideCount(int sideCount) =>
        this with { SideCount = NormalizeSideCount(sideCount) };

    public Point2 GetRadiusPoint() =>
        PointAtDegrees(Center, Math.Max(Math.Abs(Radius), GeometryTolerance), RotationAngleDegrees);

    public IReadOnlyList<Point2> GetVertices()
    {
        var sideCount = NormalizedSideCount;
        var guideRadius = Math.Max(Math.Abs(Radius), GeometryTolerance);
        var vertexRadius = guideRadius;
        var angle = RotationAngleDegrees * Math.PI / 180.0;
        if (Circumscribed)
        {
            vertexRadius /= Math.Cos(Math.PI / sideCount);
            angle += Math.PI / sideCount;
        }

        return Enumerable.Range(0, sideCount)
            .Select(index => new Point2(
                Center.X + Math.Cos(angle + index * Math.Tau / sideCount) * vertexRadius,
                Center.Y + Math.Sin(angle + index * Math.Tau / sideCount) * vertexRadius))
            .ToArray();
    }

    public static PolygonEntity FromCenterAndRadiusPoint(
        EntityId id,
        Point2 center,
        Point2 radiusPoint,
        bool circumscribed,
        int sideCount,
        bool isConstruction = false)
    {
        var deltaX = radiusPoint.X - center.X;
        var deltaY = radiusPoint.Y - center.Y;
        var radius = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        var angleDegrees = Math.Atan2(deltaY, deltaX) * 180.0 / Math.PI;
        return new PolygonEntity(
            id,
            center,
            Math.Max(radius, GeometryTolerance),
            angleDegrees,
            NormalizeSideCount(sideCount),
            circumscribed,
            isConstruction);
    }

    public static int NormalizeSideCount(double sideCount)
    {
        if (!double.IsFinite(sideCount))
        {
            return MinSideCount;
        }

        return NormalizeSideCount((int)Math.Round(sideCount));
    }

    public static int NormalizeSideCount(int sideCount) =>
        Math.Clamp(sideCount, MinSideCount, MaxSideCount);

    private static Point2 PointAtDegrees(Point2 center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point2(
            center.X + Math.Cos(radians) * radius,
            center.Y + Math.Sin(radians) * radius);
    }
}

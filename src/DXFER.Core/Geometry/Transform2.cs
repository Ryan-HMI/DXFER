namespace DXFER.Core.Geometry;

public readonly record struct Transform2(
    double M11,
    double M12,
    double M21,
    double M22,
    double OffsetX,
    double OffsetY)
{
    public static Transform2 Identity => new(1, 0, 0, 1, 0, 0);

    public static Transform2 RotationDegrees(double degrees)
    {
        var radians = degrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        return new Transform2(cos, -sin, sin, cos, 0, 0);
    }

    public static Transform2 Translation(double x, double y) => new(1, 0, 0, 1, x, y);

    public static Transform2 RotationDegreesAbout(double degrees, Point2 center)
    {
        var radians = degrees * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);

        return new Transform2(
            cos,
            -sin,
            sin,
            cos,
            center.X - center.X * cos + center.Y * sin,
            center.Y - center.X * sin - center.Y * cos);
    }

    public Point2 Apply(Point2 point) =>
        new(
            point.X * M11 + point.Y * M12 + OffsetX,
            point.X * M21 + point.Y * M22 + OffsetY);

    internal double RotationDegreesComponent => Math.Atan2(M21, M11) * 180.0 / Math.PI;
}

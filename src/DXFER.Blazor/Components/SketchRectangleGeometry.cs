using DXFER.Core.Geometry;

namespace DXFER.Blazor.Components;

internal static class SketchRectangleGeometry
{
    public static Point2[] GetCenterRectangleCorners(Point2 center, Point2 corner)
    {
        var opposite = new Point2((2 * center.X) - corner.X, (2 * center.Y) - corner.Y);
        var minX = Math.Min(opposite.X, corner.X);
        var maxX = Math.Max(opposite.X, corner.X);
        var minY = Math.Min(opposite.Y, corner.Y);
        var maxY = Math.Max(opposite.Y, corner.Y);

        return new[]
        {
            new Point2(minX, minY),
            new Point2(maxX, minY),
            new Point2(maxX, maxY),
            new Point2(minX, maxY)
        };
    }
}

namespace DXFER.Core.Geometry;

public readonly record struct Bounds2(double MinX, double MinY, double MaxX, double MaxY)
{
    public double Width => MaxX - MinX;

    public double Height => MaxY - MinY;

    public static Bounds2 Empty => new(0, 0, 0, 0);

    public static Bounds2 FromPoints(IEnumerable<Point2> points)
    {
        using var enumerator = points.GetEnumerator();
        if (!enumerator.MoveNext())
        {
            return Empty;
        }

        var minX = enumerator.Current.X;
        var minY = enumerator.Current.Y;
        var maxX = enumerator.Current.X;
        var maxY = enumerator.Current.Y;

        while (enumerator.MoveNext())
        {
            var point = enumerator.Current;
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        return new Bounds2(minX, minY, maxX, maxY);
    }

    public Bounds2 Union(Bounds2 other) =>
        new(
            Math.Min(MinX, other.MinX),
            Math.Min(MinY, other.MinY),
            Math.Max(MaxX, other.MaxX),
            Math.Max(MaxY, other.MaxY));
}

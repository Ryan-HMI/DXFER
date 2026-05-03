namespace DXFER.Core.Geometry;

public readonly record struct Point2(double X, double Y)
{
    public Point2 Transform(Transform2 transform) => transform.Apply(this);
}

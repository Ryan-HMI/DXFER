using DXFER.Core.Geometry;

namespace DXFER.Core.Documents;

public sealed record PolylineEntity : DrawingEntity
{
    public PolylineEntity(EntityId id, IEnumerable<Point2> vertices)
        : base(id)
    {
        var points = vertices.ToArray();
        if (points.Length < 2)
        {
            throw new ArgumentException("A polyline requires at least two vertices.", nameof(vertices));
        }

        Vertices = Array.AsReadOnly(points);
    }

    public override string Kind => "polyline";

    public IReadOnlyList<Point2> Vertices { get; }

    public override Bounds2 GetBounds() => Bounds2.FromPoints(Vertices);

    public override DrawingEntity Transform(Transform2 transform) =>
        new PolylineEntity(Id, Vertices.Select(point => point.Transform(transform)));
}

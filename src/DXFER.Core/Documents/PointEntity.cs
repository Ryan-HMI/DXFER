using DXFER.Core.Geometry;

namespace DXFER.Core.Documents;

public sealed record PointEntity(EntityId Id, Point2 Location) : DrawingEntity(Id)
{
    public override string Kind => "point";

    public override Bounds2 GetBounds() => Bounds2.FromPoints(new[] { Location });

    public override DrawingEntity Transform(Transform2 transform) =>
        new PointEntity(Id, Location.Transform(transform));
}

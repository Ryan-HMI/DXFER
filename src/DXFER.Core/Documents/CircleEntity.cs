using DXFER.Core.Geometry;

namespace DXFER.Core.Documents;

public sealed record CircleEntity(
    EntityId Id,
    Point2 Center,
    double Radius,
    bool IsConstruction = false) : DrawingEntity(Id, IsConstruction)
{
    public override string Kind => "circle";

    public override Bounds2 GetBounds() =>
        new(Center.X - Radius, Center.Y - Radius, Center.X + Radius, Center.Y + Radius);

    public override DrawingEntity Transform(Transform2 transform) =>
        new CircleEntity(Id, Center.Transform(transform), Radius, IsConstruction);

    public override DrawingEntity WithConstruction(bool isConstruction) =>
        this with { IsConstruction = isConstruction };
}

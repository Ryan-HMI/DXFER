using DXFER.Core.Geometry;

namespace DXFER.Core.Documents;

public sealed record LineEntity(
    EntityId Id,
    Point2 Start,
    Point2 End,
    bool IsConstruction = false) : DrawingEntity(Id, IsConstruction)
{
    public override string Kind => "line";

    public override Bounds2 GetBounds() => Bounds2.FromPoints(new[] { Start, End });

    public override DrawingEntity Transform(Transform2 transform) =>
        new LineEntity(Id, Start.Transform(transform), End.Transform(transform), IsConstruction);

    public override DrawingEntity WithConstruction(bool isConstruction) =>
        this with { IsConstruction = isConstruction };
}

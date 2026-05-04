using DXFER.Core.Geometry;

namespace DXFER.Core.Documents;

public abstract record DrawingEntity(EntityId Id, bool IsConstruction = false)
{
    public abstract string Kind { get; }

    public abstract Bounds2 GetBounds();

    public abstract DrawingEntity Transform(Transform2 transform);

    public abstract DrawingEntity WithConstruction(bool isConstruction);
}

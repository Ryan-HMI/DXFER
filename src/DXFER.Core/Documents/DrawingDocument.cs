using DXFER.Core.Geometry;

namespace DXFER.Core.Documents;

public sealed class DrawingDocument
{
    public DrawingDocument(IEnumerable<DrawingEntity> entities)
    {
        Entities = Array.AsReadOnly(entities.ToArray());
    }

    public IReadOnlyList<DrawingEntity> Entities { get; }

    public Bounds2 GetBounds()
    {
        if (Entities.Count == 0)
        {
            return Bounds2.Empty;
        }

        var bounds = Entities[0].GetBounds();
        for (var i = 1; i < Entities.Count; i++)
        {
            bounds = bounds.Union(Entities[i].GetBounds());
        }

        return bounds;
    }
}

using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Core.Documents;

public sealed class DrawingDocument
{
    public DrawingDocument(IEnumerable<DrawingEntity> entities)
        : this(entities, Array.Empty<SketchDimension>(), Array.Empty<SketchConstraint>())
    {
    }

    public DrawingDocument(
        IEnumerable<DrawingEntity> entities,
        IEnumerable<SketchDimension> dimensions,
        IEnumerable<SketchConstraint> constraints)
    {
        ArgumentNullException.ThrowIfNull(entities);
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(constraints);

        Entities = Array.AsReadOnly(entities.ToArray());
        Dimensions = Array.AsReadOnly(dimensions.ToArray());
        Constraints = Array.AsReadOnly(constraints.ToArray());
    }

    public IReadOnlyList<DrawingEntity> Entities { get; }

    public IReadOnlyList<SketchDimension> Dimensions { get; }

    public IReadOnlyList<SketchConstraint> Constraints { get; }

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

using System.Collections.ObjectModel;
using DXFER.Core.Documents;

namespace DXFER.Core.Sketching;

public sealed class SketchSolveRequest
{
    public SketchSolveRequest(
        DrawingDocument document,
        IEnumerable<SketchConstraint> constraints,
        IEnumerable<SketchDimension> dimensions)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(constraints);
        ArgumentNullException.ThrowIfNull(dimensions);

        Document = document;
        Constraints = Array.AsReadOnly(constraints.ToArray());
        Dimensions = Array.AsReadOnly(dimensions.ToArray());
    }

    public DrawingDocument Document { get; }

    public IReadOnlyList<DrawingEntity> Entities => Document.Entities;

    public ReadOnlyCollection<SketchConstraint> Constraints { get; }

    public ReadOnlyCollection<SketchDimension> Dimensions { get; }

    public static SketchSolveRequest FromDocument(DrawingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new SketchSolveRequest(document, document.Constraints, document.Dimensions);
    }
}

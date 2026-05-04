using System.Collections.ObjectModel;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

public sealed record SketchDimension
{
    public SketchDimension(
        string id,
        SketchDimensionKind kind,
        IEnumerable<string> referenceKeys,
        double value,
        Point2? anchor = null,
        bool isDriving = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(referenceKeys);

        Id = id;
        Kind = kind;
        ReferenceKeys = Array.AsReadOnly(referenceKeys.ToArray());
        Value = value;
        Anchor = anchor;
        IsDriving = isDriving;
    }

    public string Id { get; }

    public SketchDimensionKind Kind { get; }

    public ReadOnlyCollection<string> ReferenceKeys { get; }

    public double Value { get; }

    public Point2? Anchor { get; }

    public bool IsDriving { get; }
}

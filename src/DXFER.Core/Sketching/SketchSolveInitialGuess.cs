using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

public sealed record SketchSolveInitialGuess
{
    public SketchSolveInitialGuess(string referenceKey, Point2 point)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceKey);

        ReferenceKey = SketchReference.TryNormalize(referenceKey, out var normalized)
            ? normalized
            : referenceKey.Trim();
        Point = point;
    }

    public string ReferenceKey { get; }

    public Point2 Point { get; }
}

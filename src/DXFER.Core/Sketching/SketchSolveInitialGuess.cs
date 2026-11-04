using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

public sealed record SketchSolveInitialGuess
{
    public SketchSolveInitialGuess(string referenceKey, Point2 point)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(referenceKey);

        RawReferenceKey = referenceKey.Trim();
        ReferenceKey = SketchReference.TryNormalize(referenceKey, out var normalized)
            ? normalized
            : RawReferenceKey;
        Point = point;
    }

    public string ReferenceKey { get; }

    internal string RawReferenceKey { get; }

    public Point2 Point { get; }
}

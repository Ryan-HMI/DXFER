namespace DXFER.Core.Sketching;

internal sealed class SketchFixedReferences
{
    private readonly HashSet<string> fixedReferences;

    private SketchFixedReferences(HashSet<string> fixedReferences)
    {
        this.fixedReferences = fixedReferences;
    }

    public static SketchFixedReferences FromConstraints(IEnumerable<SketchConstraint> constraints)
    {
        var fixedReferences = new HashSet<string>(StringComparer.Ordinal);

        foreach (var constraint in constraints)
        {
            if (constraint.Kind != SketchConstraintKind.Fix
                || constraint.State == SketchConstraintState.Suppressed)
            {
                continue;
            }

            foreach (var key in constraint.ReferenceKeys)
            {
                if (SketchReference.TryNormalize(key, out var normalized))
                {
                    fixedReferences.Add(normalized);
                }
            }
        }

        return new SketchFixedReferences(fixedReferences);
    }

    public bool IsFixed(SketchReference reference)
    {
        if (fixedReferences.Contains(reference.ToString()))
        {
            return true;
        }

        return reference.Target != SketchReferenceTarget.Entity
            && fixedReferences.Contains(reference.EntityId);
    }

    public bool IsWholeEntityFixed(SketchReference reference) =>
        fixedReferences.Contains(reference.EntityId);

    public bool CanMovePoint(SketchReference reference) =>
        !IsFixed(reference);

    public bool CanMoveWholeLine(SketchReference reference) =>
        !IsWholeEntityFixed(reference)
        && !IsFixed(new SketchReference(reference.EntityId, SketchReferenceTarget.Start))
        && !IsFixed(new SketchReference(reference.EntityId, SketchReferenceTarget.End));

    public bool CanChangeLineEndpoint(SketchReference lineReference, SketchReferenceTarget endpoint) =>
        !IsWholeEntityFixed(lineReference)
        && !IsFixed(new SketchReference(lineReference.EntityId, endpoint));

    public bool CanMoveCircleLikeCenter(SketchReference reference) =>
        !IsWholeEntityFixed(reference)
        && !IsFixed(new SketchReference(reference.EntityId, SketchReferenceTarget.Center));

    public bool CanChangeCircleLikeRadius(SketchReference reference) =>
        !IsWholeEntityFixed(reference);
}

using DXFER.Core.Documents;

namespace DXFER.Core.Sketching;

internal sealed class SketchFixedReferences
{
    private readonly HashSet<string> fixedReferences;

    private SketchFixedReferences(HashSet<string> fixedReferences)
    {
        this.fixedReferences = fixedReferences;
    }

    public static SketchFixedReferences FromConstraints(IEnumerable<SketchConstraint> constraints, IReadOnlyList<DrawingEntity> entities)
    {
        var constraintList = constraints.ToArray();
        var fixedReferences = new HashSet<string>(StringComparer.Ordinal);

        foreach (var constraint in constraintList)
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

        foreach (var polyline in entities.OfType<PolylineEntity>().Where(p => p.Vertices.Count > 2 && p.Vertices[0] == p.Vertices[^1]))
        {
            var first = new SketchReference(polyline.Id.Value, SketchReferenceTarget.Start, 0).ToString();
            var lastIndex = polyline.Vertices.Count - 2;
            var last = new SketchReference(polyline.Id.Value, SketchReferenceTarget.End, lastIndex).ToString();
            if (fixedReferences.Contains(first) || fixedReferences.Contains(last)
                || fixedReferences.Contains(new SketchReference(polyline.Id.Value, SketchReferenceTarget.Entity, 0).ToString())
                || fixedReferences.Contains(new SketchReference(polyline.Id.Value, SketchReferenceTarget.Entity, lastIndex).ToString()))
            {
                fixedReferences.Add(first);
                fixedReferences.Add(last);
            }
        }
        var result = new SketchFixedReferences(fixedReferences);
        // Close satisfied point anchors without fixing the containing entity.
        // An anchored endpoint still permits rotation; an anchored center permits resizing.
        for (var pass = 0; pass < constraintList.Length; pass++)
        {
            var changed = false;
            foreach (var constraint in constraintList)
            {
                if (constraint.State != SketchConstraintState.Satisfied || constraint.ReferenceKeys.Count != 2
                    || constraint.Kind is not (SketchConstraintKind.Coincident or SketchConstraintKind.Concentric)
                    || !SketchReference.TryParse(constraint.ReferenceKeys[0], out var first)
                    || !SketchReference.TryParse(constraint.ReferenceKeys[1], out var second)) continue;
                if (constraint.Kind == SketchConstraintKind.Concentric)
                {
                    if (!IsCurveOrOrigin(first) || !IsCurveOrOrigin(second)) continue;
                    first = new SketchReference(first.EntityId, SketchReferenceTarget.Center);
                    second = new SketchReference(second.EntityId, SketchReferenceTarget.Center);
                }
                if (result.IsFixed(first) && IsPoint(second)) changed |= fixedReferences.Add(second.ToString());
                if (result.IsFixed(second) && IsPoint(first)) changed |= fixedReferences.Add(first.ToString());
            }
            if (!changed) break;
        }
        return result;

        bool IsCurveOrOrigin(SketchReference reference) => reference.IsOrigin
            || SketchGeometryEditor.TryGetCircleLike(entities, reference, out _, out _, out _);
        bool IsPoint(SketchReference reference) => !reference.IsOrigin
            && (!reference.IsEntity || entities.Any(entity => entity.Id.Value == reference.EntityId && entity is PointEntity))
            && SketchGeometryEditor.TryGetPoint(entities, reference, out _);
    }

    public bool IsFixed(SketchReference reference)
    {
        if (reference.IsOrigin || fixedReferences.Contains(reference.ToString()))
        {
            return true;
        }

        if (reference.Target != SketchReferenceTarget.Entity
            && reference.SegmentIndex.HasValue
            && fixedReferences.Contains(new SketchReference(
                reference.EntityId,
                SketchReferenceTarget.Entity,
                reference.SegmentIndex).ToString()))
        {
            return true;
        }

        return reference.Target != SketchReferenceTarget.Entity
            && fixedReferences.Contains(reference.EntityId);
    }

    public bool IsWholeEntityFixed(SketchReference reference) =>
        reference.EntityId == SketchReference.OriginEntityId
        || fixedReferences.Contains(reference.EntityId)
        || (reference.SegmentIndex.HasValue && fixedReferences.Contains(reference.ToString()));

    public bool CanMovePoint(SketchReference reference) =>
        !IsFixed(reference);

    public bool CanMoveWholeLine(SketchReference reference) =>
        !IsWholeEntityFixed(reference)
        && !IsFixed(new SketchReference(reference.EntityId, SketchReferenceTarget.Start, reference.SegmentIndex))
        && !IsFixed(new SketchReference(reference.EntityId, SketchReferenceTarget.End, reference.SegmentIndex));

    public bool CanChangeLineEndpoint(SketchReference lineReference, SketchReferenceTarget endpoint) =>
        !IsWholeEntityFixed(lineReference)
        && !IsFixed(new SketchReference(lineReference.EntityId, endpoint, lineReference.SegmentIndex));

    public bool CanMoveCircleLikeCenter(SketchReference reference) =>
        !IsWholeEntityFixed(reference)
        && !IsFixed(new SketchReference(reference.EntityId, SketchReferenceTarget.Center));

    public bool CanChangeCircleLikeRadius(SketchReference reference) =>
        !IsWholeEntityFixed(reference);
}

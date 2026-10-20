using System.Text.Json;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;

namespace DXFER.Core.Sketching;

public static class SketchDragSnapService
{
    public static DrawingDocument AddPersistentConstraint(DrawingDocument original, DrawingDocument accepted,
        string selectionKey, ref string status)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(accepted);
        if (!TryGetDraggedPointReference(accepted.Entities, selectionKey, out var draggedReference)
            || !SketchGeometryEditor.TryGetPoint(accepted.Entities, draggedReference, out var draggedPoint)
            || !double.IsFinite(draggedPoint.X) || !double.IsFinite(draggedPoint.Y)
            || !DrawingLayerService.CanEdit(accepted, draggedReference.EntityId))
            return accepted;

        var anchors = UnchangedVisibleAnchors(original, accepted, draggedReference.EntityId).ToArray();
        var kind = SketchConstraintKind.Coincident;
        if (TryFindCoincidentPointReference(anchors, draggedPoint, out var anchorReference))
        {
            if (HasCoincidentConstraint(accepted.Constraints, anchorReference, draggedReference)) return accepted;
        }
        else
        {
            kind = SketchConstraintKind.Midpoint;
            if (!TryFindMidpointReference(anchors, draggedPoint, out anchorReference)
                || HasPair(accepted.Constraints, kind, anchorReference, draggedReference))
                return accepted;
        }

        var constraint = new SketchConstraint($"drag-{kind.ToString().ToLowerInvariant()}-{Guid.NewGuid():N}",
            kind, new[] { anchorReference.ToString(), draggedReference.ToString() }, SketchConstraintState.Satisfied);
        if (SketchConstraintService.ValidateConstraint(accepted, constraint).State != SketchConstraintState.Satisfied)
            return accepted;
        // Accepted geometry is authoritative. This only persists an already satisfied
        // relation; it must never solve, propagate, or reposition either participant.
        status = $"{status} Added {kind.ToString().ToLowerInvariant()} constraint.";
        return new DrawingDocument(accepted.Entities, accepted.Dimensions, accepted.Constraints.Append(constraint), accepted.Metadata);
    }

    private static IEnumerable<DrawingEntity> UnchangedVisibleAnchors(DrawingDocument original,
        DrawingDocument accepted, string draggedEntityId)
    {
        var originals = original.Entities.ToDictionary(e => e.Id.Value, StringComparer.Ordinal);
        foreach (var entity in accepted.Entities)
        {
            if (entity.Id.Value == draggedEntityId || !originals.TryGetValue(entity.Id.Value, out var before)
                || DrawingLayerService.Layer(original, entity.Id.Value).IsHidden
                || DrawingLayerService.Layer(accepted, entity.Id.Value).IsHidden
                || before.GetType() != entity.GetType()) continue;
            if (ReferenceEquals(before, entity)
                || JsonSerializer.Serialize(before, before.GetType()) == JsonSerializer.Serialize(entity, entity.GetType()))
                yield return entity;
        }
    }

    private static bool TryGetDraggedPointReference(IReadOnlyList<DrawingEntity> entities,
        string selectionKey, out SketchReference reference)
    {
        reference = default;
        if (!SketchReference.TryParse(selectionKey, out var parsed)
            || parsed.IsOrigin || parsed.Target == SketchReferenceTarget.Midpoint
            || !SketchGeometryEditor.TryGetEntity(entities, parsed, out _, out var entity)
            || parsed.Target == SketchReferenceTarget.Entity && entity is not PointEntity
            || parsed.SegmentIndex is { } segment
                && (entity is not PolylineEntity polyline || segment < 0 || segment >= polyline.Vertices.Count - 1))
            return false;
        reference = parsed;
        return true;
    }

    private static bool TryFindMidpointReference(IReadOnlyList<DrawingEntity> anchors, Point2 point,
        out SketchReference reference)
    {
        foreach (var entity in anchors)
        {
            var count = entity is PolylineEntity polyline ? polyline.Vertices.Count - 1 : entity is LineEntity ? 1 : 0;
            for (var index = 0; index < count; index++)
            {
                var middle = new SketchReference(entity.Id.Value, SketchReferenceTarget.Midpoint, entity is PolylineEntity ? index : null);
                if (SketchGeometryEditor.TryGetPoint(anchors, middle, out var midpoint)
                    && SketchGeometryEditor.AreClose(point, midpoint))
                {
                    reference = new SketchReference(entity.Id.Value, SketchReferenceTarget.Entity, middle.SegmentIndex);
                    return true;
                }
            }
        }
        reference = default;
        return false;
    }

    private static bool TryFindCoincidentPointReference(IReadOnlyList<DrawingEntity> anchors, Point2 point,
        out SketchReference reference)
    {
        if (SketchGeometryEditor.AreClose(point, new Point2(0, 0)))
        {
            reference = SketchReference.Origin;
            return true;
        }
        foreach (var candidate in SketchReferenceResolver.EnumeratePersistentPoints(anchors))
        {
            if (!SketchGeometryEditor.AreClose(candidate.Point, point)) continue;
            reference = candidate.Reference;
            return true;
        }
        reference = default;
        return false;
    }

    private static bool HasPair(IReadOnlyList<SketchConstraint> constraints, SketchConstraintKind kind,
        SketchReference first, SketchReference second) =>
        constraints.Any(c => c.State != SketchConstraintState.Suppressed && c.Kind == kind && c.ReferenceKeys.Count == 2
            && SketchReference.TryParse(c.ReferenceKeys[0], out var a)
            && SketchReference.TryParse(c.ReferenceKeys[1], out var b)
            && (a == first && b == second || a == second && b == first));

    private static bool HasCoincidentConstraint(IReadOnlyList<SketchConstraint> constraints,
        SketchReference first, SketchReference second) =>
        HasPair(constraints, SketchConstraintKind.Coincident, first, second)
        || IsCenterLike(first) && IsCenterLike(second)
            && constraints.Any(c => c.State != SketchConstraintState.Suppressed && c.Kind == SketchConstraintKind.Concentric
                && c.ReferenceKeys.Count == 2 && SketchReference.TryParse(c.ReferenceKeys[0], out var a)
                && SketchReference.TryParse(c.ReferenceKeys[1], out var b) && IsCenterLike(a) && IsCenterLike(b)
                && (a.EntityId == first.EntityId && b.EntityId == second.EntityId
                    || a.EntityId == second.EntityId && b.EntityId == first.EntityId));

    private static bool IsCenterLike(SketchReference reference) =>
        reference.Target is SketchReferenceTarget.Entity or SketchReferenceTarget.Center;
}

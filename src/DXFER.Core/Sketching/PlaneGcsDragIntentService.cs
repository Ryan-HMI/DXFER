using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;

namespace DXFER.Core.Sketching;

internal static class PlaneGcsDragIntentService
{
    public static bool TryCreate(DrawingDocument document, string draggedKey, PlaneGcsConstraint intent,
        out SketchConstraint? constraint, out string reason)
    {
        constraint = null;
        reason = "Invalid PlaneGCS drag snap reference.";
        if (!Enum.TryParse<SketchConstraintKind>(intent.Kind, out var kind)
            || kind is not (SketchConstraintKind.Coincident or SketchConstraintKind.Midpoint)
            || intent.ReferenceKeys is not { Count: 2 }
            || !SketchReference.TryParse(draggedKey, out var dragged)
            || dragged.IsOrigin
            || !SketchGeometryEditor.TryGetPoint(document.Entities, dragged, out _)
            || !SketchReference.TryParse(intent.ReferenceKeys[0], out var first)
            || !SketchReference.TryParse(intent.ReferenceKeys[1], out var second)
            || first != dragged && second != dragged)
            return false;

        var target = first == dragged ? second : first;
        if (target.EntityId == dragged.EntityId
            || !target.IsOrigin && (!SketchGeometryEditor.TryGetEntity(document.Entities, target, out _, out _)
                || DrawingLayerService.Layer(document, target.EntityId).IsHidden))
            return false;
        if (kind == SketchConstraintKind.Coincident)
        {
            if (!SketchGeometryEditor.TryGetPoint(document.Entities, target, out _))
                return false;
        }
        else
        {
            if (target.Target == SketchReferenceTarget.Midpoint)
                target = new SketchReference(target.EntityId, SketchReferenceTarget.Entity, target.SegmentIndex);
            if (!target.IsEntity || !SketchGeometryEditor.TryGetLine(document.Entities, target, out _, out _))
                return false;
        }

        var references = new[] { dragged.ToString(), target.ToString() };
        var existing = document.Constraints.FirstOrDefault(c => c.Kind == kind
            && c.State != SketchConstraintState.Suppressed && c.ReferenceKeys.Count == 2
            && c.ReferenceKeys.Select(key => SketchReference.TryNormalize(key, out var normalized) ? normalized : key)
                .Order(StringComparer.Ordinal).SequenceEqual(references.Order(StringComparer.Ordinal)));
        if (existing is null)
            constraint = new SketchConstraint($"drag-snap-{Guid.NewGuid():N}", kind, references,
                SketchConstraintState.Satisfied);
        reason = string.Empty;
        return true;
    }
}

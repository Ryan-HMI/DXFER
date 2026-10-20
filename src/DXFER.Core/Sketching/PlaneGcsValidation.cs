using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;

namespace DXFER.Core.Sketching;

internal static class PlaneGcsValidation
{
    public static bool ValidateInput(DrawingDocument document, IReadOnlyList<string> fixedKeys,
        IReadOnlyList<SketchSolveInitialGuess> guesses, PlaneGcsDragRequest? drag, out string reason)
    {
        reason = string.Empty;
        if (document.Entities.Select(e => e.Id.Value).Distinct(StringComparer.Ordinal).Count() != document.Entities.Count)
            return Refuse("Duplicate entity IDs cannot be solved.", out reason);
        foreach (var key in fixedKeys)
            if (!TryReference(document, key, out _))
                return Refuse($"Unsupported or missing fixed reference '{key}'.", out reason);
        foreach (var constraint in document.Constraints.Where(c => c.State != SketchConstraintState.Suppressed))
        {
            if (!Enum.IsDefined(constraint.Kind) || constraint.ReferenceKeys.Count == 0
                || constraint.ReferenceKeys.Any(key => !TryReference(document, key, out _)))
                return Refuse($"Unsupported or missing reference in constraint '{constraint.Id}'.", out reason);
        }
        foreach (var dimension in document.Dimensions)
        {
            if (!double.IsFinite(dimension.Value) || dimension.Anchor is { } anchor && !Finite(anchor)
                || !Enum.IsDefined(dimension.Kind) || dimension.ReferenceKeys.Count == 0
                || dimension.ReferenceKeys.Any(key => !TryReference(document, key, out _)))
                return Refuse($"Invalid dimension '{dimension.Id}'.", out reason);
        }
        foreach (var guess in guesses)
            if (!Finite(guess.Point) || !TryReference(document, guess.ReferenceKey, out _))
                return Refuse("Invalid PlaneGCS initial guess.", out reason);
        if (drag is not null)
        {
            if (drag.Start is null || drag.End is null || !Finite(new(drag.Start.X, drag.Start.Y))
                || !Finite(new(drag.End.X, drag.End.Y)) || !TryDragEntity(document, drag.Key, out var entityId))
                return Refuse("Invalid PlaneGCS drag target.", out reason);
            if (!DrawingLayerService.CanEdit(document, entityId)
                || document.Metadata.Patterns.SelectMany(p => p.Members).Any(m => m.EntityId == entityId))
                return Refuse("Protected geometry or generated pattern members cannot be dragged independently.", out reason);
            if (drag.TemporarySnapIntent is not null)
                return Refuse("Temporary snap intent is not supported by the PlaneGCS adapter yet.", out reason);
        }
        return true;
    }

    public static bool ValidateResult(DrawingDocument original, DrawingDocument candidate, IReadOnlyList<string> fixedKeys,
        PlaneGcsDragRequest? drag, out string reason)
    {
        reason = string.Empty;
        foreach (var key in fixedKeys)
        {
            if (!TryReference(original, key, out var reference))
                return Refuse($"Unsupported fixed reference '{key}'.", out reason);
            if (reference.IsOrigin) continue;
            if (reference.IsEntity && !reference.SegmentIndex.HasValue)
            {
                if (!SketchGeometryEditor.TryGetEntity(original.Entities, reference, out _, out var before)
                    || !SketchGeometryEditor.TryGetEntity(candidate.Entities, reference, out _, out var after)
                    || !PlaneGcsDocumentMapper.SameGeometry(before, after))
                    return Refuse($"PlaneGCS would move fixed entity '{key}'.", out reason);
            }
            else if (reference.IsEntity)
            {
                if (!SketchGeometryEditor.TryGetLine(original.Entities, reference, out _, out var before)
                    || !SketchGeometryEditor.TryGetLine(candidate.Entities, reference, out _, out var after)
                    || !SketchGeometryEditor.AreClose(before.Start, after.Start)
                    || !SketchGeometryEditor.AreClose(before.End, after.End))
                    return Refuse($"PlaneGCS would move fixed segment '{key}'.", out reason);
            }
            else if (!SketchGeometryEditor.TryGetPoint(original.Entities, reference, out var beforePoint)
                || !SketchGeometryEditor.TryGetPoint(candidate.Entities, reference, out var afterPoint)
                || !SketchGeometryEditor.AreClose(beforePoint, afterPoint))
                return Refuse($"PlaneGCS would move fixed point '{key}'.", out reason);
        }
        if (!DrawingLayerService.PreservesProtectedGeometry(original, candidate))
            return Refuse("PlaneGCS would change hidden or locked geometry or intent.", out reason);
        foreach (var constraint in candidate.Constraints.Where(c => c.State != SketchConstraintState.Suppressed))
            if (SketchConstraintService.ValidateConstraint(candidate, constraint).State != SketchConstraintState.Satisfied)
                return Refuse($"PlaneGCS result does not satisfy constraint '{constraint.Id}'.", out reason);
        foreach (var dimension in candidate.Dimensions.Where(d => d.IsDriving))
            if (!DimensionResidualSatisfied(candidate, dimension))
                return Refuse($"PlaneGCS result does not satisfy driving dimension '{dimension.Id}'.", out reason);
        if (!DrawingPatternService.TryValidate(candidate, out var patternReason))
            return Refuse($"PlaneGCS pattern result is unsupported or invalid: {patternReason}", out reason);
        if (drag is not null && TryDragEntity(original, drag.Key, out var draggedId))
        {
            var before = original.Entities.Single(e => e.Id.Value == draggedId);
            var after = candidate.Entities.Single(e => e.Id.Value == draggedId);
            if (PlaneGcsDocumentMapper.SameGeometry(before, after))
                return Refuse("Selected geometry is constrained; PlaneGCS produced no drag movement.", out reason);
        }
        return true;
    }

    public static bool DimensionResidualSatisfied(DrawingDocument document, SketchDimension dimension)
    {
        if (!double.IsFinite(dimension.Value)
            || !SketchDimensionSolverService.TryGetDimensionMeasurement(document, dimension, out var measured)
            || !double.IsFinite(measured)) return false;
        if (!dimension.IsDriving) return true;
        // Measurements use document length units and degrees, not solver radians.
        // Keep the existing 0.001 residual contract without legacy redundancy tests.
        if (dimension.Kind == SketchDimensionKind.Count)
            return dimension.Value == Math.Truncate(dimension.Value) && measured == dimension.Value;
        return Math.Abs(measured - Math.Abs(dimension.Value)) <= 0.001;
    }

    private static bool TryReference(DrawingDocument document, string key, out SketchReference reference)
    {
        if (!SketchReference.TryParse(key, out reference)) return false;
        if (reference.IsOrigin) return true;
        if (!SketchGeometryEditor.TryGetEntity(document.Entities, reference, out _, out var entity)) return false;
        if (reference.SegmentIndex is { } segment
            && (entity is not PolylineEntity polyline || segment < 0 || segment >= polyline.Vertices.Count - 1)) return false;
        return reference.IsEntity || SketchGeometryEditor.TryGetPoint(document.Entities, reference, out _);
    }

    private static bool TryDragEntity(DrawingDocument document, string key, out string entityId)
    {
        entityId = string.Empty;
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (SketchReference.TryParseCanvasPointCoordinates(key, out var pointId, out _, out _))
            entityId = pointId;
        else if (TryReference(document, key, out var reference) && !reference.IsOrigin)
            entityId = reference.EntityId;
        var selectedId = entityId;
        return document.Entities.Any(e => e.Id.Value == selectedId);
    }

    private static bool Finite(Point2 point) => double.IsFinite(point.X) && double.IsFinite(point.Y);

    private static bool Refuse(string message, out string reason)
    {
        reason = message;
        return false;
    }
}

using System.Text.Json;
using System.Text.Json.Nodes;
using DXFER.Core.Documents;
using DXFER.Core.Operations;

namespace DXFER.Core.Sketching;

/// <summary>Validates pure geometry candidates against original intent. Never solves, remaps, or repairs.</summary>
public static class SketchDirectEditValidationService
{
    public static bool TryValidate(DrawingDocument original, DrawingDocument candidate,
        out string reason, bool topologyChange = false)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(candidate);
        reason = string.Empty;
        try
        {
            if (original.Metadata.Mode == DrawingDocumentMode.ReferenceOnly)
                return Refuse("Reference-only document cannot be edited.", out reason);
            if (!Unique(original) || !Unique(candidate))
                return Refuse("Duplicate geometry or intent IDs cannot be validated.", out reason);
            var before = original.Entities.ToDictionary(e => e.Id.Value, StringComparer.Ordinal);
            var after = candidate.Entities.ToDictionary(e => e.Id.Value, StringComparer.Ordinal);
            // Serialization also rejects non-finite numbers in every supported entity's stored geometry.
            var beforeGeometry = before.ToDictionary(p => p.Key, p => Geometry(p.Value), StringComparer.Ordinal);
            var afterGeometry = after.ToDictionary(p => p.Key, p => Geometry(p.Value), StringComparer.Ordinal);
            var changed = before.Keys.Union(after.Keys).Where(id =>
                !JsonNode.DeepEquals(beforeGeometry.GetValueOrDefault(id), afterGeometry.GetValueOrDefault(id)))
                .ToHashSet(StringComparer.Ordinal);

            if (!Same(original.Metadata, candidate.Metadata with { EntityStyles = original.Metadata.EntityStyles })
                || original.Metadata.EntityStyles.Any(p => !candidate.Metadata.EntityStyles.TryGetValue(p.Key, out var style)
                    || !Same(p.Value, style))
                || candidate.Metadata.EntityStyles.Keys.Except(original.Metadata.EntityStyles.Keys)
                    .Any(id => before.ContainsKey(id) || !after.ContainsKey(id)))
                return Refuse("Direct geometry edits must preserve existing metadata, layers, and styles.", out reason);
            if (!DrawingLayerService.PreservesProtectedGeometry(original, candidate))
                return Refuse("Direct edit would change hidden or locked geometry or intent.", out reason);
            if (original.Constraints.Count != candidate.Constraints.Count
                || original.Dimensions.Count != candidate.Dimensions.Count)
                return Refuse("Direct edit would add or discard sketch intent.", out reason);
            var constraints = candidate.Constraints.ToDictionary(c => c.Id, StringComparer.Ordinal);
            var dimensions = candidate.Dimensions.ToDictionary(d => d.Id, StringComparer.Ordinal);
            var protectedIds = before.Keys.Where(id => !DrawingLayerService.CanEdit(original, id)).ToHashSet(StringComparer.Ordinal);
            if (after.Keys.Except(before.Keys).Any(id => !DrawingLayerService.CanEdit(candidate, id)))
                return Refuse("Direct edit would add geometry to a hidden or locked layer.", out reason);
            foreach (var constraint in original.Constraints)
            {
                if (!constraints.TryGetValue(constraint.Id, out var next)
                    || constraint.Kind != next.Kind || constraint.State != next.State
                    || !SameReferences(constraint.ReferenceKeys, next.ReferenceKeys))
                    return Refuse($"Direct edit would change constraint '{constraint.Id}' or its reference lineage.", out reason);
                if (constraint.ReferenceKeys.Any(key => protectedIds.Contains(EntityIdForReference(key))) && !Same(constraint, next))
                    return Refuse($"Direct edit would change protected constraint '{constraint.Id}'.", out reason);
            }
            foreach (var dimension in original.Dimensions)
            {
                if (!dimensions.TryGetValue(dimension.Id, out var next)
                    || dimension.Kind != next.Kind || dimension.IsDriving != next.IsDriving
                    || dimension.IsDriving && dimension.Value != next.Value
                    || !SameReferences(dimension.ReferenceKeys, next.ReferenceKeys))
                    return Refuse($"Direct edit would change dimension '{dimension.Id}' or its reference lineage.", out reason);
                if (!double.IsFinite(next.Value) || next.Anchor is { } anchor
                    && (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y)))
                    return Refuse($"Dimension '{dimension.Id}' has a non-finite value or label position.", out reason);
                if (dimension.ReferenceKeys.Any(key => protectedIds.Contains(EntityIdForReference(key))) && !Same(dimension, next))
                    return Refuse($"Direct edit would change protected dimension '{dimension.Id}'.", out reason);
            }

            var referenceKeys = original.Constraints.SelectMany(c => c.ReferenceKeys)
                .Concat(original.Dimensions.SelectMany(d => d.ReferenceKeys)).ToArray();
            var referencedIds = referenceKeys.Select(EntityIdForReference).ToHashSet(StringComparer.Ordinal);
            if (topologyChange && changed.Overlaps(referencedIds))
                return Refuse("Topology reference lineage is not proven for affected sketch intent. Edit unreferenced geometry or use a supported solver operation.", out reason);
            foreach (var id in changed.Intersect(referencedIds))
            {
                if (!before.TryGetValue(id, out var oldEntity) || !after.TryGetValue(id, out var newEntity)
                    || oldEntity.GetType() != newEntity.GetType()
                    || referenceKeys.Any(key => EntityIdForReference(key) == id && !SketchReference.TryNormalize(key, out _))
                    || oldEntity is PolylineEntity oldPath && newEntity is PolylineEntity newPath
                        && oldPath.Vertices.Count != newPath.Vertices.Count
                    || oldEntity is PolygonEntity oldPolygon && newEntity is PolygonEntity newPolygon
                        && oldPolygon.GetVertices().Count != newPolygon.GetVertices().Count
                    || oldEntity is SplineEntity oldSpline && newEntity is SplineEntity newSpline
                        && (oldSpline.Degree != newSpline.Degree || oldSpline.ControlPoints.Count != newSpline.ControlPoints.Count
                            || oldSpline.FitPoints.Count != newSpline.FitPoints.Count))
                    return Refuse($"Reference lineage changed for '{id}'.", out reason);
            }

            foreach (var constraint in original.Constraints.Where(c => c.State != SketchConstraintState.Suppressed))
            {
                if (!constraint.ReferenceKeys.Any(key => changed.Contains(EntityIdForReference(key)))) continue;
                if (constraint.Kind == SketchConstraintKind.Fix)
                {
                    if (constraint.ReferenceKeys.Any(key => !FixedReferenceUnchanged(original, candidate, key, beforeGeometry, afterGeometry)))
                        return Refuse($"Direct edit would move original fixed reference in '{constraint.Id}'.", out reason);
                }
                else if (SketchConstraintService.ValidateConstraint(original, constraint).State != SketchConstraintState.Satisfied
                    || SketchConstraintService.ValidateConstraint(candidate, constraints[constraint.Id]).State != SketchConstraintState.Satisfied)
                    return Refuse($"Direct edit cannot preserve constraint '{constraint.Id}'. Existing failed intent must be resolved through the solver.", out reason);
            }
            foreach (var dimension in original.Dimensions.Where(d => d.IsDriving))
            {
                if (!dimension.ReferenceKeys.Any(key => changed.Contains(EntityIdForReference(key)))) continue;
                if (!DrivingSatisfied(original, dimension) || !DrivingSatisfied(candidate, dimensions[dimension.Id]))
                    return Refuse($"Direct edit would violate driving dimension '{dimension.Id}'.", out reason);
            }
            if (!DrawingPatternService.TryValidate(candidate, out var patternReason))
                return Refuse($"Direct edit cannot preserve pattern dependencies: {patternReason}", out reason);
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or JsonException or NotSupportedException)
        {
            return Refuse($"Direct edit could not be validated: {exception.Message}", out reason);
        }
    }

    private static bool Unique(DrawingDocument document) =>
        document.Entities.Select(e => e.Id.Value).Distinct(StringComparer.Ordinal).Count() == document.Entities.Count
        && document.Constraints.Select(c => c.Id).Distinct(StringComparer.Ordinal).Count() == document.Constraints.Count
        && document.Dimensions.Select(d => d.Id).Distinct(StringComparer.Ordinal).Count() == document.Dimensions.Count;

    private static JsonNode? Geometry(DrawingEntity entity) => JsonSerializer.SerializeToNode(entity, entity.GetType());
    private static bool Same(object first, object second) =>
        JsonNode.DeepEquals(JsonSerializer.SerializeToNode(first, first.GetType()), JsonSerializer.SerializeToNode(second, second.GetType()));

    private static string EntityIdForReference(string key) =>
        SketchReference.TryParse(key, out var reference) ? reference.EntityId
            : SketchReference.TryParseCanvasPointCoordinates(key, out var entityId, out _, out _) ? entityId : key;

    private static bool SameReferences(IReadOnlyList<string> first, IReadOnlyList<string> second) =>
        first.Count == second.Count && first.Zip(second).All(pair =>
            pair.First == pair.Second || SketchReference.TryNormalize(pair.First, out var a)
            && SketchReference.TryNormalize(pair.Second, out var b) && a == b);

    private static bool DrivingSatisfied(DrawingDocument document, SketchDimension dimension) =>
        double.IsFinite(dimension.Value)
        && SketchDimensionSolverService.TryGetDimensionMeasurement(document, dimension, out var measured)
        && double.IsFinite(measured)
        && (dimension.Kind == SketchDimensionKind.Count
            ? dimension.Value == Math.Truncate(dimension.Value) && measured == dimension.Value
            : Math.Abs(measured - Math.Abs(dimension.Value)) <= 0.001);

    private static bool FixedReferenceUnchanged(DrawingDocument original, DrawingDocument candidate, string key,
        IReadOnlyDictionary<string, JsonNode?> before, IReadOnlyDictionary<string, JsonNode?> after)
    {
        if (!SketchReference.TryParse(key, out var reference)) return false;
        if (reference.IsOrigin) return true;
        if (reference.IsEntity && !reference.SegmentIndex.HasValue)
            return before.TryGetValue(reference.EntityId, out var first)
                && after.TryGetValue(reference.EntityId, out var second) && JsonNode.DeepEquals(first, second);
        if (reference.IsEntity)
            return SketchGeometryEditor.TryGetLine(original.Entities, reference, out _, out var oldLine)
                && SketchGeometryEditor.TryGetLine(candidate.Entities, reference, out _, out var newLine)
                && SketchGeometryEditor.AreClose(oldLine.Start, newLine.Start) && SketchGeometryEditor.AreClose(oldLine.End, newLine.End);
        return SketchGeometryEditor.TryGetPoint(original.Entities, reference, out var oldPoint)
            && SketchGeometryEditor.TryGetPoint(candidate.Entities, reference, out var newPoint)
            && SketchGeometryEditor.AreClose(oldPoint, newPoint);
    }

    private static bool Refuse(string message, out string reason)
    {
        reason = $"{message} Geometry unchanged.";
        return false;
    }
}

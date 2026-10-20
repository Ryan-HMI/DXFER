using DXFER.Core.Documents;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;

namespace DXFER.Blazor.Components;

public partial class DrawingWorkbench
{
    private bool TryStagePlaneGcsPolygonCount(SketchDimension dimension, out DrawingDocument candidate)
    {
        candidate = _document;
        if (_document.Metadata.Mode == DrawingDocumentMode.ReferenceOnly
            || dimension.ReferenceKeys.Count != 1
            || !SketchReference.TryParse(dimension.ReferenceKeys[0], out var target)
            || !target.IsEntity || target.SegmentIndex.HasValue
            || _document.Entities.FirstOrDefault(e => e.Id.Value == target.EntityId) is not PolygonEntity polygon
            || !DrawingLayerService.CanEdit(_document, target.EntityId))
            return RefusePolygonCount("Count needs an editable whole polygon.");

        var count = PolygonEntity.NormalizeSideCount(dimension.Value);
        if (polygon.NormalizedSideCount == count) return true;
        if (_document.Metadata.Patterns.Any(pattern => pattern.SourceEntityIds.Contains(target.EntityId)
            || pattern.Members.Any(member => member.EntityId == target.EntityId)))
            return RefusePolygonCount("Polygon Count cannot change linked pattern topology.");

        foreach (var constraint in _document.Constraints)
        {
            foreach (var key in constraint.ReferenceKeys)
            {
                if (!ReferencesPolygon(key, target.EntityId)) continue;
                if (!StablePolygonReference(key))
                    return RefusePolygonCount("Polygon Count cannot remap attached vertex or edge references.");
                if (constraint.Kind == SketchConstraintKind.Fix && constraint.State != SketchConstraintState.Suppressed
                    && SketchReference.TryParse(key, out var fixedReference) && fixedReference.IsEntity)
                    return RefusePolygonCount("Polygon Count cannot change a fixed polygon.");
            }
        }
        foreach (var other in _document.Dimensions.Where(d => d.Id != dimension.Id))
        {
            if (other.ReferenceKeys.Any(key => ReferencesPolygon(key, target.EntityId) && !StablePolygonReference(key)))
                return RefusePolygonCount("Polygon Count cannot remap attached dimension references.");
        }

        // Count is discrete generation, not a legacy constraint solve. PlaneGCS validates
        // the staged full snapshot and all remaining intent before the normal commit.
        candidate = new DrawingDocument(
            _document.Entities.Select(e => e.Id == polygon.Id ? polygon.WithSideCount(count) : e),
            _document.Dimensions, _document.Constraints, _document.Metadata);
        return true;
    }

    private static bool ReferencesPolygon(string key, string id) =>
        SketchReference.TryParse(key, out var reference) && reference.EntityId == id
        || key.StartsWith(id + ":", StringComparison.Ordinal)
        || key.StartsWith(id + "|", StringComparison.Ordinal);

    private static bool StablePolygonReference(string key) =>
        SketchReference.TryParse(key, out var reference) && !reference.SegmentIndex.HasValue
        && (reference.IsEntity || reference.Target == SketchReferenceTarget.Center);

    private bool RefusePolygonCount(string reason)
    {
        ReportSolveFailure($"{reason} Geometry unchanged.");
        return false;
    }
}

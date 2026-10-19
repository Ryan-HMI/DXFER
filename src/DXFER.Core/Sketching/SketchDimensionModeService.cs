using DXFER.Core.Documents;
using DXFER.Core.Operations;

namespace DXFER.Core.Sketching;

public static class SketchDimensionModeService
{
    public static bool TrySetDriving(DrawingDocument document, string id, bool driving,
        out DrawingDocument result, out string reason)
    {
        result = document;
        reason = "Dimension is unavailable or belongs to protected geometry.";
        var dimension = document.Dimensions.FirstOrDefault(d => d.Id == id);
        if (dimension is null || document.Metadata.Mode == DrawingDocumentMode.ReferenceOnly
            || dimension.ReferenceKeys.Any(k => SketchReference.TryParse(k, out var r)
                && !r.IsOrigin && !DrawingLayerService.CanEdit(document, r.EntityId))) return false;
        if (dimension.IsDriving == driving) { reason = "Dimension mode unchanged."; return true; }
        if (!SketchDimensionSolverService.TryGetDimensionMeasurement(document, dimension, out var value)
            || !double.IsFinite(value)) { reason = "Dimension cannot be measured. Mode unchanged."; return false; }
        var changed = new SketchDimension(dimension.Id, dimension.Kind, dimension.ReferenceKeys, value, dimension.Anchor, driving);
        var candidate = new DrawingDocument(document.Entities,
            document.Dimensions.Select(d => d.Id == id ? changed : d), document.Constraints, document.Metadata);
        // A mode toggle captures the current measurement; it must never solve by moving geometry.
        if (driving && !SketchDimensionSolverService.IsDimensionSatisfied(candidate, changed))
        {
            reason = "Driving dimension is redundant, conflicting or unsupported. Kept Driven; geometry unchanged.";
            return false;
        }
        result = candidate;
        reason = driving ? "Dimension set to Driving." : "Dimension set to Driven.";
        return true;
    }

    public static DrawingDocument RefreshMeasurements(DrawingDocument document)
    {
        var changed = false;
        var dimensions = document.Dimensions.Select(d =>
        {
            if (d.IsDriving || !SketchDimensionSolverService.TryGetDimensionMeasurement(document, d, out var value)
                || !double.IsFinite(value) || value == d.Value) return d;
            changed = true;
            return new SketchDimension(d.Id, d.Kind, d.ReferenceKeys, value, d.Anchor, false);
        }).ToArray();
        return changed ? new(document.Entities, dimensions, document.Constraints, document.Metadata) : document;
    }
}

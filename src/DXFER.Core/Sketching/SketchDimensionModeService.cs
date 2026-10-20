using DXFER.Core.Documents;
using DXFER.Core.Operations;

namespace DXFER.Core.Sketching;

public static class SketchDimensionModeService
{
    public static bool TrySetRadialKind(DrawingDocument document, string id, SketchDimensionKind kind,
        out DrawingDocument result, out string reason)
    {
        ArgumentNullException.ThrowIfNull(document);
        result = document;
        reason = "Select a Radius or Diameter dimension on a circle, arc or polygon.";
        var matches = document.Dimensions.Where(d => d.Id == id).Take(2).ToArray();
        if (matches.Length != 1 || kind is not (SketchDimensionKind.Radius or SketchDimensionKind.Diameter)) return false;
        var dimension = matches[0];
        if (dimension.Kind is not (SketchDimensionKind.Radius or SketchDimensionKind.Diameter)
            || dimension.ReferenceKeys.Count != 1
            || !SketchReference.TryParse(dimension.ReferenceKeys[0], out var reference)
            || !reference.IsEntity || reference.SegmentIndex.HasValue) return false;

        if (!SketchDimensionSolverService.TryGetDimensionMeasurement(document, dimension, out var measurement)
            || !double.IsFinite(measurement) || measurement <= 0) return false;
        if (document.Metadata.Mode == DrawingDocumentMode.ReferenceOnly
            || !DrawingLayerService.CanEdit(document, reference.EntityId))
        {
            reason = "Dimension belongs to protected geometry.";
            return false;
        }
        if (!double.IsFinite(dimension.Value) || dimension.Value <= 0)
        {
            reason = "Dimension value is invalid. Radial kind unchanged.";
            return false;
        }
        if (dimension.Kind == kind) { reason = "Radial dimension kind unchanged."; return true; }

        // Convert driving intent, including failed intent; never solve or replace it with measured geometry.
        var value = (dimension.IsDriving ? dimension.Value : measurement)
            * (kind == SketchDimensionKind.Diameter ? 2.0 : 0.5);
        if (!double.IsFinite(value) || value <= 0)
        {
            reason = "Converted dimension value is invalid. Radial kind unchanged.";
            return false;
        }
        var changed = new SketchDimension(dimension.Id, kind, dimension.ReferenceKeys, value,
            dimension.Anchor, dimension.IsDriving);
        result = new DrawingDocument(document.Entities,
            document.Dimensions.Select(d => d.Id == id ? changed : d), document.Constraints, document.Metadata);
        reason = kind == SketchDimensionKind.Diameter ? "Dimension set to Diameter." : "Dimension set to Radius.";
        return true;
    }

    public static bool TrySetDriving(DrawingDocument document, string id, bool driving,
        out DrawingDocument result, out string reason, ISketchSolver? solver = null)
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
        if (solver is not null)
        {
            // Keep the original snapshot as the baseline so promotion is a new
            // driving intent and receives the native rank check immediately.
            var solved = solver.Solve(new SketchSolveRequest(document, [], [changed]));
            if (solved.Status is not (SketchSolveStatus.Solved or SketchSolveStatus.UnderConstrained))
            {
                reason = $"{string.Join(" ", solved.Diagnostics)} Dimension mode unchanged; geometry unchanged.";
                return false;
            }
            result = solved.Document;
            reason = driving ? "Dimension set to Driving." : "Dimension set to Driven.";
            return true;
        }
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
        return changed ? new(document.Entities, dimensions, document.Constraints, document.Metadata, document.SolveEvidence) : document;
    }
}

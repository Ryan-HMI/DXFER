using DXFER.Core.Operations;

namespace DXFER.CadIO;

public sealed record DxfCleanupResult(string? CleanedDxf, int RemovedCount,
    decimal? CutLengthInches, string? CutLengthReviewReason,
    int? ClosedContourCount, string? ContourReviewReason,
    double BoundingWidth, double BoundingHeight, string? ReviewReason);

public static class DxfCleanupAnalysis
{
    public static DxfCleanupResult Analyze(string source)
    {
        var original = ManufacturingUnits.AssumeInches(DxfDocumentReader.Read(source));
        var bounds = original.GetBounds();
        DxfCleanupResult Reject(string reason) => new(null, 0, null, reason, null, reason, bounds.Width, bounds.Height, reason);
        if (original.Entities.Count == 0) return Reject("No supported geometry.");
        if (original.Entities.Select(e => e.Id).Distinct().Count() != original.Entities.Count)
            return Reject("Duplicate entity identifiers require manual review.");
        var cleanup = ExactDuplicateGeometryService.Remove(original);
        var cut = CutPathLengthService.Calculate(cleanup.Document);
        var contours = ClosedContourService.Calculate(cleanup.Document);
        if (cut.CutLengthReviewReason != null) return Reject(cut.CutLengthReviewReason);
        string? cleaned = null;
        if (cleanup.RemovedCount > 0)
        {
            var kept = cleanup.Document.Entities.Select(e => e.Id).ToHashSet();
            var removed = original.Entities.Where(e => !kept.Contains(e.Id))
                .Select(e => e.Id.Value[(e.Id.Value.IndexOf('-') + 1)..]).ToHashSet(StringComparer.Ordinal);
            try { cleaned = DxfDocumentReader.RemoveEntitiesByHandle(source, removed); }
            catch (InvalidOperationException ex) { return Reject(ex.Message); }
            var roundTrip = ManufacturingUnits.AssumeInches(DxfDocumentReader.Read(cleaned));
            if (roundTrip.GetBounds() != bounds || roundTrip.Entities.Count != cleanup.Document.Entities.Count
                || CutPathLengthService.Calculate(roundTrip) != cut || ClosedContourService.Calculate(roundTrip) != contours)
                return Reject("Cleanup round-trip geometry did not match; manual review required.");
        }
        return new(cleaned, cleanup.RemovedCount, cut.CutLengthInches, cut.CutLengthReviewReason,
            contours.ClosedContourCount, contours.ContourReviewReason, bounds.Width, bounds.Height, null);
    }
}

using DXFER.Core.Documents;

namespace DXFER.Core.Sync;

public sealed record SyncSavePackage(
    string ArtifactId,
    string JobId,
    string EditToken,
    string NormalizedDxfFileName,
    string NormalizedDxfContent,
    double BoundingWidth,
    double BoundingHeight,
    double RotationDegrees,
    double OriginShiftX,
    double OriginShiftY,
    GrainDirectionOption GrainDirection,
    bool ManualOverride,
    decimal? CutLengthInches = null,
    string? CutLengthReviewReason = null,
    DrawingUnits Units = DrawingUnits.Unspecified,
    int? ClosedContourCount = null,
    string? ContourReviewReason = null);

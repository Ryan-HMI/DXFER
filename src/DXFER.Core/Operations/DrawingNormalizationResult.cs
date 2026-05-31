using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Operations;

public sealed record DrawingNormalizationResult(
    DrawingDocument OriginalDocument,
    DrawingDocument RotatedDocument,
    DrawingDocument NormalizedDocument,
    Bounds2 OriginalBounds,
    Bounds2 RotatedBounds,
    Bounds2 NormalizedBounds,
    double RotationDegrees,
    double OriginShiftX,
    double OriginShiftY,
    bool ManualOverride);

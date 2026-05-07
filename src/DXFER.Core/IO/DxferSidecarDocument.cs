namespace DXFER.Core.IO;

public sealed record DxferSidecarDocument(
    int SchemaVersion,
    DxferSidecarFileReference Source,
    DxferSidecarFileReference Normalized,
    string Units,
    string Mode,
    bool TrustedSource,
    DxferSidecarBounds Bounds,
    DxferSidecarNormalizationSummary Normalization,
    DxferSidecarGrainMetadata Grain,
    IReadOnlyList<DxferSidecarWarning> Warnings,
    IReadOnlyDictionary<string, int> UnsupportedEntityCounts);

public sealed record DxferSidecarFileReference(
    string? FileName,
    string? Sha256);

public sealed record DxferSidecarBounds(
    double MinX,
    double MinY,
    double MaxX,
    double MaxY,
    double Width,
    double Height,
    double Diagonal);

public sealed record DxferSidecarNormalizationSummary(
    int EntityCount,
    int ExportedEntityCount,
    int ConstructionEntityCount,
    int DimensionCount,
    int ConstraintCount);

public sealed record DxferSidecarGrainMetadata
{
    public string? Source { get; init; }

    public double? DirectionDegrees { get; init; }
}

public sealed record DxferSidecarWarning(
    string Code,
    string Severity,
    string Message);

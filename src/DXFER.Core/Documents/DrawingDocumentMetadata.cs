namespace DXFER.Core.Documents;

public sealed record DrawingDocumentMetadata
{
    public static DrawingDocumentMetadata Empty { get; } = new();

    public string? SourceFileName { get; init; }

    public string? SourceSha256 { get; init; }

    public string? NormalizedFileName { get; init; }

    public string? NormalizedSha256 { get; init; }

    public DrawingUnits Units { get; init; } = DrawingUnits.Unspecified;

    public DrawingDocumentMode Mode { get; init; } = DrawingDocumentMode.Editable;

    public bool TrustedSource { get; init; }

    public IReadOnlyList<DrawingDocumentWarning> Warnings { get; init; } = Array.Empty<DrawingDocumentWarning>();

    public IReadOnlyDictionary<string, int> UnsupportedEntityCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
}

public enum DrawingUnits
{
    Unspecified,
    Inches,
    Millimeters
}

public enum DrawingDocumentMode
{
    Editable,
    ReferenceOnly
}

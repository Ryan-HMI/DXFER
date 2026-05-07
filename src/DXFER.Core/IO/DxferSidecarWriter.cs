using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DXFER.Core.Documents;

namespace DXFER.Core.IO;

public static class DxferSidecarWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static DxferSidecarDocument Create(
        DrawingDocument document,
        string? sourceContent = null,
        string? normalizedContent = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var metadata = document.Metadata;
        var bounds = document.GetBounds();
        var constructionEntityCount = document.Entities.Count(entity => entity.IsConstruction);
        var unsupportedEntityCounts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (entityKind, count) in metadata.UnsupportedEntityCounts)
        {
            unsupportedEntityCounts[entityKind] = count;
        }

        return new DxferSidecarDocument(
            SchemaVersion: 1,
            Source: new DxferSidecarFileReference(
                metadata.SourceFileName,
                ResolveSha256(sourceContent, metadata.SourceSha256)),
            Normalized: new DxferSidecarFileReference(
                metadata.NormalizedFileName,
                ResolveSha256(normalizedContent, metadata.NormalizedSha256)),
            Units: FormatUnits(metadata.Units),
            Mode: FormatMode(metadata.Mode),
            TrustedSource: metadata.TrustedSource,
            Bounds: new DxferSidecarBounds(
                bounds.MinX,
                bounds.MinY,
                bounds.MaxX,
                bounds.MaxY,
                bounds.Width,
                bounds.Height,
                Math.Sqrt((bounds.Width * bounds.Width) + (bounds.Height * bounds.Height))),
            Normalization: new DxferSidecarNormalizationSummary(
                document.Entities.Count,
                document.Entities.Count - constructionEntityCount,
                constructionEntityCount,
                document.Dimensions.Count,
                document.Constraints.Count),
            Grain: new DxferSidecarGrainMetadata(),
            Warnings: metadata.Warnings
                .Select(warning => new DxferSidecarWarning(
                    warning.Code,
                    FormatSeverity(warning.Severity),
                    warning.Message))
                .ToArray(),
            UnsupportedEntityCounts: unsupportedEntityCounts);
    }

    public static string Write(
        DrawingDocument document,
        string? sourceContent = null,
        string? normalizedContent = null) =>
        JsonSerializer.Serialize(Create(document, sourceContent, normalizedContent), JsonOptions)
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string? ResolveSha256(string? content, string? fallbackHash)
    {
        if (content is null)
        {
            return fallbackHash;
        }

        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return string.Concat(hash.Select(part => part.ToString("x2", CultureInfo.InvariantCulture)));
    }

    private static string FormatUnits(DrawingUnits units) =>
        units switch
        {
            DrawingUnits.Inches => "inches",
            DrawingUnits.Millimeters => "millimeters",
            _ => "unspecified"
        };

    private static string FormatMode(DrawingDocumentMode mode) =>
        mode switch
        {
            DrawingDocumentMode.ReferenceOnly => "referenceOnly",
            _ => "editable"
        };

    private static string FormatSeverity(DrawingDocumentWarningSeverity severity) =>
        severity switch
        {
            DrawingDocumentWarningSeverity.Error => "error",
            DrawingDocumentWarningSeverity.Warning => "warning",
            _ => "info"
        };
}

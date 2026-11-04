using System.Globalization;
using System.Text;
using DXFER.Core.Documents;
using DXFER.Core.Operations;
using HMISync.Web.Infrastructure;
using HMISync.Web.Production;

namespace DXFER.CadIO;

public sealed record ProductionCleanupReport(
    int SmallCirclesRemoved, int SmallCirclesProtected, int TearSlotsCreated,
    int ExistingTearSlots, int CleanupLayersRemoved, int ExactDuplicatesRemoved);

public sealed record ProductionPreparedDxf(
    DrawingDocument Document, string PreparedText, ProductionCleanupReport Cleanup);

public sealed class ProductionDxfPreparationException(string message) : InvalidOperationException(message);

/// <summary>One manufacturing import policy for the API, local files and Sync launches.</summary>
public static class ProductionDxfPreparation
{
    public static ProductionPreparedDxf Prepare(string sourceText, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceText);
        var bytes = Encoding.UTF8.GetBytes(sourceText);
        try
        {
            var pairs = DxfCompatibility.ReadPairs(bytes, cancellationToken);
            ValidateUnitsAndIntent(pairs);
            var workingBytes = ProductionDxfMetadata.PrepareWorkingCopy(bytes, pairs, cancellationToken);
            // Validate before deletion: removing a tiny invalid circle must not hide
            // nonplanar/ambiguous geometry and turn a damaged source into a success.
            RefuseUnsafeImport(DxfDocumentReader.Read(sourceText));
            var circles = DxfSmallCircleFilter.Apply(workingBytes, cancellationToken);
            if (circles.ProtectedCount > 0)
                throw new ProductionDxfPreparationException("Production cleanup requires review: small circles have retained DXF references. Original file unchanged.");
            var layers = BatchDxfLayers.Prepare(circles.Bytes, Array.Empty<string>(), cancellationToken);
            var prepared = BatchDxfCutLayers.Consolidate(layers.Bytes, cancellationToken);
            var text = Encoding.UTF8.GetString(prepared);
            var document = DxfDocumentReader.Read(text);
            if (document.Entities.Count == 0)
                throw new ProductionDxfPreparationException("Production cleanup refused: no supported DXF geometry. Original file unchanged.");
            RefuseUnsafeImport(document);
            if (document.Metadata.UnsupportedEntityCounts.Count > 0)
                throw new ProductionDxfPreparationException("Production cleanup requires review: unsupported, nonplanar, paper-space or incomplete geometry cannot be exported safely. Original file unchanged.");
            if (!ReferenceEquals(bytes, workingBytes))
                document = new DrawingDocument(document.Entities, document.Dimensions, document.Constraints,
                    document.Metadata with { Warnings = document.Metadata.Warnings.Append(new DrawingDocumentWarning(
                        "production-metadata-removed", DrawingDocumentWarningSeverity.Info,
                        "Nongeometric source identifiers were omitted from the production working copy. Original file unchanged.")).ToArray() });
            var duplicates = ExactDuplicateGeometryService.Remove(ManufacturingUnits.AssumeInches(document));
            return new(duplicates.Document, text, new(circles.RemovedCount, circles.ProtectedCount,
                layers.TearSlotsCreated, layers.ExistingTearSlots, layers.Layers.Count(layer => layer.Removed),
                duplicates.RemovedCount));
        }
        catch (InvalidDataException)
        {
            // Raw file content can include private URLs/metadata; do not echo parser text.
            throw new ProductionDxfPreparationException("Production cleanup requires review: incomplete DXF, unsupported records or ambiguous louver geometry. Original file unchanged.");
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or OverflowException ||
            ex is InvalidOperationException and not ProductionDxfPreparationException)
        {
            throw new ProductionDxfPreparationException("Production cleanup requires review: invalid or ambiguous DXF geometry. Original file unchanged.");
        }
    }

    private static void RefuseUnsafeImport(DrawingDocument document)
    {
        if (document.Metadata.Warnings.Any(warning => warning.Code == CutPathLengthService.UnsafeImportCode))
            throw new ProductionDxfPreparationException("Production cleanup requires review: unsafe or incomplete source geometry. Original file unchanged.");
    }

    private static void ValidateUnitsAndIntent(IReadOnlyList<DxfCompatibility.Pair> pairs)
    {
        if (pairs.Any(pair => pair.Value.Contains("DXFER_NATIVE_V1", StringComparison.Ordinal)))
            throw new ProductionDxfPreparationException("This drawing contains native DXFER constraints. Use the editing workspace; production cleanup will not discard its design intent.");

        var declarations = pairs.Select((pair, index) => (pair, index))
            .Where(item => item.pair.Code == 9 && item.pair.Value == "$INSUNITS").ToArray();
        if (declarations.Length > 1 || declarations.Any(item =>
                item.index + 1 >= pairs.Count || pairs[item.index + 1].Code != 70 ||
                !int.TryParse(pairs[item.index + 1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var units) ||
                units is not (0 or 1)))
            throw new ProductionDxfPreparationException("Production cleanup requires inch coordinates (or an unspecified inch source). Explicit non-inch or ambiguous units require review; no scaling was guessed.");
    }
}

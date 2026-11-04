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
            var circles = DxfSmallCircleFilter.Apply(bytes, cancellationToken);
            if (circles.ProtectedCount > 0)
                throw new ProductionDxfPreparationException("Production cleanup requires review: small circles have retained DXF references. Original file unchanged.");
            var layers = BatchDxfLayers.Prepare(circles.Bytes, Array.Empty<string>(), cancellationToken);
            var prepared = BatchDxfCutLayers.Consolidate(layers.Bytes, cancellationToken);
            var text = Encoding.UTF8.GetString(prepared);
            var document = DxfDocumentReader.Read(text);
            if (document.Entities.Count == 0)
                throw new ProductionDxfPreparationException("Production cleanup refused: no supported DXF geometry. Original file unchanged.");
            if (document.Metadata.UnsupportedEntityCounts.Count > 0 ||
                document.Metadata.Warnings.Any(warning => warning.Code == CutPathLengthService.UnsafeImportCode))
                throw new ProductionDxfPreparationException("Production cleanup requires review: unsupported, nonplanar, paper-space or incomplete geometry cannot be exported safely. Original file unchanged.");
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
    }

    private static void ValidateUnitsAndIntent(IReadOnlyList<DxfCompatibility.Pair> pairs)
    {
        if (pairs.Any(pair => pair.Value.Contains("DXFER_NATIVE_V1", StringComparison.Ordinal)))
            throw new ProductionDxfPreparationException("This drawing contains native DXFER constraints. Use the editing workspace; production cleanup will not discard its design intent.");

        // The prep writer preserves geometry, not DXF reactors or application data.
        // Reject associations before parsing strips the evidence needed to protect them.
        var entityHandles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var section = "";
        for (var index = 0; index < pairs.Count; index++)
        {
            var pair = pairs[index];
            if (pair.Code == 0 && pair.Value == "SECTION" && index + 1 < pairs.Count)
                section = pairs[index + 1].Value;
            else if (pair.Code == 0 && pair.Value == "ENDSEC") section = "";
            else if (section == "ENTITIES" && pair.Code == 5) entityHandles.Add(pair.Value);
        }
        if (pairs.Any(pair => pair.Code is 102 or >= 1000 ||
                (pair.Code is >= 320 and <= 369 && pair.Code != 330 || pair.Code is >= 390 and <= 399)
                && entityHandles.Contains(pair.Value)))
            throw new ProductionDxfPreparationException("Production cleanup requires review: associated DXF records or application data cannot be discarded by the preparation workspace. Original file unchanged.");

        var declarations = pairs.Select((pair, index) => (pair, index))
            .Where(item => item.pair.Code == 9 && item.pair.Value == "$INSUNITS").ToArray();
        if (declarations.Length > 1 || declarations.Any(item =>
                item.index + 1 >= pairs.Count || pairs[item.index + 1].Code != 70 ||
                !int.TryParse(pairs[item.index + 1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var units) ||
                units is not (0 or 1)))
            throw new ProductionDxfPreparationException("Production cleanup requires inch coordinates (or an unspecified inch source). Explicit non-inch or ambiguous units require review; no scaling was guessed.");
    }
}

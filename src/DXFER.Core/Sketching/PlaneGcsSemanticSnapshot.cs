using System.Security.Cryptography;
using System.Text.Json;
using DXFER.Core.Documents;
using DXFER.Core.Operations;

namespace DXFER.Core.Sketching;

internal static class PlaneGcsSemanticSnapshot
{
    public static string Document(DrawingDocument document) => Hash(new
    {
        Entities = document.Entities.OrderBy(e => e.Id.Value, StringComparer.Ordinal).Select(PlaneGcsDocumentMapper.Entity),
        Constraints = document.Constraints.OrderBy(c => c.Id, StringComparer.Ordinal).Select(ConstraintValue),
        Dimensions = document.Dimensions.OrderBy(d => d.Id, StringComparer.Ordinal).Select(DimensionValue),
        document.Metadata.Units,
        document.Metadata.Mode,
        Patterns = document.Metadata.Patterns.OrderBy(p => p.Id, StringComparer.Ordinal),
        Layers = DrawingLayerService.GetLayers(document).OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new { Name = p.Key, p.Value.IsHidden, p.Value.IsLocked }),
        EntityLayers = document.Entities.OrderBy(e => e.Id.Value, StringComparer.Ordinal)
            .Select(e => new { Id = e.Id.Value, Layer = DrawingLayerService.LayerName(document, e.Id.Value) })
    });

    public static string Intent(SketchDimension dimension) => Hash(DimensionValue(dimension));
    public static string Intent(SketchConstraint constraint) => Hash(ConstraintValue(constraint));

    private static object DimensionValue(SketchDimension dimension) =>
        new { dimension.Id, dimension.Kind, dimension.ReferenceKeys,
            Value = dimension.IsDriving ? (double?)dimension.Value : null, dimension.IsDriving };

    private static object ConstraintValue(SketchConstraint constraint) =>
        new { constraint.Id, constraint.Kind, constraint.ReferenceKeys, Suppressed = constraint.State == SketchConstraintState.Suppressed };

    private static string Hash(object value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, JsonSerializer.SerializeToElement(value));
        return Convert.ToHexString(SHA256.HashData(stream.ToArray()));
    }

    // Exclude display anchors/status and provenance from the semantic input.
    // Normalize numeric signed zero and dictionary order across save/reopen.
    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                var number = value.GetDouble();
                writer.WriteNumberValue(number == 0 ? 0 : number);
                break;
            default:
                value.WriteTo(writer);
                break;
        }
    }
}

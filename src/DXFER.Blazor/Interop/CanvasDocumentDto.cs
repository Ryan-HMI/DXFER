using System.Text.Json.Serialization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Blazor.Interop;

public sealed record CanvasDocumentDto(
    [property: JsonPropertyName("entities")] IReadOnlyList<CanvasEntityDto> Entities,
    [property: JsonPropertyName("bounds")] CanvasBoundsDto Bounds)
{
    public static CanvasDocumentDto Empty { get; } = new(
        Array.Empty<CanvasEntityDto>(),
        new CanvasBoundsDto(0, 0, 0, 0));

    public static CanvasDocumentDto FromDocument(DrawingDocument document)
    {
        var bounds = document.GetBounds();
        var entities = document.Entities.Select(FromEntity).ToArray();

        return new CanvasDocumentDto(entities, FromBounds(bounds));
    }

    private static CanvasEntityDto FromEntity(DrawingEntity entity)
    {
        var id = entity.Id.Value;
        var kind = entity.Kind.ToLowerInvariant();

        return entity switch
        {
            LineEntity line => new CanvasEntityDto(
                id,
                kind,
                new[] { FromPoint(line.Start), FromPoint(line.End) },
                null,
                null,
                null,
                null),
            CircleEntity circle => new CanvasEntityDto(
                id,
                kind,
                Array.Empty<CanvasPointDto>(),
                FromPoint(circle.Center),
                circle.Radius,
                null,
                null),
            ArcEntity arc => new CanvasEntityDto(
                id,
                kind,
                Array.Empty<CanvasPointDto>(),
                FromPoint(arc.Center),
                arc.Radius,
                arc.StartAngleDegrees,
                arc.EndAngleDegrees),
            PolylineEntity polyline => new CanvasEntityDto(
                id,
                kind,
                polyline.Vertices.Select(FromPoint).ToArray(),
                null,
                null,
                null,
                null),
            SplineEntity spline => new CanvasEntityDto(
                id,
                kind,
                spline.GetSamplePoints().Select(FromPoint).ToArray(),
                null,
                null,
                null,
                null),
            _ => new CanvasEntityDto(
                id,
                kind,
                Array.Empty<CanvasPointDto>(),
                null,
                null,
                null,
                null)
        };
    }

    private static CanvasPointDto FromPoint(Point2 point) => new(point.X, point.Y);

    private static CanvasBoundsDto FromBounds(Bounds2 bounds) =>
        new(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);
}

public sealed record CanvasEntityDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("points")] IReadOnlyList<CanvasPointDto> Points,
    [property: JsonPropertyName("center")] CanvasPointDto? Center,
    [property: JsonPropertyName("radius")] double? Radius,
    [property: JsonPropertyName("startAngleDegrees")] double? StartAngleDegrees,
    [property: JsonPropertyName("endAngleDegrees")] double? EndAngleDegrees);

public sealed record CanvasPointDto(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y);

public sealed record CanvasBoundsDto(
    [property: JsonPropertyName("minX")] double MinX,
    [property: JsonPropertyName("minY")] double MinY,
    [property: JsonPropertyName("maxX")] double MaxX,
    [property: JsonPropertyName("maxY")] double MaxY);

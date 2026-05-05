using System.Text.Json.Serialization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Blazor.Interop;

public sealed record CanvasDocumentDto(
    [property: JsonPropertyName("entities")] IReadOnlyList<CanvasEntityDto> Entities,
    [property: JsonPropertyName("bounds")] CanvasBoundsDto Bounds,
    [property: JsonPropertyName("dimensions")] IReadOnlyList<CanvasSketchDimensionDto> Dimensions,
    [property: JsonPropertyName("constraints")] IReadOnlyList<CanvasSketchConstraintDto> Constraints)
{
    public static CanvasDocumentDto Empty { get; } = new(
        Array.Empty<CanvasEntityDto>(),
        new CanvasBoundsDto(0, 0, 0, 0),
        Array.Empty<CanvasSketchDimensionDto>(),
        Array.Empty<CanvasSketchConstraintDto>());

    public static CanvasDocumentDto FromDocument(DrawingDocument document)
    {
        var bounds = document.GetBounds();
        var entities = document.Entities.Select(FromEntity).ToArray();
        var dimensions = document.Dimensions.Select(FromDimension).ToArray();
        var constraints = document.Constraints.Select(FromConstraint).ToArray();

        return new CanvasDocumentDto(entities, FromBounds(bounds), dimensions, constraints);
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
                null,
                line.IsConstruction),
            CircleEntity circle => new CanvasEntityDto(
                id,
                kind,
                Array.Empty<CanvasPointDto>(),
                FromPoint(circle.Center),
                circle.Radius,
                null,
                null,
                circle.IsConstruction),
            ArcEntity arc => new CanvasEntityDto(
                id,
                kind,
                Array.Empty<CanvasPointDto>(),
                FromPoint(arc.Center),
                arc.Radius,
                arc.StartAngleDegrees,
                arc.EndAngleDegrees,
                arc.IsConstruction),
            EllipseEntity ellipse => new CanvasEntityDto(
                id,
                kind,
                Array.Empty<CanvasPointDto>(),
                FromPoint(ellipse.Center),
                null,
                ellipse.StartParameterDegrees,
                ellipse.EndParameterDegrees,
                ellipse.IsConstruction,
                FromPoint(ellipse.MajorAxisEndPoint),
                ellipse.MinorRadiusRatio),
            PointEntity point => new CanvasEntityDto(
                id,
                kind,
                new[] { FromPoint(point.Location) },
                null,
                null,
                null,
                null,
                point.IsConstruction),
            PolylineEntity polyline => new CanvasEntityDto(
                id,
                kind,
                polyline.Vertices.Select(FromPoint).ToArray(),
                null,
                null,
                null,
                null,
                polyline.IsConstruction),
            PolygonEntity polygon => new CanvasEntityDto(
                id,
                kind,
                polygon.GetVertices().Select(FromPoint).ToArray(),
                FromPoint(polygon.Center),
                polygon.Radius,
                null,
                null,
                polygon.IsConstruction,
                null,
                null,
                polygon.RotationAngleDegrees,
                polygon.NormalizedSideCount,
                polygon.Circumscribed),
            SplineEntity spline => new CanvasEntityDto(
                id,
                kind,
                spline.GetSamplePoints().Select(FromPoint).ToArray(),
                null,
                null,
                null,
                null,
                spline.IsConstruction),
            _ => new CanvasEntityDto(
                id,
                kind,
                Array.Empty<CanvasPointDto>(),
                null,
                null,
                null,
                null,
                entity.IsConstruction)
        };
    }

    private static CanvasPointDto FromPoint(Point2 point) => new(point.X, point.Y);

    private static CanvasBoundsDto FromBounds(Bounds2 bounds) =>
        new(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);

    private static CanvasSketchDimensionDto FromDimension(SketchDimension dimension) =>
        new(
            dimension.Id,
            dimension.Kind.ToString(),
            dimension.ReferenceKeys,
            dimension.Value,
            dimension.Anchor is null ? null : FromPoint(dimension.Anchor.Value),
            dimension.IsDriving);

    private static CanvasSketchConstraintDto FromConstraint(SketchConstraint constraint) =>
        new(
            constraint.Id,
            constraint.Kind.ToString(),
            constraint.ReferenceKeys,
            constraint.State.ToString());
}

public sealed record CanvasEntityDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("points")] IReadOnlyList<CanvasPointDto> Points,
    [property: JsonPropertyName("center")] CanvasPointDto? Center,
    [property: JsonPropertyName("radius")] double? Radius,
    [property: JsonPropertyName("startAngleDegrees")] double? StartAngleDegrees,
    [property: JsonPropertyName("endAngleDegrees")] double? EndAngleDegrees,
    [property: JsonPropertyName("isConstruction")] bool IsConstruction,
    [property: JsonPropertyName("majorAxisEndPoint")] CanvasPointDto? MajorAxisEndPoint = null,
    [property: JsonPropertyName("minorRadiusRatio")] double? MinorRadiusRatio = null,
    [property: JsonPropertyName("rotationAngleDegrees")] double? RotationAngleDegrees = null,
    [property: JsonPropertyName("sideCount")] int? SideCount = null,
    [property: JsonPropertyName("circumscribed")] bool? Circumscribed = null);

public sealed record CanvasPointDto(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y);

public sealed record CanvasBoundsDto(
    [property: JsonPropertyName("minX")] double MinX,
    [property: JsonPropertyName("minY")] double MinY,
    [property: JsonPropertyName("maxX")] double MaxX,
    [property: JsonPropertyName("maxY")] double MaxY);

public sealed record CanvasSketchDimensionDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("referenceKeys")] IReadOnlyList<string> ReferenceKeys,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("anchor")] CanvasPointDto? Anchor,
    [property: JsonPropertyName("isDriving")] bool IsDriving);

public sealed record CanvasSketchConstraintDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("referenceKeys")] IReadOnlyList<string> ReferenceKeys,
    [property: JsonPropertyName("state")] string State);

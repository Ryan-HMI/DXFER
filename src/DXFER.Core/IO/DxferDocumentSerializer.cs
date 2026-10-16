using System.Text.Json;
using System.Text.Json.Serialization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Core.IO;

/// <summary>Lossless native document IO, separate from geometry-only DXF and analysis sidecars.</summary>
public static class DxferDocumentSerializer
{
    public const string Format = "dxfer-document";
    public const int SchemaVersion = 1;
    public const string FileExtension = ".dxfer.json";
    public const int MaxTextLength = 32 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        MaxDepth = 32,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false), new PointConverter() }
    };

    public static string Write(DrawingDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        Validate(document);
        var data = new DocumentData
        {
            Format = Format,
            SchemaVersion = SchemaVersion,
            Entities = document.Entities.Select(EntityData.FromEntity).ToArray(),
            Dimensions = document.Dimensions.Select(d => new DimensionData
            {
                Id = d.Id,
                Kind = d.Kind,
                ReferenceKeys = d.ReferenceKeys.ToArray(),
                Value = d.Value,
                Anchor = d.Anchor,
                IsDriving = d.IsDriving
            }).ToArray(),
            Constraints = document.Constraints.Select(c => new ConstraintData
            {
                Id = c.Id,
                Kind = c.Kind,
                ReferenceKeys = c.ReferenceKeys.ToArray(),
                State = c.State
            }).ToArray(),
            Metadata = document.Metadata
        };
        var text = JsonSerializer.Serialize(data, Options);
        Require(text.Length <= MaxTextLength, "Native document exceeds the size limit.");
        return text;
    }

    /// <summary>Returns a complete snapshot or throws FormatException; never repairs or solves intent.</summary>
    public static DrawingDocument Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Require(text.Length <= MaxTextLength, "Native document exceeds the size limit.");
        try
        {
            using var json = JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 32 });
            ValidateJson(json.RootElement);
            Require(json.RootElement.ValueKind == JsonValueKind.Object
                && json.RootElement.TryGetProperty("format", out var format)
                && format.ValueKind == JsonValueKind.String && format.GetString() == Format,
                "Not a native DXFER document. Analysis sidecars do not contain editable geometry or constraints.");
            Require(json.RootElement.TryGetProperty("schemaVersion", out var version)
                && version.TryGetInt32(out var number) && number == SchemaVersion,
                "Unsupported native DXFER document version.");
            var data = json.RootElement.Deserialize<DocumentData>(Options)!;
            Require(data.Entities is not null && data.Dimensions is not null
                && data.Constraints is not null && data.Metadata is not null,
                "Native document collections and metadata are required.");
            Require(data.Entities!.All(e => e is not null)
                && data.Dimensions!.All(d => d is not null) && data.Constraints!.All(c => c is not null),
                "Native document contains a null item.");
            var document = new DrawingDocument(
                data.Entities!.Select(e => e.ToEntity()),
                data.Dimensions!.Select(d => new SketchDimension(d.Id, d.Kind, d.ReferenceKeys, d.Value, d.Anchor, d.IsDriving)),
                data.Constraints!.Select(c => new SketchConstraint(c.Id, c.Kind, c.ReferenceKeys, c.State)),
                data.Metadata! with { TrustedSource = false });
            Validate(document);
            return document;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or OverflowException)
        {
            throw new FormatException("Invalid native DXFER document: " + ex.Message, ex);
        }
    }

    private static void ValidateJson(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                Require(names.Add(property.Name), "Duplicate JSON property: " + property.Name);
                ValidateJson(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) ValidateJson(item);
        else if (element.ValueKind == JsonValueKind.Number)
            Require(element.TryGetDouble(out var number) && double.IsFinite(number), "Non-finite numeric value.");
    }

    private static void Validate(DrawingDocument document)
    {
        UniqueIds(document.Entities.Select(e => e.Id.Value), "entity");
        UniqueIds(document.Dimensions.Select(d => d.Id), "dimension");
        UniqueIds(document.Constraints.Select(c => c.Id), "constraint");
        foreach (var entity in document.Entities) EntityData.FromEntity(entity).ToEntity();
        foreach (var dimension in document.Dimensions)
        {
            Require(Enum.IsDefined(dimension.Kind) && double.IsFinite(dimension.Value), "Invalid dimension.");
            if (dimension.Anchor is { } anchor) ValidatePoint(anchor);
            ValidateReferences(dimension.ReferenceKeys);
        }
        foreach (var constraint in document.Constraints)
        {
            Require(Enum.IsDefined(constraint.Kind) && Enum.IsDefined(constraint.State), "Invalid constraint.");
            ValidateReferences(constraint.ReferenceKeys);
        }
        var metadata = document.Metadata;
        Require(Enum.IsDefined(metadata.Units) && Enum.IsDefined(metadata.Mode), "Invalid document metadata.");
        Require(metadata.Warnings is not null && metadata.UnsupportedEntityCounts is not null
            && metadata.EntityStyles is not null && metadata.Layers is not null, "Invalid metadata collections.");
        Require(metadata.Layers!.All(p => DXFER.Core.Operations.DrawingLayerService.ValidName(p.Key)
            && p.Value is not null && p.Value.TrueColor is not (< 0 or > 0xFFFFFF)), "Invalid layer metadata.");
        Require(DXFER.Core.Operations.DrawingLayerService.ValidName(metadata.ActiveLayerName), "Invalid active layer.");
        Require(metadata.Warnings!.All(w => w is not null && !string.IsNullOrWhiteSpace(w.Code)
            && w.Message is not null && Enum.IsDefined(w.Severity)), "Invalid document warning.");
        Require(metadata.UnsupportedEntityCounts!.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value >= 0)
            && metadata.EntityStyles!.All(p => !string.IsNullOrWhiteSpace(p.Key) && p.Value is not null),
            "Invalid entity metadata.");
    }

    private static void UniqueIds(IEnumerable<string> ids, string kind)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var id in ids)
            Require(!string.IsNullOrWhiteSpace(id) && seen.Add(id), "Empty or duplicate " + kind + " ID.");
    }

    // Unresolved references can be legitimate failed intent. Preserve them verbatim, not as new geometry.
    private static void ValidateReferences(IEnumerable<string> references) =>
        Require(references.All(r => !string.IsNullOrWhiteSpace(r)), "Empty sketch reference.");

    private static void ValidatePoint(Point2 point) =>
        Require(double.IsFinite(point.X) && double.IsFinite(point.Y), "Non-finite point.");

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new FormatException(message);
    }

    private sealed class DocumentData
    {
        public required string Format { get; init; }
        public required int SchemaVersion { get; init; }
        public required EntityData[] Entities { get; init; }
        public required DimensionData[] Dimensions { get; init; }
        public required ConstraintData[] Constraints { get; init; }
        public required DrawingDocumentMetadata Metadata { get; init; }
    }

    private sealed class PointData
    {
        public required double X { get; init; }
        public required double Y { get; init; }
    }

    private sealed class PointConverter : JsonConverter<Point2>
    {
        public override Point2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var data = JsonSerializer.Deserialize<PointData>(ref reader, options)
                ?? throw new JsonException("Missing point coordinates.");
            return new Point2(data.X, data.Y);
        }

        public override void Write(Utf8JsonWriter writer, Point2 value, JsonSerializerOptions options) =>
            JsonSerializer.Serialize(writer, new PointData { X = value.X, Y = value.Y }, options);
    }

    private sealed class DimensionData
    {
        public required string Id { get; init; }
        public required SketchDimensionKind Kind { get; init; }
        public required string[] ReferenceKeys { get; init; }
        public required double Value { get; init; }
        public Point2? Anchor { get; init; }
        public required bool IsDriving { get; init; }
    }

    private sealed class ConstraintData
    {
        public required string Id { get; init; }
        public required SketchConstraintKind Kind { get; init; }
        public required string[] ReferenceKeys { get; init; }
        public required SketchConstraintState State { get; init; }
    }

    private sealed record EntityData
    {
        public required string Id { get; init; }
        public required string Kind { get; init; }
        public required bool IsConstruction { get; init; }
        public Point2? Start { get; init; }
        public Point2? End { get; init; }
        public Point2? Center { get; init; }
        public Point2? Location { get; init; }
        public Point2? MajorAxisEndPoint { get; init; }
        public double? Radius { get; init; }
        public double? StartAngleDegrees { get; init; }
        public double? EndAngleDegrees { get; init; }
        public double? MinorRadiusRatio { get; init; }
        public double? StartParameterDegrees { get; init; }
        public double? EndParameterDegrees { get; init; }
        public double? RotationAngleDegrees { get; init; }
        public int? SideCount { get; init; }
        public bool? Circumscribed { get; init; }
        public Point2[]? Vertices { get; init; }
        public int? Degree { get; init; }
        public Point2[]? ControlPoints { get; init; }
        public double[]? Knots { get; init; }
        public double[]? Weights { get; init; }
        public Point2[]? FitPoints { get; init; }
        public Point2? StartTangentHandle { get; init; }
        public Point2? EndTangentHandle { get; init; }

        public static EntityData FromEntity(DrawingEntity entity)
        {
            var data = new EntityData { Id = entity.Id.Value, Kind = entity.Kind, IsConstruction = entity.IsConstruction };
            return entity switch
            {
                LineEntity e => data with { Start = e.Start, End = e.End },
                CircleEntity e => data with { Center = e.Center, Radius = e.Radius },
                ArcEntity e => data with
                {
                    Center = e.Center,
                    Radius = e.Radius,
                    StartAngleDegrees = e.StartAngleDegrees,
                    EndAngleDegrees = e.EndAngleDegrees
                },
                EllipseEntity e => data with
                {
                    Center = e.Center,
                    MajorAxisEndPoint = e.MajorAxisEndPoint,
                    MinorRadiusRatio = e.MinorRadiusRatio,
                    StartParameterDegrees = e.StartParameterDegrees,
                    EndParameterDegrees = e.EndParameterDegrees
                },
                PointEntity e => data with { Location = e.Location },
                PolylineEntity e => data with { Vertices = e.Vertices.ToArray() },
                PolygonEntity e => data with
                {
                    Center = e.Center,
                    Radius = e.Radius,
                    RotationAngleDegrees = e.RotationAngleDegrees,
                    SideCount = e.SideCount,
                    Circumscribed = e.Circumscribed
                },
                SplineEntity e => data with
                {
                    Degree = e.Degree,
                    ControlPoints = e.ControlPoints.ToArray(),
                    Knots = e.Knots.ToArray(),
                    Weights = e.Weights.ToArray(),
                    FitPoints = e.FitPoints.ToArray(),
                    StartTangentHandle = e.StartTangentHandle,
                    EndTangentHandle = e.EndTangentHandle
                },
                _ => throw new FormatException("Unsupported native entity type: " + entity.Kind)
            };
        }

        public DrawingEntity ToEntity()
        {
            var id = EntityId.Create(Id);
            foreach (var point in new[] { Start, End, Center, Location, MajorAxisEndPoint, StartTangentHandle, EndTangentHandle })
                if (point is { } p) ValidatePoint(p);
            foreach (var points in new[] { Vertices, ControlPoints, FitPoints })
                if (points is not null) foreach (var point in points) ValidatePoint(point);
            foreach (var value in new[] { Radius, StartAngleDegrees, EndAngleDegrees, MinorRadiusRatio,
                StartParameterDegrees, EndParameterDegrees, RotationAngleDegrees })
                Require(value is null || double.IsFinite(value.Value), "Non-finite entity value.");
            if (Radius is { } radius) Require(radius > 0, "Invalid radius.");
            if (MinorRadiusRatio is { } ratio) Require(ratio > 0, "Invalid ellipse ratio.");
            if (Kind == "arc") Require(Math.Abs(Required(EndAngleDegrees) - Required(StartAngleDegrees)) <= 360,
                "Invalid arc sweep.");
            if (Kind == "ellipse") Require(Math.Abs(Required(EndParameterDegrees) - Required(StartParameterDegrees)) <= 360.000001,
                "Invalid ellipse sweep.");
            if (Kind == "polygon") Require(SideCount is >= PolygonEntity.MinSideCount and <= PolygonEntity.MaxSideCount,
                "Invalid polygon side count.");
            if (Kind == "spline")
            {
                Require(Degree is >= 1 && ControlPoints is not null && Degree < ControlPoints.Length,
                    "Invalid spline degree or controls.");
                Require(Knots is not null && Knots.Length == (long)ControlPoints!.Length + Degree + 1
                    && Knots.All(double.IsFinite) && Knots.Zip(Knots.Skip(1)).All(p => p.First <= p.Second)
                    && Knots[Degree!.Value] < Knots[ControlPoints.Length],
                    "Invalid spline knots.");
                Require(Weights is not null && Weights.Length == ControlPoints!.Length
                    && Weights.All(w => double.IsFinite(w) && w > 0) && FitPoints is not null, "Invalid spline weights or fit points.");
            }
            return Kind switch
            {
                "line" => new LineEntity(id, Required(Start), Required(End), IsConstruction),
                "circle" => new CircleEntity(id, Required(Center), Required(Radius), IsConstruction),
                "arc" => new ArcEntity(id, Required(Center), Required(Radius), Required(StartAngleDegrees), Required(EndAngleDegrees), IsConstruction),
                "ellipse" => new EllipseEntity(id, Required(Center), Required(MajorAxisEndPoint), Required(MinorRadiusRatio),
                    Required(StartParameterDegrees), Required(EndParameterDegrees), IsConstruction),
                "point" => new PointEntity(id, Required(Location), IsConstruction),
                "polyline" => new PolylineEntity(id, Vertices ?? throw new FormatException("Missing vertices."), IsConstruction),
                "polygon" => new PolygonEntity(id, Required(Center), Required(Radius), Required(RotationAngleDegrees),
                    Required(SideCount), Required(Circumscribed), IsConstruction),
                "spline" => new SplineEntity(id, Required(Degree), ControlPoints!, Knots!, Weights, IsConstruction,
                    FitPoints, StartTangentHandle, EndTangentHandle),
                _ => throw new FormatException("Unsupported native entity kind: " + Kind)
            };
        }

        private static T Required<T>(T? value) where T : struct =>
            value ?? throw new FormatException("Missing required entity geometry.");
    }
}

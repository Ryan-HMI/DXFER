using System.Text.Json;
using System.Text.Json.Serialization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;

namespace DXFER.Core.Sketching;

internal static class PlaneGcsDocumentMapper
{
    private static readonly JsonSerializerOptions MetadataOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public static PlaneGcsPoint Point(Point2 point) => new(point.X, point.Y);

    public static PlaneGcsConstraint Constraint(SketchConstraint constraint) =>
        new(constraint.Id, constraint.Kind.ToString(), constraint.ReferenceKeys.ToArray(), constraint.State.ToString());

    public static PlaneGcsDimension Dimension(DrawingDocument document, SketchDimension dimension) =>
        new(dimension.Id, dimension.Kind.ToString(), dimension.ReferenceKeys.ToArray(), dimension.Value,
            dimension.Anchor is { } anchor ? Point(anchor) : null, dimension.IsDriving,
            (PlaneGcsValidation.DimensionResidualSatisfied(document, dimension)
                ? SketchConstraintState.Satisfied : SketchConstraintState.Unsatisfied).ToString());

    public static PlaneGcsDocument Document(DrawingDocument document)
    {
        var layers = DrawingLayerService.GetLayers(document);
        var generated = document.Metadata.Patterns.SelectMany(p => p.Members).Select(m => m.EntityId).ToHashSet(StringComparer.Ordinal);
        return new(document.Entities.Select(entity =>
        {
            var layerName = DrawingLayerService.LayerName(document, entity.Id.Value);
            var layer = layers[layerName];
            return Entity(entity) with { LayerName = layerName, IsLocked = layer.IsLocked,
                IsHidden = layer.IsHidden, IsGenerated = generated.Contains(entity.Id.Value) };
        }).ToArray(), document.Constraints.Select(Constraint).ToArray(),
            document.Dimensions.Select(d => Dimension(document, d)).ToArray(),
            JsonSerializer.SerializeToElement(document.Metadata, MetadataOptions), document.Metadata.Patterns.Count > 0);
    }

    public static PlaneGcsEntity Entity(DrawingEntity entity)
    {
        var wire = new PlaneGcsEntity(entity.Id.Value, entity.Kind.ToLowerInvariant(),
            Array.Empty<PlaneGcsPoint>(), IsConstruction: entity.IsConstruction);
        return entity switch
        {
            LineEntity line => wire with { Points = new[] { Point(line.Start), Point(line.End) } },
            PointEntity point => wire with { Points = new[] { Point(point.Location) } },
            PolylineEntity polyline => wire with { Points = polyline.Vertices.Select(Point).ToArray() },
            CircleEntity circle => wire with { Center = Point(circle.Center), Radius = circle.Radius },
            ArcEntity arc => wire with { Center = Point(arc.Center), Radius = arc.Radius,
                StartAngleDegrees = arc.StartAngleDegrees, EndAngleDegrees = arc.EndAngleDegrees },
            EllipseEntity ellipse => wire with { Center = Point(ellipse.Center), MajorAxisEndPoint = Point(ellipse.MajorAxisEndPoint),
                MinorRadiusRatio = ellipse.MinorRadiusRatio, StartAngleDegrees = ellipse.StartParameterDegrees,
                EndAngleDegrees = ellipse.EndParameterDegrees },
            PolygonEntity polygon => wire with { Points = polygon.GetVertices().Select(Point).ToArray(),
                Center = Point(polygon.Center), Radius = polygon.Radius, RotationAngleDegrees = polygon.RotationAngleDegrees,
                SideCount = polygon.SideCount, Circumscribed = polygon.Circumscribed },
            // Never substitute rendered samples or omit native degree-one controls.
            SplineEntity spline => wire with { ControlPoints = spline.ControlPoints.Select(Point).ToArray(),
                FitPoints = spline.FitPoints.Select(Point).ToArray(), SplineDegree = spline.Degree,
                SplineKnots = spline.Knots.ToArray(), SplineWeights = spline.Weights.ToArray(),
                StartTangentHandle = spline.StartTangentHandle is { } start ? Point(start) : null,
                EndTangentHandle = spline.EndTangentHandle is { } end ? Point(end) : null },
            _ => throw new NotSupportedException($"PlaneGCS geometry kind '{entity.Kind}' has no wire mapping.")
        };
    }

    public static DrawingEntity[] ReadEntities(DrawingDocument baseline, IReadOnlyList<PlaneGcsEntity> response)
    {
        if (response.Count != baseline.Entities.Count || response.Any(e => e is null || string.IsNullOrWhiteSpace(e.Id))
            || response.Select(e => e.Id).Distinct(StringComparer.Ordinal).Count() != response.Count)
            throw new InvalidOperationException("PlaneGCS must return exactly the original entity IDs.");
        var byId = response.ToDictionary(e => e.Id, StringComparer.Ordinal);
        return baseline.Entities.Select(original =>
        {
            if (!byId.TryGetValue(original.Id.Value, out var wire)
                || !string.Equals(original.Kind, wire.Kind, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("PlaneGCS changed an entity identity or kind.");
            CheckFinite(wire);
            var mapped = ReadEntity(original, wire);
            return SameGeometry(original, mapped) ? original : mapped;
        }).ToArray();
    }

    public static bool SameGeometry(DrawingEntity first, DrawingEntity second)
    {
        var a = Entity(first);
        var b = Entity(second);
        // JSON normalizes signed zero. Compare numeric values exactly so an
        // unchanged fixed entity survives transport without admitting real drift.
        return (a with { Points = b.Points, ControlPoints = b.ControlPoints, FitPoints = b.FitPoints,
            SplineKnots = b.SplineKnots, SplineWeights = b.SplineWeights }) == b
            && a.Points.SequenceEqual(b.Points)
            && SameSequence(a.ControlPoints, b.ControlPoints)
            && SameSequence(a.FitPoints, b.FitPoints)
            && SameSequence(a.SplineKnots, b.SplineKnots)
            && SameSequence(a.SplineWeights, b.SplineWeights);
    }

    private static bool SameSequence<T>(IReadOnlyList<T>? first, IReadOnlyList<T>? second) =>
        first is null ? second is null : second is not null && first.SequenceEqual(second);

    public static void CheckFinite(PlaneGcsEntity entity)
    {
        if (entity.Points is null) throw new InvalidOperationException("PlaneGCS entity points are missing.");
        foreach (var point in entity.Points.Concat(entity.ControlPoints ?? Array.Empty<PlaneGcsPoint>())
                     .Concat(entity.FitPoints ?? Array.Empty<PlaneGcsPoint>()))
            _ = ReadPoint(point);
        foreach (var point in new[] { entity.Center, entity.MajorAxisEndPoint, entity.StartTangentHandle, entity.EndTangentHandle })
            if (point is not null) _ = ReadPoint(point);
        foreach (var value in new[] { entity.Radius, entity.StartAngleDegrees, entity.EndAngleDegrees,
                     entity.MinorRadiusRatio, entity.RotationAngleDegrees })
            if (value.HasValue) _ = ReadNumber(value);
        if ((entity.SplineKnots ?? Array.Empty<double>()).Concat(entity.SplineWeights ?? Array.Empty<double>())
            .Any(value => !double.IsFinite(value)))
            throw new InvalidOperationException("PlaneGCS returned nonfinite spline parameters.");
    }

    private static DrawingEntity ReadEntity(DrawingEntity original, PlaneGcsEntity wire)
    {
        switch (original)
        {
            case LineEntity line:
                Require(wire.Points.Count == 2, "line endpoint count");
                var start = ReadPoint(wire.Points[0]);
                var end = ReadPoint(wire.Points[1]);
                Require(start != end, "degenerate line");
                return line with { Start = start, End = end };
            case PointEntity point:
                Require(wire.Points.Count == 1, "point count");
                return point with { Location = ReadPoint(wire.Points[0]) };
            case PolylineEntity polyline:
                Require(wire.Points.Count == polyline.Vertices.Count, "polyline vertex count");
                var vertices = wire.Points.Select(ReadPoint).ToArray();
                Require((polyline.Vertices[0] == polyline.Vertices[^1]) == (vertices[0] == vertices[^1]), "polyline closure");
                for (var i = 1; i < vertices.Length; i++)
                    Require(vertices[i] != vertices[i - 1] || polyline.Vertices[i] == polyline.Vertices[i - 1], "collapsed polyline segment");
                return new PolylineEntity(polyline.Id, vertices, polyline.IsConstruction);
            case CircleEntity circle:
                return circle with { Center = ReadPoint(wire.Center), Radius = Positive(wire.Radius) };
            case ArcEntity arc:
                return arc with { Center = ReadPoint(wire.Center), Radius = Positive(wire.Radius),
                    StartAngleDegrees = ReadNumber(wire.StartAngleDegrees), EndAngleDegrees = ReadNumber(wire.EndAngleDegrees) };
            case EllipseEntity ellipse:
                var major = ReadPoint(wire.MajorAxisEndPoint);
                Require(major != new Point2(0, 0), "ellipse major axis");
                return ellipse with { Center = ReadPoint(wire.Center), MajorAxisEndPoint = major,
                    MinorRadiusRatio = Positive(wire.MinorRadiusRatio), StartParameterDegrees = ReadNumber(wire.StartAngleDegrees),
                    EndParameterDegrees = ReadNumber(wire.EndAngleDegrees) };
            case PolygonEntity polygon:
                Require(wire.SideCount == polygon.SideCount && wire.Circumscribed == polygon.Circumscribed, "polygon topology");
                return polygon with { Center = ReadPoint(wire.Center), Radius = Positive(wire.Radius),
                    RotationAngleDegrees = ReadNumber(wire.RotationAngleDegrees) };
            case SplineEntity spline:
                Require(wire.SplineDegree == spline.Degree && wire.ControlPoints?.Count == spline.ControlPoints.Count
                    && wire.FitPoints?.Count == spline.FitPoints.Count
                    && wire.SplineKnots is not null && wire.SplineKnots.SequenceEqual(spline.Knots)
                    && wire.SplineWeights is not null && wire.SplineWeights.SequenceEqual(spline.Weights)
                    && (wire.StartTangentHandle is null) == (spline.StartTangentHandle is null)
                    && (wire.EndTangentHandle is null) == (spline.EndTangentHandle is null), "spline topology");
                return new SplineEntity(spline.Id, spline.Degree, wire.ControlPoints!.Select(ReadPoint), spline.Knots,
                    spline.Weights, spline.IsConstruction, wire.FitPoints!.Select(ReadPoint),
                    wire.StartTangentHandle is null ? null : ReadPoint(wire.StartTangentHandle),
                    wire.EndTangentHandle is null ? null : ReadPoint(wire.EndTangentHandle));
            default:
                throw new NotSupportedException($"PlaneGCS geometry kind '{original.Kind}' has no result mapping.");
        }
    }

    private static Point2 ReadPoint(PlaneGcsPoint? point)
    {
        if (point is null || !double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new InvalidOperationException("PlaneGCS point is missing or nonfinite.");
        return new(point.X, point.Y);
    }

    private static double ReadNumber(double? value) => value.HasValue && double.IsFinite(value.Value)
        ? value.Value : throw new InvalidOperationException("PlaneGCS parameter is missing or nonfinite.");

    private static double Positive(double? value)
    {
        var number = ReadNumber(value);
        Require(number > 0, "nonpositive radius or ratio");
        return number;
    }

    private static void Require(bool condition, string field)
    {
        if (!condition) throw new InvalidOperationException($"PlaneGCS changed or omitted required geometry: {field}.");
    }
}

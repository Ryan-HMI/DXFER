using System.Text.Json.Serialization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using DXFER.Core.Operations;

namespace DXFER.Blazor.Interop;

public sealed record CanvasDocumentDto(
    [property: JsonPropertyName("entities")] IReadOnlyList<CanvasEntityDto> Entities,
    [property: JsonPropertyName("bounds")] CanvasBoundsDto Bounds,
    [property: JsonPropertyName("dimensions")] IReadOnlyList<CanvasSketchDimensionDto> Dimensions,
    [property: JsonPropertyName("constraints")] IReadOnlyList<CanvasSketchConstraintDto> Constraints)
{
    [JsonPropertyName("hasPatterns")] public bool HasPatterns { get; init; }
    [JsonPropertyName("radialCenterPreviews")]
    public IReadOnlyList<CanvasRadialCenterPreviewDto> RadialCenterPreviews { get; init; } = Array.Empty<CanvasRadialCenterPreviewDto>();
    public static CanvasDocumentDto Empty { get; } = new(
        Array.Empty<CanvasEntityDto>(),
        new CanvasBoundsDto(0, 0, 0, 0),
        Array.Empty<CanvasSketchDimensionDto>(),
        Array.Empty<CanvasSketchConstraintDto>());

    public static CanvasDocumentDto FromDocument(DrawingDocument document)
    {
        var layers = DrawingLayerService.GetLayers(document);
        DxfEntityStyle Layer(DrawingEntity entity) => layers[DrawingLayerService.LayerName(document, entity.Id.Value)];
        var visible = document.Entities.Where(e => !Layer(e).IsHidden).ToArray();
        var bounds = new DrawingDocument(visible).GetBounds();
        var entities = visible.Select(entity => FromEntity(entity, document.Metadata) with
        {
            LayerName = DrawingLayerService.LayerName(document, entity.Id.Value),
            LayerColor = DrawingLayerService.Color(Layer(entity)),
            IsLocked = Layer(entity).IsLocked
        }).ToArray();
        var hidden = document.Entities.Except(visible).Select(e => e.Id.Value).ToHashSet();
        bool IsVisible(IReadOnlyList<string> references) => !references.Any(r => hidden.Contains(r.Split('|')[0]));
        var dimensions = document.Dimensions.Where(d => IsVisible(d.ReferenceKeys)).Select(dimension => FromDimension(document, dimension)).ToArray();
        var constraints = document.Constraints.Where(c => IsVisible(c.ReferenceKeys)).Select(FromConstraint).ToArray();

        return new CanvasDocumentDto(entities, FromBounds(bounds), dimensions, constraints)
        {
            HasPatterns = document.Metadata.Patterns?.Count > 0,
            RadialCenterPreviews = GetRadialCenterPreviews(document, layers)
        };
    }

    private static IReadOnlyList<CanvasRadialCenterPreviewDto> GetRadialCenterPreviews(
        DrawingDocument document, IReadOnlyDictionary<string, DxfEntityStyle> layers)
    {
        var patterns = document.Metadata.Patterns;
        if (document.Metadata.Mode != DrawingDocumentMode.Editable || patterns is null
            || patterns.Any(p => p is null || p.SourceEntityIds is null || p.Members is null || p.Members.Any(m => m is null))
            || !patterns.Any(p => p.Kind == PatternKind.Radial)
            || !DrawingPatternService.TryValidate(document, out _)) return Array.Empty<CanvasRadialCenterPreviewDto>();

        var entities = document.Entities.ToDictionary(e => e.Id.Value, StringComparer.Ordinal);
        bool ResolveReference(string key, out string entityId)
        {
            entityId = string.Empty;
            if (!SketchReference.TryParse(key, out var reference)) return false;
            entityId = reference.EntityId;
            if (reference.IsOrigin) return true;
            if (!entities.TryGetValue(entityId, out var entity)) return false;
            if (reference.SegmentIndex is { } index
                && (entity is not PolylineEntity polyline || index < 0 || index >= polyline.Vertices.Count - 1)) return false;
            return reference.IsEntity || SketchReferenceResolver.TryGetPoint(document, reference.ToString(), out _);
        }

        // Local preview cannot update any intent, including reference measurements
        // and suppressed relations. Resolve once here, never on pointer movement.
        if (document.Constraints.Any(c => !Enum.IsDefined(c.Kind) || !Enum.IsDefined(c.State) || c.ReferenceKeys.Count == 0)
            || document.Dimensions.Any(d => !Enum.IsDefined(d.Kind) || !double.IsFinite(d.Value) || d.ReferenceKeys.Count == 0
                || d.Anchor is { } anchor && (!double.IsFinite(anchor.X) || !double.IsFinite(anchor.Y))))
            return Array.Empty<CanvasRadialCenterPreviewDto>();
        var intentIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in document.Constraints.SelectMany(c => c.ReferenceKeys).Concat(document.Dimensions.SelectMany(d => d.ReferenceKeys)))
        {
            if (!ResolveReference(key, out var id)) return Array.Empty<CanvasRadialCenterPreviewDto>();
            intentIds.Add(id);
        }
        var dependencies = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var pattern in patterns)
        {
            var ids = pattern.SourceEntityIds.ToHashSet(StringComparer.Ordinal);
            if (pattern.CenterPointId is { } centerId) ids.Add(centerId);
            foreach (var key in new[] { pattern.UDirectionReference, pattern.VDirectionReference })
            {
                if (key is null) continue;
                if (!ResolveReference(key, out var id)) return Array.Empty<CanvasRadialCenterPreviewDto>();
                ids.Add(id);
            }
            dependencies.Add(pattern.Id, ids);
        }

        var previews = new List<CanvasRadialCenterPreviewDto>();
        foreach (var pattern in patterns.Where(p => p.Kind == PatternKind.Radial))
        {
            var moved = pattern.Members.Select(m => m.EntityId).ToHashSet(StringComparer.Ordinal);
            if (pattern.CenterPointId is { } centerId) moved.Add(centerId);
            if (moved.Overlaps(intentIds)
                || dependencies.Any(p => p.Key != pattern.Id && moved.Overlaps(p.Value))) continue;
            var participants = moved.Concat(pattern.SourceEntityIds);
            if (participants.Any(id => !entities.TryGetValue(id, out var entity)
                || entity is not (PointEntity or LineEntity or PolylineEntity or CircleEntity or ArcEntity or EllipseEntity or PolygonEntity or SplineEntity)
                || !layers.TryGetValue(DrawingLayerService.LayerName(document, id), out var layer)
                || layer.IsHidden || layer.IsLocked)) continue;
            previews.Add(new CanvasRadialCenterPreviewDto(pattern.Id, FromPoint(pattern.Center), pattern.CenterPointId,
                pattern.FullCircle ? 360.0 / pattern.Count : pattern.StepAngleDegrees,
                pattern.Members.Select(m => new CanvasRadialCenterPreviewMemberDto(m.EntityId, m.Column)).ToArray()));
        }
        return previews;
    }

    private static CanvasEntityDto FromEntity(DrawingEntity entity, DrawingDocumentMetadata metadata)
    {
        var id = entity.Id.Value;
        var kind = entity.Kind.ToLowerInvariant();
        metadata.EntityStyles.TryGetValue(id, out var style);
        var lineTypeName = style?.LineTypeName;

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
                line.IsConstruction,
                LineTypeName: lineTypeName),
            CircleEntity circle => new CanvasEntityDto(
                id,
                kind,
                Array.Empty<CanvasPointDto>(),
                FromPoint(circle.Center),
                circle.Radius,
                null,
                null,
                circle.IsConstruction,
                LineTypeName: lineTypeName),
            ArcEntity arc => new CanvasEntityDto(
                id,
                kind,
                Array.Empty<CanvasPointDto>(),
                FromPoint(arc.Center),
                arc.Radius,
                arc.StartAngleDegrees,
                arc.EndAngleDegrees,
                arc.IsConstruction,
                LineTypeName: lineTypeName),
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
                ellipse.MinorRadiusRatio,
                LineTypeName: lineTypeName),
            PointEntity point => new CanvasEntityDto(
                id,
                kind,
                new[] { FromPoint(point.Location) },
                null,
                null,
                null,
                null,
                point.IsConstruction,
                LineTypeName: lineTypeName),
            PolylineEntity polyline => new CanvasEntityDto(
                id,
                kind,
                polyline.Vertices.Select(FromPoint).ToArray(),
                null,
                null,
                null,
                null,
                polyline.IsConstruction,
                LineTypeName: lineTypeName),
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
                polygon.Circumscribed,
                LineTypeName: lineTypeName),
            SplineEntity spline => new CanvasEntityDto(
                id,
                kind,
                spline.GetSamplePoints().Select(FromPoint).ToArray(),
                null,
                null,
                null,
                null,
                spline.IsConstruction,
                ControlPoints: GetSplineControlPointsForCanvas(spline),
                FitPoints: spline.FitPoints.Count >= 2
                    ? spline.FitPoints.Select(FromPoint).ToArray()
                    : null,
                StartTangentHandle: spline.StartTangentHandle is { } startTangentHandle ? FromPoint(startTangentHandle) : null,
                EndTangentHandle: spline.EndTangentHandle is { } endTangentHandle ? FromPoint(endTangentHandle) : null,
                LineTypeName: lineTypeName,
                SplineDegree: spline.Degree,
                SplineKnots: spline.Knots,
                SplineWeights: spline.Weights,
                StartTangentControlPointIndex: spline.GetTangentControlPointIndex(true),
                EndTangentControlPointIndex: spline.GetTangentControlPointIndex(false)),
            _ => new CanvasEntityDto(
                id,
                kind,
                Array.Empty<CanvasPointDto>(),
                null,
                null,
                null,
                null,
                entity.IsConstruction,
                LineTypeName: lineTypeName)
        };
    }

    private static IReadOnlyList<CanvasPointDto>? GetSplineControlPointsForCanvas(SplineEntity spline)
    {
        if (spline.FitPoints.Count < 2
            && spline.Degree == 1
            && spline.ControlPoints.Count > 2)
        {
            return null;
        }

        return spline.ControlPoints.Select(FromPoint).ToArray();
    }

    private static CanvasPointDto FromPoint(Point2 point) => new(point.X, point.Y);

    private static CanvasBoundsDto FromBounds(Bounds2 bounds) =>
        new(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);

    private static CanvasSketchDimensionDto FromDimension(DrawingDocument document, SketchDimension dimension)
    {
        var state = SketchDimensionSolverService.GetDimensionState(document, dimension);
        CanvasPointDto? offsetStart = null, offsetEnd = null;
        if (dimension.Kind == SketchDimensionKind.Offset
            && SketchOffsetRelation.TryMeasure(document.Entities, dimension.ReferenceKeys, out _, out var start, out var end))
        { offsetStart = FromPoint(start); offsetEnd = FromPoint(end); }
        return new CanvasSketchDimensionDto(
            dimension.Id,
            dimension.Kind.ToString(),
            dimension.ReferenceKeys,
            !dimension.IsDriving && SketchDimensionSolverService.TryGetDimensionMeasurement(document, dimension, out var measured)
                ? measured : dimension.Value,
            dimension.Anchor is null ? null : FromPoint(dimension.Anchor.Value),
            dimension.IsDriving,
            state.ToString(),
            state == SketchConstraintState.Unsatisfied
                ? dimension.ReferenceKeys
                : Array.Empty<string>(), offsetStart, offsetEnd);
    }

    private static CanvasSketchConstraintDto FromConstraint(SketchConstraint constraint) =>
        new(
            constraint.Id,
            constraint.Kind.ToString(),
            constraint.ReferenceKeys,
            constraint.State.ToString(),
            constraint.State == SketchConstraintState.Unsatisfied
                ? constraint.ReferenceKeys
                : Array.Empty<string>());
}

public sealed record CanvasRadialCenterPreviewDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("center")] CanvasPointDto Center,
    [property: JsonPropertyName("centerPointId")] string? CenterPointId,
    [property: JsonPropertyName("stepAngleDegrees")] double StepAngleDegrees,
    [property: JsonPropertyName("members")] IReadOnlyList<CanvasRadialCenterPreviewMemberDto> Members);

public sealed record CanvasRadialCenterPreviewMemberDto(
    [property: JsonPropertyName("entityId")] string EntityId,
    [property: JsonPropertyName("column")] int Column);

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
    [property: JsonPropertyName("circumscribed")] bool? Circumscribed = null,
    [property: JsonPropertyName("controlPoints")] IReadOnlyList<CanvasPointDto>? ControlPoints = null,
    [property: JsonPropertyName("fitPoints")] IReadOnlyList<CanvasPointDto>? FitPoints = null,
    [property: JsonPropertyName("startTangentHandle")] CanvasPointDto? StartTangentHandle = null,
    [property: JsonPropertyName("endTangentHandle")] CanvasPointDto? EndTangentHandle = null,
    [property: JsonPropertyName("lineTypeName")] string? LineTypeName = null,
    [property: JsonPropertyName("splineDegree")] int? SplineDegree = null,
    [property: JsonPropertyName("splineKnots")] IReadOnlyList<double>? SplineKnots = null,
    [property: JsonPropertyName("splineWeights")] IReadOnlyList<double>? SplineWeights = null,
    [property: JsonPropertyName("startTangentControlPointIndex")] int? StartTangentControlPointIndex = null,
    [property: JsonPropertyName("endTangentControlPointIndex")] int? EndTangentControlPointIndex = null,
    [property: JsonPropertyName("layerName")] string? LayerName = null,
    [property: JsonPropertyName("layerColor")] string? LayerColor = null,
    [property: JsonPropertyName("isLocked")] bool IsLocked = false);

public sealed record CanvasPointDto(
    [property: JsonPropertyName("x")] double X,
    [property: JsonPropertyName("y")] double Y);

public sealed record CanvasPowerTrimPickDto(
    string TargetKey,
    CanvasPointDto Point);

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
    [property: JsonPropertyName("isDriving")] bool IsDriving,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("affectedReferenceKeys")] IReadOnlyList<string> AffectedReferenceKeys,
    [property: JsonPropertyName("offsetStart")] CanvasPointDto? OffsetStart = null,
    [property: JsonPropertyName("offsetEnd")] CanvasPointDto? OffsetEnd = null);

public sealed record CanvasSketchConstraintDto(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("referenceKeys")] IReadOnlyList<string> ReferenceKeys,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("affectedReferenceKeys")] IReadOnlyList<string> AffectedReferenceKeys);

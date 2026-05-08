using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Core.Operations;

public readonly record struct PowerTrimLinePick(
    string TargetEntityId,
    Point2 PickedPoint);

public static class DrawingModifyService
{
    private const double GeometryTolerance = 0.000001;
    private const double SampledPathProjectionTolerance = 0.05;

    private readonly record struct SampledPathCut(double Distance, Point2 Point);
    private readonly record struct LineEndpointReference(LineEntity Line, string Target, Point2 Point);

    public static DrawingDocument TranslateSelected(
        DrawingDocument document,
        IEnumerable<string> selectedEntityIds,
        Point2 from,
        Point2 to)
    {
        var deltaX = to.X - from.X;
        var deltaY = to.Y - from.Y;
        if (Math.Sqrt(deltaX * deltaX + deltaY * deltaY) <= GeometryTolerance)
        {
            return document;
        }

        return DrawingPrepService.TransformSelected(
            document,
            selectedEntityIds,
            Transform2.Translation(deltaX, deltaY));
    }

    public static DrawingDocument RotateSelected(
        DrawingDocument document,
        IEnumerable<string> selectedEntityIds,
        Point2 center,
        Point2 reference,
        Point2 target)
    {
        if (!TryGetAngleDelta(center, reference, target, out var degrees))
        {
            return document;
        }

        return DrawingPrepService.TransformSelected(
            document,
            selectedEntityIds,
            Transform2.RotationDegreesAbout(degrees, center));
    }

    public static DrawingDocument ScaleSelected(
        DrawingDocument document,
        IEnumerable<string> selectedEntityIds,
        Point2 center,
        Point2 reference,
        Point2 target)
    {
        var referenceRadius = Distance(center, reference);
        var targetRadius = Distance(center, target);
        if (referenceRadius <= GeometryTolerance || targetRadius <= GeometryTolerance)
        {
            return document;
        }

        var scale = targetRadius / referenceRadius;
        return ReplaceSelected(document, selectedEntityIds, entity => ScaleEntity(entity, center, scale));
    }

    public static DrawingDocument MirrorSelected(
        DrawingDocument document,
        IEnumerable<string> selectedEntityIds,
        Point2 axisStart,
        Point2 axisEnd)
    {
        if (Distance(axisStart, axisEnd) <= GeometryTolerance)
        {
            return document;
        }

        return ReplaceSelected(document, selectedEntityIds, entity => MirrorEntity(entity, axisStart, axisEnd));
    }

    public static bool TryOffsetSelected(
        DrawingDocument document,
        IEnumerable<string> selectedEntityIds,
        Point2 throughPoint,
        Func<string, EntityId> createEntityId,
        out DrawingDocument nextDocument,
        out int createdCount)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedEntityIds);
        ArgumentNullException.ThrowIfNull(createEntityId);

        var selected = selectedEntityIds.ToHashSet(StringComparer.Ordinal);
        var nextEntities = new List<DrawingEntity>(document.Entities);
        foreach (var entity in document.Entities)
        {
            if (!selected.Contains(entity.Id.Value)
                || !TryOffsetEntity(entity, throughPoint, createEntityId(entity.Kind), out var offsetEntity))
            {
                continue;
            }

            nextEntities.Add(offsetEntity);
        }

        createdCount = nextEntities.Count - document.Entities.Count;
        nextDocument = createdCount > 0
            ? new DrawingDocument(nextEntities, document.Dimensions, document.Constraints, document.Metadata)
            : document;
        return createdCount > 0;
    }

    public static bool TryLinearPatternSelected(
        DrawingDocument document,
        IEnumerable<string> selectedEntityIds,
        Point2 from,
        Point2 to,
        int instanceCount,
        Func<string, EntityId> createEntityId,
        out DrawingDocument nextDocument,
        out int createdCount)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedEntityIds);
        ArgumentNullException.ThrowIfNull(createEntityId);

        var count = Math.Max(2, instanceCount);
        var deltaX = to.X - from.X;
        var deltaY = to.Y - from.Y;
        if (Math.Sqrt(deltaX * deltaX + deltaY * deltaY) <= GeometryTolerance)
        {
            nextDocument = document;
            createdCount = 0;
            return false;
        }

        var selected = GetSelectedEntities(document, selectedEntityIds);
        if (selected.Count == 0)
        {
            nextDocument = document;
            createdCount = 0;
            return false;
        }

        var nextEntities = new List<DrawingEntity>(document.Entities);
        for (var instanceIndex = 1; instanceIndex < count; instanceIndex++)
        {
            var transform = Transform2.Translation(deltaX * instanceIndex, deltaY * instanceIndex);
            foreach (var entity in selected)
            {
                nextEntities.Add(WithId(entity.Transform(transform), createEntityId(entity.Kind)));
            }
        }

        createdCount = nextEntities.Count - document.Entities.Count;
        nextDocument = new DrawingDocument(nextEntities, document.Dimensions, document.Constraints, document.Metadata);
        return true;
    }

    public static bool TryCircularPatternSelected(
        DrawingDocument document,
        IEnumerable<string> selectedEntityIds,
        Point2 center,
        Point2 reference,
        Point2 target,
        int instanceCount,
        Func<string, EntityId> createEntityId,
        out DrawingDocument nextDocument,
        out int createdCount)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedEntityIds);
        ArgumentNullException.ThrowIfNull(createEntityId);

        if (!TryGetAngleDelta(center, reference, target, out var degrees))
        {
            nextDocument = document;
            createdCount = 0;
            return false;
        }

        var selected = GetSelectedEntities(document, selectedEntityIds);
        if (selected.Count == 0)
        {
            nextDocument = document;
            createdCount = 0;
            return false;
        }

        var count = Math.Max(2, instanceCount);
        var nextEntities = new List<DrawingEntity>(document.Entities);
        for (var instanceIndex = 1; instanceIndex < count; instanceIndex++)
        {
            var transform = Transform2.RotationDegreesAbout(degrees * instanceIndex, center);
            foreach (var entity in selected)
            {
                nextEntities.Add(WithId(entity.Transform(transform), createEntityId(entity.Kind)));
            }
        }

        createdCount = nextEntities.Count - document.Entities.Count;
        nextDocument = new DrawingDocument(nextEntities, document.Dimensions, document.Constraints, document.Metadata);
        return true;
    }

    public static bool TryAddSplinePoint(
        DrawingDocument document,
        IEnumerable<string> selectedEntityIds,
        Point2 pickedPoint,
        out DrawingDocument nextDocument)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedEntityIds);

        var splines = GetSelectedEntities(document, selectedEntityIds)
            .OfType<SplineEntity>()
            .Where(spline => spline.FitPoints.Count >= 2)
            .ToArray();
        if (splines.Length != 1)
        {
            return FailUnchanged(document, out nextDocument);
        }

        var target = splines[0];
        var samples = target.GetSamplePoints();
        if (samples.Count < 2
            || !TryBuildSampledPathDistances(samples, out var cumulativeDistances, out _)
            || !TryProjectPointToSampledPath(
                samples,
                cumulativeDistances,
                pickedPoint,
                SampledPathProjectionTolerance,
                out var pickedDistance))
        {
            return FailUnchanged(document, out nextDocument);
        }

        var insertedPoint = PointAtSampledPathDistance(samples, cumulativeDistances, pickedDistance);
        if (target.FitPoints.Any(fitPoint => Distance(fitPoint, insertedPoint) <= GeometryTolerance)
            || !TryGetSplineFitPointPathDistances(target, samples, cumulativeDistances, out var fitPointDistances))
        {
            return FailUnchanged(document, out nextDocument);
        }

        var insertionIndex = GetSplineFitPointInsertionIndex(fitPointDistances, pickedDistance);
        if (insertionIndex <= 0 || insertionIndex >= target.FitPoints.Count)
        {
            return FailUnchanged(document, out nextDocument);
        }

        var nextFitPoints = target.FitPoints.ToList();
        nextFitPoints.Insert(insertionIndex, insertedPoint);
        var replacement = SplineEntity.FromFitPoints(
            target.Id,
            nextFitPoints,
            target.IsConstruction,
            target.StartTangentHandle,
            target.EndTangentHandle);

        nextDocument = ReplaceEntities(
            document,
            new Dictionary<string, DrawingEntity>(StringComparer.Ordinal)
            {
                [target.Id.Value] = replacement
            },
            Array.Empty<DrawingEntity>());
        return true;
    }

    public static bool TryChamferSelectedLines(
        DrawingDocument document,
        IEnumerable<string> selectedEntityIds,
        double distance,
        out DrawingDocument nextDocument)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedEntityIds);

        var lines = GetSelectedEntities(document, selectedEntityIds).OfType<LineEntity>().ToArray();
        if (lines.Length != 2
            || !TryGetLineIntersection(lines[0], lines[1], false, false, out var intersection, out _, out _))
        {
            nextDocument = document;
            return false;
        }

        var trimDistance = Math.Max(GeometryTolerance, distance);
        if (!TryTrimLineFromIntersection(lines[0], intersection, trimDistance, out var firstTrimmed, out var firstChamferPoint)
            || !TryTrimLineFromIntersection(lines[1], intersection, trimDistance, out var secondTrimmed, out var secondChamferPoint))
        {
            nextDocument = document;
            return false;
        }

        var chamfer = new LineEntity(
            EntityId.Create($"{lines[0].Id.Value}-chamfer-{lines[1].Id.Value}"),
            firstChamferPoint,
            secondChamferPoint,
            lines[0].IsConstruction && lines[1].IsConstruction);

        nextDocument = ReplaceEntities(
            document,
            new Dictionary<string, DrawingEntity>(StringComparer.Ordinal)
            {
                [lines[0].Id.Value] = firstTrimmed,
                [lines[1].Id.Value] = secondTrimmed
            },
            new[] { chamfer });
        return true;
    }

    public static bool TryFilletSelectedLines(
        DrawingDocument document,
        IEnumerable<string> selectedEntityIds,
        double radius,
        out DrawingDocument nextDocument)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectedEntityIds);

        var lines = GetSelectedEntities(document, selectedEntityIds).OfType<LineEntity>().ToArray();
        if (lines.Length != 2
            || !TryGetLineIntersection(lines[0], lines[1], false, false, out var intersection, out _, out _))
        {
            nextDocument = document;
            return false;
        }

        var filletRadius = Math.Max(GeometryTolerance, radius);
        if (!TryBuildFillet(lines[0], lines[1], intersection, filletRadius, out var firstTrimmed, out var secondTrimmed, out var arc))
        {
            nextDocument = document;
            return false;
        }

        var fillet = arc with
        {
            Id = EntityId.Create($"{lines[0].Id.Value}-fillet-{lines[1].Id.Value}"),
            IsConstruction = lines[0].IsConstruction && lines[1].IsConstruction
        };

        nextDocument = ReplaceEntities(
            document,
            new Dictionary<string, DrawingEntity>(StringComparer.Ordinal)
            {
                [lines[0].Id.Value] = firstTrimmed,
                [lines[1].Id.Value] = secondTrimmed
            },
            new[] { fillet });
        return true;
    }

    public static bool TryPowerTrimOrExtendLine(
        DrawingDocument document,
        string targetEntityId,
        Point2 pickedPoint,
        Func<string, EntityId> createEntityId,
        out DrawingDocument nextDocument)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(createEntityId);

        var targetEntity = document.Entities
            .FirstOrDefault(entity => StringComparer.Ordinal.Equals(entity.Id.Value, targetEntityId));
        if (targetEntity is null)
        {
            nextDocument = document;
            return false;
        }

        if (targetEntity is PolylineEntity polylineTarget)
        {
            return TryPowerTrimPolyline(document, polylineTarget, pickedPoint, createEntityId, out nextDocument);
        }

        if (targetEntity is PolygonEntity polygonTarget)
        {
            return TryPowerTrimPolygon(document, polygonTarget, pickedPoint, createEntityId, out nextDocument);
        }

        if (targetEntity is PointEntity pointTarget)
        {
            return RemoveTargetEntity(document, pointTarget.Id.Value, out nextDocument);
        }

        if (targetEntity is CircleEntity circleTarget)
        {
            return TryPowerTrimCircle(document, circleTarget, pickedPoint, out nextDocument);
        }

        if (targetEntity is ArcEntity arcTarget)
        {
            return TryPowerTrimArc(document, arcTarget, pickedPoint, createEntityId, out nextDocument);
        }

        if (targetEntity is EllipseEntity ellipseTarget)
        {
            return TryPowerTrimEllipse(document, ellipseTarget, pickedPoint, createEntityId, out nextDocument);
        }

        if (targetEntity is SplineEntity splineTarget)
        {
            return TryPowerTrimSpline(document, splineTarget, pickedPoint, createEntityId, out nextDocument);
        }

        if (targetEntity is not LineEntity target)
        {
            return FailUnchanged(document, out nextDocument);
        }

        if (!TryProjectParameter(target, pickedPoint, out var pickedParameter))
        {
            nextDocument = document;
            return false;
        }

        var cutParameters = new List<double>();
        AddLineTargetCutParameters(document, target, cutParameters);

        var distinctCuts = cutParameters
            .Where(parameter => double.IsFinite(parameter))
            .DistinctBy(parameter => Math.Round(parameter, 6))
            .OrderBy(parameter => parameter)
            .ToArray();
        if (distinctCuts.Length == 0)
        {
            return pickedParameter is > GeometryTolerance and < 1.0 - GeometryTolerance
                ? RemoveTargetEntity(document, target.Id.Value, out nextDocument)
                : FailUnchanged(document, out nextDocument);
        }

        if (pickedParameter < -GeometryTolerance)
        {
            var extensionCut = distinctCuts.Where(parameter => parameter < 0).LastOrDefault(double.NaN);
            if (!double.IsFinite(extensionCut))
            {
                nextDocument = document;
                return false;
            }

            return ReplaceTargetLine(document, target, target with { Start = PointAtParameter(target, extensionCut) }, out nextDocument);
        }

        if (pickedParameter > 1.0 + GeometryTolerance)
        {
            var extensionCut = distinctCuts.FirstOrDefault(parameter => parameter > 1.0, double.NaN);
            if (!double.IsFinite(extensionCut))
            {
                nextDocument = document;
                return false;
            }

            return ReplaceTargetLine(document, target, target with { End = PointAtParameter(target, extensionCut) }, out nextDocument);
        }

        var insideCuts = distinctCuts
            .Where(parameter => parameter > GeometryTolerance && parameter < 1.0 - GeometryTolerance)
            .ToArray();
        if (insideCuts.Length == 0)
        {
            return pickedParameter is > GeometryTolerance and < 1.0 - GeometryTolerance
                ? RemoveTargetEntity(document, target.Id.Value, out nextDocument)
                : FailUnchanged(document, out nextDocument);
        }

        var left = insideCuts.LastOrDefault(parameter => parameter < pickedParameter, double.NaN);
        var right = insideCuts.FirstOrDefault(parameter => parameter > pickedParameter, double.NaN);
        if (double.IsFinite(left) && double.IsFinite(right))
        {
            var nextEntities = new List<DrawingEntity>();
            foreach (var entity in document.Entities)
            {
                if (!StringComparer.Ordinal.Equals(entity.Id.Value, target.Id.Value))
                {
                    nextEntities.Add(entity);
                    continue;
                }

                nextEntities.Add(target with { End = PointAtParameter(target, left) });
                nextEntities.Add(new LineEntity(
                    createEntityId("line"),
                    PointAtParameter(target, right),
                    target.End,
                    target.IsConstruction));
            }

            nextDocument = new DrawingDocument(nextEntities, document.Dimensions, document.Constraints, document.Metadata);
            return true;
        }

        if (double.IsFinite(left))
        {
            return ReplaceTargetLine(document, target, target with { End = PointAtParameter(target, left) }, out nextDocument);
        }

        return ReplaceTargetLine(document, target, target with { Start = PointAtParameter(target, right) }, out nextDocument);
    }

    private static bool TryPowerTrimPolyline(
        DrawingDocument document,
        PolylineEntity target,
        Point2 pickedPoint,
        Func<string, EntityId> createEntityId,
        out DrawingDocument nextDocument)
    {
        var samples = target.Vertices;
        if (samples.Count < 2
            || !TryBuildSampledPathDistances(samples, out var cumulativeDistances, out var totalLength))
        {
            return FailUnchanged(document, out nextDocument);
        }

        if (!TryProjectPointToSampledPath(samples, cumulativeDistances, pickedPoint, out var pickedDistance))
        {
            return TryExtendPolylineTarget(document, target, pickedPoint, out nextDocument);
        }

        var cutDistances = new List<double>();
        foreach (var cutter in document.Entities)
        {
            if (StringComparer.Ordinal.Equals(cutter.Id.Value, target.Id.Value))
            {
                continue;
            }

            switch (cutter)
            {
                case LineEntity line:
                    AddSampledPathLineCutDistances(samples, cumulativeDistances, line, cutDistances);
                    break;

                case CircleEntity circle:
                    AddSampledPathCircleCutDistances(samples, cumulativeDistances, circle, cutDistances);
                    break;

                case ArcEntity arc:
                    AddSampledPathArcCutDistances(samples, cumulativeDistances, arc, cutDistances);
                    break;

                case EllipseEntity ellipse:
                    AddSampledPathEllipseCutDistances(samples, cumulativeDistances, ellipse, cutDistances);
                    break;

                case PolylineEntity polyline:
                    AddSampledPathSegmentCutDistances(samples, cumulativeDistances, polyline.Vertices, closeLoop: false, cutDistances);
                    break;

                case PolygonEntity polygon:
                    AddSampledPathSegmentCutDistances(samples, cumulativeDistances, polygon.GetVertices(), closeLoop: true, cutDistances);
                    break;

                case SplineEntity spline:
                    AddSampledPathSegmentCutDistances(samples, cumulativeDistances, spline.GetSamplePoints(), closeLoop: false, cutDistances);
                    break;

                case PointEntity point:
                    AddSampledPathPointCutDistance(samples, cumulativeDistances, point.Location, cutDistances);
                    break;
            }
        }

        if (!TryGetPickedSampledPathSegmentBounds(
                samples,
                cumulativeDistances,
                pickedPoint,
                pickedDistance,
                out var pickedSegmentStartDistance,
                out var pickedSegmentEndDistance))
        {
            return FailUnchanged(document, out nextDocument);
        }

        var distinctCuts = cutDistances
            .Where(distance => double.IsFinite(distance))
            .Where(distance => distance > GeometryTolerance && distance < totalLength - GeometryTolerance)
            .Where(distance => distance > pickedSegmentStartDistance + GeometryTolerance
                && distance < pickedSegmentEndDistance - GeometryTolerance)
            .DistinctBy(distance => Math.Round(distance, 6))
            .OrderBy(distance => distance)
            .ToArray();
        if (distinctCuts.Length == 0)
        {
            return pickedDistance > GeometryTolerance && pickedDistance < totalLength - GeometryTolerance
                ? RemoveTargetEntity(document, target.Id.Value, out nextDocument)
                : FailUnchanged(document, out nextDocument);
        }

        var left = distinctCuts.LastOrDefault(distance => distance < pickedDistance, double.NaN);
        var right = distinctCuts.FirstOrDefault(distance => distance > pickedDistance, double.NaN);
        var keptPolylines = new List<DrawingEntity>();
        if (double.IsFinite(left) && double.IsFinite(right))
        {
            AddTrimmedPolylineIfValid(target, samples, cumulativeDistances, 0, left, target.Id, keptPolylines);
            AddTrimmedPolylineIfValid(target, samples, cumulativeDistances, right, totalLength, createEntityId("polyline"), keptPolylines);
        }
        else if (double.IsFinite(left))
        {
            AddTrimmedPolylineIfValid(target, samples, cumulativeDistances, 0, left, target.Id, keptPolylines);
        }
        else
        {
            AddTrimmedPolylineIfValid(target, samples, cumulativeDistances, right, totalLength, target.Id, keptPolylines);
        }

        if (keptPolylines.Count == 0)
        {
            return FailUnchanged(document, out nextDocument);
        }

        nextDocument = ReplaceEntities(
            document,
            new Dictionary<string, DrawingEntity>(StringComparer.Ordinal)
            {
                [target.Id.Value] = keptPolylines[0]
            },
            keptPolylines.Skip(1));
        return true;
    }

    private static bool TryExtendPolylineTarget(
        DrawingDocument document,
        PolylineEntity target,
        Point2 pickedPoint,
        out DrawingDocument nextDocument)
    {
        if (!TryGetSampledPathExtensionLine(target.Vertices, target.Id, pickedPoint, out var extendStart, out var extensionLine))
        {
            return FailUnchanged(document, out nextDocument);
        }

        var cutParameters = new List<double>();
        AddLineTargetCutParameters(document, extensionLine, cutParameters);
        var extensionParameter = cutParameters
            .Where(parameter => parameter > GeometryTolerance)
            .DistinctBy(parameter => Math.Round(parameter, 6))
            .OrderBy(parameter => parameter)
            .FirstOrDefault(double.NaN);
        if (!double.IsFinite(extensionParameter))
        {
            return FailUnchanged(document, out nextDocument);
        }

        var extensionPoint = PointAtParameter(extensionLine, extensionParameter);
        var vertices = target.Vertices.ToList();
        if (extendStart)
        {
            if (Distance(vertices[0], extensionPoint) <= GeometryTolerance)
            {
                return FailUnchanged(document, out nextDocument);
            }

            vertices.Insert(0, extensionPoint);
        }
        else
        {
            if (Distance(vertices[^1], extensionPoint) <= GeometryTolerance)
            {
                return FailUnchanged(document, out nextDocument);
            }

            vertices.Add(extensionPoint);
        }

        nextDocument = ReplaceEntities(
            document,
            new Dictionary<string, DrawingEntity>(StringComparer.Ordinal)
            {
                [target.Id.Value] = new PolylineEntity(target.Id, vertices, target.IsConstruction)
            },
            Array.Empty<DrawingEntity>());
        return true;
    }

    private static bool TryPowerTrimPolygon(
        DrawingDocument document,
        PolygonEntity target,
        Point2 pickedPoint,
        Func<string, EntityId> createEntityId,
        out DrawingDocument nextDocument)
    {
        var samples = GetClosedPathSamples(target.GetVertices());
        if (samples.Count < 4
            || !TryBuildSampledPathDistances(samples, out var cumulativeDistances, out var totalLength)
            || !TryProjectPointToSampledPath(samples, cumulativeDistances, pickedPoint, out var pickedDistance))
        {
            return FailUnchanged(document, out nextDocument);
        }

        var cutDistances = new List<double>();
        foreach (var cutter in document.Entities)
        {
            if (StringComparer.Ordinal.Equals(cutter.Id.Value, target.Id.Value))
            {
                continue;
            }

            switch (cutter)
            {
                case LineEntity line:
                    AddSampledPathLineCutDistances(samples, cumulativeDistances, line, cutDistances);
                    break;

                case CircleEntity circle:
                    AddSampledPathCircleCutDistances(samples, cumulativeDistances, circle, cutDistances);
                    break;

                case ArcEntity arc:
                    AddSampledPathArcCutDistances(samples, cumulativeDistances, arc, cutDistances);
                    break;

                case EllipseEntity ellipse:
                    AddSampledPathEllipseCutDistances(samples, cumulativeDistances, ellipse, cutDistances);
                    break;

                case PolylineEntity polyline:
                    AddSampledPathSegmentCutDistances(samples, cumulativeDistances, polyline.Vertices, closeLoop: false, cutDistances);
                    break;

                case PolygonEntity polygon:
                    AddSampledPathSegmentCutDistances(samples, cumulativeDistances, polygon.GetVertices(), closeLoop: true, cutDistances);
                    break;

                case SplineEntity spline:
                    AddSampledPathSegmentCutDistances(samples, cumulativeDistances, spline.GetSamplePoints(), closeLoop: false, cutDistances);
                    break;

                case PointEntity point:
                    AddSampledPathPointCutDistance(samples, cumulativeDistances, point.Location, cutDistances);
                    break;
            }
        }

        if (!TryGetPickedSampledPathSegmentBounds(
                samples,
                cumulativeDistances,
                pickedPoint,
                pickedDistance,
                out var pickedSegmentStartDistance,
                out var pickedSegmentEndDistance))
        {
            return FailUnchanged(document, out nextDocument);
        }

        var distinctCuts = cutDistances
            .Where(distance => double.IsFinite(distance))
            .Where(distance => distance > GeometryTolerance && distance < totalLength - GeometryTolerance)
            .Where(distance => distance > pickedSegmentStartDistance + GeometryTolerance
                && distance < pickedSegmentEndDistance - GeometryTolerance)
            .DistinctBy(distance => Math.Round(distance, 6))
            .OrderBy(distance => distance)
            .ToArray();
        if (distinctCuts.Length < 1)
        {
            return FailUnchanged(document, out nextDocument);
        }

        var boundaryDistances = cumulativeDistances
            .Concat(distinctCuts)
            .Where(distance => double.IsFinite(distance))
            .Select(distance => Math.Clamp(distance, 0.0, totalLength))
            .DistinctBy(distance => Math.Round(distance, 6))
            .OrderBy(distance => distance)
            .ToArray();
        if (boundaryDistances.Length < 2)
        {
            return FailUnchanged(document, out nextDocument);
        }

        var omittedSegmentIndex = FindPickedBoundarySpan(
            samples,
            cumulativeDistances,
            boundaryDistances,
            pickedDistance,
            pickedPoint);
        var keptLines = new List<DrawingEntity>();
        AddLineSegmentsFromPathBoundaries(
            samples,
            cumulativeDistances,
            boundaryDistances,
            omittedSegmentIndex,
            target.IsConstruction,
            "polygon-line",
            createEntityId,
            keptLines);
        if (keptLines.Count == 0)
        {
            return FailUnchanged(document, out nextDocument);
        }

        var coincidentConstraints = CreateCoincidentConstraintsForSharedLineEndpoints(keptLines);
        nextDocument = ReplaceEntityWithAdditionsRemovingReferences(document, target.Id.Value, keptLines, coincidentConstraints);
        return true;
    }

    private static bool TryGetPickedSampledPathSegmentBounds(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 pickedPoint,
        double pickedDistance,
        out double startDistance,
        out double endDistance)
    {
        startDistance = default;
        endDistance = default;
        var closestSegmentIndex = -1;
        var closestDistance = double.PositiveInfinity;
        var normalizedPickedDistance = Math.Clamp(pickedDistance, 0.0, cumulativeDistances[^1]);

        for (var index = 1; index < samples.Count; index++)
        {
            var segmentLength = cumulativeDistances[index] - cumulativeDistances[index - 1];
            if (segmentLength <= GeometryTolerance)
            {
                continue;
            }

            var segment = new LineEntity(EntityId.Create("picked-polygon-segment"), samples[index - 1], samples[index]);
            if (!TryProjectParameter(segment, pickedPoint, out var parameter))
            {
                continue;
            }

            var clampedParameter = Math.Clamp(parameter, 0.0, 1.0);
            var projectedPoint = PointAtParameter(segment, clampedParameter);
            var distance = Distance(projectedPoint, pickedPoint);
            var projectedDistance = cumulativeDistances[index - 1] + (segmentLength * clampedParameter);
            var containsPickedDistance = normalizedPickedDistance >= cumulativeDistances[index - 1] - GeometryTolerance
                && normalizedPickedDistance <= cumulativeDistances[index] + GeometryTolerance;
            var better = distance < closestDistance - GeometryTolerance
                || (Math.Abs(distance - closestDistance) <= GeometryTolerance
                    && containsPickedDistance
                    && Math.Abs(projectedDistance - normalizedPickedDistance)
                        < Math.Abs(GetPickedDistanceOnSegment(cumulativeDistances, closestSegmentIndex, normalizedPickedDistance) - normalizedPickedDistance));

            if (!better)
            {
                continue;
            }

            closestDistance = distance;
            closestSegmentIndex = index;
        }

        if (closestSegmentIndex < 1)
        {
            return false;
        }

        startDistance = cumulativeDistances[closestSegmentIndex - 1];
        endDistance = cumulativeDistances[closestSegmentIndex];
        return endDistance - startDistance > GeometryTolerance;
    }

    private static double GetPickedDistanceOnSegment(
        IReadOnlyList<double> cumulativeDistances,
        int segmentIndex,
        double pickedDistance)
    {
        if (segmentIndex < 1 || segmentIndex >= cumulativeDistances.Count)
        {
            return double.PositiveInfinity;
        }

        return Math.Clamp(pickedDistance, cumulativeDistances[segmentIndex - 1], cumulativeDistances[segmentIndex]);
    }

    private static bool TryPowerTrimCircle(
        DrawingDocument document,
        CircleEntity target,
        Point2 pickedPoint,
        out DrawingDocument nextDocument)
    {
        if (!TryGetCirclePickAngle(target, pickedPoint, out var pickedAngle))
        {
            return FailUnchanged(document, out nextDocument);
        }

        var cutAngles = new List<double>();
        AddCircleTargetCutAngles(document, target, cutAngles);

        var distinctCuts = cutAngles
            .Where(double.IsFinite)
            .DistinctBy(angle => Math.Round(angle, 6))
            .OrderBy(angle => angle)
            .ToArray();
        if (distinctCuts.Length < 2)
        {
            return FailUnchanged(document, out nextDocument);
        }

        pickedAngle = NormalizeAngleDegrees(pickedAngle);
        var left = distinctCuts.LastOrDefault(angle => angle < pickedAngle - GeometryTolerance, double.NaN);
        if (!double.IsFinite(left))
        {
            left = distinctCuts[^1] - 360.0;
        }

        var right = distinctCuts.FirstOrDefault(angle => angle > pickedAngle + GeometryTolerance, double.NaN);
        if (!double.IsFinite(right))
        {
            right = distinctCuts[0] + 360.0;
        }

        if (right - left <= GeometryTolerance)
        {
            return FailUnchanged(document, out nextDocument);
        }

        var startAngle = NormalizeAngleDegrees(right);
        var endAngle = left;
        while (endAngle <= startAngle + GeometryTolerance)
        {
            endAngle += 360.0;
        }

        if (endAngle - startAngle <= GeometryTolerance)
        {
            return FailUnchanged(document, out nextDocument);
        }

        var replacement = new ArcEntity(
            target.Id,
            target.Center,
            target.Radius,
            startAngle,
            endAngle,
            target.IsConstruction);

        nextDocument = ReplaceEntities(
            document,
            new Dictionary<string, DrawingEntity>(StringComparer.Ordinal)
            {
                [target.Id.Value] = replacement
            },
            Array.Empty<DrawingEntity>());
        return true;
    }

    private static bool TryPowerTrimArc(
        DrawingDocument document,
        ArcEntity target,
        Point2 pickedPoint,
        Func<string, EntityId> createEntityId,
        out DrawingDocument nextDocument)
    {
        var sweep = GetPositiveSweepDegrees(target.StartAngleDegrees, target.EndAngleDegrees);
        if (target.Radius <= GeometryTolerance || sweep <= GeometryTolerance)
        {
            return FailUnchanged(document, out nextDocument);
        }

        if (!TryGetArcSweepParameter(target, pickedPoint, allowEndpoints: true, out var pickedParameter))
        {
            return TryGetArcExtensionParameter(target, pickedPoint, sweep, out var extensionParameter)
                ? TryExtendArcTarget(document, target, sweep, extensionParameter, out nextDocument)
                : FailUnchanged(document, out nextDocument);
        }

        var cutParameters = new List<double>();
        var targetCircle = new CircleEntity(target.Id, target.Center, target.Radius, target.IsConstruction);
        foreach (var cutter in document.Entities.OfType<LineEntity>())
        {
            if (TryGetLineCircleIntersections(targetCircle, cutter, out var intersections))
            {
                AddArcCutParameters(target, intersections, cutParameters);
            }
        }

        foreach (var cutter in document.Entities.OfType<CircleEntity>())
        {
            if (TryGetCircleCircleIntersections(targetCircle, cutter, out var intersections))
            {
                AddArcCutParameters(target, intersections, cutParameters);
            }
        }

        foreach (var cutter in document.Entities.OfType<ArcEntity>())
        {
            if (StringComparer.Ordinal.Equals(cutter.Id.Value, target.Id.Value))
            {
                continue;
            }

            if (TryGetArcArcIntersections(target, cutter, out var intersections))
            {
                AddArcCutParameters(target, intersections, cutParameters);
            }
        }

        foreach (var cutter in document.Entities)
        {
            if (StringComparer.Ordinal.Equals(cutter.Id.Value, target.Id.Value))
            {
                continue;
            }

            switch (cutter)
            {
                case EllipseEntity ellipse:
                    AddArcTargetSegmentCutParameters(target, ellipse.GetSamplePoints(144), closeLoop: false, cutParameters);
                    break;

                case PolylineEntity polyline:
                    AddArcTargetSegmentCutParameters(target, polyline.Vertices, closeLoop: false, cutParameters);
                    break;

                case PolygonEntity polygon:
                    AddArcTargetSegmentCutParameters(target, polygon.GetVertices(), closeLoop: true, cutParameters);
                    break;

                case SplineEntity spline:
                    AddArcSplineCutParameters(target, spline, cutParameters);
                    break;

                case PointEntity point:
                    AddArcCutParameters(target, new[] { point.Location }, cutParameters);
                    break;
            }
        }

        var distinctCuts = cutParameters
            .Where(parameter => double.IsFinite(parameter))
            .Where(parameter => parameter > GeometryTolerance && parameter < sweep - GeometryTolerance)
            .DistinctBy(parameter => Math.Round(parameter, 6))
            .OrderBy(parameter => parameter)
            .ToArray();
        if (distinctCuts.Length == 0)
        {
            return pickedParameter > GeometryTolerance && pickedParameter < sweep - GeometryTolerance
                ? RemoveTargetEntity(document, target.Id.Value, out nextDocument)
                : FailUnchanged(document, out nextDocument);
        }

        var left = distinctCuts.LastOrDefault(parameter => parameter < pickedParameter, double.NaN);
        var right = distinctCuts.FirstOrDefault(parameter => parameter > pickedParameter, double.NaN);
        if (double.IsFinite(left) && double.IsFinite(right))
        {
            var keptArcs = new List<DrawingEntity>();
            AddTrimmedArcIfValid(target, 0, left, target.Id, keptArcs);
            AddTrimmedArcIfValid(target, right, sweep, createEntityId("arc"), keptArcs);
            if (keptArcs.Count == 0)
            {
                return FailUnchanged(document, out nextDocument);
            }

            nextDocument = ReplaceEntities(
                document,
                new Dictionary<string, DrawingEntity>(StringComparer.Ordinal)
                {
                    [target.Id.Value] = keptArcs[0]
                },
                keptArcs.Skip(1));
            return true;
        }

        if (double.IsFinite(left))
        {
            return ReplaceTargetArc(document, target, 0, left, out nextDocument);
        }

        return ReplaceTargetArc(document, target, right, sweep, out nextDocument);
    }

    private static bool TryExtendArcTarget(
        DrawingDocument document,
        ArcEntity target,
        double sweep,
        double pickedParameter,
        out DrawingDocument nextDocument)
    {
        if (sweep >= 360.0 - GeometryTolerance)
        {
            return FailUnchanged(document, out nextDocument);
        }

        var boundaryParameters = new List<double>();
        AddArcExtensionBoundaryParameters(document, target, sweep, boundaryParameters);
        var distinctParameters = boundaryParameters
            .Where(double.IsFinite)
            .DistinctBy(parameter => Math.Round(parameter, 6))
            .OrderBy(parameter => parameter)
            .ToArray();

        if (pickedParameter < -GeometryTolerance)
        {
            var extensionParameter = distinctParameters
                .Where(parameter => parameter < -GeometryTolerance)
                .LastOrDefault(double.NaN);
            return double.IsFinite(extensionParameter)
                ? ReplaceTargetArc(document, target, extensionParameter, sweep, out nextDocument)
                : FailUnchanged(document, out nextDocument);
        }

        if (pickedParameter > sweep + GeometryTolerance)
        {
            var extensionParameter = distinctParameters
                .FirstOrDefault(parameter => parameter > sweep + GeometryTolerance, double.NaN);
            return double.IsFinite(extensionParameter)
                ? ReplaceTargetArc(document, target, 0, extensionParameter, out nextDocument)
                : FailUnchanged(document, out nextDocument);
        }

        return FailUnchanged(document, out nextDocument);
    }

    private static bool TryPowerTrimEllipse(
        DrawingDocument document,
        EllipseEntity target,
        Point2 pickedPoint,
        Func<string, EntityId> createEntityId,
        out DrawingDocument nextDocument)
    {
        var sweep = GetPositiveSweepDegrees(target.StartParameterDegrees, target.EndParameterDegrees);
        if (sweep <= GeometryTolerance)
        {
            return FailUnchanged(document, out nextDocument);
        }

        if (!TryGetEllipseSweepParameter(target, pickedPoint, allowEndpoints: true, out var pickedParameter))
        {
            return TryGetEllipseExtensionParameter(target, pickedPoint, sweep, out var extensionParameter)
                ? TryExtendEllipseTarget(document, target, sweep, extensionParameter, out nextDocument)
                : FailUnchanged(document, out nextDocument);
        }

        var cutParameters = new List<double>();
        foreach (var cutter in document.Entities.OfType<LineEntity>())
        {
            if (TryGetLineEllipseIntersections(target, cutter, requireLineSegment: true, out var intersections))
            {
                AddEllipseCutParameters(target, intersections, cutParameters);
            }
        }

        foreach (var cutter in document.Entities)
        {
            if (StringComparer.Ordinal.Equals(cutter.Id.Value, target.Id.Value))
            {
                continue;
            }

            switch (cutter)
            {
                case CircleEntity circle:
                    AddEllipseTargetSegmentCutParameters(target, GetCircleSamplePoints(circle, 144), closeLoop: true, cutParameters);
                    break;

                case ArcEntity arc:
                    AddEllipseTargetSegmentCutParameters(target, arc.GetSamplePoints(72), closeLoop: false, cutParameters);
                    break;

                case EllipseEntity ellipse:
                    AddEllipseTargetSegmentCutParameters(target, ellipse.GetSamplePoints(144), closeLoop: false, cutParameters);
                    break;

                case PolylineEntity polyline:
                    AddEllipseTargetSegmentCutParameters(target, polyline.Vertices, closeLoop: false, cutParameters);
                    break;

                case PolygonEntity polygon:
                    AddEllipseTargetSegmentCutParameters(target, polygon.GetVertices(), closeLoop: true, cutParameters);
                    break;

                case SplineEntity spline:
                    AddEllipseSplineCutParameters(target, spline, cutParameters);
                    break;

                case PointEntity point:
                    AddEllipseCutParameters(target, new[] { point.Location }, cutParameters);
                    break;
            }
        }

        var distinctCuts = cutParameters
            .Where(parameter => double.IsFinite(parameter))
            .Where(parameter => parameter > GeometryTolerance && parameter < sweep - GeometryTolerance)
            .DistinctBy(parameter => Math.Round(parameter, 6))
            .OrderBy(parameter => parameter)
            .ToArray();
        if (distinctCuts.Length == 0)
        {
            return sweep < 360.0 - GeometryTolerance
                && pickedParameter > GeometryTolerance
                && pickedParameter < sweep - GeometryTolerance
                    ? RemoveTargetEntity(document, target.Id.Value, out nextDocument)
                    : FailUnchanged(document, out nextDocument);
        }

        var left = distinctCuts.LastOrDefault(parameter => parameter < pickedParameter, double.NaN);
        var right = distinctCuts.FirstOrDefault(parameter => parameter > pickedParameter, double.NaN);
        if (double.IsFinite(left) && double.IsFinite(right))
        {
            var keptEllipses = new List<DrawingEntity>();
            AddTrimmedEllipseIfValid(target, 0, left, target.Id, keptEllipses);
            AddTrimmedEllipseIfValid(target, right, sweep, createEntityId("ellipse"), keptEllipses);
            if (keptEllipses.Count == 0)
            {
                return FailUnchanged(document, out nextDocument);
            }

            nextDocument = ReplaceEntities(
                document,
                new Dictionary<string, DrawingEntity>(StringComparer.Ordinal)
                {
                    [target.Id.Value] = keptEllipses[0]
                },
                keptEllipses.Skip(1));
            return true;
        }

        if (double.IsFinite(left))
        {
            return ReplaceTargetEllipse(document, target, 0, left, out nextDocument);
        }

        return ReplaceTargetEllipse(document, target, right, sweep, out nextDocument);
    }

    private static bool TryExtendEllipseTarget(
        DrawingDocument document,
        EllipseEntity target,
        double sweep,
        double pickedParameter,
        out DrawingDocument nextDocument)
    {
        if (sweep >= 360.0 - GeometryTolerance)
        {
            return FailUnchanged(document, out nextDocument);
        }

        var boundaryParameters = new List<double>();
        AddEllipseExtensionBoundaryParameters(document, target, sweep, boundaryParameters);
        var distinctParameters = boundaryParameters
            .Where(double.IsFinite)
            .DistinctBy(parameter => Math.Round(parameter, 6))
            .OrderBy(parameter => parameter)
            .ToArray();

        if (pickedParameter < -GeometryTolerance)
        {
            var extensionParameter = distinctParameters
                .Where(parameter => parameter < -GeometryTolerance)
                .LastOrDefault(double.NaN);
            return double.IsFinite(extensionParameter)
                ? ReplaceTargetEllipse(document, target, extensionParameter, sweep, out nextDocument)
                : FailUnchanged(document, out nextDocument);
        }

        if (pickedParameter > sweep + GeometryTolerance)
        {
            var extensionParameter = distinctParameters
                .FirstOrDefault(parameter => parameter > sweep + GeometryTolerance, double.NaN);
            return double.IsFinite(extensionParameter)
                ? ReplaceTargetEllipse(document, target, 0, extensionParameter, out nextDocument)
                : FailUnchanged(document, out nextDocument);
        }

        return FailUnchanged(document, out nextDocument);
    }

    private static bool TryPowerTrimSpline(
        DrawingDocument document,
        SplineEntity target,
        Point2 pickedPoint,
        Func<string, EntityId> createEntityId,
        out DrawingDocument nextDocument)
    {
        var samples = target.GetSamplePoints();
        if (samples.Count < 2
            || !TryBuildSampledPathDistances(samples, out var cumulativeDistances, out var totalLength))
        {
            return FailUnchanged(document, out nextDocument);
        }

        if (!TryProjectPointToSampledPath(samples, cumulativeDistances, pickedPoint, out var pickedDistance))
        {
            return TryExtendSplineTarget(document, target, samples, pickedPoint, out nextDocument);
        }

        var cutDistances = new List<double>();
        var preciseCuts = new List<SampledPathCut>();
        foreach (var cutter in document.Entities)
        {
            if (StringComparer.Ordinal.Equals(cutter.Id.Value, target.Id.Value))
            {
                continue;
            }

            switch (cutter)
            {
                case LineEntity line:
                    AddSplineTargetLineCuts(target, samples, cumulativeDistances, line, preciseCuts);
                    break;

                case CircleEntity circle:
                    AddSplineTargetCircleCuts(target, samples, cumulativeDistances, circle, preciseCuts);
                    break;

                case ArcEntity arc:
                    AddSplineTargetArcCuts(target, samples, cumulativeDistances, arc, preciseCuts);
                    break;

                case EllipseEntity ellipse:
                    AddSplineTargetEllipseCuts(target, samples, cumulativeDistances, ellipse, preciseCuts);
                    break;

                case PolylineEntity polyline:
                    AddSplineTargetSegmentCuts(target, samples, cumulativeDistances, polyline.Vertices, closeLoop: false, preciseCuts);
                    break;

                case PolygonEntity polygon:
                    AddSplineTargetSegmentCuts(target, samples, cumulativeDistances, polygon.GetVertices(), closeLoop: true, preciseCuts);
                    break;

                case SplineEntity spline:
                    AddSplineTargetSplineCuts(target, samples, cumulativeDistances, spline, preciseCuts);
                    break;

                case PointEntity point:
                    AddSampledPathPointCutDistance(samples, cumulativeDistances, point.Location, cutDistances);
                    break;
            }
        }

        var distinctCuts = cutDistances
            .Select(distance => new SampledPathCut(distance, PointAtSampledPathDistance(samples, cumulativeDistances, distance)))
            .Concat(preciseCuts)
            .Where(cut => double.IsFinite(cut.Distance))
            .Where(cut => cut.Distance > GeometryTolerance && cut.Distance < totalLength - GeometryTolerance)
            .DistinctBy(cut => Math.Round(cut.Distance, 6))
            .OrderBy(cut => cut.Distance)
            .ToArray();
        if (distinctCuts.Length == 0)
        {
            return pickedDistance > GeometryTolerance && pickedDistance < totalLength - GeometryTolerance
                ? RemoveTargetEntity(document, target.Id.Value, out nextDocument)
                : FailUnchanged(document, out nextDocument);
        }

        var leftCandidates = distinctCuts.Where(cut => cut.Distance < pickedDistance).ToArray();
        var rightCandidates = distinctCuts.Where(cut => cut.Distance > pickedDistance).ToArray();
        var hasLeft = leftCandidates.Length > 0;
        var hasRight = rightCandidates.Length > 0;
        var left = hasLeft ? leftCandidates[^1] : default;
        var right = hasRight ? rightCandidates[0] : default;
        var keptSplines = new List<DrawingEntity>();
        if (hasLeft && hasRight)
        {
            AddTrimmedSplineIfValid(target, samples, cumulativeDistances, 0, left.Distance, target.Id, keptSplines, endPointOverride: left.Point);
            AddTrimmedSplineIfValid(target, samples, cumulativeDistances, right.Distance, totalLength, createEntityId("spline"), keptSplines, startPointOverride: right.Point);
        }
        else if (hasLeft)
        {
            AddTrimmedSplineIfValid(target, samples, cumulativeDistances, 0, left.Distance, target.Id, keptSplines, endPointOverride: left.Point);
        }
        else
        {
            AddTrimmedSplineIfValid(target, samples, cumulativeDistances, right.Distance, totalLength, target.Id, keptSplines, startPointOverride: right.Point);
        }

        if (keptSplines.Count == 0)
        {
            return FailUnchanged(document, out nextDocument);
        }

        nextDocument = ReplaceEntities(
            document,
            new Dictionary<string, DrawingEntity>(StringComparer.Ordinal)
            {
                [target.Id.Value] = keptSplines[0]
            },
            keptSplines.Skip(1));
        return true;
    }

    private static bool TryExtendSplineTarget(
        DrawingDocument document,
        SplineEntity target,
        IReadOnlyList<Point2> samples,
        Point2 pickedPoint,
        out DrawingDocument nextDocument)
    {
        if (!TryGetSampledPathExtensionLine(samples, target.Id, pickedPoint, out var extendStart, out var extensionLine))
        {
            return FailUnchanged(document, out nextDocument);
        }

        var cutParameters = new List<double>();
        AddLineTargetCutParameters(document, extensionLine, cutParameters);
        var extensionParameter = cutParameters
            .Where(parameter => parameter > GeometryTolerance)
            .DistinctBy(parameter => Math.Round(parameter, 6))
            .OrderBy(parameter => parameter)
            .FirstOrDefault(double.NaN);
        if (!double.IsFinite(extensionParameter))
        {
            return FailUnchanged(document, out nextDocument);
        }

        var extensionPoint = PointAtParameter(extensionLine, extensionParameter);
        var points = samples.ToList();
        if (extendStart)
        {
            if (Distance(points[0], extensionPoint) <= GeometryTolerance)
            {
                return FailUnchanged(document, out nextDocument);
            }

            points.Insert(0, extensionPoint);
        }
        else
        {
            if (Distance(points[^1], extensionPoint) <= GeometryTolerance)
            {
                return FailUnchanged(document, out nextDocument);
            }

            points.Add(extensionPoint);
        }

        nextDocument = ReplaceEntities(
            document,
            new Dictionary<string, DrawingEntity>(StringComparer.Ordinal)
            {
                [target.Id.Value] = new SplineEntity(target.Id, 1, points, Array.Empty<double>(), isConstruction: target.IsConstruction)
            },
            Array.Empty<DrawingEntity>());
        return true;
    }

    public static int PowerTrimOrExtendLines(
        DrawingDocument document,
        IEnumerable<PowerTrimLinePick> picks,
        Func<string, EntityId> createEntityId,
        out DrawingDocument nextDocument)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(picks);
        ArgumentNullException.ThrowIfNull(createEntityId);

        nextDocument = document;
        var appliedCount = 0;
        var appliedTargets = new HashSet<string>(StringComparer.Ordinal);
        foreach (var pick in picks)
        {
            if (!appliedTargets.Add(pick.TargetEntityId))
            {
                continue;
            }

            if (TryPowerTrimOrExtendLine(
                    nextDocument,
                    pick.TargetEntityId,
                    pick.PickedPoint,
                    createEntityId,
                    out var candidateDocument))
            {
                nextDocument = candidateDocument;
                appliedCount++;
            }
        }

        return appliedCount;
    }

    private static DrawingDocument ReplaceSelected(
        DrawingDocument document,
        IEnumerable<string> selectedEntityIds,
        Func<DrawingEntity, DrawingEntity> replace)
    {
        var selected = selectedEntityIds.ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0)
        {
            return document;
        }

        return new DrawingDocument(
            document.Entities.Select(entity => selected.Contains(entity.Id.Value) ? replace(entity) : entity),
            document.Dimensions,
            document.Constraints,
            document.Metadata);
    }

    private static IReadOnlyList<DrawingEntity> GetSelectedEntities(
        DrawingDocument document,
        IEnumerable<string> selectedEntityIds)
    {
        var selected = selectedEntityIds.ToHashSet(StringComparer.Ordinal);
        return document.Entities
            .Where(entity => selected.Contains(entity.Id.Value))
            .ToArray();
    }

    private static DrawingDocument ReplaceEntities(
        DrawingDocument document,
        IReadOnlyDictionary<string, DrawingEntity> replacements,
        IEnumerable<DrawingEntity> additions)
    {
        return new DrawingDocument(
            document.Entities.Select(entity =>
                replacements.TryGetValue(entity.Id.Value, out var replacement) ? replacement : entity)
                .Concat(additions),
            document.Dimensions,
            document.Constraints,
            document.Metadata);
    }

    private static DrawingDocument ReplaceEntityWithAdditionsRemovingReferences(
        DrawingDocument document,
        string removedEntityId,
        IEnumerable<DrawingEntity> additions,
        IEnumerable<SketchConstraint>? additionalConstraints = null)
    {
        var newEntities = additions.ToArray();
        var newConstraints = additionalConstraints?.ToArray() ?? Array.Empty<SketchConstraint>();
        var nextEntities = new List<DrawingEntity>(document.Entities.Count + newEntities.Length);
        foreach (var entity in document.Entities)
        {
            if (StringComparer.Ordinal.Equals(entity.Id.Value, removedEntityId))
            {
                nextEntities.AddRange(newEntities);
                continue;
            }

            nextEntities.Add(entity);
        }

        return new DrawingDocument(
            nextEntities,
            document.Dimensions.Where(dimension => !ReferenceKeysContainEntity(dimension.ReferenceKeys, removedEntityId)),
            document.Constraints
                .Where(constraint => !ReferenceKeysContainEntity(constraint.ReferenceKeys, removedEntityId))
                .Concat(newConstraints),
            document.Metadata);
    }

    private static IReadOnlyList<SketchConstraint> CreateCoincidentConstraintsForSharedLineEndpoints(
        IEnumerable<DrawingEntity> entities)
    {
        var endpoints = entities
            .OfType<LineEntity>()
            .SelectMany(line => new[]
            {
                new LineEndpointReference(line, "start", line.Start),
                new LineEndpointReference(line, "end", line.End)
            })
            .ToArray();
        var constraints = new List<SketchConstraint>();

        for (var firstIndex = 0; firstIndex < endpoints.Length; firstIndex++)
        {
            var first = endpoints[firstIndex];
            for (var secondIndex = firstIndex + 1; secondIndex < endpoints.Length; secondIndex++)
            {
                var second = endpoints[secondIndex];
                if (first.Line.Id == second.Line.Id
                    || Distance(first.Point, second.Point) > GeometryTolerance)
                {
                    continue;
                }

                var firstReference = $"{first.Line.Id.Value}:{first.Target}";
                var secondReference = $"{second.Line.Id.Value}:{second.Target}";
                constraints.Add(new SketchConstraint(
                    $"coincident-{first.Line.Id.Value}-{first.Target}-{second.Line.Id.Value}-{second.Target}",
                    SketchConstraintKind.Coincident,
                    new[] { firstReference, secondReference },
                    SketchConstraintState.Satisfied));
            }
        }

        return constraints;
    }

    private static bool ReplaceTargetLine(
        DrawingDocument document,
        LineEntity target,
        LineEntity replacement,
        out DrawingDocument nextDocument)
    {
        if (Distance(replacement.Start, replacement.End) <= GeometryTolerance)
        {
            nextDocument = document;
            return false;
        }

        nextDocument = ReplaceEntities(
            document,
            new Dictionary<string, DrawingEntity>(StringComparer.Ordinal)
            {
                [target.Id.Value] = replacement
            },
            Array.Empty<DrawingEntity>());
        return true;
    }

    private static bool RemoveTargetEntity(
        DrawingDocument document,
        string entityId,
        out DrawingDocument nextDocument)
    {
        var nextEntities = document.Entities
            .Where(entity => !StringComparer.Ordinal.Equals(entity.Id.Value, entityId))
            .ToArray();
        if (nextEntities.Length == document.Entities.Count)
        {
            nextDocument = document;
            return false;
        }

        nextDocument = new DrawingDocument(
            nextEntities,
            document.Dimensions.Where(dimension => !ReferenceKeysContainEntity(dimension.ReferenceKeys, entityId)),
            document.Constraints.Where(constraint => !ReferenceKeysContainEntity(constraint.ReferenceKeys, entityId)),
            document.Metadata);
        return true;
    }

    private static bool FailUnchanged(DrawingDocument document, out DrawingDocument nextDocument)
    {
        nextDocument = document;
        return false;
    }

    private static bool TryOffsetEntity(
        DrawingEntity entity,
        Point2 throughPoint,
        EntityId entityId,
        out DrawingEntity offsetEntity)
    {
        switch (entity)
        {
            case LineEntity line:
                if (TryOffsetLine(line, throughPoint, entityId, out var offsetLine))
                {
                    offsetEntity = offsetLine;
                    return true;
                }

                break;
            case CircleEntity circle:
                var circleRadius = Distance(circle.Center, throughPoint);
                if (circleRadius > GeometryTolerance)
                {
                    offsetEntity = new CircleEntity(entityId, circle.Center, circleRadius, circle.IsConstruction);
                    return true;
                }

                break;
            case ArcEntity arc:
                var arcRadius = Distance(arc.Center, throughPoint);
                if (arcRadius > GeometryTolerance)
                {
                    offsetEntity = new ArcEntity(
                        entityId,
                        arc.Center,
                        arcRadius,
                        arc.StartAngleDegrees,
                        arc.EndAngleDegrees,
                        arc.IsConstruction);
                    return true;
                }

                break;
            case PolylineEntity polyline:
                if (TryOffsetPolyline(polyline, throughPoint, entityId, out var offsetPolyline))
                {
                    offsetEntity = offsetPolyline;
                    return true;
                }

                break;
            case SplineEntity spline:
                if (TryOffsetSpline(spline, throughPoint, entityId, out var offsetSpline))
                {
                    offsetEntity = offsetSpline;
                    return true;
                }

                break;
        }

        offsetEntity = default!;
        return false;
    }

    private static bool TryOffsetLine(LineEntity line, Point2 throughPoint, EntityId entityId, out LineEntity offsetLine)
    {
        if (!TryGetLineOffsetVector(line, throughPoint, out var offset))
        {
            offsetLine = default!;
            return false;
        }

        offsetLine = new LineEntity(entityId, Add(line.Start, offset), Add(line.End, offset), line.IsConstruction);
        return true;
    }

    private static bool TryGetLineOffsetVector(LineEntity line, Point2 throughPoint, out Point2 offset)
    {
        var deltaX = line.End.X - line.Start.X;
        var deltaY = line.End.Y - line.Start.Y;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (length <= GeometryTolerance)
        {
            offset = default;
            return false;
        }

        var normal = new Point2(-deltaY / length, deltaX / length);
        var signedDistance = Dot(Subtract(throughPoint, line.Start), normal);
        if (Math.Abs(signedDistance) <= GeometryTolerance)
        {
            offset = default;
            return false;
        }

        offset = Multiply(normal, signedDistance);
        return true;
    }

    private static bool TryOffsetPolyline(
        PolylineEntity polyline,
        Point2 throughPoint,
        EntityId entityId,
        out PolylineEntity offsetPolyline)
    {
        var vertices = polyline.Vertices;
        var firstSegment = new LineEntity(polyline.Id, vertices[0], vertices[1], polyline.IsConstruction);
        if (!TryOffsetLine(firstSegment, throughPoint, entityId, out var offsetFirst))
        {
            offsetPolyline = default!;
            return false;
        }

        var offsetVector = Subtract(offsetFirst.Start, firstSegment.Start);
        offsetPolyline = new PolylineEntity(entityId, vertices.Select(vertex => Add(vertex, offsetVector)), polyline.IsConstruction);
        return true;
    }

    private static bool TryOffsetSpline(
        SplineEntity spline,
        Point2 throughPoint,
        EntityId entityId,
        out SplineEntity offsetSpline)
    {
        var samples = spline.GetSamplePoints();
        if (samples.Count < 2)
        {
            offsetSpline = default!;
            return false;
        }

        LineEntity? closestSegment = null;
        var closestDistance = double.PositiveInfinity;
        for (var index = 1; index < samples.Count; index++)
        {
            var segment = new LineEntity(spline.Id, samples[index - 1], samples[index], spline.IsConstruction);
            var distance = DistancePointToSegment(throughPoint, segment);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestSegment = segment;
            }
        }

        if (closestSegment is null || !TryGetLineOffsetVector(closestSegment, throughPoint, out var offsetVector))
        {
            offsetSpline = default!;
            return false;
        }

        offsetSpline = new SplineEntity(
            entityId,
            spline.Degree,
            spline.ControlPoints.Select(point => Add(point, offsetVector)),
            spline.Knots,
            spline.Weights,
            spline.IsConstruction,
            spline.FitPoints.Select(point => Add(point, offsetVector)),
            spline.StartTangentHandle is { } startTangentHandle ? Add(startTangentHandle, offsetVector) : null,
            spline.EndTangentHandle is { } endTangentHandle ? Add(endTangentHandle, offsetVector) : null);
        return true;
    }

    private static DrawingEntity ScaleEntity(DrawingEntity entity, Point2 center, double scale) =>
        entity switch
        {
            LineEntity line => line with
            {
                Start = ScalePoint(line.Start, center, scale),
                End = ScalePoint(line.End, center, scale)
            },
            PolylineEntity polyline => new PolylineEntity(
                polyline.Id,
                polyline.Vertices.Select(vertex => ScalePoint(vertex, center, scale)),
                polyline.IsConstruction),
            CircleEntity circle => circle with
            {
                Center = ScalePoint(circle.Center, center, scale),
                Radius = circle.Radius * scale
            },
            ArcEntity arc => arc with
            {
                Center = ScalePoint(arc.Center, center, scale),
                Radius = arc.Radius * scale
            },
            PolygonEntity polygon => polygon with
            {
                Center = ScalePoint(polygon.Center, center, scale),
                Radius = polygon.Radius * Math.Abs(scale),
                SideCount = polygon.NormalizedSideCount
            },
            PointEntity point => point with { Location = ScalePoint(point.Location, center, scale) },
            SplineEntity spline => new SplineEntity(
                spline.Id,
                spline.Degree,
                spline.ControlPoints.Select(point => ScalePoint(point, center, scale)),
                spline.Knots,
                spline.Weights,
                spline.IsConstruction,
                spline.FitPoints.Select(point => ScalePoint(point, center, scale)),
                spline.StartTangentHandle is { } startTangentHandle ? ScalePoint(startTangentHandle, center, scale) : null,
                spline.EndTangentHandle is { } endTangentHandle ? ScalePoint(endTangentHandle, center, scale) : null),
            _ => entity
        };

    private static DrawingEntity MirrorEntity(DrawingEntity entity, Point2 axisStart, Point2 axisEnd) =>
        entity switch
        {
            LineEntity line => line with
            {
                Start = MirrorPoint(line.Start, axisStart, axisEnd),
                End = MirrorPoint(line.End, axisStart, axisEnd)
            },
            PolylineEntity polyline => new PolylineEntity(
                polyline.Id,
                polyline.Vertices.Select(vertex => MirrorPoint(vertex, axisStart, axisEnd)),
                polyline.IsConstruction),
            CircleEntity circle => circle with { Center = MirrorPoint(circle.Center, axisStart, axisEnd) },
            ArcEntity arc => MirrorArc(arc, axisStart, axisEnd),
            PolygonEntity polygon => MirrorPolygon(polygon, axisStart, axisEnd),
            PointEntity point => point with { Location = MirrorPoint(point.Location, axisStart, axisEnd) },
            SplineEntity spline => new SplineEntity(
                spline.Id,
                spline.Degree,
                spline.ControlPoints.Select(point => MirrorPoint(point, axisStart, axisEnd)),
                spline.Knots,
                spline.Weights,
                spline.IsConstruction,
                spline.FitPoints.Select(point => MirrorPoint(point, axisStart, axisEnd)),
                spline.StartTangentHandle is { } startTangentHandle ? MirrorPoint(startTangentHandle, axisStart, axisEnd) : null,
                spline.EndTangentHandle is { } endTangentHandle ? MirrorPoint(endTangentHandle, axisStart, axisEnd) : null),
            _ => entity
        };

    private static ArcEntity MirrorArc(ArcEntity arc, Point2 axisStart, Point2 axisEnd)
    {
        var startPoint = MirrorPoint(PointOnArc(arc, arc.StartAngleDegrees), axisStart, axisEnd);
        var endPoint = MirrorPoint(PointOnArc(arc, arc.EndAngleDegrees), axisStart, axisEnd);
        var center = MirrorPoint(arc.Center, axisStart, axisEnd);

        return arc with
        {
            Center = center,
            StartAngleDegrees = AngleDegrees(center, endPoint),
            EndAngleDegrees = AngleDegrees(center, startPoint)
        };
    }

    private static DrawingEntity WithId(DrawingEntity entity, EntityId id) =>
        entity switch
        {
            LineEntity line => line with { Id = id },
            PolylineEntity polyline => new PolylineEntity(id, polyline.Vertices, polyline.IsConstruction),
            CircleEntity circle => circle with { Id = id },
            ArcEntity arc => arc with { Id = id },
            PolygonEntity polygon => polygon with { Id = id, SideCount = polygon.NormalizedSideCount },
            PointEntity point => point with { Id = id },
            SplineEntity spline => new SplineEntity(id, spline.Degree, spline.ControlPoints, spline.Knots, spline.Weights, spline.IsConstruction, spline.FitPoints, spline.StartTangentHandle, spline.EndTangentHandle),
            _ => entity
        };

    private static PolygonEntity MirrorPolygon(PolygonEntity polygon, Point2 axisStart, Point2 axisEnd) =>
        PolygonEntity.FromCenterAndRadiusPoint(
            polygon.Id,
            MirrorPoint(polygon.Center, axisStart, axisEnd),
            MirrorPoint(polygon.GetRadiusPoint(), axisStart, axisEnd),
            polygon.Circumscribed,
            polygon.NormalizedSideCount,
            polygon.IsConstruction);

    private static bool TryBuildFillet(
        LineEntity first,
        LineEntity second,
        Point2 intersection,
        double radius,
        out LineEntity firstTrimmed,
        out LineEntity secondTrimmed,
        out ArcEntity arc)
    {
        firstTrimmed = default!;
        secondTrimmed = default!;
        arc = default!;

        var firstAway = GetLinePointAwayFrom(first, intersection);
        var secondAway = GetLinePointAwayFrom(second, intersection);
        var firstDirection = Unit(Subtract(firstAway, intersection));
        var secondDirection = Unit(Subtract(secondAway, intersection));
        if (firstDirection is null || secondDirection is null)
        {
            return false;
        }

        var dot = Math.Clamp(Dot(firstDirection.Value, secondDirection.Value), -1, 1);
        var angle = Math.Acos(dot);
        if (angle <= GeometryTolerance || Math.Abs(Math.PI - angle) <= GeometryTolerance)
        {
            return false;
        }

        var tangentDistance = radius / Math.Tan(angle / 2.0);
        if (tangentDistance <= GeometryTolerance
            || tangentDistance >= Distance(intersection, firstAway)
            || tangentDistance >= Distance(intersection, secondAway))
        {
            return false;
        }

        var firstTangent = Add(intersection, Multiply(firstDirection.Value, tangentDistance));
        var secondTangent = Add(intersection, Multiply(secondDirection.Value, tangentDistance));
        var bisector = Unit(Add(firstDirection.Value, secondDirection.Value));
        if (bisector is null)
        {
            return false;
        }

        var centerDistance = radius / Math.Sin(angle / 2.0);
        var center = Add(intersection, Multiply(bisector.Value, centerDistance));
        firstTrimmed = TrimLineToPoint(first, intersection, firstTangent);
        secondTrimmed = TrimLineToPoint(second, intersection, secondTangent);

        var startAngle = AngleDegrees(center, firstTangent);
        var endAngle = AngleDegrees(center, secondTangent);
        arc = new ArcEntity(EntityId.Create("fillet"), center, radius, startAngle, endAngle);
        return true;
    }

    private static bool TryTrimLineFromIntersection(
        LineEntity line,
        Point2 intersection,
        double distance,
        out LineEntity trimmed,
        out Point2 trimPoint)
    {
        trimmed = default!;
        trimPoint = default;

        var away = GetLinePointAwayFrom(line, intersection);
        var length = Distance(intersection, away);
        if (length <= distance + GeometryTolerance)
        {
            return false;
        }

        var direction = Unit(Subtract(away, intersection));
        if (direction is null)
        {
            return false;
        }

        trimPoint = Add(intersection, Multiply(direction.Value, distance));
        trimmed = TrimLineToPoint(line, intersection, trimPoint);
        return true;
    }

    private static LineEntity TrimLineToPoint(LineEntity line, Point2 intersection, Point2 trimPoint) =>
        Distance(line.Start, intersection) < Distance(line.End, intersection)
            ? line with { Start = trimPoint }
            : line with { End = trimPoint };

    private static Point2 GetLinePointAwayFrom(LineEntity line, Point2 intersection) =>
        Distance(line.Start, intersection) > Distance(line.End, intersection)
            ? line.Start
            : line.End;

    private static bool TryGetLineIntersection(
        LineEntity first,
        LineEntity second,
        bool requireSecondSegment,
        bool requireFirstSegment,
        out Point2 intersection,
        out double firstParameter,
        out double secondParameter)
    {
        var firstVector = Subtract(first.End, first.Start);
        var secondVector = Subtract(second.End, second.Start);
        var denominator = Cross(firstVector, secondVector);
        if (Math.Abs(denominator) <= GeometryTolerance)
        {
            intersection = default;
            firstParameter = default;
            secondParameter = default;
            return false;
        }

        var startDelta = Subtract(second.Start, first.Start);
        firstParameter = Cross(startDelta, secondVector) / denominator;
        secondParameter = Cross(startDelta, firstVector) / denominator;
        if ((requireFirstSegment && (firstParameter < -GeometryTolerance || firstParameter > 1.0 + GeometryTolerance))
            || (requireSecondSegment && (secondParameter < -GeometryTolerance || secondParameter > 1.0 + GeometryTolerance)))
        {
            intersection = default;
            return false;
        }

        intersection = PointAtParameter(first, firstParameter);
        return true;
    }

    private static bool TryProjectParameter(LineEntity line, Point2 point, out double parameter)
    {
        var delta = Subtract(line.End, line.Start);
        var lengthSquared = Dot(delta, delta);
        if (lengthSquared <= GeometryTolerance * GeometryTolerance)
        {
            parameter = default;
            return false;
        }

        parameter = Dot(Subtract(point, line.Start), delta) / lengthSquared;
        return true;
    }

    private static Point2 PointAtParameter(LineEntity line, double parameter) =>
        new(
            line.Start.X + (line.End.X - line.Start.X) * parameter,
            line.Start.Y + (line.End.Y - line.Start.Y) * parameter);

    private static void AddLineTargetCutParameters(
        DrawingDocument document,
        LineEntity target,
        ICollection<double> cutParameters)
    {
        foreach (var cutter in document.Entities)
        {
            if (StringComparer.Ordinal.Equals(cutter.Id.Value, target.Id.Value))
            {
                continue;
            }

            switch (cutter)
            {
                case LineEntity line:
                    AddLineTargetSegmentCutParameter(target, line.Start, line.End, cutParameters);
                    break;

                case CircleEntity circle:
                    AddLineCircleCutParameters(circle, target, cutParameters, requireTargetSegment: false);
                    break;

                case ArcEntity arc:
                    AddLineArcCutParameters(target, arc, cutParameters);
                    break;

                case EllipseEntity ellipse:
                    AddLineTargetSegmentCutParameters(target, ellipse.GetSamplePoints(144), closeLoop: false, cutParameters);
                    break;

                case PolylineEntity polyline:
                    AddLineTargetSegmentCutParameters(target, polyline.Vertices, closeLoop: false, cutParameters);
                    break;

                case PolygonEntity polygon:
                    AddLineTargetSegmentCutParameters(target, polygon.GetVertices(), closeLoop: true, cutParameters);
                    break;

                case SplineEntity spline:
                    AddLineSplineCutParameters(target, spline, cutParameters);
                    break;

                case PointEntity point:
                    AddLinePointCutParameter(target, point.Location, cutParameters);
                    break;
            }
        }
    }

    private static void AddCircleTargetCutAngles(
        DrawingDocument document,
        CircleEntity target,
        ICollection<double> cutAngles)
    {
        foreach (var cutter in document.Entities)
        {
            if (StringComparer.Ordinal.Equals(cutter.Id.Value, target.Id.Value))
            {
                continue;
            }

            switch (cutter)
            {
                case LineEntity line:
                    if (TryGetLineCircleIntersections(target, line, out var lineIntersections))
                    {
                        AddCircleTargetCutPoints(target, lineIntersections, cutAngles);
                    }

                    break;

                case CircleEntity circle:
                    if (TryGetCircleCircleIntersections(target, circle, out var circleIntersections))
                    {
                        AddCircleTargetCutPoints(target, circleIntersections, cutAngles);
                    }

                    break;

                case ArcEntity arc:
                    AddCircleTargetArcCutAngles(target, arc, cutAngles);
                    break;

                case EllipseEntity ellipse:
                    AddCircleTargetSegmentCutAngles(target, ellipse.GetSamplePoints(144), closeLoop: false, cutAngles);
                    break;

                case PolylineEntity polyline:
                    AddCircleTargetSegmentCutAngles(target, polyline.Vertices, closeLoop: false, cutAngles);
                    break;

                case PolygonEntity polygon:
                    AddCircleTargetSegmentCutAngles(target, polygon.GetVertices(), closeLoop: true, cutAngles);
                    break;

                case SplineEntity spline:
                    AddCircleSplineCutAngles(target, spline, cutAngles);
                    break;

                case PointEntity point:
                    AddCircleTargetCutPoint(target, point.Location, cutAngles);
                    break;
            }
        }
    }

    private static void AddCircleTargetArcCutAngles(
        CircleEntity target,
        ArcEntity cutter,
        ICollection<double> cutAngles)
    {
        var cutterCircle = new CircleEntity(cutter.Id, cutter.Center, cutter.Radius, cutter.IsConstruction);
        if (!TryGetCircleCircleIntersections(target, cutterCircle, out var intersections))
        {
            return;
        }

        foreach (var point in intersections)
        {
            if (TryGetArcSweepParameter(cutter, point, allowEndpoints: true, out _))
            {
                AddCircleTargetCutPoint(target, point, cutAngles);
            }
        }
    }

    private static void AddCircleTargetSegmentCutAngles(
        CircleEntity target,
        IReadOnlyList<Point2> vertices,
        bool closeLoop,
        ICollection<double> cutAngles)
    {
        if (vertices.Count < 2)
        {
            return;
        }

        for (var index = 1; index < vertices.Count; index++)
        {
            AddCircleTargetSegmentCutAngles(target, vertices[index - 1], vertices[index], cutAngles);
        }

        if (closeLoop && vertices.Count > 2)
        {
            AddCircleTargetSegmentCutAngles(target, vertices[^1], vertices[0], cutAngles);
        }
    }

    private static void AddCircleTargetSegmentCutAngles(
        CircleEntity target,
        Point2 start,
        Point2 end,
        ICollection<double> cutAngles)
    {
        var segment = new LineEntity(EntityId.Create("__circle-target-segment"), start, end);
        if (TryGetLineCircleIntersections(target, segment, out var intersections))
        {
            AddCircleTargetCutPoints(target, intersections, cutAngles);
        }
    }

    private static void AddCircleSplineCutAngles(
        CircleEntity target,
        SplineEntity cutter,
        ICollection<double> cutAngles)
    {
        if (cutter.FitPoints.Count < 2)
        {
            AddCircleKnotSplineCutAngles(target, cutter, cutAngles);
            return;
        }

        for (var spanIndex = 0; spanIndex < cutter.FitPoints.Count - 1; spanIndex++)
        {
            AddCircleFitSplineSpanCutAngles(target, cutter.FitPoints, spanIndex, cutAngles);
        }
    }

    private static void AddCircleKnotSplineCutAngles(
        CircleEntity target,
        SplineEntity cutter,
        ICollection<double> cutAngles)
    {
        AddKnotSplineImplicitRootCuts(
            cutter,
            point => CircleImplicitValue(target.Center, target.Radius, point),
            GeometryTolerance,
            point => AddCircleTargetCutPoint(target, point, cutAngles));
    }

    private static void AddCircleFitSplineSpanCutAngles(
        CircleEntity target,
        IReadOnlyList<Point2> fitPoints,
        int spanIndex,
        ICollection<double> cutAngles)
    {
        const int searchIntervals = 64;
        var previous = spanIndex == 0 ? fitPoints[spanIndex] : fitPoints[spanIndex - 1];
        var start = fitPoints[spanIndex];
        var end = fitPoints[spanIndex + 1];
        var next = spanIndex + 2 < fitPoints.Count ? fitPoints[spanIndex + 2] : end;
        var previousParameter = 0.0;
        var previousValue = CircleImplicitValue(target.Center, target.Radius, start);
        AddCircleSplineRootIfNear(target, start, previousValue, cutAngles);

        for (var interval = 1; interval <= searchIntervals; interval++)
        {
            var parameter = (double)interval / searchIntervals;
            var point = EvaluateCatmullRom(previous, start, end, next, parameter);
            var value = CircleImplicitValue(target.Center, target.Radius, point);
            AddCircleSplineRootIfNear(target, point, value, cutAngles);

            if ((previousValue < -GeometryTolerance && value > GeometryTolerance)
                || (previousValue > GeometryTolerance && value < -GeometryTolerance))
            {
                var rootParameter = RefineCatmullRomCircleRoot(
                    target.Center,
                    target.Radius,
                    previous,
                    start,
                    end,
                    next,
                    previousParameter,
                    parameter);
                AddCircleTargetCutPoint(target, EvaluateCatmullRom(previous, start, end, next, rootParameter), cutAngles);
            }

            previousParameter = parameter;
            previousValue = value;
        }
    }

    private static void AddCircleSplineRootIfNear(
        CircleEntity target,
        Point2 point,
        double implicitValue,
        ICollection<double> cutAngles)
    {
        if (Math.Abs(implicitValue) <= GeometryTolerance)
        {
            AddCircleTargetCutPoint(target, point, cutAngles);
        }
    }

    private static void AddCircleTargetCutPoints(
        CircleEntity target,
        IReadOnlyList<Point2> points,
        ICollection<double> cutAngles)
    {
        foreach (var point in points)
        {
            AddCircleTargetCutPoint(target, point, cutAngles);
        }
    }

    private static void AddCircleTargetCutPoint(
        CircleEntity target,
        Point2 point,
        ICollection<double> cutAngles)
    {
        if (Math.Abs(Distance(target.Center, point) - target.Radius) <= GeometryTolerance)
        {
            cutAngles.Add(NormalizeAngleDegrees(AngleDegrees(target.Center, point)));
        }
    }

    private static double RefineCatmullRomCircleRoot(
        Point2 center,
        double radius,
        Point2 previous,
        Point2 start,
        Point2 end,
        Point2 next,
        double low,
        double high)
    {
        var lowValue = CircleImplicitValue(center, radius, EvaluateCatmullRom(previous, start, end, next, low));
        for (var index = 0; index < 80; index++)
        {
            var middle = (low + high) / 2.0;
            var middleValue = CircleImplicitValue(center, radius, EvaluateCatmullRom(previous, start, end, next, middle));
            if ((lowValue < 0 && middleValue > 0) || (lowValue > 0 && middleValue < 0))
            {
                high = middle;
            }
            else
            {
                low = middle;
                lowValue = middleValue;
            }
        }

        return (low + high) / 2.0;
    }

    private static double CircleImplicitValue(Point2 center, double radius, Point2 point)
    {
        var delta = Subtract(point, center);
        return Dot(delta, delta) - (radius * radius);
    }

    private static void AddArcTargetSegmentCutParameters(
        ArcEntity target,
        IReadOnlyList<Point2> vertices,
        bool closeLoop,
        ICollection<double> cutParameters)
    {
        if (vertices.Count < 2)
        {
            return;
        }

        for (var index = 1; index < vertices.Count; index++)
        {
            AddArcTargetSegmentCutParameters(target, vertices[index - 1], vertices[index], cutParameters);
        }

        if (closeLoop && vertices.Count > 2)
        {
            AddArcTargetSegmentCutParameters(target, vertices[^1], vertices[0], cutParameters);
        }
    }

    private static void AddArcTargetSegmentCutParameters(
        ArcEntity target,
        Point2 start,
        Point2 end,
        ICollection<double> cutParameters)
    {
        var targetCircle = new CircleEntity(target.Id, target.Center, target.Radius, target.IsConstruction);
        var segment = new LineEntity(EntityId.Create("__arc-target-segment"), start, end);
        if (TryGetLineCircleIntersections(targetCircle, segment, out var intersections))
        {
            AddArcCutParameters(target, intersections, cutParameters);
        }
    }

    private static void AddArcSplineCutParameters(
        ArcEntity target,
        SplineEntity cutter,
        ICollection<double> cutParameters)
    {
        if (cutter.FitPoints.Count < 2)
        {
            AddArcKnotSplineCutParameters(target, cutter, cutParameters);
            return;
        }

        for (var spanIndex = 0; spanIndex < cutter.FitPoints.Count - 1; spanIndex++)
        {
            AddArcFitSplineSpanCutParameters(target, cutter.FitPoints, spanIndex, cutParameters);
        }
    }

    private static void AddArcKnotSplineCutParameters(
        ArcEntity target,
        SplineEntity cutter,
        ICollection<double> cutParameters)
    {
        AddKnotSplineImplicitRootCuts(
            cutter,
            point => CircleImplicitValue(target.Center, target.Radius, point),
            GeometryTolerance,
            point => AddArcCutParameters(target, new[] { point }, cutParameters));
    }

    private static void AddArcFitSplineSpanCutParameters(
        ArcEntity target,
        IReadOnlyList<Point2> fitPoints,
        int spanIndex,
        ICollection<double> cutParameters)
    {
        const int searchIntervals = 64;
        var previous = spanIndex == 0 ? fitPoints[spanIndex] : fitPoints[spanIndex - 1];
        var start = fitPoints[spanIndex];
        var end = fitPoints[spanIndex + 1];
        var next = spanIndex + 2 < fitPoints.Count ? fitPoints[spanIndex + 2] : end;
        var previousParameter = 0.0;
        var previousValue = CircleImplicitValue(target.Center, target.Radius, start);
        AddArcSplineRootIfNear(target, start, previousValue, cutParameters);

        for (var interval = 1; interval <= searchIntervals; interval++)
        {
            var parameter = (double)interval / searchIntervals;
            var point = EvaluateCatmullRom(previous, start, end, next, parameter);
            var value = CircleImplicitValue(target.Center, target.Radius, point);
            AddArcSplineRootIfNear(target, point, value, cutParameters);

            if ((previousValue < -GeometryTolerance && value > GeometryTolerance)
                || (previousValue > GeometryTolerance && value < -GeometryTolerance))
            {
                var rootParameter = RefineCatmullRomCircleRoot(
                    target.Center,
                    target.Radius,
                    previous,
                    start,
                    end,
                    next,
                    previousParameter,
                    parameter);
                AddArcCutParameters(target, new[] { EvaluateCatmullRom(previous, start, end, next, rootParameter) }, cutParameters);
            }

            previousParameter = parameter;
            previousValue = value;
        }
    }

    private static void AddArcSplineRootIfNear(
        ArcEntity target,
        Point2 point,
        double implicitValue,
        ICollection<double> cutParameters)
    {
        if (Math.Abs(implicitValue) <= GeometryTolerance)
        {
            AddArcCutParameters(target, new[] { point }, cutParameters);
        }
    }

    private static void AddEllipseTargetSegmentCutParameters(
        EllipseEntity target,
        IReadOnlyList<Point2> vertices,
        bool closeLoop,
        ICollection<double> cutParameters)
    {
        if (vertices.Count < 2)
        {
            return;
        }

        for (var index = 1; index < vertices.Count; index++)
        {
            AddEllipseTargetSegmentCutParameters(target, vertices[index - 1], vertices[index], cutParameters);
        }

        if (closeLoop && vertices.Count > 2)
        {
            AddEllipseTargetSegmentCutParameters(target, vertices[^1], vertices[0], cutParameters);
        }
    }

    private static void AddEllipseTargetSegmentCutParameters(
        EllipseEntity target,
        Point2 start,
        Point2 end,
        ICollection<double> cutParameters)
    {
        var segment = new LineEntity(EntityId.Create("__ellipse-target-segment"), start, end);
        if (TryGetLineEllipseIntersections(target, segment, requireLineSegment: true, out var intersections))
        {
            AddEllipseCutParameters(target, intersections, cutParameters);
        }
    }

    private static void AddEllipseSplineCutParameters(
        EllipseEntity target,
        SplineEntity cutter,
        ICollection<double> cutParameters)
    {
        if (cutter.FitPoints.Count < 2)
        {
            AddEllipseKnotSplineCutParameters(target, cutter, cutParameters);
            return;
        }

        for (var spanIndex = 0; spanIndex < cutter.FitPoints.Count - 1; spanIndex++)
        {
            AddEllipseFitSplineSpanCutParameters(target, cutter.FitPoints, spanIndex, cutParameters);
        }
    }

    private static void AddEllipseKnotSplineCutParameters(
        EllipseEntity target,
        SplineEntity cutter,
        ICollection<double> cutParameters)
    {
        if (!TryGetEllipseAxes(target, out var majorUnit, out var minorUnit, out var majorLength, out var minorLength))
        {
            return;
        }

        AddKnotSplineImplicitRootCuts(
            cutter,
            point => EllipseImplicitValue(target, majorUnit, minorUnit, majorLength, minorLength, point),
            GeometryTolerance,
            point => AddEllipseCutParameters(target, new[] { point }, cutParameters));
    }

    private static void AddEllipseFitSplineSpanCutParameters(
        EllipseEntity target,
        IReadOnlyList<Point2> fitPoints,
        int spanIndex,
        ICollection<double> cutParameters)
    {
        if (!TryGetEllipseAxes(target, out var majorUnit, out var minorUnit, out var majorLength, out var minorLength))
        {
            return;
        }

        const int searchIntervals = 64;
        var previous = spanIndex == 0 ? fitPoints[spanIndex] : fitPoints[spanIndex - 1];
        var start = fitPoints[spanIndex];
        var end = fitPoints[spanIndex + 1];
        var next = spanIndex + 2 < fitPoints.Count ? fitPoints[spanIndex + 2] : end;
        var previousParameter = 0.0;
        var previousValue = EllipseImplicitValue(target, majorUnit, minorUnit, majorLength, minorLength, start);
        AddEllipseSplineRootIfNear(target, start, previousValue, cutParameters);

        for (var interval = 1; interval <= searchIntervals; interval++)
        {
            var parameter = (double)interval / searchIntervals;
            var point = EvaluateCatmullRom(previous, start, end, next, parameter);
            var value = EllipseImplicitValue(target, majorUnit, minorUnit, majorLength, minorLength, point);
            AddEllipseSplineRootIfNear(target, point, value, cutParameters);

            if ((previousValue < -GeometryTolerance && value > GeometryTolerance)
                || (previousValue > GeometryTolerance && value < -GeometryTolerance))
            {
                var rootParameter = RefineCatmullRomEllipseRoot(
                    target,
                    majorUnit,
                    minorUnit,
                    majorLength,
                    minorLength,
                    previous,
                    start,
                    end,
                    next,
                    previousParameter,
                    parameter);
                AddEllipseCutParameters(target, new[] { EvaluateCatmullRom(previous, start, end, next, rootParameter) }, cutParameters);
            }

            previousParameter = parameter;
            previousValue = value;
        }
    }

    private static void AddEllipseSplineRootIfNear(
        EllipseEntity target,
        Point2 point,
        double implicitValue,
        ICollection<double> cutParameters)
    {
        if (Math.Abs(implicitValue) <= GeometryTolerance)
        {
            AddEllipseCutParameters(target, new[] { point }, cutParameters);
        }
    }

    private static double RefineCatmullRomEllipseRoot(
        EllipseEntity target,
        Point2 majorUnit,
        Point2 minorUnit,
        double majorLength,
        double minorLength,
        Point2 previous,
        Point2 start,
        Point2 end,
        Point2 next,
        double low,
        double high)
    {
        var lowValue = EllipseImplicitValue(
            target,
            majorUnit,
            minorUnit,
            majorLength,
            minorLength,
            EvaluateCatmullRom(previous, start, end, next, low));
        for (var index = 0; index < 80; index++)
        {
            var middle = (low + high) / 2.0;
            var middleValue = EllipseImplicitValue(
                target,
                majorUnit,
                minorUnit,
                majorLength,
                minorLength,
                EvaluateCatmullRom(previous, start, end, next, middle));
            if ((lowValue < 0 && middleValue > 0) || (lowValue > 0 && middleValue < 0))
            {
                high = middle;
            }
            else
            {
                low = middle;
                lowValue = middleValue;
            }
        }

        return (low + high) / 2.0;
    }

    private static double EllipseImplicitValue(
        EllipseEntity ellipse,
        Point2 majorUnit,
        Point2 minorUnit,
        double majorLength,
        double minorLength,
        Point2 point)
    {
        var local = ToEllipseLocalUnit(point, ellipse.Center, majorUnit, minorUnit, majorLength, minorLength);
        return Dot(local, local) - 1.0;
    }

    private static void AddLineTargetSegmentCutParameters(
        LineEntity target,
        IReadOnlyList<Point2> vertices,
        bool closeLoop,
        ICollection<double> cutParameters)
    {
        if (vertices.Count < 2)
        {
            return;
        }

        for (var index = 1; index < vertices.Count; index++)
        {
            AddLineTargetSegmentCutParameter(target, vertices[index - 1], vertices[index], cutParameters);
        }

        if (closeLoop && vertices.Count > 2)
        {
            AddLineTargetSegmentCutParameter(target, vertices[^1], vertices[0], cutParameters);
        }
    }

    private static void AddLineTargetSegmentCutParameter(
        LineEntity target,
        Point2 cutterStart,
        Point2 cutterEnd,
        ICollection<double> cutParameters)
    {
        if (Distance(cutterStart, cutterEnd) <= GeometryTolerance)
        {
            return;
        }

        var cutterSegment = new LineEntity(EntityId.Create("cutter-segment"), cutterStart, cutterEnd);
        if (TryGetLineIntersection(
                target,
                cutterSegment,
                requireSecondSegment: true,
                requireFirstSegment: false,
                out _,
                out var targetParameter,
                out _))
        {
            AddLineCutParameter(targetParameter, cutParameters);
        }
    }

    private static void AddLineSplineCutParameters(
        LineEntity target,
        SplineEntity cutter,
        ICollection<double> cutParameters)
    {
        if (cutter.FitPoints.Count < 2)
        {
            AddLineKnotSplineCutParameters(target, cutter, cutParameters);
            return;
        }

        for (var spanIndex = 0; spanIndex < cutter.FitPoints.Count - 1; spanIndex++)
        {
            AddLineFitSplineSpanCutParameters(target, cutter.FitPoints, spanIndex, cutParameters);
        }
    }

    private static void AddLineKnotSplineCutParameters(
        LineEntity target,
        SplineEntity cutter,
        ICollection<double> cutParameters)
    {
        var targetDirection = Subtract(target.End, target.Start);
        var lineTolerance = GeometryTolerance * Math.Max(1.0, Math.Sqrt(Dot(targetDirection, targetDirection)));
        AddKnotSplineImplicitRootCuts(
            cutter,
            point => SignedLineDistanceValue(target, point),
            lineTolerance,
            point => AddLinePointCutParameter(target, point, cutParameters));
    }

    private static void AddLineFitSplineSpanCutParameters(
        LineEntity target,
        IReadOnlyList<Point2> fitPoints,
        int spanIndex,
        ICollection<double> cutParameters)
    {
        const int searchIntervals = 64;
        var previous = spanIndex == 0 ? fitPoints[spanIndex] : fitPoints[spanIndex - 1];
        var start = fitPoints[spanIndex];
        var end = fitPoints[spanIndex + 1];
        var next = spanIndex + 2 < fitPoints.Count ? fitPoints[spanIndex + 2] : end;
        var targetDirection = Subtract(target.End, target.Start);
        var lineTolerance = GeometryTolerance * Math.Max(1.0, Math.Sqrt(Dot(targetDirection, targetDirection)));
        var previousParameter = 0.0;
        var previousValue = SignedLineDistanceValue(target, start);
        AddLineSplineRootIfNear(target, start, previousValue, lineTolerance, cutParameters);

        for (var interval = 1; interval <= searchIntervals; interval++)
        {
            var parameter = (double)interval / searchIntervals;
            var point = EvaluateCatmullRom(previous, start, end, next, parameter);
            var value = SignedLineDistanceValue(target, point);
            AddLineSplineRootIfNear(target, point, value, lineTolerance, cutParameters);

            if ((previousValue < -lineTolerance && value > lineTolerance)
                || (previousValue > lineTolerance && value < -lineTolerance))
            {
                var rootParameter = RefineCatmullRomLineRoot(
                    target,
                    previous,
                    start,
                    end,
                    next,
                    previousParameter,
                    parameter);
                var rootPoint = EvaluateCatmullRom(previous, start, end, next, rootParameter);
                AddLinePointCutParameter(target, rootPoint, cutParameters);
            }

            previousParameter = parameter;
            previousValue = value;
        }
    }

    private static void AddLineSplineRootIfNear(
        LineEntity target,
        Point2 point,
        double signedLineValue,
        double lineTolerance,
        ICollection<double> cutParameters)
    {
        if (Math.Abs(signedLineValue) <= lineTolerance)
        {
            AddLinePointCutParameter(target, point, cutParameters);
        }
    }

    private static double RefineCatmullRomLineRoot(
        LineEntity target,
        Point2 previous,
        Point2 start,
        Point2 end,
        Point2 next,
        double low,
        double high)
    {
        var lowValue = SignedLineDistanceValue(target, EvaluateCatmullRom(previous, start, end, next, low));
        for (var index = 0; index < 80; index++)
        {
            var middle = (low + high) / 2.0;
            var middleValue = SignedLineDistanceValue(target, EvaluateCatmullRom(previous, start, end, next, middle));
            if ((lowValue < 0 && middleValue > 0) || (lowValue > 0 && middleValue < 0))
            {
                high = middle;
            }
            else
            {
                low = middle;
                lowValue = middleValue;
            }
        }

        return (low + high) / 2.0;
    }

    private static double SignedLineDistanceValue(LineEntity line, Point2 point) =>
        Cross(Subtract(line.End, line.Start), Subtract(point, line.Start));

    private static Point2 EvaluateCatmullRom(Point2 previous, Point2 start, Point2 end, Point2 next, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return new Point2(
            0.5 * ((2 * start.X)
                + (-previous.X + end.X) * t
                + ((2 * previous.X) - (5 * start.X) + (4 * end.X) - next.X) * t2
                + (-previous.X + (3 * start.X) - (3 * end.X) + next.X) * t3),
            0.5 * ((2 * start.Y)
                + (-previous.Y + end.Y) * t
                + ((2 * previous.Y) - (5 * start.Y) + (4 * end.Y) - next.Y) * t2
                + (-previous.Y + (3 * start.Y) - (3 * end.Y) + next.Y) * t3));
    }

    private static Point2 EvaluateCatmullRomDerivative(Point2 previous, Point2 start, Point2 end, Point2 next, double t)
    {
        var t2 = t * t;
        return new Point2(
            0.5 * ((-previous.X + end.X)
                + (2 * ((2 * previous.X) - (5 * start.X) + (4 * end.X) - next.X) * t)
                + (3 * (-previous.X + (3 * start.X) - (3 * end.X) + next.X) * t2)),
            0.5 * ((-previous.Y + end.Y)
                + (2 * ((2 * previous.Y) - (5 * start.Y) + (4 * end.Y) - next.Y) * t)
                + (3 * (-previous.Y + (3 * start.Y) - (3 * end.Y) + next.Y) * t2)));
    }

    private static void AddLineCircleCutParameters(
        CircleEntity circle,
        LineEntity target,
        ICollection<double> cutParameters,
        bool requireTargetSegment)
    {
        if (!TryGetLineCircleIntersections(circle, target, out var intersections, requireLineSegment: requireTargetSegment))
        {
            return;
        }

        foreach (var intersection in intersections)
        {
            if (TryProjectParameter(target, intersection, out var targetParameter))
            {
                AddLineCutParameter(targetParameter, cutParameters);
            }
        }
    }

    private static void AddLineArcCutParameters(
        LineEntity target,
        ArcEntity arc,
        ICollection<double> cutParameters)
    {
        var targetCircle = new CircleEntity(arc.Id, arc.Center, arc.Radius, arc.IsConstruction);
        if (!TryGetLineCircleIntersections(targetCircle, target, out var intersections, requireLineSegment: false))
        {
            return;
        }

        foreach (var intersection in intersections)
        {
            if (TryGetArcSweepParameter(arc, intersection, allowEndpoints: true, out _)
                && TryProjectParameter(target, intersection, out var targetParameter))
            {
                AddLineCutParameter(targetParameter, cutParameters);
            }
        }
    }

    private static void AddLinePointCutParameter(
        LineEntity target,
        Point2 point,
        ICollection<double> cutParameters)
    {
        if (!TryProjectParameter(target, point, out var targetParameter))
        {
            return;
        }

        var projectedPoint = PointAtParameter(target, targetParameter);
        if (Distance(projectedPoint, point) <= GeometryTolerance)
        {
            AddLineCutParameter(targetParameter, cutParameters);
        }
    }

    private static void AddLineCutParameter(double parameter, ICollection<double> cutParameters)
    {
        if (double.IsFinite(parameter))
        {
            cutParameters.Add(parameter);
        }
    }

    private static bool TryGetCirclePickAngle(CircleEntity circle, Point2 pickedPoint, out double angle)
    {
        if (circle.Radius <= GeometryTolerance
            || Distance(circle.Center, pickedPoint) <= GeometryTolerance)
        {
            angle = default;
            return false;
        }

        angle = AngleDegrees(circle.Center, pickedPoint);
        return true;
    }

    private static bool TryGetLineCircleIntersections(
        CircleEntity circle,
        LineEntity line,
        out IReadOnlyList<Point2> intersections,
        bool requireLineSegment = true)
    {
        var direction = Subtract(line.End, line.Start);
        var startFromCenter = Subtract(line.Start, circle.Center);
        var a = Dot(direction, direction);
        if (a <= GeometryTolerance * GeometryTolerance || circle.Radius <= GeometryTolerance)
        {
            intersections = Array.Empty<Point2>();
            return false;
        }

        var b = 2.0 * Dot(startFromCenter, direction);
        var c = Dot(startFromCenter, startFromCenter) - (circle.Radius * circle.Radius);
        var discriminant = (b * b) - (4.0 * a * c);
        if (discriminant < -GeometryTolerance)
        {
            intersections = Array.Empty<Point2>();
            return false;
        }

        var points = new List<Point2>(2);
        if (Math.Abs(discriminant) <= GeometryTolerance)
        {
            AddLineCircleIntersectionPoint(line, -b / (2.0 * a), points, requireLineSegment);
        }
        else
        {
            var sqrt = Math.Sqrt(discriminant);
            AddLineCircleIntersectionPoint(line, (-b - sqrt) / (2.0 * a), points, requireLineSegment);
            AddLineCircleIntersectionPoint(line, (-b + sqrt) / (2.0 * a), points, requireLineSegment);
        }

        intersections = points;
        return points.Count > 0;
    }

    private static IReadOnlyList<Point2> GetCircleSamplePoints(CircleEntity circle, int segmentCount)
    {
        if (circle.Radius <= GeometryTolerance)
        {
            return Array.Empty<Point2>();
        }

        var count = Math.Max(8, segmentCount);
        var points = new Point2[count];
        for (var index = 0; index < count; index++)
        {
            points[index] = PointOnCircle(circle.Center, circle.Radius, index * 360.0 / count);
        }

        return points;
    }

    private static void AddLineCircleIntersectionPoint(
        LineEntity line,
        double parameter,
        ICollection<Point2> points,
        bool requireLineSegment)
    {
        if (requireLineSegment && (parameter < -GeometryTolerance || parameter > 1.0 + GeometryTolerance))
        {
            return;
        }

        points.Add(PointAtParameter(line, requireLineSegment ? Math.Clamp(parameter, 0.0, 1.0) : parameter));
    }

    private static bool TryGetCircleCircleIntersections(
        CircleEntity first,
        CircleEntity second,
        out IReadOnlyList<Point2> intersections)
    {
        var centerDelta = Subtract(second.Center, first.Center);
        var centerDistance = Math.Sqrt(Dot(centerDelta, centerDelta));
        if (first.Radius <= GeometryTolerance
            || second.Radius <= GeometryTolerance
            || centerDistance <= GeometryTolerance
            || centerDistance > first.Radius + second.Radius + GeometryTolerance
            || centerDistance < Math.Abs(first.Radius - second.Radius) - GeometryTolerance)
        {
            intersections = Array.Empty<Point2>();
            return false;
        }

        var a = ((first.Radius * first.Radius) - (second.Radius * second.Radius) + (centerDistance * centerDistance))
            / (2.0 * centerDistance);
        var heightSquared = (first.Radius * first.Radius) - (a * a);
        if (heightSquared < -GeometryTolerance)
        {
            intersections = Array.Empty<Point2>();
            return false;
        }

        var basePoint = Add(first.Center, Multiply(centerDelta, a / centerDistance));
        if (Math.Abs(heightSquared) <= GeometryTolerance)
        {
            intersections = new[] { basePoint };
            return true;
        }

        var height = Math.Sqrt(heightSquared);
        var perpendicular = new Point2(-centerDelta.Y / centerDistance, centerDelta.X / centerDistance);
        intersections = new[]
        {
            Add(basePoint, Multiply(perpendicular, height)),
            Add(basePoint, Multiply(perpendicular, -height))
        };
        return true;
    }

    private static bool TryGetArcArcIntersections(
        ArcEntity first,
        ArcEntity second,
        out IReadOnlyList<Point2> intersections)
    {
        var firstCircle = new CircleEntity(first.Id, first.Center, first.Radius, first.IsConstruction);
        var secondCircle = new CircleEntity(second.Id, second.Center, second.Radius, second.IsConstruction);
        if (!TryGetCircleCircleIntersections(firstCircle, secondCircle, out var circleIntersections))
        {
            intersections = Array.Empty<Point2>();
            return false;
        }

        var points = circleIntersections
            .Where(point =>
                TryGetArcSweepParameter(first, point, allowEndpoints: true, out _)
                && TryGetArcSweepParameter(second, point, allowEndpoints: true, out _))
            .ToArray();

        intersections = points;
        return points.Length > 0;
    }

    private static bool TryGetLineEllipseIntersections(
        EllipseEntity ellipse,
        LineEntity line,
        bool requireLineSegment,
        out IReadOnlyList<Point2> intersections)
    {
        if (!TryGetEllipseAxes(ellipse, out var majorUnit, out var minorUnit, out var majorLength, out var minorLength))
        {
            intersections = Array.Empty<Point2>();
            return false;
        }

        var localStart = ToEllipseLocalUnit(line.Start, ellipse.Center, majorUnit, minorUnit, majorLength, minorLength);
        var localEnd = ToEllipseLocalUnit(line.End, ellipse.Center, majorUnit, minorUnit, majorLength, minorLength);
        var direction = Subtract(localEnd, localStart);
        var a = Dot(direction, direction);
        if (a <= GeometryTolerance * GeometryTolerance)
        {
            intersections = Array.Empty<Point2>();
            return false;
        }

        var b = 2.0 * Dot(localStart, direction);
        var c = Dot(localStart, localStart) - 1.0;
        var discriminant = (b * b) - (4.0 * a * c);
        if (discriminant < -GeometryTolerance)
        {
            intersections = Array.Empty<Point2>();
            return false;
        }

        var points = new List<Point2>(2);
        if (Math.Abs(discriminant) <= GeometryTolerance)
        {
            AddLineEllipseIntersectionPoint(line, -b / (2.0 * a), points, requireLineSegment);
        }
        else
        {
            var sqrt = Math.Sqrt(discriminant);
            AddLineEllipseIntersectionPoint(line, (-b - sqrt) / (2.0 * a), points, requireLineSegment);
            AddLineEllipseIntersectionPoint(line, (-b + sqrt) / (2.0 * a), points, requireLineSegment);
        }

        intersections = points;
        return points.Count > 0;
    }

    private static void AddLineEllipseIntersectionPoint(
        LineEntity line,
        double parameter,
        ICollection<Point2> points,
        bool requireLineSegment)
    {
        if (requireLineSegment && (parameter < -GeometryTolerance || parameter > 1.0 + GeometryTolerance))
        {
            return;
        }

        points.Add(PointAtParameter(line, requireLineSegment ? Math.Clamp(parameter, 0.0, 1.0) : parameter));
    }

    private static void AddArcCutParameters(
        ArcEntity arc,
        IReadOnlyList<Point2> points,
        ICollection<double> cutParameters)
    {
        foreach (var point in points)
        {
            if (TryGetArcSweepParameter(arc, point, allowEndpoints: false, out var parameter))
            {
                cutParameters.Add(parameter);
            }
        }
    }

    private static void AddEllipseCutParameters(
        EllipseEntity ellipse,
        IReadOnlyList<Point2> points,
        ICollection<double> cutParameters)
    {
        foreach (var point in points)
        {
            if (TryGetEllipseSweepParameter(ellipse, point, allowEndpoints: false, out var parameter))
            {
                cutParameters.Add(parameter);
            }
        }
    }

    private static void AddArcExtensionBoundaryParameters(
        DrawingDocument document,
        ArcEntity target,
        double sweep,
        ICollection<double> boundaryParameters)
    {
        var targetCircle = new CircleEntity(target.Id, target.Center, target.Radius, target.IsConstruction);
        foreach (var boundary in document.Entities)
        {
            if (StringComparer.Ordinal.Equals(boundary.Id.Value, target.Id.Value))
            {
                continue;
            }

            switch (boundary)
            {
                case LineEntity line:
                    if (TryGetLineCircleIntersections(targetCircle, line, out var lineIntersections))
                    {
                        AddArcExtensionParameters(target, sweep, lineIntersections, boundaryParameters);
                    }
                    break;

                case CircleEntity circle:
                    if (TryGetCircleCircleIntersections(targetCircle, circle, out var circleIntersections))
                    {
                        AddArcExtensionParameters(target, sweep, circleIntersections, boundaryParameters);
                    }
                    break;

                case ArcEntity arc:
                    var arcCircle = new CircleEntity(arc.Id, arc.Center, arc.Radius, arc.IsConstruction);
                    if (TryGetCircleCircleIntersections(targetCircle, arcCircle, out var arcIntersections))
                    {
                        AddArcExtensionParameters(
                            target,
                            sweep,
                            arcIntersections.Where(point => TryGetArcSweepParameter(arc, point, allowEndpoints: true, out _)),
                            boundaryParameters);
                    }
                    break;

                case EllipseEntity ellipse:
                    AddArcExtensionSegmentBoundaryParameters(target, sweep, ellipse.GetSamplePoints(144), closeLoop: false, boundaryParameters);
                    break;

                case PolylineEntity polyline:
                    AddArcExtensionSegmentBoundaryParameters(target, sweep, polyline.Vertices, closeLoop: false, boundaryParameters);
                    break;

                case PolygonEntity polygon:
                    AddArcExtensionSegmentBoundaryParameters(target, sweep, polygon.GetVertices(), closeLoop: true, boundaryParameters);
                    break;

                case SplineEntity spline:
                    AddArcExtensionSegmentBoundaryParameters(target, sweep, spline.GetSamplePoints(), closeLoop: false, boundaryParameters);
                    break;

                case PointEntity point:
                    AddArcExtensionParameter(target, sweep, point.Location, boundaryParameters);
                    break;
            }
        }
    }

    private static void AddArcExtensionSegmentBoundaryParameters(
        ArcEntity target,
        double sweep,
        IReadOnlyList<Point2> vertices,
        bool closeLoop,
        ICollection<double> boundaryParameters)
    {
        if (vertices.Count < 2)
        {
            return;
        }

        var targetCircle = new CircleEntity(target.Id, target.Center, target.Radius, target.IsConstruction);
        for (var index = 1; index < vertices.Count; index++)
        {
            AddArcExtensionSegmentBoundaryParameters(target, sweep, targetCircle, vertices[index - 1], vertices[index], boundaryParameters);
        }

        if (closeLoop && vertices.Count > 2)
        {
            AddArcExtensionSegmentBoundaryParameters(target, sweep, targetCircle, vertices[^1], vertices[0], boundaryParameters);
        }
    }

    private static void AddArcExtensionSegmentBoundaryParameters(
        ArcEntity target,
        double sweep,
        CircleEntity targetCircle,
        Point2 start,
        Point2 end,
        ICollection<double> boundaryParameters)
    {
        var segment = new LineEntity(EntityId.Create("__arc-extension-boundary-segment"), start, end);
        if (TryGetLineCircleIntersections(targetCircle, segment, out var intersections, requireLineSegment: true))
        {
            AddArcExtensionParameters(target, sweep, intersections, boundaryParameters);
        }
    }

    private static void AddArcExtensionParameters(
        ArcEntity target,
        double sweep,
        IEnumerable<Point2> points,
        ICollection<double> boundaryParameters)
    {
        foreach (var point in points)
        {
            AddArcExtensionParameter(target, sweep, point, boundaryParameters);
        }
    }

    private static void AddArcExtensionParameter(
        ArcEntity target,
        double sweep,
        Point2 point,
        ICollection<double> boundaryParameters)
    {
        foreach (var parameter in GetArcExtensionBoundaryParameters(target, point, sweep))
        {
            boundaryParameters.Add(parameter);
        }
    }

    private static void AddEllipseExtensionBoundaryParameters(
        DrawingDocument document,
        EllipseEntity target,
        double sweep,
        ICollection<double> boundaryParameters)
    {
        foreach (var boundary in document.Entities)
        {
            if (StringComparer.Ordinal.Equals(boundary.Id.Value, target.Id.Value))
            {
                continue;
            }

            switch (boundary)
            {
                case LineEntity line:
                    if (TryGetLineEllipseIntersections(target, line, requireLineSegment: true, out var lineIntersections))
                    {
                        AddEllipseExtensionParameters(target, sweep, lineIntersections, boundaryParameters);
                    }
                    break;

                case CircleEntity circle:
                    AddEllipseExtensionImplicitBoundaryParameters(
                        target,
                        sweep,
                        point => CircleImplicitValue(circle.Center, circle.Radius, point),
                        _ => true,
                        boundaryParameters);
                    break;

                case ArcEntity arc:
                    AddEllipseExtensionImplicitBoundaryParameters(
                        target,
                        sweep,
                        point => CircleImplicitValue(arc.Center, arc.Radius, point),
                        point => TryGetArcSweepParameter(arc, point, allowEndpoints: true, out _),
                        boundaryParameters);
                    break;

                case EllipseEntity ellipse:
                    if (TryGetEllipseAxes(ellipse, out var majorUnit, out var minorUnit, out var majorLength, out var minorLength))
                    {
                        AddEllipseExtensionImplicitBoundaryParameters(
                            target,
                            sweep,
                            point => EllipseImplicitValue(ellipse, majorUnit, minorUnit, majorLength, minorLength, point),
                            point => TryGetEllipseSweepParameter(ellipse, point, allowEndpoints: true, out _),
                            boundaryParameters);
                    }
                    break;

                case PolylineEntity polyline:
                    AddEllipseExtensionSegmentBoundaryParameters(target, sweep, polyline.Vertices, closeLoop: false, boundaryParameters);
                    break;

                case PolygonEntity polygon:
                    AddEllipseExtensionSegmentBoundaryParameters(target, sweep, polygon.GetVertices(), closeLoop: true, boundaryParameters);
                    break;

                case SplineEntity spline:
                    AddEllipseExtensionSegmentBoundaryParameters(target, sweep, spline.GetSamplePoints(), closeLoop: false, boundaryParameters);
                    break;

                case PointEntity point:
                    AddEllipseExtensionParameter(target, sweep, point.Location, boundaryParameters);
                    break;
            }
        }
    }

    private static void AddEllipseExtensionSegmentBoundaryParameters(
        EllipseEntity target,
        double sweep,
        IReadOnlyList<Point2> vertices,
        bool closeLoop,
        ICollection<double> boundaryParameters)
    {
        if (vertices.Count < 2)
        {
            return;
        }

        for (var index = 1; index < vertices.Count; index++)
        {
            AddEllipseExtensionSegmentBoundaryParameters(target, sweep, vertices[index - 1], vertices[index], boundaryParameters);
        }

        if (closeLoop && vertices.Count > 2)
        {
            AddEllipseExtensionSegmentBoundaryParameters(target, sweep, vertices[^1], vertices[0], boundaryParameters);
        }
    }

    private static void AddEllipseExtensionSegmentBoundaryParameters(
        EllipseEntity target,
        double sweep,
        Point2 start,
        Point2 end,
        ICollection<double> boundaryParameters)
    {
        var segment = new LineEntity(EntityId.Create("__ellipse-extension-boundary-segment"), start, end);
        if (TryGetLineEllipseIntersections(target, segment, requireLineSegment: true, out var intersections))
        {
            AddEllipseExtensionParameters(target, sweep, intersections, boundaryParameters);
        }
    }

    private static void AddEllipseExtensionParameters(
        EllipseEntity target,
        double sweep,
        IEnumerable<Point2> points,
        ICollection<double> boundaryParameters)
    {
        foreach (var point in points)
        {
            AddEllipseExtensionParameter(target, sweep, point, boundaryParameters);
        }
    }

    private static void AddEllipseExtensionParameter(
        EllipseEntity target,
        double sweep,
        Point2 point,
        ICollection<double> boundaryParameters)
    {
        foreach (var parameter in GetEllipseExtensionBoundaryParameters(target, point, sweep))
        {
            boundaryParameters.Add(parameter);
        }
    }

    private static void AddEllipseExtensionImplicitBoundaryParameters(
        EllipseEntity target,
        double sweep,
        Func<Point2, double> implicitValue,
        Func<Point2, bool> isOnBoundary,
        ICollection<double> boundaryParameters)
    {
        const int searchIntervals = 720;
        var previousParameter = 0.0;
        var previousPoint = target.PointAtParameterDegrees(target.StartParameterDegrees);
        var previousValue = implicitValue(previousPoint);
        AddEllipseExtensionImplicitRootIfNear(target, sweep, previousParameter, previousPoint, previousValue, isOnBoundary, boundaryParameters);

        for (var interval = 1; interval <= searchIntervals; interval++)
        {
            var parameter = 360.0 * interval / searchIntervals;
            var point = target.PointAtParameterDegrees(target.StartParameterDegrees + parameter);
            var value = implicitValue(point);
            AddEllipseExtensionImplicitRootIfNear(target, sweep, parameter, point, value, isOnBoundary, boundaryParameters);

            if ((previousValue < -GeometryTolerance && value > GeometryTolerance)
                || (previousValue > GeometryTolerance && value < -GeometryTolerance))
            {
                var rootParameter = RefineEllipseParameterRoot(target, implicitValue, previousParameter, parameter);
                var rootPoint = target.PointAtParameterDegrees(target.StartParameterDegrees + rootParameter);
                if (isOnBoundary(rootPoint) && TryGetUnwrappedExtensionParameter(rootParameter, sweep, out var extensionParameter))
                {
                    boundaryParameters.Add(extensionParameter);
                }
            }

            previousParameter = parameter;
            previousValue = value;
        }
    }

    private static void AddEllipseExtensionImplicitRootIfNear(
        EllipseEntity target,
        double sweep,
        double parameter,
        Point2 point,
        double value,
        Func<Point2, bool> isOnBoundary,
        ICollection<double> boundaryParameters)
    {
        if (Math.Abs(value) <= 0.0001
            && isOnBoundary(point)
            && TryGetUnwrappedExtensionParameter(parameter, sweep, out var extensionParameter))
        {
            boundaryParameters.Add(extensionParameter);
        }
    }

    private static double RefineEllipseParameterRoot(
        EllipseEntity target,
        Func<Point2, double> implicitValue,
        double low,
        double high)
    {
        var lowValue = implicitValue(target.PointAtParameterDegrees(target.StartParameterDegrees + low));
        for (var index = 0; index < 80; index++)
        {
            var middle = (low + high) / 2.0;
            var middleValue = implicitValue(target.PointAtParameterDegrees(target.StartParameterDegrees + middle));
            if ((lowValue < 0 && middleValue > 0) || (lowValue > 0 && middleValue < 0))
            {
                high = middle;
            }
            else
            {
                low = middle;
                lowValue = middleValue;
            }
        }

        return (low + high) / 2.0;
    }

    private static bool TryGetArcSweepParameter(
        ArcEntity arc,
        Point2 point,
        bool allowEndpoints,
        out double parameter)
    {
        parameter = default;
        if (arc.Radius <= GeometryTolerance)
        {
            return false;
        }

        var distance = Distance(arc.Center, point);
        if (distance <= GeometryTolerance
            || (!allowEndpoints && Math.Abs(distance - arc.Radius) > Math.Max(GeometryTolerance, arc.Radius * 0.000001)))
        {
            return false;
        }

        var sweep = GetPositiveSweepDegrees(arc.StartAngleDegrees, arc.EndAngleDegrees);
        parameter = GetCounterClockwiseDeltaDegrees(arc.StartAngleDegrees, AngleDegrees(arc.Center, point));
        if (allowEndpoints)
        {
            return parameter >= -GeometryTolerance && parameter <= sweep + GeometryTolerance;
        }

        return parameter > GeometryTolerance && parameter < sweep - GeometryTolerance;
    }

    private static bool TryGetArcExtensionParameter(
        ArcEntity arc,
        Point2 point,
        double sweep,
        out double parameter)
    {
        parameter = default;
        if (arc.Radius <= GeometryTolerance || sweep >= 360.0 - GeometryTolerance)
        {
            return false;
        }

        var distance = Distance(arc.Center, point);
        if (distance <= GeometryTolerance
            || Math.Abs(distance - arc.Radius) > Math.Max(SampledPathProjectionTolerance, arc.Radius * 0.000001))
        {
            return false;
        }

        var rawParameter = GetCounterClockwiseDeltaDegrees(arc.StartAngleDegrees, AngleDegrees(arc.Center, point));
        return TryGetUnwrappedExtensionParameter(rawParameter, sweep, out parameter);
    }

    private static IEnumerable<double> GetArcExtensionBoundaryParameters(
        ArcEntity arc,
        Point2 point,
        double sweep)
    {
        if (arc.Radius <= GeometryTolerance || sweep >= 360.0 - GeometryTolerance)
        {
            yield break;
        }

        var distance = Distance(arc.Center, point);
        if (distance <= GeometryTolerance
            || Math.Abs(distance - arc.Radius) > Math.Max(SampledPathProjectionTolerance, arc.Radius * 0.000001))
        {
            yield break;
        }

        var rawParameter = GetCounterClockwiseDeltaDegrees(arc.StartAngleDegrees, AngleDegrees(arc.Center, point));
        foreach (var parameter in GetUnwrappedExtensionParameters(rawParameter, sweep))
        {
            yield return parameter;
        }
    }

    private static bool TryGetEllipseSweepParameter(
        EllipseEntity ellipse,
        Point2 point,
        bool allowEndpoints,
        out double parameter)
    {
        parameter = default;
        if (!TryGetEllipseAxes(ellipse, out var majorUnit, out var minorUnit, out var majorLength, out var minorLength))
        {
            return false;
        }

        var local = ToEllipseLocalUnit(point, ellipse.Center, majorUnit, minorUnit, majorLength, minorLength);
        var radialDistance = Math.Sqrt(Dot(local, local));
        if (radialDistance <= GeometryTolerance
            || (!allowEndpoints && Math.Abs(radialDistance - 1.0) > 0.000001))
        {
            return false;
        }

        var parameterDegrees = Math.Atan2(local.Y, local.X) * 180.0 / Math.PI;
        var sweep = GetPositiveSweepDegrees(ellipse.StartParameterDegrees, ellipse.EndParameterDegrees);
        parameter = GetCounterClockwiseDeltaDegrees(ellipse.StartParameterDegrees, parameterDegrees);
        if (allowEndpoints)
        {
            return parameter >= -GeometryTolerance && parameter <= sweep + GeometryTolerance;
        }

        return parameter > GeometryTolerance && parameter < sweep - GeometryTolerance;
    }

    private static bool TryGetEllipseExtensionParameter(
        EllipseEntity ellipse,
        Point2 point,
        double sweep,
        out double parameter)
    {
        parameter = default;
        if (sweep >= 360.0 - GeometryTolerance
            || !TryGetEllipseAxes(ellipse, out var majorUnit, out var minorUnit, out var majorLength, out var minorLength))
        {
            return false;
        }

        var local = ToEllipseLocalUnit(point, ellipse.Center, majorUnit, minorUnit, majorLength, minorLength);
        var radialDistance = Math.Sqrt(Dot(local, local));
        var radialTolerance = SampledPathProjectionTolerance / Math.Max(Math.Min(majorLength, minorLength), GeometryTolerance);
        if (radialDistance <= GeometryTolerance
            || Math.Abs(radialDistance - 1.0) > Math.Max(0.000001, radialTolerance))
        {
            return false;
        }

        var parameterDegrees = Math.Atan2(local.Y, local.X) * 180.0 / Math.PI;
        var rawParameter = GetCounterClockwiseDeltaDegrees(ellipse.StartParameterDegrees, parameterDegrees);
        return TryGetUnwrappedExtensionParameter(rawParameter, sweep, out parameter);
    }

    private static IEnumerable<double> GetEllipseExtensionBoundaryParameters(
        EllipseEntity ellipse,
        Point2 point,
        double sweep)
    {
        if (sweep >= 360.0 - GeometryTolerance
            || !TryGetEllipseAxes(ellipse, out var majorUnit, out var minorUnit, out var majorLength, out var minorLength))
        {
            yield break;
        }

        var local = ToEllipseLocalUnit(point, ellipse.Center, majorUnit, minorUnit, majorLength, minorLength);
        var radialDistance = Math.Sqrt(Dot(local, local));
        var radialTolerance = SampledPathProjectionTolerance / Math.Max(Math.Min(majorLength, minorLength), GeometryTolerance);
        if (radialDistance <= GeometryTolerance
            || Math.Abs(radialDistance - 1.0) > Math.Max(0.000001, radialTolerance))
        {
            yield break;
        }

        var parameterDegrees = Math.Atan2(local.Y, local.X) * 180.0 / Math.PI;
        var rawParameter = GetCounterClockwiseDeltaDegrees(ellipse.StartParameterDegrees, parameterDegrees);
        foreach (var parameter in GetUnwrappedExtensionParameters(rawParameter, sweep))
        {
            yield return parameter;
        }
    }

    private static bool TryGetUnwrappedExtensionParameter(
        double rawParameter,
        double sweep,
        out double parameter)
    {
        parameter = default;
        if (rawParameter >= -GeometryTolerance && rawParameter <= sweep + GeometryTolerance)
        {
            return false;
        }

        var beforeStart = rawParameter - 360.0;
        var afterEnd = rawParameter;
        parameter = Math.Abs(beforeStart) < Math.Abs(afterEnd - sweep)
            ? beforeStart
            : afterEnd;
        return parameter < -GeometryTolerance || parameter > sweep + GeometryTolerance;
    }

    private static IEnumerable<double> GetUnwrappedExtensionParameters(double rawParameter, double sweep)
    {
        if (rawParameter >= -GeometryTolerance && rawParameter <= sweep + GeometryTolerance)
        {
            yield break;
        }

        var beforeStart = rawParameter - 360.0;
        if (beforeStart < -GeometryTolerance)
        {
            yield return beforeStart;
        }

        var afterEnd = rawParameter;
        if (afterEnd > sweep + GeometryTolerance)
        {
            yield return afterEnd;
        }
    }

    private static bool TryGetEllipseAxes(
        EllipseEntity ellipse,
        out Point2 majorUnit,
        out Point2 minorUnit,
        out double majorLength,
        out double minorLength)
    {
        majorLength = Math.Sqrt(Dot(ellipse.MajorAxisEndPoint, ellipse.MajorAxisEndPoint));
        minorLength = majorLength * ellipse.MinorRadiusRatio;
        if (majorLength <= GeometryTolerance || minorLength <= GeometryTolerance)
        {
            majorUnit = default;
            minorUnit = default;
            return false;
        }

        majorUnit = new Point2(ellipse.MajorAxisEndPoint.X / majorLength, ellipse.MajorAxisEndPoint.Y / majorLength);
        minorUnit = new Point2(-majorUnit.Y, majorUnit.X);
        return true;
    }

    private static Point2 ToEllipseLocalUnit(
        Point2 point,
        Point2 center,
        Point2 majorUnit,
        Point2 minorUnit,
        double majorLength,
        double minorLength)
    {
        var delta = Subtract(point, center);
        return new Point2(
            Dot(delta, majorUnit) / majorLength,
            Dot(delta, minorUnit) / minorLength);
    }

    private static bool TryBuildSampledPathDistances(
        IReadOnlyList<Point2> samples,
        out double[] cumulativeDistances,
        out double totalLength)
    {
        cumulativeDistances = new double[samples.Count];
        totalLength = 0;
        for (var index = 1; index < samples.Count; index++)
        {
            totalLength += Distance(samples[index - 1], samples[index]);
            cumulativeDistances[index] = totalLength;
        }

        return totalLength > GeometryTolerance;
    }

    private static bool TryProjectPointToSampledPath(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        out double pathDistance) =>
        TryProjectPointToSampledPath(samples, cumulativeDistances, point, 0.001, out pathDistance);

    private static bool TryProjectPointToSampledPath(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        double maximumDistance,
        out double pathDistance)
    {
        pathDistance = default;
        var closestDistance = double.PositiveInfinity;
        for (var index = 1; index < samples.Count; index++)
        {
            var segmentLength = cumulativeDistances[index] - cumulativeDistances[index - 1];
            if (segmentLength <= GeometryTolerance)
            {
                continue;
            }

            var segment = new LineEntity(EntityId.Create("sample-segment"), samples[index - 1], samples[index]);
            if (!TryProjectParameter(segment, point, out var parameter))
            {
                continue;
            }

            var clampedParameter = Math.Clamp(parameter, 0.0, 1.0);
            var projectedPoint = PointAtParameter(segment, clampedParameter);
            var distance = Distance(projectedPoint, point);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                pathDistance = cumulativeDistances[index - 1] + (segmentLength * clampedParameter);
            }
        }

        return double.IsFinite(closestDistance) && closestDistance <= maximumDistance;
    }

    private static bool TryGetSplineFitPointPathDistances(
        SplineEntity spline,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        out double[] fitPointDistances)
    {
        fitPointDistances = new double[spline.FitPoints.Count];
        if (spline.FitPoints.Count < 2 || samples.Count < 2)
        {
            return false;
        }

        fitPointDistances[0] = 0;
        fitPointDistances[^1] = cumulativeDistances[^1];
        for (var index = 1; index < spline.FitPoints.Count - 1; index++)
        {
            if (!TryProjectPointToSampledPath(
                    samples,
                    cumulativeDistances,
                    spline.FitPoints[index],
                    SampledPathProjectionTolerance,
                    out var fitPointDistance))
            {
                return false;
            }

            fitPointDistances[index] = fitPointDistance;
        }

        return true;
    }

    private static int GetSplineFitPointInsertionIndex(IReadOnlyList<double> fitPointDistances, double pickedDistance)
    {
        for (var index = 1; index < fitPointDistances.Count; index++)
        {
            if (pickedDistance < fitPointDistances[index] - GeometryTolerance)
            {
                return index;
            }
        }

        return fitPointDistances.Count - 1;
    }

    private static bool TryGetSampledPathExtensionLine(
        IReadOnlyList<Point2> samples,
        EntityId targetId,
        Point2 pickedPoint,
        out bool extendStart,
        out LineEntity extensionLine)
    {
        extendStart = false;
        extensionLine = default!;
        if (samples.Count < 2)
        {
            return false;
        }

        var firstSegment = new LineEntity(targetId, samples[0], samples[1]);
        if (TryGetEndpointExtensionLine(firstSegment, pickedPoint, beforeStart: true, out extensionLine))
        {
            extendStart = true;
            return true;
        }

        var lastSegment = new LineEntity(targetId, samples[^2], samples[^1]);
        return TryGetEndpointExtensionLine(lastSegment, pickedPoint, beforeStart: false, out extensionLine);
    }

    private static bool TryGetEndpointExtensionLine(
        LineEntity endpointSegment,
        Point2 pickedPoint,
        bool beforeStart,
        out LineEntity extensionLine)
    {
        extensionLine = default!;
        if (!TryProjectParameter(endpointSegment, pickedPoint, out var parameter)
            || (beforeStart && parameter >= -GeometryTolerance)
            || (!beforeStart && parameter <= 1.0 + GeometryTolerance))
        {
            return false;
        }

        var projectedPoint = PointAtParameter(endpointSegment, parameter);
        if (Distance(projectedPoint, pickedPoint) > SampledPathProjectionTolerance)
        {
            return false;
        }

        var start = beforeStart ? endpointSegment.Start : endpointSegment.End;
        var adjacent = beforeStart ? endpointSegment.End : endpointSegment.Start;
        var direction = Subtract(start, adjacent);
        if (Dot(direction, direction) <= GeometryTolerance * GeometryTolerance)
        {
            return false;
        }

        extensionLine = new LineEntity(endpointSegment.Id, start, Add(start, direction), endpointSegment.IsConstruction);
        return true;
    }

    private static void AddSplineTargetLineCuts(
        SplineEntity target,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        LineEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (target.FitPoints.Count < 2)
        {
            AddKnotSplineTargetLineCuts(target, samples, cumulativeDistances, cutter, cuts);
            return;
        }

        for (var spanIndex = 0; spanIndex < target.FitPoints.Count - 1; spanIndex++)
        {
            AddFitSplineTargetLineSpanCuts(target.FitPoints, spanIndex, samples, cumulativeDistances, cutter, cuts);
        }
    }

    private static void AddKnotSplineTargetLineCuts(
        SplineEntity target,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        LineEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        var cutterDirection = Subtract(cutter.End, cutter.Start);
        var lineTolerance = GeometryTolerance * Math.Max(1.0, Math.Sqrt(Dot(cutterDirection, cutterDirection)));
        AddKnotSplineImplicitRootCuts(
            target,
            point => SignedLineDistanceValue(cutter, point),
            lineTolerance,
            point => AddKnownSplineTargetLineCut(samples, cumulativeDistances, point, cutter, cuts));
    }

    private static void AddFitSplineTargetLineSpanCuts(
        IReadOnlyList<Point2> fitPoints,
        int spanIndex,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        LineEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        const int searchIntervals = 64;
        var previous = spanIndex == 0 ? fitPoints[spanIndex] : fitPoints[spanIndex - 1];
        var start = fitPoints[spanIndex];
        var end = fitPoints[spanIndex + 1];
        var next = spanIndex + 2 < fitPoints.Count ? fitPoints[spanIndex + 2] : end;
        var cutterDirection = Subtract(cutter.End, cutter.Start);
        var lineTolerance = GeometryTolerance * Math.Max(1.0, Math.Sqrt(Dot(cutterDirection, cutterDirection)));
        var previousParameter = 0.0;
        var previousValue = SignedLineDistanceValue(cutter, start);
        AddSplineTargetLineRootIfNear(samples, cumulativeDistances, start, previousValue, lineTolerance, cutter, cuts);

        for (var interval = 1; interval <= searchIntervals; interval++)
        {
            var parameter = (double)interval / searchIntervals;
            var point = EvaluateCatmullRom(previous, start, end, next, parameter);
            var value = SignedLineDistanceValue(cutter, point);
            AddSplineTargetLineRootIfNear(samples, cumulativeDistances, point, value, lineTolerance, cutter, cuts);

            if ((previousValue < -lineTolerance && value > lineTolerance)
                || (previousValue > lineTolerance && value < -lineTolerance))
            {
                var rootParameter = RefineCatmullRomLineRoot(
                    cutter,
                    previous,
                    start,
                    end,
                    next,
                    previousParameter,
                    parameter);
                AddSplineTargetLineCut(samples, cumulativeDistances, EvaluateCatmullRom(previous, start, end, next, rootParameter), cutter, cuts);
            }

            previousParameter = parameter;
            previousValue = value;
        }
    }

    private static void AddSplineTargetLineRootIfNear(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        double signedLineValue,
        double lineTolerance,
        LineEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (Math.Abs(signedLineValue) <= lineTolerance)
        {
            AddSplineTargetLineCut(samples, cumulativeDistances, point, cutter, cuts);
        }
    }

    private static void AddSplineTargetLineCut(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        LineEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (!TryProjectParameter(cutter, point, out var cutterParameter)
            || cutterParameter < -GeometryTolerance
            || cutterParameter > 1.0 + GeometryTolerance)
        {
            return;
        }

        AddSplineTargetCut(samples, cumulativeDistances, point, cuts);
    }

    private static void AddKnownSplineTargetLineCut(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        LineEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (!TryProjectParameter(cutter, point, out var cutterParameter)
            || cutterParameter < -GeometryTolerance
            || cutterParameter > 1.0 + GeometryTolerance)
        {
            return;
        }

        AddKnownSplineTargetCut(samples, cumulativeDistances, point, cuts);
    }

    private static void AddSampledPathLineCuts(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        LineEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        for (var index = 1; index < samples.Count; index++)
        {
            var sampleSegment = new LineEntity(EntityId.Create("sample-segment"), samples[index - 1], samples[index]);
            if (TryGetLineIntersection(
                    sampleSegment,
                    cutter,
                    requireSecondSegment: true,
                    requireFirstSegment: true,
                    out var intersection,
                    out var sampleParameter,
                    out _))
            {
                var segmentLength = cumulativeDistances[index] - cumulativeDistances[index - 1];
                cuts.Add(new SampledPathCut(cumulativeDistances[index - 1] + (segmentLength * sampleParameter), intersection));
            }
        }
    }

    private static void AddSampledPathLineCutDistances(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        LineEntity cutter,
        ICollection<double> cutDistances)
    {
        for (var index = 1; index < samples.Count; index++)
        {
            var sampleSegment = new LineEntity(EntityId.Create("sample-segment"), samples[index - 1], samples[index]);
            if (TryGetLineIntersection(
                    sampleSegment,
                    cutter,
                    requireSecondSegment: true,
                    requireFirstSegment: true,
                    out _,
                    out var sampleParameter,
                    out _))
            {
                var segmentLength = cumulativeDistances[index] - cumulativeDistances[index - 1];
                cutDistances.Add(cumulativeDistances[index - 1] + (segmentLength * sampleParameter));
            }
        }
    }

    private static void AddSampledPathCircleCutDistances(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        CircleEntity cutter,
        ICollection<double> cutDistances)
    {
        for (var index = 1; index < samples.Count; index++)
        {
            var sampleSegment = new LineEntity(EntityId.Create("sample-segment"), samples[index - 1], samples[index]);
            if (!TryGetLineCircleIntersections(cutter, sampleSegment, out var intersections, requireLineSegment: true))
            {
                continue;
            }

            AddSampledSegmentCutDistances(sampleSegment, cumulativeDistances[index - 1], cumulativeDistances[index], intersections, cutDistances);
        }
    }

    private static void AddSplineTargetCircleCuts(
        SplineEntity target,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        CircleEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (target.FitPoints.Count < 2)
        {
            AddKnotSplineTargetCircleCuts(target, samples, cumulativeDistances, cutter, cuts);
            return;
        }

        for (var spanIndex = 0; spanIndex < target.FitPoints.Count - 1; spanIndex++)
        {
            AddFitSplineTargetCircleSpanCuts(target.FitPoints, spanIndex, samples, cumulativeDistances, cutter, cuts);
        }
    }

    private static void AddKnotSplineTargetCircleCuts(
        SplineEntity target,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        CircleEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        AddKnotSplineImplicitRootCuts(
            target,
            point => CircleImplicitValue(cutter.Center, cutter.Radius, point),
            GeometryTolerance,
            point => AddKnownSplineTargetCut(samples, cumulativeDistances, point, cuts));
    }

    private static void AddFitSplineTargetCircleSpanCuts(
        IReadOnlyList<Point2> fitPoints,
        int spanIndex,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        CircleEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        const int searchIntervals = 64;
        var previous = spanIndex == 0 ? fitPoints[spanIndex] : fitPoints[spanIndex - 1];
        var start = fitPoints[spanIndex];
        var end = fitPoints[spanIndex + 1];
        var next = spanIndex + 2 < fitPoints.Count ? fitPoints[spanIndex + 2] : end;
        var previousParameter = 0.0;
        var previousValue = CircleImplicitValue(cutter.Center, cutter.Radius, start);
        AddSplineTargetCircleRootIfNear(samples, cumulativeDistances, start, previousValue, cuts);

        for (var interval = 1; interval <= searchIntervals; interval++)
        {
            var parameter = (double)interval / searchIntervals;
            var point = EvaluateCatmullRom(previous, start, end, next, parameter);
            var value = CircleImplicitValue(cutter.Center, cutter.Radius, point);
            AddSplineTargetCircleRootIfNear(samples, cumulativeDistances, point, value, cuts);

            if ((previousValue < -GeometryTolerance && value > GeometryTolerance)
                || (previousValue > GeometryTolerance && value < -GeometryTolerance))
            {
                var rootParameter = RefineCatmullRomCircleRoot(
                    cutter.Center,
                    cutter.Radius,
                    previous,
                    start,
                    end,
                    next,
                    previousParameter,
                    parameter);
                AddSplineTargetCut(samples, cumulativeDistances, EvaluateCatmullRom(previous, start, end, next, rootParameter), cuts);
            }

            previousParameter = parameter;
            previousValue = value;
        }
    }

    private static void AddSplineTargetCircleRootIfNear(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        double implicitValue,
        ICollection<SampledPathCut> cuts)
    {
        if (Math.Abs(implicitValue) <= GeometryTolerance)
        {
            AddSplineTargetCut(samples, cumulativeDistances, point, cuts);
        }
    }

    private static void AddSampledPathCircleCuts(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        CircleEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        for (var index = 1; index < samples.Count; index++)
        {
            var sampleSegment = new LineEntity(EntityId.Create("sample-segment"), samples[index - 1], samples[index]);
            if (!TryGetLineCircleIntersections(cutter, sampleSegment, out var intersections, requireLineSegment: true))
            {
                continue;
            }

            AddSampledSegmentCuts(sampleSegment, cumulativeDistances[index - 1], cumulativeDistances[index], intersections, cuts);
        }
    }

    private static void AddSampledPathArcCutDistances(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        ArcEntity cutter,
        ICollection<double> cutDistances)
    {
        var cutterCircle = new CircleEntity(cutter.Id, cutter.Center, cutter.Radius, cutter.IsConstruction);
        for (var index = 1; index < samples.Count; index++)
        {
            var sampleSegment = new LineEntity(EntityId.Create("sample-segment"), samples[index - 1], samples[index]);
            if (!TryGetLineCircleIntersections(cutterCircle, sampleSegment, out var intersections, requireLineSegment: true))
            {
                continue;
            }

            AddSampledSegmentCutDistances(
                sampleSegment,
                cumulativeDistances[index - 1],
                cumulativeDistances[index],
                intersections.Where(point => TryGetArcSweepParameter(cutter, point, allowEndpoints: true, out _)),
                cutDistances);
        }
    }

    private static void AddSplineTargetArcCuts(
        SplineEntity target,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        ArcEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (target.FitPoints.Count < 2)
        {
            AddKnotSplineTargetArcCuts(target, samples, cumulativeDistances, cutter, cuts);
            return;
        }

        for (var spanIndex = 0; spanIndex < target.FitPoints.Count - 1; spanIndex++)
        {
            AddFitSplineTargetArcSpanCuts(target.FitPoints, spanIndex, samples, cumulativeDistances, cutter, cuts);
        }
    }

    private static void AddKnotSplineTargetArcCuts(
        SplineEntity target,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        ArcEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        AddKnotSplineImplicitRootCuts(
            target,
            point => CircleImplicitValue(cutter.Center, cutter.Radius, point),
            GeometryTolerance,
            point => AddKnownSplineTargetArcCut(samples, cumulativeDistances, point, cutter, cuts));
    }

    private static void AddFitSplineTargetArcSpanCuts(
        IReadOnlyList<Point2> fitPoints,
        int spanIndex,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        ArcEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        const int searchIntervals = 64;
        var previous = spanIndex == 0 ? fitPoints[spanIndex] : fitPoints[spanIndex - 1];
        var start = fitPoints[spanIndex];
        var end = fitPoints[spanIndex + 1];
        var next = spanIndex + 2 < fitPoints.Count ? fitPoints[spanIndex + 2] : end;
        var previousParameter = 0.0;
        var previousValue = CircleImplicitValue(cutter.Center, cutter.Radius, start);
        AddSplineTargetArcRootIfNear(samples, cumulativeDistances, start, previousValue, cutter, cuts);

        for (var interval = 1; interval <= searchIntervals; interval++)
        {
            var parameter = (double)interval / searchIntervals;
            var point = EvaluateCatmullRom(previous, start, end, next, parameter);
            var value = CircleImplicitValue(cutter.Center, cutter.Radius, point);
            AddSplineTargetArcRootIfNear(samples, cumulativeDistances, point, value, cutter, cuts);

            if ((previousValue < -GeometryTolerance && value > GeometryTolerance)
                || (previousValue > GeometryTolerance && value < -GeometryTolerance))
            {
                var rootParameter = RefineCatmullRomCircleRoot(
                    cutter.Center,
                    cutter.Radius,
                    previous,
                    start,
                    end,
                    next,
                    previousParameter,
                    parameter);
                AddSplineTargetArcCut(samples, cumulativeDistances, EvaluateCatmullRom(previous, start, end, next, rootParameter), cutter, cuts);
            }

            previousParameter = parameter;
            previousValue = value;
        }
    }

    private static void AddSplineTargetArcRootIfNear(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        double implicitValue,
        ArcEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (Math.Abs(implicitValue) <= GeometryTolerance)
        {
            AddSplineTargetArcCut(samples, cumulativeDistances, point, cutter, cuts);
        }
    }

    private static void AddSplineTargetArcCut(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        ArcEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (TryGetArcSweepParameter(cutter, point, allowEndpoints: true, out _))
        {
            AddSplineTargetCut(samples, cumulativeDistances, point, cuts);
        }
    }

    private static void AddKnownSplineTargetArcCut(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        ArcEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (TryGetArcSweepParameter(cutter, point, allowEndpoints: true, out _))
        {
            AddKnownSplineTargetCut(samples, cumulativeDistances, point, cuts);
        }
    }

    private static void AddSampledPathArcCuts(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        ArcEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        var cutterCircle = new CircleEntity(cutter.Id, cutter.Center, cutter.Radius, cutter.IsConstruction);
        for (var index = 1; index < samples.Count; index++)
        {
            var sampleSegment = new LineEntity(EntityId.Create("sample-segment"), samples[index - 1], samples[index]);
            if (!TryGetLineCircleIntersections(cutterCircle, sampleSegment, out var intersections, requireLineSegment: true))
            {
                continue;
            }

            AddSampledSegmentCuts(
                sampleSegment,
                cumulativeDistances[index - 1],
                cumulativeDistances[index],
                intersections.Where(point => TryGetArcSweepParameter(cutter, point, allowEndpoints: true, out _)),
                cuts);
        }
    }

    private static void AddSampledPathEllipseCutDistances(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        EllipseEntity cutter,
        ICollection<double> cutDistances)
    {
        for (var index = 1; index < samples.Count; index++)
        {
            var sampleSegment = new LineEntity(EntityId.Create("sample-segment"), samples[index - 1], samples[index]);
            if (!TryGetLineEllipseIntersections(cutter, sampleSegment, requireLineSegment: true, out var intersections))
            {
                continue;
            }

            AddSampledSegmentCutDistances(sampleSegment, cumulativeDistances[index - 1], cumulativeDistances[index], intersections, cutDistances);
        }
    }

    private static void AddSplineTargetEllipseCuts(
        SplineEntity target,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        EllipseEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (target.FitPoints.Count < 2
            || !TryGetEllipseAxes(cutter, out var majorUnit, out var minorUnit, out var majorLength, out var minorLength))
        {
            if (target.FitPoints.Count < 2)
            {
                AddKnotSplineTargetEllipseCuts(target, samples, cumulativeDistances, cutter, cuts);
                return;
            }

            AddSampledPathEllipseCuts(samples, cumulativeDistances, cutter, cuts);
            return;
        }

        for (var spanIndex = 0; spanIndex < target.FitPoints.Count - 1; spanIndex++)
        {
            AddFitSplineTargetEllipseSpanCuts(
                target.FitPoints,
                spanIndex,
                samples,
                cumulativeDistances,
                cutter,
                majorUnit,
                minorUnit,
                majorLength,
                minorLength,
                cuts);
        }
    }

    private static void AddKnotSplineTargetEllipseCuts(
        SplineEntity target,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        EllipseEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (!TryGetEllipseAxes(cutter, out var majorUnit, out var minorUnit, out var majorLength, out var minorLength))
        {
            return;
        }

        AddKnotSplineImplicitRootCuts(
            target,
            point => EllipseImplicitValue(cutter, majorUnit, minorUnit, majorLength, minorLength, point),
            GeometryTolerance,
            point => AddKnownSplineTargetEllipseCut(samples, cumulativeDistances, point, cutter, cuts));
    }

    private static void AddFitSplineTargetEllipseSpanCuts(
        IReadOnlyList<Point2> fitPoints,
        int spanIndex,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        EllipseEntity cutter,
        Point2 majorUnit,
        Point2 minorUnit,
        double majorLength,
        double minorLength,
        ICollection<SampledPathCut> cuts)
    {
        const int searchIntervals = 64;
        var previous = spanIndex == 0 ? fitPoints[spanIndex] : fitPoints[spanIndex - 1];
        var start = fitPoints[spanIndex];
        var end = fitPoints[spanIndex + 1];
        var next = spanIndex + 2 < fitPoints.Count ? fitPoints[spanIndex + 2] : end;
        var previousParameter = 0.0;
        var previousValue = EllipseImplicitValue(cutter, majorUnit, minorUnit, majorLength, minorLength, start);
        AddSplineTargetEllipseRootIfNear(samples, cumulativeDistances, start, previousValue, cutter, cuts);

        for (var interval = 1; interval <= searchIntervals; interval++)
        {
            var parameter = (double)interval / searchIntervals;
            var point = EvaluateCatmullRom(previous, start, end, next, parameter);
            var value = EllipseImplicitValue(cutter, majorUnit, minorUnit, majorLength, minorLength, point);
            AddSplineTargetEllipseRootIfNear(samples, cumulativeDistances, point, value, cutter, cuts);

            if ((previousValue < -GeometryTolerance && value > GeometryTolerance)
                || (previousValue > GeometryTolerance && value < -GeometryTolerance))
            {
                var rootParameter = RefineCatmullRomEllipseRoot(
                    cutter,
                    majorUnit,
                    minorUnit,
                    majorLength,
                    minorLength,
                    previous,
                    start,
                    end,
                    next,
                    previousParameter,
                    parameter);
                AddSplineTargetEllipseCut(samples, cumulativeDistances, EvaluateCatmullRom(previous, start, end, next, rootParameter), cutter, cuts);
            }

            previousParameter = parameter;
            previousValue = value;
        }
    }

    private static void AddSplineTargetEllipseRootIfNear(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        double implicitValue,
        EllipseEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (Math.Abs(implicitValue) <= GeometryTolerance)
        {
            AddSplineTargetEllipseCut(samples, cumulativeDistances, point, cutter, cuts);
        }
    }

    private static void AddSplineTargetEllipseCut(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        EllipseEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (TryGetEllipseSweepParameter(cutter, point, allowEndpoints: true, out _))
        {
            AddSplineTargetCut(samples, cumulativeDistances, point, cuts);
        }
    }

    private static void AddKnownSplineTargetEllipseCut(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        EllipseEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (TryGetEllipseSweepParameter(cutter, point, allowEndpoints: true, out _))
        {
            AddKnownSplineTargetCut(samples, cumulativeDistances, point, cuts);
        }
    }

    private static void AddSampledPathEllipseCuts(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        EllipseEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        for (var index = 1; index < samples.Count; index++)
        {
            var sampleSegment = new LineEntity(EntityId.Create("sample-segment"), samples[index - 1], samples[index]);
            if (!TryGetLineEllipseIntersections(cutter, sampleSegment, requireLineSegment: true, out var intersections))
            {
                continue;
            }

            AddSampledSegmentCuts(sampleSegment, cumulativeDistances[index - 1], cumulativeDistances[index], intersections, cuts);
        }
    }

    private static void AddSplineTargetSegmentCuts(
        SplineEntity target,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        IReadOnlyList<Point2> cutterVertices,
        bool closeLoop,
        ICollection<SampledPathCut> cuts)
    {
        if (cutterVertices.Count < 2)
        {
            return;
        }

        for (var index = 1; index < cutterVertices.Count; index++)
        {
            AddSplineTargetLineCuts(
                target,
                samples,
                cumulativeDistances,
                new LineEntity(EntityId.Create("cutter-segment"), cutterVertices[index - 1], cutterVertices[index]),
                cuts);
        }

        if (closeLoop && cutterVertices.Count > 2)
        {
            AddSplineTargetLineCuts(
                target,
                samples,
                cumulativeDistances,
                new LineEntity(EntityId.Create("cutter-segment"), cutterVertices[^1], cutterVertices[0]),
                cuts);
        }
    }

    private static void AddSplineTargetSplineCuts(
        SplineEntity target,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        SplineEntity cutter,
        ICollection<SampledPathCut> cuts)
    {
        if (target.FitPoints.Count < 2 || cutter.FitPoints.Count < 2)
        {
            AddSplineTargetSegmentCuts(target, samples, cumulativeDistances, cutter.GetSamplePoints(), closeLoop: false, cuts);
            return;
        }

        for (var targetSpanIndex = 0; targetSpanIndex < target.FitPoints.Count - 1; targetSpanIndex++)
        {
            for (var cutterSpanIndex = 0; cutterSpanIndex < cutter.FitPoints.Count - 1; cutterSpanIndex++)
            {
                AddFitSplineTargetSplineSpanCuts(
                    target.FitPoints,
                    targetSpanIndex,
                    cutter.FitPoints,
                    cutterSpanIndex,
                    samples,
                    cumulativeDistances,
                    cuts);
            }
        }
    }

    private static void AddFitSplineTargetSplineSpanCuts(
        IReadOnlyList<Point2> targetFitPoints,
        int targetSpanIndex,
        IReadOnlyList<Point2> cutterFitPoints,
        int cutterSpanIndex,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        ICollection<SampledPathCut> cuts)
    {
        const int searchIntervals = 16;
        var targetPrevious = targetSpanIndex == 0 ? targetFitPoints[targetSpanIndex] : targetFitPoints[targetSpanIndex - 1];
        var targetStart = targetFitPoints[targetSpanIndex];
        var targetEnd = targetFitPoints[targetSpanIndex + 1];
        var targetNext = targetSpanIndex + 2 < targetFitPoints.Count ? targetFitPoints[targetSpanIndex + 2] : targetEnd;
        var cutterPrevious = cutterSpanIndex == 0 ? cutterFitPoints[cutterSpanIndex] : cutterFitPoints[cutterSpanIndex - 1];
        var cutterStart = cutterFitPoints[cutterSpanIndex];
        var cutterEnd = cutterFitPoints[cutterSpanIndex + 1];
        var cutterNext = cutterSpanIndex + 2 < cutterFitPoints.Count ? cutterFitPoints[cutterSpanIndex + 2] : cutterEnd;

        var targetPoints = new Point2[searchIntervals + 1];
        var cutterPoints = new Point2[searchIntervals + 1];
        for (var index = 0; index <= searchIntervals; index++)
        {
            var parameter = (double)index / searchIntervals;
            targetPoints[index] = EvaluateCatmullRom(targetPrevious, targetStart, targetEnd, targetNext, parameter);
            cutterPoints[index] = EvaluateCatmullRom(cutterPrevious, cutterStart, cutterEnd, cutterNext, parameter);
        }

        for (var targetIndex = 1; targetIndex <= searchIntervals; targetIndex++)
        {
            var targetSegment = new LineEntity(EntityId.Create("target-spline-segment"), targetPoints[targetIndex - 1], targetPoints[targetIndex]);
            for (var cutterIndex = 1; cutterIndex <= searchIntervals; cutterIndex++)
            {
                var cutterSegment = new LineEntity(EntityId.Create("cutter-spline-segment"), cutterPoints[cutterIndex - 1], cutterPoints[cutterIndex]);
                if (!TryGetLineIntersection(
                        targetSegment,
                        cutterSegment,
                        requireSecondSegment: true,
                        requireFirstSegment: true,
                        out _,
                        out var targetSegmentParameter,
                        out var cutterSegmentParameter))
                {
                    continue;
                }

                var targetInitialParameter = ((targetIndex - 1) + Math.Clamp(targetSegmentParameter, 0.0, 1.0)) / searchIntervals;
                var cutterInitialParameter = ((cutterIndex - 1) + Math.Clamp(cutterSegmentParameter, 0.0, 1.0)) / searchIntervals;
                if (TryRefineCatmullRomIntersection(
                        targetPrevious,
                        targetStart,
                        targetEnd,
                        targetNext,
                        cutterPrevious,
                        cutterStart,
                        cutterEnd,
                        cutterNext,
                        targetInitialParameter,
                        cutterInitialParameter,
                        out var point))
                {
                    AddSplineTargetCut(samples, cumulativeDistances, point, cuts);
                }
            }
        }
    }

    private static bool TryRefineCatmullRomIntersection(
        Point2 targetPrevious,
        Point2 targetStart,
        Point2 targetEnd,
        Point2 targetNext,
        Point2 cutterPrevious,
        Point2 cutterStart,
        Point2 cutterEnd,
        Point2 cutterNext,
        double targetParameter,
        double cutterParameter,
        out Point2 point)
    {
        var targetT = Math.Clamp(targetParameter, 0.0, 1.0);
        var cutterT = Math.Clamp(cutterParameter, 0.0, 1.0);
        for (var iteration = 0; iteration < 32; iteration++)
        {
            var targetPoint = EvaluateCatmullRom(targetPrevious, targetStart, targetEnd, targetNext, targetT);
            var cutterPoint = EvaluateCatmullRom(cutterPrevious, cutterStart, cutterEnd, cutterNext, cutterT);
            var delta = Subtract(targetPoint, cutterPoint);
            if (Math.Sqrt(Dot(delta, delta)) <= GeometryTolerance)
            {
                point = targetPoint;
                return true;
            }

            var targetDerivative = EvaluateCatmullRomDerivative(targetPrevious, targetStart, targetEnd, targetNext, targetT);
            var cutterDerivative = EvaluateCatmullRomDerivative(cutterPrevious, cutterStart, cutterEnd, cutterNext, cutterT);
            var a = targetDerivative.X;
            var b = -cutterDerivative.X;
            var c = targetDerivative.Y;
            var d = -cutterDerivative.Y;
            var determinant = (a * d) - (b * c);
            if (Math.Abs(determinant) <= 1e-12)
            {
                break;
            }

            var rightX = -delta.X;
            var rightY = -delta.Y;
            var targetStep = ((rightX * d) - (b * rightY)) / determinant;
            var cutterStep = ((a * rightY) - (rightX * c)) / determinant;
            if (!double.IsFinite(targetStep) || !double.IsFinite(cutterStep))
            {
                break;
            }

            targetT = Math.Clamp(targetT + targetStep, 0.0, 1.0);
            cutterT = Math.Clamp(cutterT + cutterStep, 0.0, 1.0);
            if (Math.Abs(targetStep) + Math.Abs(cutterStep) <= 1e-12)
            {
                break;
            }
        }

        var finalTargetPoint = EvaluateCatmullRom(targetPrevious, targetStart, targetEnd, targetNext, targetT);
        var finalCutterPoint = EvaluateCatmullRom(cutterPrevious, cutterStart, cutterEnd, cutterNext, cutterT);
        if (Distance(finalTargetPoint, finalCutterPoint) <= SampledPathProjectionTolerance)
        {
            point = new Point2(
                (finalTargetPoint.X + finalCutterPoint.X) / 2.0,
                (finalTargetPoint.Y + finalCutterPoint.Y) / 2.0);
            return true;
        }

        point = default;
        return false;
    }

    private static void AddKnotSplineImplicitRootCuts(
        SplineEntity spline,
        Func<Point2, double> implicitValue,
        double tolerance,
        Action<Point2> addCut)
    {
        const int searchIntervals = 64;
        foreach (var (spanStart, spanEnd) in spline.GetKnotParameterSpans())
        {
            if (!spline.TryEvaluateKnotParameter(spanStart, out var previousPoint))
            {
                continue;
            }

            var previousParameter = spanStart;
            var previousValue = implicitValue(previousPoint);
            AddKnotSplineImplicitRootIfNear(previousPoint, previousValue, tolerance, addCut);

            for (var interval = 1; interval <= searchIntervals; interval++)
            {
                var parameter = interval == searchIntervals
                    ? spanEnd
                    : spanStart + ((spanEnd - spanStart) * interval / searchIntervals);
                if (!spline.TryEvaluateKnotParameter(parameter, out var point))
                {
                    continue;
                }

                var value = implicitValue(point);
                AddKnotSplineImplicitRootIfNear(point, value, tolerance, addCut);

                if (HasOppositeSigns(previousValue, value, tolerance))
                {
                    var rootParameter = RefineKnotSplineImplicitRoot(
                        spline,
                        implicitValue,
                        previousParameter,
                        parameter);
                    if (spline.TryEvaluateKnotParameter(rootParameter, out var rootPoint))
                    {
                        addCut(rootPoint);
                    }
                }

                previousParameter = parameter;
                previousValue = value;
            }
        }
    }

    private static void AddKnotSplineImplicitRootIfNear(
        Point2 point,
        double value,
        double tolerance,
        Action<Point2> addCut)
    {
        if (Math.Abs(value) <= tolerance)
        {
            addCut(point);
        }
    }

    private static bool HasOppositeSigns(double first, double second, double tolerance) =>
        (first < -tolerance && second > tolerance)
        || (first > tolerance && second < -tolerance);

    private static double RefineKnotSplineImplicitRoot(
        SplineEntity spline,
        Func<Point2, double> implicitValue,
        double low,
        double high)
    {
        if (!spline.TryEvaluateKnotParameter(low, out var lowPoint))
        {
            return (low + high) / 2.0;
        }

        var lowValue = implicitValue(lowPoint);
        for (var index = 0; index < 80; index++)
        {
            var middle = (low + high) / 2.0;
            if (!spline.TryEvaluateKnotParameter(middle, out var middlePoint))
            {
                return middle;
            }

            var middleValue = implicitValue(middlePoint);
            if (HasOppositeSigns(lowValue, middleValue, 0))
            {
                high = middle;
            }
            else
            {
                low = middle;
                lowValue = middleValue;
            }
        }

        return (low + high) / 2.0;
    }

    private static void AddSampledPathSegmentCutDistances(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        IReadOnlyList<Point2> cutterVertices,
        bool closeLoop,
        ICollection<double> cutDistances)
    {
        if (cutterVertices.Count < 2)
        {
            return;
        }

        for (var index = 1; index < cutterVertices.Count; index++)
        {
            AddSampledPathLineCutDistances(
                samples,
                cumulativeDistances,
                new LineEntity(EntityId.Create("cutter-segment"), cutterVertices[index - 1], cutterVertices[index]),
                cutDistances);
        }

        if (closeLoop && cutterVertices.Count > 2)
        {
            AddSampledPathLineCutDistances(
                samples,
                cumulativeDistances,
                new LineEntity(EntityId.Create("cutter-segment"), cutterVertices[^1], cutterVertices[0]),
                cutDistances);
        }
    }

    private static void AddSampledPathPointCutDistance(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        ICollection<double> cutDistances)
    {
        if (TryProjectPointToSampledPath(samples, cumulativeDistances, point, out var pathDistance))
        {
            cutDistances.Add(pathDistance);
        }
    }

    private static void AddSampledSegmentCutDistances(
        LineEntity sampleSegment,
        double startDistance,
        double endDistance,
        IEnumerable<Point2> points,
        ICollection<double> cutDistances)
    {
        foreach (var point in points)
        {
            if (!TryProjectParameter(sampleSegment, point, out var parameter)
                || parameter < -GeometryTolerance
                || parameter > 1.0 + GeometryTolerance)
            {
                continue;
            }

            var segmentLength = endDistance - startDistance;
            cutDistances.Add(startDistance + (segmentLength * Math.Clamp(parameter, 0.0, 1.0)));
        }
    }

    private static void AddSampledSegmentCuts(
        LineEntity sampleSegment,
        double startDistance,
        double endDistance,
        IEnumerable<Point2> points,
        ICollection<SampledPathCut> cuts)
    {
        foreach (var point in points)
        {
            if (!TryProjectParameter(sampleSegment, point, out var parameter)
                || parameter < -GeometryTolerance
                || parameter > 1.0 + GeometryTolerance)
            {
                continue;
            }

            var segmentLength = endDistance - startDistance;
            cuts.Add(new SampledPathCut(startDistance + (segmentLength * Math.Clamp(parameter, 0.0, 1.0)), point));
        }
    }

    private static void AddSplineTargetCut(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        ICollection<SampledPathCut> cuts)
    {
        if (TryProjectPointToSampledPath(samples, cumulativeDistances, point, SampledPathProjectionTolerance, out var pathDistance))
        {
            cuts.Add(new SampledPathCut(pathDistance, point));
        }
    }

    private static void AddKnownSplineTargetCut(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        Point2 point,
        ICollection<SampledPathCut> cuts)
    {
        if (TryProjectPointToSampledPath(samples, cumulativeDistances, point, double.PositiveInfinity, out var pathDistance))
        {
            cuts.Add(new SampledPathCut(pathDistance, point));
        }
    }

    private static void AddTrimmedSplineIfValid(
        SplineEntity target,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        double startDistance,
        double endDistance,
        EntityId id,
        ICollection<DrawingEntity> entities,
        Point2? startPointOverride = null,
        Point2? endPointOverride = null)
    {
        if (endDistance - startDistance <= GeometryTolerance)
        {
            return;
        }

        var points = GetSampledPathSpan(samples, cumulativeDistances, startDistance, endDistance, startPointOverride, endPointOverride);
        if (points.Count < 2)
        {
            return;
        }

        entities.Add(new SplineEntity(id, 1, points, Array.Empty<double>(), isConstruction: target.IsConstruction));
    }

    private static void AddTrimmedPolylineIfValid(
        PolylineEntity target,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        double startDistance,
        double endDistance,
        EntityId id,
        ICollection<DrawingEntity> entities)
    {
        if (endDistance - startDistance <= GeometryTolerance)
        {
            return;
        }

        var points = GetSampledPathSpan(samples, cumulativeDistances, startDistance, endDistance);
        if (points.Count < 2)
        {
            return;
        }

        entities.Add(new PolylineEntity(id, points, target.IsConstruction));
    }

    private static void AddLineSegmentsFromPoints(
        IReadOnlyList<Point2> points,
        bool isConstruction,
        string idPrefix,
        Func<string, EntityId> createEntityId,
        ICollection<DrawingEntity> entities)
    {
        var lineIndex = 1;
        for (var index = 1; index < points.Count; index++)
        {
            var start = points[index - 1];
            var end = points[index];
            if (Distance(start, end) <= GeometryTolerance)
            {
                continue;
            }

            entities.Add(new LineEntity(createEntityId($"{idPrefix}-{lineIndex}"), start, end, isConstruction));
            lineIndex++;
        }
    }

    private static void AddLineSegmentsFromPathBoundaries(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        IReadOnlyList<double> boundaryDistances,
        int omittedSegmentIndex,
        bool isConstruction,
        string idPrefix,
        Func<string, EntityId> createEntityId,
        ICollection<DrawingEntity> entities)
    {
        var lineIndex = 1;
        for (var index = 1; index < boundaryDistances.Count; index++)
        {
            if (index - 1 == omittedSegmentIndex)
            {
                continue;
            }

            var start = PointAtSampledPathDistance(samples, cumulativeDistances, boundaryDistances[index - 1]);
            var end = PointAtSampledPathDistance(samples, cumulativeDistances, boundaryDistances[index]);
            if (Distance(start, end) <= GeometryTolerance)
            {
                continue;
            }

            entities.Add(new LineEntity(createEntityId($"{idPrefix}-{lineIndex}"), start, end, isConstruction));
            lineIndex++;
        }
    }

    private static int FindPickedBoundarySpan(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        IReadOnlyList<double> boundaryDistances,
        double pickedDistance,
        Point2 pickedPoint)
    {
        var normalizedPickedDistance = Math.Clamp(pickedDistance, 0.0, cumulativeDistances[^1]);
        for (var index = 1; index < boundaryDistances.Count; index++)
        {
            if (normalizedPickedDistance > boundaryDistances[index - 1] + GeometryTolerance
                && normalizedPickedDistance < boundaryDistances[index] - GeometryTolerance)
            {
                return index - 1;
            }
        }

        var closestIndex = 0;
        var closestDistance = double.PositiveInfinity;
        for (var index = 1; index < boundaryDistances.Count; index++)
        {
            var start = PointAtSampledPathDistance(samples, cumulativeDistances, boundaryDistances[index - 1]);
            var end = PointAtSampledPathDistance(samples, cumulativeDistances, boundaryDistances[index]);
            var segment = new LineEntity(EntityId.Create("boundary-segment"), start, end);
            if (!TryProjectParameter(segment, pickedPoint, out var parameter))
            {
                continue;
            }

            var projectedPoint = PointAtParameter(segment, Math.Clamp(parameter, 0.0, 1.0));
            var distance = Distance(projectedPoint, pickedPoint);
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestIndex = index - 1;
            }
        }

        return closestIndex;
    }

    private static IReadOnlyList<Point2> GetSampledPathSpan(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        double startDistance,
        double endDistance,
        Point2? startPointOverride = null,
        Point2? endPointOverride = null)
    {
        var points = new List<Point2>
        {
            startPointOverride ?? PointAtSampledPathDistance(samples, cumulativeDistances, startDistance)
        };

        for (var index = 1; index < samples.Count - 1; index++)
        {
            var distance = cumulativeDistances[index];
            if (distance > startDistance + GeometryTolerance && distance < endDistance - GeometryTolerance)
            {
                AddDistinctPoint(points, samples[index]);
            }
        }

        AddDistinctPoint(points, endPointOverride ?? PointAtSampledPathDistance(samples, cumulativeDistances, endDistance));
        return points;
    }

    private static IReadOnlyList<Point2> GetClosedPathSamples(IReadOnlyList<Point2> vertices)
    {
        if (vertices.Count == 0)
        {
            return Array.Empty<Point2>();
        }

        var samples = vertices.ToList();
        AddDistinctPoint(samples, vertices[0]);
        return samples;
    }

    private static IReadOnlyList<Point2> GetClosedSampledPathSpan(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        double totalLength,
        double startDistance,
        double endDistance)
    {
        var points = new List<Point2>
        {
            PointAtClosedPathDistance(samples, cumulativeDistances, totalLength, startDistance)
        };

        AddClosedSampledPathInteriorPoints(points, samples, cumulativeDistances, startDistance, endDistance, offset: 0);
        AddClosedSampledPathInteriorPoints(points, samples, cumulativeDistances, startDistance, endDistance, offset: totalLength);
        AddDistinctPoint(points, PointAtClosedPathDistance(samples, cumulativeDistances, totalLength, endDistance));
        return points;
    }

    private static void AddClosedSampledPathInteriorPoints(
        ICollection<Point2> points,
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        double startDistance,
        double endDistance,
        double offset)
    {
        for (var index = 1; index < samples.Count; index++)
        {
            var distance = cumulativeDistances[index] + offset;
            if (distance > startDistance + GeometryTolerance && distance < endDistance - GeometryTolerance)
            {
                AddDistinctPoint(points, samples[index]);
            }
        }
    }

    private static Point2 PointAtClosedPathDistance(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        double totalLength,
        double distance)
    {
        var normalized = distance % totalLength;
        if (normalized < 0)
        {
            normalized += totalLength;
        }

        if (totalLength - normalized <= GeometryTolerance)
        {
            normalized = totalLength;
        }

        return PointAtSampledPathDistance(samples, cumulativeDistances, normalized);
    }

    private static Point2 PointAtSampledPathDistance(
        IReadOnlyList<Point2> samples,
        IReadOnlyList<double> cumulativeDistances,
        double distance)
    {
        if (distance <= GeometryTolerance)
        {
            return samples[0];
        }

        for (var index = 1; index < samples.Count; index++)
        {
            if (distance <= cumulativeDistances[index] + GeometryTolerance)
            {
                var segmentLength = cumulativeDistances[index] - cumulativeDistances[index - 1];
                if (segmentLength <= GeometryTolerance)
                {
                    return samples[index];
                }

                var parameter = Math.Clamp((distance - cumulativeDistances[index - 1]) / segmentLength, 0.0, 1.0);
                return PointAtParameter(new LineEntity(EntityId.Create("sample-segment"), samples[index - 1], samples[index]), parameter);
            }
        }

        return samples[^1];
    }

    private static void AddDistinctPoint(ICollection<Point2> points, Point2 point)
    {
        if (points.Count == 0 || Distance(points.Last(), point) > GeometryTolerance)
        {
            points.Add(point);
        }
    }

    private static bool ReplaceTargetArc(
        DrawingDocument document,
        ArcEntity target,
        double startParameter,
        double endParameter,
        out DrawingDocument nextDocument)
    {
        var replacements = new List<DrawingEntity>();
        AddTrimmedArcIfValid(target, startParameter, endParameter, target.Id, replacements);
        if (replacements.Count != 1)
        {
            nextDocument = document;
            return false;
        }

        nextDocument = ReplaceEntities(
            document,
            new Dictionary<string, DrawingEntity>(StringComparer.Ordinal)
            {
                [target.Id.Value] = replacements[0]
            },
            Array.Empty<DrawingEntity>());
        return true;
    }

    private static bool ReplaceTargetEllipse(
        DrawingDocument document,
        EllipseEntity target,
        double startParameter,
        double endParameter,
        out DrawingDocument nextDocument)
    {
        var replacements = new List<DrawingEntity>();
        AddTrimmedEllipseIfValid(target, startParameter, endParameter, target.Id, replacements);
        if (replacements.Count != 1)
        {
            nextDocument = document;
            return false;
        }

        nextDocument = ReplaceEntities(
            document,
            new Dictionary<string, DrawingEntity>(StringComparer.Ordinal)
            {
                [target.Id.Value] = replacements[0]
            },
            Array.Empty<DrawingEntity>());
        return true;
    }

    private static void AddTrimmedArcIfValid(
        ArcEntity target,
        double startParameter,
        double endParameter,
        EntityId id,
        ICollection<DrawingEntity> entities)
    {
        if (endParameter - startParameter <= GeometryTolerance)
        {
            return;
        }

        entities.Add(target with
        {
            Id = id,
            StartAngleDegrees = target.StartAngleDegrees + startParameter,
            EndAngleDegrees = target.StartAngleDegrees + endParameter
        });
    }

    private static void AddTrimmedEllipseIfValid(
        EllipseEntity target,
        double startParameter,
        double endParameter,
        EntityId id,
        ICollection<DrawingEntity> entities)
    {
        if (endParameter - startParameter <= GeometryTolerance)
        {
            return;
        }

        entities.Add(target with
        {
            Id = id,
            StartParameterDegrees = target.StartParameterDegrees + startParameter,
            EndParameterDegrees = target.StartParameterDegrees + endParameter
        });
    }

    private static double NormalizeAngleDegrees(double angle)
    {
        var normalized = angle % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static double GetPositiveSweepDegrees(double startAngleDegrees, double endAngleDegrees)
    {
        var sweep = GetCounterClockwiseDeltaDegrees(startAngleDegrees, endAngleDegrees);
        return sweep <= GeometryTolerance ? 360.0 : sweep;
    }

    private static double GetCounterClockwiseDeltaDegrees(double startAngleDegrees, double angleDegrees)
    {
        var delta = (angleDegrees - startAngleDegrees) % 360.0;
        return delta < 0 ? delta + 360.0 : delta;
    }

    private static double DistancePointToSegment(Point2 point, LineEntity segment)
    {
        var delta = Subtract(segment.End, segment.Start);
        var lengthSquared = Dot(delta, delta);
        if (lengthSquared <= GeometryTolerance * GeometryTolerance)
        {
            return Distance(point, segment.Start);
        }

        var parameter = Math.Clamp(Dot(Subtract(point, segment.Start), delta) / lengthSquared, 0, 1);
        return Distance(point, PointAtParameter(segment, parameter));
    }

    private static bool TryGetAngleDelta(Point2 center, Point2 reference, Point2 target, out double degrees)
    {
        if (Distance(center, reference) <= GeometryTolerance || Distance(center, target) <= GeometryTolerance)
        {
            degrees = default;
            return false;
        }

        degrees = AngleDegrees(center, target) - AngleDegrees(center, reference);
        return Math.Abs(degrees) > GeometryTolerance;
    }

    private static Point2 ScalePoint(Point2 point, Point2 center, double scale) =>
        new(
            center.X + (point.X - center.X) * scale,
            center.Y + (point.Y - center.Y) * scale);

    private static Point2 MirrorPoint(Point2 point, Point2 axisStart, Point2 axisEnd)
    {
        var axis = Subtract(axisEnd, axisStart);
        var lengthSquared = Dot(axis, axis);
        if (lengthSquared <= GeometryTolerance * GeometryTolerance)
        {
            return point;
        }

        var parameter = Dot(Subtract(point, axisStart), axis) / lengthSquared;
        var projection = Add(axisStart, Multiply(axis, parameter));
        return new Point2(
            projection.X * 2.0 - point.X,
            projection.Y * 2.0 - point.Y);
    }

    private static Point2 PointOnArc(ArcEntity arc, double angleDegrees)
    {
        return PointOnCircle(arc.Center, arc.Radius, angleDegrees);
    }

    private static Point2 PointOnCircle(Point2 center, double radius, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point2(
            center.X + radius * Math.Cos(radians),
            center.Y + radius * Math.Sin(radians));
    }

    private static double AngleDegrees(Point2 center, Point2 point) =>
        Math.Atan2(point.Y - center.Y, point.X - center.X) * 180.0 / Math.PI;

    private static double Distance(Point2 first, Point2 second) =>
        Math.Sqrt(Math.Pow(second.X - first.X, 2) + Math.Pow(second.Y - first.Y, 2));

    private static Point2 Add(Point2 first, Point2 second) => new(first.X + second.X, first.Y + second.Y);

    private static Point2 Subtract(Point2 first, Point2 second) => new(first.X - second.X, first.Y - second.Y);

    private static Point2 Multiply(Point2 point, double scalar) => new(point.X * scalar, point.Y * scalar);

    private static double Dot(Point2 first, Point2 second) => first.X * second.X + first.Y * second.Y;

    private static double Cross(Point2 first, Point2 second) => first.X * second.Y - first.Y * second.X;

    private static bool ReferenceKeysContainEntity(IEnumerable<string> referenceKeys, string entityId)
    {
        foreach (var referenceKey in referenceKeys)
        {
            if (SketchReference.TryParse(referenceKey, out var reference)
                && StringComparer.Ordinal.Equals(reference.EntityId, entityId))
            {
                return true;
            }
        }

        return false;
    }

    private static Point2? Unit(Point2 point)
    {
        var length = Math.Sqrt(point.X * point.X + point.Y * point.Y);
        return length <= GeometryTolerance
            ? null
            : new Point2(point.X / length, point.Y / length);
    }
}

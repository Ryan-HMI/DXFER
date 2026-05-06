using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Operations;

public readonly record struct PowerTrimLinePick(
    string TargetEntityId,
    Point2 PickedPoint);

public static class DrawingModifyService
{
    private const double GeometryTolerance = 0.000001;

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
            ? new DrawingDocument(nextEntities, document.Dimensions, document.Constraints)
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
        nextDocument = new DrawingDocument(nextEntities, document.Dimensions, document.Constraints);
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
        nextDocument = new DrawingDocument(nextEntities, document.Dimensions, document.Constraints);
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

        var target = document.Entities.OfType<LineEntity>()
            .FirstOrDefault(line => StringComparer.Ordinal.Equals(line.Id.Value, targetEntityId));
        if (target is null || !TryProjectParameter(target, pickedPoint, out var pickedParameter))
        {
            nextDocument = document;
            return false;
        }

        var cutParameters = new List<double>();
        foreach (var cutter in document.Entities.OfType<LineEntity>())
        {
            if (StringComparer.Ordinal.Equals(cutter.Id.Value, target.Id.Value))
            {
                continue;
            }

            if (TryGetLineIntersection(target, cutter, true, false, out _, out var targetParameter, out _))
            {
                cutParameters.Add(targetParameter);
            }
        }

        var distinctCuts = cutParameters
            .Where(parameter => double.IsFinite(parameter))
            .DistinctBy(parameter => Math.Round(parameter, 6))
            .OrderBy(parameter => parameter)
            .ToArray();
        if (distinctCuts.Length == 0)
        {
            nextDocument = document;
            return false;
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
            nextDocument = document;
            return false;
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

            nextDocument = new DrawingDocument(nextEntities, document.Dimensions, document.Constraints);
            return true;
        }

        if (double.IsFinite(left))
        {
            return ReplaceTargetLine(document, target, target with { End = PointAtParameter(target, left) }, out nextDocument);
        }

        return ReplaceTargetLine(document, target, target with { Start = PointAtParameter(target, right) }, out nextDocument);
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
            document.Constraints);
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
            document.Constraints);
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
        }

        offsetEntity = default!;
        return false;
    }

    private static bool TryOffsetLine(LineEntity line, Point2 throughPoint, EntityId entityId, out LineEntity offsetLine)
    {
        var deltaX = line.End.X - line.Start.X;
        var deltaY = line.End.Y - line.Start.Y;
        var length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (length <= GeometryTolerance)
        {
            offsetLine = default!;
            return false;
        }

        var normal = new Point2(-deltaY / length, deltaX / length);
        var signedDistance = Dot(Subtract(throughPoint, line.Start), normal);
        if (Math.Abs(signedDistance) <= GeometryTolerance)
        {
            offsetLine = default!;
            return false;
        }

        var offset = Multiply(normal, signedDistance);
        offsetLine = new LineEntity(entityId, Add(line.Start, offset), Add(line.End, offset), line.IsConstruction);
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
                spline.IsConstruction),
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
                spline.IsConstruction),
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
            SplineEntity spline => new SplineEntity(id, spline.Degree, spline.ControlPoints, spline.Knots, spline.Weights, spline.IsConstruction),
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
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point2(
            arc.Center.X + arc.Radius * Math.Cos(radians),
            arc.Center.Y + arc.Radius * Math.Sin(radians));
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

    private static Point2? Unit(Point2 point)
    {
        var length = Math.Sqrt(point.X * point.X + point.Y * point.Y);
        return length <= GeometryTolerance
            ? null
            : new Point2(point.X / length, point.Y / length);
    }
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Blazor.Sketching;

public static class SketchCommandFactory
{
    private const double GeometryTolerance = 0.000001;

    public static bool TryBuildDimension(
        DrawingDocument document,
        IEnumerable<string> selectionKeys,
        string id,
        out SketchDimension dimension,
        out string status,
        string? activeSelectionKey = null,
        Point2? anchorOverride = null,
        bool radialDiameter = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectionKeys);

        var selections = NormalizeSelections(document, selectionKeys, activeSelectionKey);
        if (TryBuildOffsetDimension(document, selections, id, anchorOverride, out dimension))
        {
            status = "Placed offset distance dimension. Edit this distance to resize the linked offset.";
            return true;
        }
        if (selections.Length == 1 && selections[0].Line is { } line)
        {
            var value = Distance(line.Start, line.End);
            if (value <= GeometryTolerance)
            {
                return FailDimension(out dimension, out status, "Selected line has no usable length.");
            }

            var key = selections[0].Key;
            dimension = new SketchDimension(
                id,
                SketchDimensionKind.LinearDistance,
                new[] { $"{key}:start", $"{key}:end" },
                value,
                anchorOverride ?? Midpoint(line.Start, line.End),
                isDriving: true);
            status = "Added line length dimension.";
            return true;
        }

        if (selections.Length == 1 && selections[0].CircleLike is { } circleLike)
        {
            var kind = circleLike.IsFullCircle || radialDiameter
                ? SketchDimensionKind.Diameter
                : SketchDimensionKind.Radius;
            var value = kind == SketchDimensionKind.Diameter
                ? circleLike.Radius * 2.0
                : circleLike.Radius;
            dimension = new SketchDimension(
                id,
                kind,
                new[] { GetCircleLikeEntityReferenceKey(selections[0].Key) },
                value,
                anchorOverride ?? new Point2(circleLike.Center.X + circleLike.Radius, circleLike.Center.Y),
                isDriving: true);
            status = kind == SketchDimensionKind.Diameter
                ? "Added diameter dimension."
                : "Added radius dimension.";
            return true;
        }

        if (selections.Length == 2)
        {
            var pointSelections = selections.Where(selection => selection.Point.HasValue).ToArray();
            var lineSelections = selections.Where(selection => selection.Line is not null).ToArray();

            if (pointSelections.Length == 2)
            {
                var firstPoint = pointSelections[0].Point!.Value;
                var secondPoint = pointSelections[1].Point!.Value;
                dimension = new SketchDimension(
                    id,
                    SketchDimensionKind.LinearDistance,
                    new[] { pointSelections[0].Key, pointSelections[1].Key },
                    Distance(firstPoint, secondPoint),
                    anchorOverride ?? Midpoint(firstPoint, secondPoint),
                    isDriving: true);
                status = "Added point-to-point distance dimension.";
                return true;
            }

            if (pointSelections.Length == 1 && lineSelections.Length == 1)
            {
                var point = pointSelections[0].Point!.Value;
                var lineSelection = lineSelections[0];
                var projection = ProjectPointToLine(point, lineSelection.Line!);
                dimension = new SketchDimension(
                    id,
                    SketchDimensionKind.PointToLineDistance,
                    new[] { lineSelection.Key, pointSelections[0].Key },
                    Distance(point, projection),
                    anchorOverride ?? Midpoint(point, projection),
                    isDriving: true);
                status = "Added point-to-line distance dimension.";
                return true;
            }

            if (lineSelections.Length == 2)
            {
                var firstLine = lineSelections[0].Line!;
                var secondLine = lineSelections[1].Line!;
                if (AreLinesParallel(firstLine, secondLine))
                {
                    var secondMidpoint = Midpoint(secondLine.Start, secondLine.End);
                    var projection = ProjectPointToLine(secondMidpoint, firstLine);
                    dimension = new SketchDimension(
                        id,
                        SketchDimensionKind.PointToLineDistance,
                        new[] { lineSelections[0].Key, lineSelections[1].Key },
                        Distance(secondMidpoint, projection),
                        anchorOverride ?? Midpoint(secondMidpoint, projection),
                        isDriving: true);
                    status = "Added parallel line distance dimension.";
                    return true;
                }

                var dimensionAnchor = anchorOverride ?? Midpoint(
                    Midpoint(firstLine.Start, firstLine.End),
                    Midpoint(secondLine.Start, secondLine.End));
                dimension = new SketchDimension(
                    id,
                    SketchDimensionKind.Angle,
                    new[] { lineSelections[0].Key, lineSelections[1].Key },
                    AngleBetweenLines(firstLine, secondLine, anchorOverride),
                    dimensionAnchor,
                    isDriving: true);
                status = "Added line angle dimension.";
                return true;
            }
        }

        return FailDimension(
            out dimension,
            out status,
            "Select one line, one circle or arc, two points, a point and line, or two lines before dimensioning.");
    }

    private static bool TryBuildOffsetDimension(DrawingDocument document, SketchSelection[] selections,
        string id, Point2? anchor, out SketchDimension dimension)
    {
        dimension = default!;
        if (selections.Length != 2) return false;
        string EntityKey(string key) => SketchReference.TryParse(key, out var reference)
            ? reference.EntityId : GetCircleLikeEntityReferenceKey(key);
        var firstId = EntityKey(selections[0].Key);
        var secondId = EntityKey(selections[1].Key);
        if (firstId == secondId) return false;

        var matches = document.Constraints.Where(c => c.Kind == SketchConstraintKind.Offset
            && c.State != SketchConstraintState.Suppressed && c.ReferenceKeys.Count >= 2
            && (c.ReferenceKeys[^1] == firstId && c.ReferenceKeys.SkipLast(1).Contains(secondId)
                || c.ReferenceKeys[^1] == secondId && c.ReferenceKeys.SkipLast(1).Contains(firstId)))
            .ToArray();
        if (matches.Length != 1) return false;
        var keys = matches[0].ReferenceKeys;
        if (!SketchOffsetRelation.IsSatisfied(document.Entities, keys)
            || !SketchOffsetRelation.TryMeasure(document.Entities, keys, out var gap, out var start, out var end))
            return false;

        // Only a normal gap between matching edges denotes the offset parameter.
        // Opposite sides, corner diagonals and a single edge still mean ordinary dimensions.
        double? pickedGap = null;
        if (selections[0].Line is { } firstLine && selections[1].Line is { } secondLine
            && AreLinesParallel(firstLine, secondLine))
            pickedGap = Distance(secondLine.Start, ProjectPointToLine(secondLine.Start, firstLine));
        else if (selections[0].Line is { } lineA && selections[1].Point is { } pointB)
            pickedGap = Distance(pointB, ProjectPointToLine(pointB, lineA));
        else if (selections[1].Line is { } lineB && selections[0].Point is { } pointA)
            pickedGap = Distance(pointA, ProjectPointToLine(pointA, lineB));
        else if (selections.All(s => s.CircleLike.HasValue && !s.Point.HasValue))
            pickedGap = Math.Abs(selections[0].CircleLike!.Value.Radius - selections[1].CircleLike!.Value.Radius);
        if (!pickedGap.HasValue || Math.Abs(pickedGap.Value - gap) > GeometryTolerance) return false;

        var existing = document.Dimensions.Where(d => d.Kind == SketchDimensionKind.Offset
            && d.ReferenceKeys.SequenceEqual(keys)).ToArray();
        if (existing.Length > 1) return false;
        var previous = existing.SingleOrDefault();
        dimension = new SketchDimension(previous?.Id ?? id, SketchDimensionKind.Offset, keys,
            previous?.Value ?? gap, anchor ?? previous?.Anchor ?? Midpoint(start, end), previous?.IsDriving ?? true);
        return true;
    }

    public static bool TryBuildConstraint(
        DrawingDocument document,
        IEnumerable<string> selectionKeys,
        SketchConstraintKind kind,
        string id,
        out SketchConstraint constraint,
        out string status,
        string? activeSelectionKey = null)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(selectionKeys);

        var axisConstraint = kind is SketchConstraintKind.Horizontal or SketchConstraintKind.Vertical;
        var picks = kind == SketchConstraintKind.Concentric
            ? selectionKeys.Select(key => NormalizeConcentricPick(document, key))
            : kind == SketchConstraintKind.Coincident ? selectionKeys.Select(key => NormalizeCircleIncidencePick(document, key))
            : axisConstraint ? selectionKeys.Select(key => NormalizeAxisPick(document, key)) : selectionKeys;
        var selections = NormalizeSelections(document, picks,
            activeSelectionKey is null ? null : axisConstraint ? NormalizeAxisPick(document, activeSelectionKey)
            : kind == SketchConstraintKind.Coincident ? NormalizeCircleIncidencePick(document, activeSelectionKey) : activeSelectionKey);
        var pointSelections = selections.Where(selection => selection.Point.HasValue).ToArray();
        var lineSelections = selections.Where(selection => selection.Line is not null).ToArray();
        var circleLikeSelections = selections.Where(selection => selection.CircleLike is not null).ToArray();
        var circleSelections = selections.Where(selection => !selection.Point.HasValue
            && TryFindEntity(document, selection.Key, out var entity) && entity is CircleEntity).ToArray();

        if (kind == SketchConstraintKind.Coincident && selections.Length == 2)
        {
            if (pointSelections.Length == 1 && lineSelections.Length == 1) kind = SketchConstraintKind.PointOnLine;
            else if (pointSelections.Length == 1 && circleSelections.Length == 1) kind = SketchConstraintKind.PointOnCircle;
            else if (pointSelections.Length == 0 && lineSelections.Length == 2) kind = SketchConstraintKind.Collinear;
        }

        string[]? references = kind switch
        {
            SketchConstraintKind.Coincident when pointSelections.Length == 2 =>
                Keys(pointSelections),
            SketchConstraintKind.PointOnLine when pointSelections.Length == 1 && lineSelections.Length == 1 =>
                Keys(selections),
            SketchConstraintKind.PointOnCircle when selections.Length == 2 && pointSelections.Length == 1
                && circleSelections.Length == 1
                && SketchReference.TryParse(pointSelections[0].Key, out var pointReference)
                && pointReference.EntityId != circleSelections[0].Key =>
                new[] { pointReference.ToString(), circleSelections[0].Key },
            SketchConstraintKind.Collinear when lineSelections.Length == 2 =>
                Keys(lineSelections),
            SketchConstraintKind.Horizontal when lineSelections.Length == 1 && selections.Length == 1 =>
                Keys(lineSelections),
            SketchConstraintKind.Horizontal when pointSelections.Length == 2 =>
                Keys(pointSelections),
            SketchConstraintKind.Vertical when lineSelections.Length == 1 && selections.Length == 1 =>
                Keys(lineSelections),
            SketchConstraintKind.Vertical when pointSelections.Length == 2 =>
                Keys(pointSelections),
            SketchConstraintKind.Parallel when lineSelections.Length == 2 =>
                Keys(lineSelections),
            SketchConstraintKind.Perpendicular when lineSelections.Length == 2 =>
                Keys(lineSelections),
            SketchConstraintKind.Tangent when selections.Length == 2
                && circleLikeSelections.Length >= 1
                && (circleLikeSelections.Length == 2 || lineSelections.Length == 1) =>
                selections.Select(selection => selection.CircleLike is not null
                    ? GetCircleLikeEntityReferenceKey(selection.Key) : selection.Key).ToArray(),
            SketchConstraintKind.Concentric when selections.Length == 2 && circleLikeSelections.Length == 1
                && selections.Any(selection => selection.Key == SketchReference.OriginKey) =>
                Keys(selections),
            SketchConstraintKind.Concentric when circleLikeSelections.Length == 2 =>
                Keys(circleLikeSelections),
            SketchConstraintKind.Equal when lineSelections.Length == 2 =>
                Keys(lineSelections),
            SketchConstraintKind.Equal when circleLikeSelections.Length == 2 =>
                circleLikeSelections.Select(selection => GetCircleLikeEntityReferenceKey(selection.Key)).ToArray(),
            SketchConstraintKind.Midpoint when lineSelections.Length == 1 && pointSelections.Length == 1 =>
                new[] { lineSelections[0].Key, pointSelections[0].Key },
            SketchConstraintKind.Fix when selections.Length > 0 =>
                Keys(selections),
            _ => null
        };

        if (references is null)
        {
            constraint = default!;
            status = $"Selection cannot define a {kind} constraint.";
            return false;
        }

        constraint = new SketchConstraint(id, kind, references);
        status = $"Added {kind} constraint.";
        return true;
    }

    private static SketchSelection[] NormalizeSelections(
        DrawingDocument document,
        IEnumerable<string> selectionKeys,
        string? activeSelectionKey)
    {
        var activeNormalized = NormalizeSelectionKey(activeSelectionKey);
        var orderedKeys = selectionKeys
            .Select(NormalizeSelectionKey)
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => StringComparer.Ordinal.Equals(key, activeNormalized) ? 1 : 0)
            .ToArray();
        var selections = new List<SketchSelection>();

        foreach (var key in orderedKeys)
        {
            if (key == SketchReference.OriginKey)
            {
                selections.Add(new SketchSelection(key, new Point2(0, 0), new Point2(0, 0), null, null, null));
                continue;
            }
            if (TryCreateCanvasPointSelection(document, key, out var canvasPointSelection))
            {
                selections.Add(canvasPointSelection);
                continue;
            }

            if (!SketchReference.TryParse(key, out var reference)
                || !TryFindEntity(document, reference.EntityId, out var entity))
            {
                continue;
            }

            var normalizedKey = reference.ToString();
            SketchReferenceResolver.TryGetPoint(document, normalizedKey, out var point);
            SketchReferenceResolver.TryGetLine(document, normalizedKey, out var line);
            selections.Add(new SketchSelection(
                normalizedKey,
                point,
                SketchReferenceResolver.TryGetPoint(document, normalizedKey, out _) ? point : null,
                line,
                line,
                TryGetCircleLike(reference, entity, out var circleLike) ? circleLike : null));
        }

        return selections.ToArray();
    }

    private static bool TryCreateCanvasPointSelection(
        DrawingDocument document,
        string key,
        out SketchSelection selection)
    {
        if (!SketchReference.TryParseCanvasPointCoordinates(key, out var entityId, out var label, out var point)
            || !TryFindEntity(document, entityId, out var entity))
        {
            selection = default!;
            return false;
        }

        CircleLikeSelection? circleLike = null;
        if (IsCurvePerimeterCanvasPoint(label)
            && TryGetCircleLike(
                new SketchReference(entityId, SketchReferenceTarget.Entity),
                entity,
                out var curveSelection))
        {
            circleLike = curveSelection;
        }

        selection = new SketchSelection(
            key,
            point,
            point,
            null,
            null,
            circleLike);
        return true;
    }

    private static bool IsCurvePerimeterCanvasPoint(string label)
    {
        var normalized = label.Split('|', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return !StringComparer.OrdinalIgnoreCase.Equals(normalized, "center");
    }

    private static string GetCircleLikeEntityReferenceKey(string key)
    {
        var pointSeparatorIndex = key.IndexOf("|point|", StringComparison.Ordinal);
        return pointSeparatorIndex > 0 ? key[..pointSeparatorIndex] : key;
    }

    private static string NormalizeConcentricPick(DrawingDocument document, string key)
    {
        var entityKey = GetCircleLikeEntityReferenceKey(key);
        if (SketchReference.TryParse(entityKey, out var reference)
            && TryFindEntity(document, reference.EntityId, out var entity)
            && entity is CircleEntity or ArcEntity)
            return reference.EntityId;
        return key;
    }

    private static string NormalizeAxisPick(DrawingDocument document, string key)
    {
        if (SketchReference.TryParse(key, out var reference) && !reference.IsEntity)
            return reference.ToString();
        if (!SketchReference.TryParseCanvasPointCoordinates(key, out var entityId, out var label, out _)
            || !TryFindEntity(document, entityId, out var entity)) return key;

        SketchReference? midpoint = null;
        if (entity is LineEntity && label == "mid")
            midpoint = new SketchReference(entityId, SketchReferenceTarget.Midpoint);
        else if (entity is PolylineEntity && label.StartsWith("mid-", StringComparison.Ordinal)
            && int.TryParse(label.AsSpan(4), System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var segment) && segment >= 0)
            midpoint = new SketchReference(entityId, SketchReferenceTarget.Midpoint, segment);

        return midpoint.HasValue && SketchReferenceResolver.TryGetPoint(document, midpoint.Value.ToString(), out _)
            ? midpoint.Value.ToString() : key;
    }

    private static string NormalizeCircleIncidencePick(DrawingDocument document, string key)
    {
        if (SketchReference.TryParseCanvasPointCoordinates(key, out var entityId, out var label, out _)
            && !StringComparer.OrdinalIgnoreCase.Equals(label, "center")
            && TryFindEntity(document, entityId, out var entity) && entity is CircleEntity)
            return entityId;
        return key;
    }

    private static string NormalizeSelectionKey(string? selectionKey)
    {
        if (string.IsNullOrWhiteSpace(selectionKey))
        {
            return string.Empty;
        }

        if (selectionKey.Contains("|point|", StringComparison.Ordinal))
        {
            return selectionKey.Trim();
        }

        return SketchReference.TryNormalize(selectionKey, out var normalized)
            ? normalized
            : selectionKey.Trim();
    }

    private static bool TryFindEntity(DrawingDocument document, string entityId, out DrawingEntity entity)
    {
        foreach (var candidate in document.Entities)
        {
            if (StringComparer.Ordinal.Equals(candidate.Id.Value, entityId))
            {
                entity = candidate;
                return true;
            }
        }

        entity = default!;
        return false;
    }

    private static bool TryGetCircleLike(
        SketchReference reference,
        DrawingEntity entity,
        out CircleLikeSelection circleLike)
    {
        if (reference.Target != SketchReferenceTarget.Entity)
        {
            circleLike = default;
            return false;
        }

        if (reference.SegmentIndex.HasValue)
        {
            circleLike = default;
            return false;
        }

        switch (entity)
        {
            case CircleEntity circle:
                circleLike = new CircleLikeSelection(circle.Center, circle.Radius, IsFullCircle: true);
                return true;
            case ArcEntity arc:
                circleLike = new CircleLikeSelection(arc.Center, arc.Radius, IsFullCircle: false);
                return true;
            default:
                circleLike = default;
                return false;
        }
    }

    private static bool FailDimension(out SketchDimension dimension, out string status, string message)
    {
        dimension = default!;
        status = message;
        return false;
    }

    private static string[] Keys(IEnumerable<SketchSelection> selections) =>
        selections.Select(selection => selection.Key).ToArray();

    private static Point2 Midpoint(Point2 first, Point2 second) =>
        new((first.X + second.X) / 2.0, (first.Y + second.Y) / 2.0);

    private static double Distance(Point2 first, Point2 second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    private static Point2 ProjectPointToLine(Point2 point, LineEntity line)
    {
        var deltaX = line.End.X - line.Start.X;
        var deltaY = line.End.Y - line.Start.Y;
        var lengthSquared = deltaX * deltaX + deltaY * deltaY;
        if (lengthSquared <= GeometryTolerance)
        {
            return line.Start;
        }

        var scalar = (((point.X - line.Start.X) * deltaX) + ((point.Y - line.Start.Y) * deltaY)) / lengthSquared;
        return new Point2(line.Start.X + deltaX * scalar, line.Start.Y + deltaY * scalar);
    }

    private static double AngleBetweenLines(LineEntity first, LineEntity second, Point2? anchor)
    {
        if (anchor.HasValue
            && TryGetLineIntersection(first, second, out var vertex)
            && TryGetAnchorSelectedAngleBetweenLines(first, second, vertex, anchor.Value, out var selectedAngle))
        {
            return selectedAngle;
        }

        return AcuteAngleBetweenLines(first, second);
    }

    private static double AcuteAngleBetweenLines(LineEntity first, LineEntity second)
    {
        var firstAngle = Math.Atan2(first.End.Y - first.Start.Y, first.End.X - first.Start.X);
        var secondAngle = Math.Atan2(second.End.Y - second.Start.Y, second.End.X - second.Start.X);
        var delta = Math.Abs((secondAngle - firstAngle) * 180.0 / Math.PI) % 180.0;
        return delta > 90.0 ? 180.0 - delta : delta;
    }

    private static bool TryGetAnchorSelectedAngleBetweenLines(
        LineEntity first,
        LineEntity second,
        Point2 vertex,
        Point2 anchor,
        out double angle)
    {
        var anchorVectorX = anchor.X - vertex.X;
        var anchorVectorY = anchor.Y - vertex.Y;
        if (Math.Sqrt(anchorVectorX * anchorVectorX + anchorVectorY * anchorVectorY) <= GeometryTolerance)
        {
            angle = default;
            return false;
        }

        var anchorAngle = GetPositiveAngleDegrees(Math.Atan2(anchorVectorY, anchorVectorX) * 180.0 / Math.PI);
        var firstAngle = GetLineAngleDegrees(first);
        var secondAngle = GetLineAngleDegrees(second);
        AngleSweep? best = null;

        foreach (var candidateFirstAngle in new[] { firstAngle, firstAngle + 180.0 })
        {
            foreach (var candidateSecondAngle in new[] { secondAngle, secondAngle + 180.0 })
            {
                var sweep = GetShortestAngleSweep(candidateFirstAngle, candidateSecondAngle);
                var containsAnchor = IsAngleWithinSweep(anchorAngle, sweep);
                var score = (containsAnchor ? 0.0 : 1000.0)
                    + AngleDistanceDegrees(anchorAngle, GetSweepMidAngle(sweep));
                if (!best.HasValue || score < best.Value.Score)
                {
                    best = sweep with { Score = score };
                }
            }
        }

        if (!best.HasValue)
        {
            angle = default;
            return false;
        }

        angle = best.Value.Measure;
        return true;
    }

    private static bool TryGetLineIntersection(LineEntity first, LineEntity second, out Point2 point)
    {
        var firstDeltaX = first.End.X - first.Start.X;
        var firstDeltaY = first.End.Y - first.Start.Y;
        var secondDeltaX = second.End.X - second.Start.X;
        var secondDeltaY = second.End.Y - second.Start.Y;
        var denominator = firstDeltaX * secondDeltaY - firstDeltaY * secondDeltaX;
        if (Math.Abs(denominator) <= GeometryTolerance)
        {
            point = default;
            return false;
        }

        var offsetX = second.Start.X - first.Start.X;
        var offsetY = second.Start.Y - first.Start.Y;
        var scalar = (offsetX * secondDeltaY - offsetY * secondDeltaX) / denominator;
        point = new Point2(first.Start.X + scalar * firstDeltaX, first.Start.Y + scalar * firstDeltaY);
        return true;
    }

    private static AngleSweep GetShortestAngleSweep(double startAngle, double endAngle)
    {
        var start = GetPositiveAngleDegrees(startAngle);
        var end = GetPositiveAngleDegrees(endAngle);
        var delta = GetPositiveAngleDegrees(end - start);
        return delta <= 180.0
            ? new AngleSweep(start, start + delta, delta, 0)
            : new AngleSweep(end, end + (360.0 - delta), 360.0 - delta, 0);
    }

    private static bool IsAngleWithinSweep(double angle, AngleSweep sweep)
    {
        var delta = GetPositiveAngleDegrees(angle - sweep.Start);
        return delta <= sweep.Measure + GeometryTolerance;
    }

    private static double GetSweepMidAngle(AngleSweep sweep) =>
        sweep.Start + sweep.Measure / 2.0;

    private static double AngleDistanceDegrees(double first, double second)
    {
        var delta = Math.Abs(GetPositiveAngleDegrees(first - second));
        return Math.Min(delta, 360.0 - delta);
    }

    private static double GetLineAngleDegrees(LineEntity line) =>
        Math.Atan2(line.End.Y - line.Start.Y, line.End.X - line.Start.X) * 180.0 / Math.PI;

    private static double GetPositiveAngleDegrees(double angle)
    {
        var normalized = angle % 360.0;
        return normalized < 0 ? normalized + 360.0 : normalized;
    }

    private static bool AreLinesParallel(LineEntity first, LineEntity second)
    {
        var firstDeltaX = first.End.X - first.Start.X;
        var firstDeltaY = first.End.Y - first.Start.Y;
        var secondDeltaX = second.End.X - second.Start.X;
        var secondDeltaY = second.End.Y - second.Start.Y;
        var firstLength = Math.Sqrt(firstDeltaX * firstDeltaX + firstDeltaY * firstDeltaY);
        var secondLength = Math.Sqrt(secondDeltaX * secondDeltaX + secondDeltaY * secondDeltaY);
        if (firstLength <= GeometryTolerance || secondLength <= GeometryTolerance)
        {
            return false;
        }

        var cross = firstDeltaX * secondDeltaY - firstDeltaY * secondDeltaX;
        return Math.Abs(cross) <= GeometryTolerance * firstLength * secondLength;
    }

    private sealed record SketchSelection(
        string Key,
        Point2 RawPoint,
        Point2? Point,
        LineEntity? RawLine,
        LineEntity? Line,
        CircleLikeSelection? CircleLike);

    private readonly record struct CircleLikeSelection(Point2 Center, double Radius, bool IsFullCircle);

    private readonly record struct AngleSweep(double Start, double End, double Measure, double Score);
}

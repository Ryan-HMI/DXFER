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
                new[] { GetCircleLikeDimensionReferenceKey(selections[0].Key) },
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
                dimension = new SketchDimension(
                    id,
                    SketchDimensionKind.Angle,
                    new[] { lineSelections[0].Key, lineSelections[1].Key },
                    AngleBetweenLines(lineSelections[0].Line!, lineSelections[1].Line!),
                    anchorOverride ?? Midpoint(
                        Midpoint(lineSelections[0].Line!.Start, lineSelections[0].Line!.End),
                        Midpoint(lineSelections[1].Line!.Start, lineSelections[1].Line!.End)),
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

        var selections = NormalizeSelections(document, selectionKeys, activeSelectionKey);
        var pointSelections = selections.Where(selection => selection.Point.HasValue).ToArray();
        var lineSelections = selections.Where(selection => selection.Line is not null).ToArray();
        var circleLikeSelections = selections.Where(selection => selection.CircleLike is not null).ToArray();

        string[]? references = kind switch
        {
            SketchConstraintKind.Coincident when pointSelections.Length == 2 =>
                Keys(pointSelections),
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
                Keys(selections),
            SketchConstraintKind.Concentric when circleLikeSelections.Length == 2 =>
                Keys(circleLikeSelections),
            SketchConstraintKind.Equal when lineSelections.Length == 2 =>
                Keys(lineSelections),
            SketchConstraintKind.Equal when circleLikeSelections.Length == 2 =>
                Keys(circleLikeSelections),
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

    private static string GetCircleLikeDimensionReferenceKey(string key)
    {
        var pointSeparatorIndex = key.IndexOf("|point|", StringComparison.Ordinal);
        return pointSeparatorIndex > 0 ? key[..pointSeparatorIndex] : key;
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

    private static double AngleBetweenLines(LineEntity first, LineEntity second)
    {
        var firstAngle = Math.Atan2(first.End.Y - first.Start.Y, first.End.X - first.Start.X);
        var secondAngle = Math.Atan2(second.End.Y - second.Start.Y, second.End.X - second.Start.X);
        var delta = Math.Abs((secondAngle - firstAngle) * 180.0 / Math.PI) % 180.0;
        return delta > 90.0 ? 180.0 - delta : delta;
    }

    private sealed record SketchSelection(
        string Key,
        Point2 RawPoint,
        Point2? Point,
        LineEntity? RawLine,
        LineEntity? Line,
        CircleLikeSelection? CircleLike);

    private readonly record struct CircleLikeSelection(Point2 Center, double Radius, bool IsFullCircle);
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Blazor.Sketching;

public static class SketchCreationDimensionFactory
{
    private const double GeometryTolerance = 0.000001;

    public static IReadOnlyList<SketchDimension> CreateDimensionsForTool(
        string toolName,
        IReadOnlyList<DrawingEntity> createdEntities,
        IReadOnlyDictionary<string, double> dimensionValues,
        Func<string> createDimensionId)
    {
        ArgumentNullException.ThrowIfNull(createdEntities);
        ArgumentNullException.ThrowIfNull(dimensionValues);
        ArgumentNullException.ThrowIfNull(createDimensionId);

        if (createdEntities.Count == 0)
        {
            return Array.Empty<SketchDimension>();
        }

        var dimensions = new List<SketchDimension>();
        var normalizedTool = NormalizeToolName(toolName);
        var lines = createdEntities.OfType<LineEntity>().ToArray();

        switch (normalizedTool)
        {
            case "line":
            case "midpointline":
                if (TryGetPositiveValue(dimensionValues, "length", out var lineLength)
                    && lines.FirstOrDefault() is { } line)
                {
                    dimensions.Add(CreateLineDimension(createDimensionId(), line, lineLength));
                }

                break;
            case "twopointrectangle":
            case "centerrectangle":
                AddLineDimensionForValue(
                    dimensions,
                    dimensionValues,
                    "width",
                    FindAxisLine(lines, preferHorizontal: true),
                    createDimensionId);
                AddLineDimensionForValue(
                    dimensions,
                    dimensionValues,
                    "height",
                    FindAxisLine(lines, preferHorizontal: false),
                    createDimensionId);
                break;
            case "alignedrectangle":
                AddLineDimensionForValue(
                    dimensions,
                    dimensionValues,
                    "length",
                    lines.Length >= 1 ? lines[0] : null,
                    createDimensionId);
                AddLineDimensionForValue(
                    dimensions,
                    dimensionValues,
                    "depth",
                    lines.Length >= 2 ? lines[1] : null,
                    createDimensionId);
                break;
            case "centercircle":
                if (TryGetPositiveValue(dimensionValues, "radius", out var circleRadius)
                    && createdEntities.OfType<CircleEntity>().FirstOrDefault() is { } circle)
                {
                    dimensions.Add(CreateRadialDimension(createDimensionId(), circle, circleRadius));
                }

                break;
            case "ellipse":
            case "ellipticalarc":
                if (createdEntities.OfType<EllipseEntity>().FirstOrDefault() is { } ellipse)
                {
                    if (TryGetPositiveValue(dimensionValues, "major", out var major))
                    {
                        var majorStart = new Point2(
                            ellipse.Center.X - ellipse.MajorAxisEndPoint.X,
                            ellipse.Center.Y - ellipse.MajorAxisEndPoint.Y);
                        var majorEnd = new Point2(
                            ellipse.Center.X + ellipse.MajorAxisEndPoint.X,
                            ellipse.Center.Y + ellipse.MajorAxisEndPoint.Y);
                        dimensions.Add(CreatePointDistanceDimension(
                            createDimensionId(),
                            ellipse.Id,
                            "major",
                            majorStart,
                            majorEnd,
                            major));
                    }

                    if (TryGetPositiveValue(dimensionValues, "minor", out var minor))
                    {
                        var minorPoint = GetEllipseMinorPoint(ellipse);
                        var oppositeMinorPoint = new Point2(
                            ellipse.Center.X - (minorPoint.X - ellipse.Center.X),
                            ellipse.Center.Y - (minorPoint.Y - ellipse.Center.Y));
                        dimensions.Add(CreatePointDistanceDimension(
                            createDimensionId(),
                            ellipse.Id,
                            "minor",
                            oppositeMinorPoint,
                            minorPoint,
                            minor));
                    }
                }

                break;
            case "inscribedpolygon":
                if (createdEntities.OfType<PolygonEntity>().FirstOrDefault() is { } polygon)
                {
                    dimensions.Add(CreateCountDimension(createDimensionId(), polygon));
                    if (TryGetPositiveValue(dimensionValues, "radius", out var polygonRadius))
                    {
                        dimensions.Add(CreateRadialDimension(createDimensionId(), polygon, polygonRadius));
                    }
                }

                break;
            case "circumscribedpolygon":
                if (createdEntities.OfType<PolygonEntity>().FirstOrDefault() is { } circumscribedPolygon)
                {
                    dimensions.Add(CreateCountDimension(createDimensionId(), circumscribedPolygon));
                    if (TryGetPositiveValue(dimensionValues, "apothem", out var apothem))
                    {
                        dimensions.Add(CreateRadialDimension(createDimensionId(), circumscribedPolygon, apothem));
                    }
                }

                break;
            case "slot":
                if (TryGetPositiveValue(dimensionValues, "length", out var slotLength)
                    && createdEntities.OfType<ArcEntity>().Take(2).ToArray() is { Length: >= 2 } slotArcs)
                {
                    dimensions.Add(CreatePointDistanceDimension(
                        createDimensionId(),
                        slotArcs[0].Id,
                        "length",
                        slotArcs[0].Center,
                        slotArcs[1].Center,
                        slotLength));
                }

                if (TryGetPositiveValue(dimensionValues, "radius", out var slotRadius)
                    && createdEntities.OfType<ArcEntity>().FirstOrDefault() is { } slotArc)
                {
                    dimensions.Add(CreateRadialDimension(createDimensionId(), slotArc, slotRadius));
                }

                break;
            case "threepointarc":
            case "tangentarc":
            case "centerpointarc":
                if (createdEntities.OfType<ArcEntity>().FirstOrDefault() is { } arc)
                {
                    if (TryGetPositiveValue(dimensionValues, "radius", out var arcRadius))
                    {
                        dimensions.Add(CreateRadialDimension(createDimensionId(), arc, arcRadius));
                    }

                    if (normalizedTool == "centerpointarc"
                        && TryGetSweepValue(dimensionValues, out var sweep))
                    {
                        dimensions.Add(CreateArcSweepDimension(createDimensionId(), arc, sweep));
                    }
                }

                break;
        }

        return dimensions;
    }

    private static SketchDimension CreatePointDistanceDimension(
        string id,
        EntityId entityId,
        string label,
        Point2 first,
        Point2 second,
        double value) =>
        new(
            id,
            SketchDimensionKind.LinearDistance,
            new[] { CreateCanvasPointReference(entityId, $"{label}-start", first), CreateCanvasPointReference(entityId, $"{label}-end", second) },
            value,
            GetLineDimensionAnchor(new LineEntity(entityId, first, second)),
            isDriving: true);

    private static Point2 GetEllipseMinorPoint(EllipseEntity ellipse)
    {
        var majorLength = Distance(ellipse.Center, new Point2(
            ellipse.Center.X + ellipse.MajorAxisEndPoint.X,
            ellipse.Center.Y + ellipse.MajorAxisEndPoint.Y));
        if (majorLength <= GeometryTolerance)
        {
            return ellipse.Center;
        }

        var minorLength = majorLength * ellipse.MinorRadiusRatio;
        var minorUnit = new Point2(-ellipse.MajorAxisEndPoint.Y / majorLength, ellipse.MajorAxisEndPoint.X / majorLength);
        return new Point2(ellipse.Center.X + minorUnit.X * minorLength, ellipse.Center.Y + minorUnit.Y * minorLength);
    }

    private static string CreateCanvasPointReference(EntityId entityId, string label, Point2 point) =>
        $"{entityId.Value}|point|{label}|{FormatReferenceNumber(point.X)}|{FormatReferenceNumber(point.Y)}";

    private static string FormatReferenceNumber(double value) =>
        value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);

    private static void AddLineDimensionForValue(
        ICollection<SketchDimension> dimensions,
        IReadOnlyDictionary<string, double> dimensionValues,
        string valueKey,
        LineEntity? line,
        Func<string> createDimensionId)
    {
        if (line is null || !TryGetPositiveValue(dimensionValues, valueKey, out var value))
        {
            return;
        }

        dimensions.Add(CreateLineDimension(createDimensionId(), line, value));
    }

    private static SketchDimension CreateLineDimension(string id, LineEntity line, double value) =>
        new(
            id,
            SketchDimensionKind.LinearDistance,
            new[] { $"{line.Id.Value}:start", $"{line.Id.Value}:end" },
            value,
            GetLineDimensionAnchor(line),
            isDriving: true);

    private static SketchDimension CreateRadialDimension(string id, CircleEntity circle, double value) =>
        new(
            id,
            SketchDimensionKind.Radius,
            new[] { circle.Id.Value },
            value,
            new Point2(circle.Center.X + circle.Radius, circle.Center.Y),
            isDriving: true);

    private static SketchDimension CreateRadialDimension(string id, ArcEntity arc, double value) =>
        new(
            id,
            SketchDimensionKind.Radius,
            new[] { arc.Id.Value },
            value,
            new Point2(arc.Center.X + arc.Radius, arc.Center.Y),
            isDriving: true);

    private static SketchDimension CreateArcSweepDimension(string id, ArcEntity arc, double value) =>
        new(
            id,
            SketchDimensionKind.Angle,
            new[] { arc.Id.Value },
            value,
            GetArcSweepDimensionAnchor(arc),
            isDriving: true);

    private static SketchDimension CreateRadialDimension(string id, PolygonEntity polygon, double value) =>
        new(
            id,
            SketchDimensionKind.Radius,
            new[] { polygon.Id.Value },
            value,
            GetPolygonRadialDimensionAnchor(polygon),
            isDriving: true);

    private static SketchDimension CreateCountDimension(string id, PolygonEntity polygon) =>
        new(
            id,
            SketchDimensionKind.Count,
            new[] { polygon.Id.Value },
            polygon.NormalizedSideCount,
            new Point2(polygon.Center.X, polygon.Center.Y - Math.Max(polygon.Radius * 0.55, 0.5)),
            isDriving: true);

    private static Point2 GetPolygonRadialDimensionAnchor(PolygonEntity polygon)
    {
        var radiusPoint = polygon.GetRadiusPoint();
        var dx = radiusPoint.X - polygon.Center.X;
        var dy = radiusPoint.Y - polygon.Center.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= GeometryTolerance)
        {
            return new Point2(polygon.Center.X + Math.Max(polygon.Radius, 0.5), polygon.Center.Y);
        }

        var offset = Math.Max(length * 0.18, 0.5);
        return new Point2(
            polygon.Center.X + dx / length * (length + offset),
            polygon.Center.Y + dy / length * (length + offset));
    }

    private static LineEntity? FindAxisLine(IEnumerable<LineEntity> lines, bool preferHorizontal)
    {
        return lines
            .Select(line => new
            {
                Line = line,
                DeltaX = Math.Abs(line.End.X - line.Start.X),
                DeltaY = Math.Abs(line.End.Y - line.Start.Y),
                Length = Distance(line.Start, line.End)
            })
            .Where(candidate => candidate.Length > GeometryTolerance)
            .Where(candidate => preferHorizontal
                ? candidate.DeltaX >= candidate.DeltaY
                : candidate.DeltaY > candidate.DeltaX)
            .OrderByDescending(candidate => candidate.Length)
            .Select(candidate => candidate.Line)
            .FirstOrDefault();
    }

    private static Point2 GetLineDimensionAnchor(LineEntity line)
    {
        var midpoint = Midpoint(line.Start, line.End);
        var dx = line.End.X - line.Start.X;
        var dy = line.End.Y - line.Start.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= GeometryTolerance)
        {
            return midpoint;
        }

        var offset = Math.Max(length * 0.12, 0.25);
        return new Point2(midpoint.X - dy / length * offset, midpoint.Y + dx / length * offset);
    }

    private static Point2 GetArcSweepDimensionAnchor(ArcEntity arc)
    {
        var sweep = GetPositiveSweepDegrees(arc.StartAngleDegrees, arc.EndAngleDegrees);
        var midAngle = arc.StartAngleDegrees + sweep / 2.0;
        var radius = arc.Radius * 0.7;
        var radians = midAngle * Math.PI / 180.0;
        return new Point2(
            arc.Center.X + Math.Cos(radians) * radius,
            arc.Center.Y + Math.Sin(radians) * radius);
    }

    private static bool TryGetPositiveValue(
        IReadOnlyDictionary<string, double> dimensionValues,
        string key,
        out double value)
    {
        foreach (var dimensionValue in dimensionValues)
        {
            if (!StringComparer.OrdinalIgnoreCase.Equals(dimensionValue.Key, key))
            {
                continue;
            }

            value = dimensionValue.Value;
            return value > GeometryTolerance && double.IsFinite(value);
        }

        value = 0;
        return false;
    }

    private static bool TryGetSweepValue(
        IReadOnlyDictionary<string, double> dimensionValues,
        out double value)
    {
        if (!TryGetPositiveValue(dimensionValues, "sweep", out value))
        {
            return false;
        }

        return value < 360.0 - GeometryTolerance;
    }

    private static double Distance(Point2 first, Point2 second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static Point2 Midpoint(Point2 first, Point2 second) =>
        new((first.X + second.X) / 2.0, (first.Y + second.Y) / 2.0);

    private static double GetPositiveSweepDegrees(double startAngleDegrees, double endAngleDegrees)
    {
        var sweep = (endAngleDegrees - startAngleDegrees) % 360.0;
        if (sweep <= 0)
        {
            sweep += 360.0;
        }

        return sweep;
    }

    private static string NormalizeToolName(string toolName) =>
        toolName.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}

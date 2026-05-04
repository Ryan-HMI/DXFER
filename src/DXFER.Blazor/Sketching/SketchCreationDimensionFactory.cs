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

        if (createdEntities.Count == 0 || dimensionValues.Count == 0)
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
            case "threepointarc":
            case "tangentarc":
            case "centerpointarc":
                if (TryGetPositiveValue(dimensionValues, "radius", out var arcRadius)
                    && createdEntities.OfType<ArcEntity>().FirstOrDefault() is { } arc)
                {
                    dimensions.Add(CreateRadialDimension(createDimensionId(), arc, arcRadius));
                }

                break;
        }

        return dimensions;
    }

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

    private static double Distance(Point2 first, Point2 second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static Point2 Midpoint(Point2 first, Point2 second) =>
        new((first.X + second.X) / 2.0, (first.Y + second.Y) / 2.0);

    private static string NormalizeToolName(string toolName) =>
        toolName.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}

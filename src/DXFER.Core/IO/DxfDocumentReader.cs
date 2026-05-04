using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.IO;

public static class DxfDocumentReader
{
    public static DrawingDocument Read(string dxfText)
    {
        ArgumentNullException.ThrowIfNull(dxfText);

        var pairs = ReadPairs(dxfText).ToArray();
        var entities = new List<DrawingEntity>();
        var generatedId = 1;

        for (var index = 0; index < pairs.Length; index++)
        {
            if (pairs[index].Code != 0)
            {
                continue;
            }

            var entityType = pairs[index].Value.Trim().ToUpperInvariant();
            switch (entityType)
            {
                case "LINE":
                    if (TryReadEntityPairs(pairs, index + 1, out var linePairs, out var nextLineIndex)
                        && TryCreateLine(linePairs, CreateId(linePairs, "line", ref generatedId), out var line))
                    {
                        entities.Add(line);
                        index = nextLineIndex - 1;
                    }

                    break;

                case "CIRCLE":
                    if (TryReadEntityPairs(pairs, index + 1, out var circlePairs, out var nextCircleIndex)
                        && TryCreateCircle(circlePairs, CreateId(circlePairs, "circle", ref generatedId), out var circle))
                    {
                        entities.Add(circle);
                        index = nextCircleIndex - 1;
                    }

                    break;

                case "ARC":
                    if (TryReadEntityPairs(pairs, index + 1, out var arcPairs, out var nextArcIndex)
                        && TryCreateArc(arcPairs, CreateId(arcPairs, "arc", ref generatedId), out var arc))
                    {
                        entities.Add(arc);
                        index = nextArcIndex - 1;
                    }

                    break;

                case "LWPOLYLINE":
                    if (TryReadEntityPairs(pairs, index + 1, out var polylinePairs, out var nextPolylineIndex)
                        && TryCreateLightweightPolyline(polylinePairs, CreateId(polylinePairs, "polyline", ref generatedId), out var polyline))
                    {
                        entities.Add(polyline);
                        index = nextPolylineIndex - 1;
                    }

                    break;

                case "POLYLINE":
                    if (TryReadPolyline(pairs, index + 1, CreateId(Array.Empty<DxfPair>(), "polyline", ref generatedId), out var classicPolyline, out var nextIndex))
                    {
                        entities.Add(classicPolyline);
                        index = nextIndex - 1;
                    }

                    break;

                case "SPLINE":
                    if (TryReadEntityPairs(pairs, index + 1, out var splinePairs, out var nextSplineIndex)
                        && TryCreateSpline(splinePairs, CreateId(splinePairs, "spline", ref generatedId), out var spline))
                    {
                        entities.Add(spline);
                        index = nextSplineIndex - 1;
                    }

                    break;
            }
        }

        return new DrawingDocument(entities);
    }

    private static bool TryReadEntityPairs(
        IReadOnlyList<DxfPair> pairs,
        int startIndex,
        out IReadOnlyList<DxfPair> entityPairs,
        out int nextIndex)
    {
        var values = new List<DxfPair>();
        for (var index = startIndex; index < pairs.Count; index++)
        {
            if (pairs[index].Code == 0)
            {
                entityPairs = values;
                nextIndex = index;
                return true;
            }

            values.Add(pairs[index]);
        }

        entityPairs = values;
        nextIndex = pairs.Count;
        return values.Count > 0;
    }

    private static bool TryReadPolyline(
        IReadOnlyList<DxfPair> pairs,
        int startIndex,
        EntityId id,
        out PolylineEntity polyline,
        out int nextIndex)
    {
        var points = new List<Point2>();
        var currentVertex = new Dictionary<int, double>();

        for (var index = startIndex; index < pairs.Count; index++)
        {
            var pair = pairs[index];
            if (pair.Code == 0)
            {
                var marker = pair.Value.Trim().ToUpperInvariant();
                if (marker == "VERTEX")
                {
                    FlushVertex(currentVertex, points);
                    currentVertex.Clear();
                    continue;
                }

                if (marker == "SEQEND")
                {
                    FlushVertex(currentVertex, points);
                    nextIndex = index + 1;
                    return TryCreatePolyline(points, false, id, out polyline);
                }

                FlushVertex(currentVertex, points);
                nextIndex = index;
                return TryCreatePolyline(points, false, id, out polyline);
            }

            if ((pair.Code == 10 || pair.Code == 20) && TryReadDouble(pair.Value, out var value))
            {
                currentVertex[pair.Code] = value;
            }
        }

        FlushVertex(currentVertex, points);
        nextIndex = pairs.Count;
        return TryCreatePolyline(points, false, id, out polyline);
    }

    private static bool TryCreateLine(IReadOnlyList<DxfPair> pairs, EntityId id, out LineEntity line)
    {
        if (TryReadPoint(pairs, 10, 20, out var start) && TryReadPoint(pairs, 11, 21, out var end))
        {
            line = new LineEntity(id, start, end);
            return true;
        }

        line = default!;
        return false;
    }

    private static bool TryCreateCircle(IReadOnlyList<DxfPair> pairs, EntityId id, out CircleEntity circle)
    {
        if (TryReadPoint(pairs, 10, 20, out var center)
            && TryReadDouble(pairs, 40, out var radius)
            && radius > 0)
        {
            circle = new CircleEntity(id, center, radius);
            return true;
        }

        circle = default!;
        return false;
    }

    private static bool TryCreateArc(IReadOnlyList<DxfPair> pairs, EntityId id, out ArcEntity arc)
    {
        if (TryReadPoint(pairs, 10, 20, out var center)
            && TryReadDouble(pairs, 40, out var radius)
            && TryReadDouble(pairs, 50, out var startAngle)
            && TryReadDouble(pairs, 51, out var endAngle)
            && radius > 0)
        {
            arc = new ArcEntity(id, center, radius, startAngle, endAngle);
            return true;
        }

        arc = default!;
        return false;
    }

    private static bool TryCreateLightweightPolyline(IReadOnlyList<DxfPair> pairs, EntityId id, out PolylineEntity polyline)
    {
        var points = ReadPointSequence(pairs, 10, 20);
        var closed = TryReadDouble(pairs, 70, out var flags) && (((int)flags) & 1) == 1;
        return TryCreatePolyline(points, closed, id, out polyline);
    }

    private static bool TryCreateSpline(IReadOnlyList<DxfPair> pairs, EntityId id, out SplineEntity spline)
    {
        var controlPoints = ReadPointSequence(pairs, 10, 20);
        var knots = ReadDoubleSequence(pairs, 40);
        var weights = ReadDoubleSequence(pairs, 41);

        if (controlPoints.Count < 2
            || !TryReadDouble(pairs, 71, out var rawDegree))
        {
            spline = default!;
            return false;
        }

        var degree = (int)Math.Round(rawDegree);
        if (degree < 1 || controlPoints.Count < degree + 1)
        {
            spline = default!;
            return false;
        }

        spline = new SplineEntity(id, degree, controlPoints, knots, weights);
        return true;
    }

    private static bool TryCreatePolyline(IReadOnlyList<Point2> points, bool closed, EntityId id, out PolylineEntity polyline)
    {
        var vertices = points.ToList();
        if (closed && vertices.Count > 1 && vertices[0] != vertices[^1])
        {
            vertices.Add(vertices[0]);
        }

        if (vertices.Count >= 2)
        {
            polyline = new PolylineEntity(id, vertices);
            return true;
        }

        polyline = default!;
        return false;
    }

    private static IReadOnlyList<Point2> ReadPointSequence(IReadOnlyList<DxfPair> pairs, int xCode, int yCode)
    {
        var points = new List<Point2>();
        var currentX = default(double?);

        foreach (var pair in pairs)
        {
            if (pair.Code == xCode && TryReadDouble(pair.Value, out var x))
            {
                currentX = x;
                continue;
            }

            if (pair.Code == yCode && currentX.HasValue && TryReadDouble(pair.Value, out var y))
            {
                points.Add(new Point2(currentX.Value, y));
                currentX = null;
            }
        }

        return points;
    }

    private static IReadOnlyList<double> ReadDoubleSequence(IReadOnlyList<DxfPair> pairs, int code)
    {
        var values = new List<double>();
        foreach (var pair in pairs)
        {
            if (pair.Code == code && TryReadDouble(pair.Value, out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static void FlushVertex(Dictionary<int, double> currentVertex, ICollection<Point2> points)
    {
        if (currentVertex.TryGetValue(10, out var x) && currentVertex.TryGetValue(20, out var y))
        {
            points.Add(new Point2(x, y));
        }
    }

    private static bool TryReadPoint(IReadOnlyList<DxfPair> pairs, int xCode, int yCode, out Point2 point)
    {
        if (TryReadDouble(pairs, xCode, out var x) && TryReadDouble(pairs, yCode, out var y))
        {
            point = new Point2(x, y);
            return true;
        }

        point = default;
        return false;
    }

    private static bool TryReadDouble(IReadOnlyList<DxfPair> pairs, int code, out double value)
    {
        if (pairs.FirstOrDefault(item => item.Code == code) is { Value: { } rawValue })
        {
            return TryReadDouble(rawValue, out value);
        }

        value = default;
        return false;
    }

    private static bool TryReadDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    private static EntityId CreateId(IReadOnlyList<DxfPair> pairs, string prefix, ref int generatedId)
    {
        var handle = pairs.FirstOrDefault(pair => pair.Code == 5)?.Value;
        if (!string.IsNullOrWhiteSpace(handle))
        {
            return EntityId.Create($"{prefix}-{handle.Trim()}");
        }

        return EntityId.Create($"{prefix}-{generatedId++}");
    }

    private static IEnumerable<DxfPair> ReadPairs(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } rawCode)
        {
            var rawValue = reader.ReadLine();
            if (rawValue is null)
            {
                yield break;
            }

            if (int.TryParse(rawCode.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            {
                yield return new DxfPair(code, rawValue.Trim());
            }
        }
    }

    private sealed record DxfPair(int Code, string Value);
}

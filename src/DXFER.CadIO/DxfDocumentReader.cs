using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;

namespace DXFER.CadIO;

public static class DxfDocumentReader
{
    public static DrawingDocument Read(string dxfText)
    {
        ArgumentNullException.ThrowIfNull(dxfText);

        var cutWarnings = new List<DrawingDocumentWarning>();
        var pairs = ReadPairs(dxfText, cutWarnings).ToArray();
        var entities = new List<DrawingEntity>();
        var entityStyles = new Dictionary<string, DxfEntityStyle>(StringComparer.Ordinal);
        var layerStyles = ReadLayerStyles(pairs);
        var unsupportedEntityCounts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var generatedId = 1;
        var section = "ENTITIES";
        var units = ReadUnits(pairs);
        foreach (var marker in pairs.Where(pair => pair.Code == 999 && pair.Value.StartsWith("DXFER_CUT_LENGTH_REVIEW:", StringComparison.Ordinal)))
            AddCutWarning(cutWarnings, marker.Value[24..].Trim());

        for (var index = 0; index < pairs.Length; index++)
        {
            if (pairs[index].Code != 0)
            {
                continue;
            }

            var entityType = pairs[index].Value.Trim().ToUpperInvariant();
            if (entityType == "SECTION")
            {
                section = index + 1 < pairs.Length && pairs[index + 1].Code == 2 ? pairs[index + 1].Value : "";
                continue;
            }
            if (entityType == "ENDSEC") { section = ""; continue; }
            if (!section.Equals("ENTITIES", StringComparison.OrdinalIgnoreCase) || entityType == "EOF") continue;
            TryReadEntityPairs(pairs, index + 1, out var rawPairs, out _);
            var nonCut = CutPathLengthService.IsAnnotation(entityType)
                || CutPathLengthService.IsNonCutLayer(ReadString(rawPairs, 8));
            if (!nonCut) CheckCutGeometry(entityType, rawPairs, cutWarnings);
            var previousEntityCount = entities.Count;
            switch (entityType)
            {
                case "LINE":
                    if (TryReadEntityPairs(pairs, index + 1, out var linePairs, out var nextLineIndex)
                        && TryCreateLine(linePairs, CreateId(linePairs, "line", ref generatedId), out var line))
                    {
                        entities.Add(line);
                        AddEntityStyle(entityStyles, line.Id, linePairs, layerStyles);
                        index = nextLineIndex - 1;
                    }

                    break;

                case "CIRCLE":
                    if (TryReadEntityPairs(pairs, index + 1, out var circlePairs, out var nextCircleIndex)
                        && TryCreateCircle(circlePairs, CreateId(circlePairs, "circle", ref generatedId), out var circle))
                    {
                        entities.Add(circle);
                        AddEntityStyle(entityStyles, circle.Id, circlePairs, layerStyles);
                        index = nextCircleIndex - 1;
                    }

                    break;

                case "ARC":
                    if (TryReadEntityPairs(pairs, index + 1, out var arcPairs, out var nextArcIndex)
                        && TryCreateArc(arcPairs, CreateId(arcPairs, "arc", ref generatedId), out var arc))
                    {
                        entities.Add(arc);
                        AddEntityStyle(entityStyles, arc.Id, arcPairs, layerStyles);
                        index = nextArcIndex - 1;
                    }

                    break;

                case "ELLIPSE":
                    if (TryReadEntityPairs(pairs, index + 1, out var ellipsePairs, out var nextEllipseIndex)
                        && TryCreateEllipse(ellipsePairs, CreateId(ellipsePairs, "ellipse", ref generatedId), out var ellipse))
                    {
                        entities.Add(ellipse);
                        AddEntityStyle(entityStyles, ellipse.Id, ellipsePairs, layerStyles);
                        index = nextEllipseIndex - 1;
                    }

                    break;

                case "POINT":
                    if (TryReadEntityPairs(pairs, index + 1, out var pointPairs, out var nextPointIndex)
                        && TryCreatePoint(pointPairs, CreateId(pointPairs, "point", ref generatedId), out var point))
                    {
                        entities.Add(point);
                        AddEntityStyle(entityStyles, point.Id, pointPairs, layerStyles);
                        index = nextPointIndex - 1;
                    }

                    break;

                case "LWPOLYLINE":
                    if (TryReadEntityPairs(pairs, index + 1, out var polylinePairs, out var nextPolylineIndex)
                        && TryCreateLightweightPolyline(polylinePairs, CreateId(polylinePairs, "polyline", ref generatedId), out var polyline))
                    {
                        entities.Add(polyline);
                        AddEntityStyle(entityStyles, polyline.Id, polylinePairs, layerStyles);
                        index = nextPolylineIndex - 1;
                    }

                    break;

                case "POLYLINE":
                    if (TryReadPolyline(pairs, index + 1, CreateId(Array.Empty<DxfPair>(), "polyline", ref generatedId), out var classicPolyline, out var nextIndex))
                    {
                        entities.Add(classicPolyline);
                        AddEntityStyle(entityStyles, classicPolyline.Id, rawPairs, layerStyles);
                        if (!nonCut)
                            CheckCutGeometry("POLYLINE", pairs.Skip(index + 1).Take(nextIndex - index - 1).ToArray(), cutWarnings);
                        index = nextIndex - 1;
                    }

                    break;

                case "SPLINE":
                    if (TryReadEntityPairs(pairs, index + 1, out var splinePairs, out var nextSplineIndex)
                        && TryCreateSpline(splinePairs, CreateId(splinePairs, "spline", ref generatedId), out var spline))
                    {
                        entities.Add(spline);
                        AddEntityStyle(entityStyles, spline.Id, splinePairs, layerStyles);
                        index = nextSplineIndex - 1;
                    }

                    break;

                default:
                    if (ShouldCountUnsupportedEntity(entityType) && !nonCut)
                    {
                        unsupportedEntityCounts[entityType] =
                            unsupportedEntityCounts.GetValueOrDefault(entityType) + 1;
                        if (TryReadEntityPairs(pairs, index + 1, out _, out var nextUnsupportedIndex))
                        {
                            index = nextUnsupportedIndex - 1;
                        }
                    }

                    break;
            }
            if (!nonCut && entities.Count == previousEntityCount
                && entityType is "LINE" or "CIRCLE" or "ARC" or "ELLIPSE" or "LWPOLYLINE" or "POLYLINE" or "SPLINE")
                AddCutWarning(cutWarnings, $"Malformed {entityType} geometry was not imported.");
        }

        var metadata = DrawingDocumentMetadata.Empty with
        {
            Units = units,
            Warnings = CreateImportWarnings(unsupportedEntityCounts)
                .Where(warning => units == DrawingUnits.Unspecified || warning.Code != "missing-units")
                .Concat(cutWarnings).ToArray(),
            UnsupportedEntityCounts = unsupportedEntityCounts,
            EntityStyles = entityStyles
        };

        return new DrawingDocument(
            entities,
            Array.Empty<SketchDimension>(),
            Array.Empty<SketchConstraint>(),
            metadata);
    }

    private static DrawingUnits ReadUnits(IReadOnlyList<DxfPair> pairs)
    {
        var declarations = pairs.Select((pair, index) => (pair, index))
            .Where(item => item.pair.Code == 9 && item.pair.Value == "$INSUNITS").ToArray();
        if (declarations.Length != 1) return DrawingUnits.Unspecified;
        var index = declarations[0].index + 1;
        if (index >= pairs.Count || pairs[index].Code != 70) return DrawingUnits.Unspecified;
        return pairs[index].Value switch { "1" => DrawingUnits.Inches, "4" => DrawingUnits.Millimeters, _ => DrawingUnits.Unspecified };
    }

    private static void AddCutWarning(ICollection<DrawingDocumentWarning> warnings, string message) =>
        warnings.Add(new(CutPathLengthService.UnsafeImportCode, DrawingDocumentWarningSeverity.Warning, message));

    private static void CheckCutGeometry(string type, IReadOnlyList<DxfPair> pairs, ICollection<DrawingDocumentWarning> warnings)
    {
        foreach (var pair in pairs)
        {
            if (pair.Code is >= 10 and <= 59 or >= 210 and <= 239)
            {
                if (!TryReadDouble(pair.Value, out var value) || !double.IsFinite(value))
                { AddCutWarning(warnings, $"Invalid numeric data in {type}."); return; }
                if ((pair.Code is >= 30 and <= 39 && value != 0)
                    || (pair.Code is 210 or 220 && value != 0) || (pair.Code == 230 && value != 1))
                { AddCutWarning(warnings, $"Nonplanar or extruded {type} geometry requires review."); return; }
                if (type is "POLYLINE" or "LWPOLYLINE" && pair.Code is 40 or 41 or 42 or 43 && value != 0)
                { AddCutWarning(warnings, "Polyline bulges or widths are not preserved; cut length requires review."); return; }
            }
        }
        if (type == "POLYLINE" && TryReadInt(pairs, 70, out var flags) && (flags & ~1) != 0)
            AddCutWarning(warnings, "Fitted, mesh, or 3D polylines require review.");
        if (type == "LWPOLYLINE" && (pairs.Count(p => p.Code == 10) != pairs.Count(p => p.Code == 20)
            || (TryReadInt(pairs, 90, out var count) && count != pairs.Count(p => p.Code == 10))))
            AddCutWarning(warnings, "Incomplete polyline vertices require review.");
        if (type == "POLYLINE")
        {
            for (var index = 0; index < pairs.Count; index++)
            {
                if (pairs[index].Code != 0 || pairs[index].Value != "VERTEX") continue;
                TryReadEntityPairs(pairs, index + 1, out var vertex, out _);
                if (!TryReadPoint(vertex, 10, 20, out _)
                    || (TryReadInt(vertex, 70, out var vertexFlags) && vertexFlags != 0))
                    AddCutWarning(warnings, "Incomplete or unsupported polyline vertex requires review.");
            }
        }
        if (TryReadInt(pairs, 67, out var space) && space != 0)
            AddCutWarning(warnings, "Paper-space geometry requires review.");
    }

    private static IReadOnlyList<DrawingDocumentWarning> CreateImportWarnings(
        IReadOnlyDictionary<string, int> unsupportedEntityCounts)
    {
        var warnings = new List<DrawingDocumentWarning>
        {
            new(
                "missing-units",
                DrawingDocumentWarningSeverity.Warning,
                "DXF units were not specified; geometry coordinates were imported as document units.")
        };

        if (unsupportedEntityCounts.Count > 0)
        {
            var unsupportedSummary = string.Join(
                ", ",
                unsupportedEntityCounts.Select(pair => $"{pair.Key} ({pair.Value})"));
            warnings.Insert(
                0,
                new DrawingDocumentWarning(
                    "unsupported-entity",
                    DrawingDocumentWarningSeverity.Warning,
                    $"Skipped unsupported DXF entities: {unsupportedSummary}."));
        }

        return warnings;
    }

    private static bool ShouldCountUnsupportedEntity(string entityType) =>
        entityType switch
        {
            "SECTION" or "ENDSEC" or "EOF" or "HEADER" or "TABLES" or "TABLE" or "ENDTAB" or "BLOCKS"
                or "ENTITIES" or "OBJECTS" or "CLASSES" or "VERTEX" or "SEQEND" => false,
            _ => true
        };

    private static IReadOnlyDictionary<string, DxfEntityStyle> ReadLayerStyles(IReadOnlyList<DxfPair> pairs)
    {
        var styles = new Dictionary<string, DxfEntityStyle>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < pairs.Count; index++)
        {
            if (pairs[index].Code != 0 ||
                !pairs[index].Value.Trim().Equals("LAYER", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!TryReadEntityPairs(pairs, index + 1, out var layerPairs, out var nextIndex))
            {
                continue;
            }

            var layerName = ReadString(layerPairs, 2);
            if (string.IsNullOrWhiteSpace(layerName))
            {
                index = nextIndex - 1;
                continue;
            }

            styles[layerName] = new DxfEntityStyle(
                layerName,
                NormalizeLineTypeName(ReadString(layerPairs, 6), layerName),
                TryReadInt(layerPairs, 62, out var colorNumber) ? colorNumber : null);
            index = nextIndex - 1;
        }

        return styles;
    }

    private static void AddEntityStyle(
        IDictionary<string, DxfEntityStyle> entityStyles,
        EntityId entityId,
        IReadOnlyList<DxfPair> entityPairs,
        IReadOnlyDictionary<string, DxfEntityStyle> layerStyles)
    {
        var layerName = ReadString(entityPairs, 8);
        var lineTypeName = ReadString(entityPairs, 6);
        var colorNumber = TryReadInt(entityPairs, 62, out var entityColorNumber) ? entityColorNumber : (int?)null;

        DxfEntityStyle? layerStyle = null;
        if (!string.IsNullOrWhiteSpace(layerName))
        {
            layerStyles.TryGetValue(layerName, out layerStyle);
        }

        var resolvedLineType = ResolveLineTypeName(layerName, lineTypeName, layerStyle?.LineTypeName);
        var resolvedColorNumber = colorNumber ?? layerStyle?.ColorNumber;
        if (string.IsNullOrWhiteSpace(layerName) &&
            string.IsNullOrWhiteSpace(resolvedLineType) &&
            resolvedColorNumber is null)
        {
            return;
        }

        entityStyles[entityId.Value] = new DxfEntityStyle(
            string.IsNullOrWhiteSpace(layerName) ? null : layerName.Trim(),
            resolvedLineType,
            resolvedColorNumber);
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
        TryReadEntityPairs(pairs, startIndex, out var headerPairs, out var verticesStart);
        var closed = TryReadInt(headerPairs, 70, out var flags) && (flags & 1) != 0;

        for (var index = verticesStart; index < pairs.Count; index++)
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
                    return TryCreatePolyline(points, closed, id, out polyline);
                }

                FlushVertex(currentVertex, points);
                nextIndex = index;
                return TryCreatePolyline(points, closed, id, out polyline);
            }

            if ((pair.Code == 10 || pair.Code == 20) && TryReadDouble(pair.Value, out var value))
            {
                currentVertex[pair.Code] = value;
            }
        }

        FlushVertex(currentVertex, points);
        nextIndex = pairs.Count;
        return TryCreatePolyline(points, closed, id, out polyline);
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
            && Math.Abs(endAngle - startAngle) <= 360
            && radius > 0)
        {
            arc = new ArcEntity(id, center, radius, startAngle, endAngle);
            return true;
        }

        arc = default!;
        return false;
    }

    private static bool TryCreateEllipse(IReadOnlyList<DxfPair> pairs, EntityId id, out EllipseEntity ellipse)
    {
        if (TryReadPoint(pairs, 10, 20, out var center)
            && TryReadPoint(pairs, 11, 21, out var majorAxisEndPoint)
            && TryReadDouble(pairs, 40, out var minorRatio)
            && minorRatio > 0)
        {
            var startParameterDegrees = TryReadDouble(pairs, 41, out var startRadians)
                ? RadiansToDegrees(startRadians)
                : 0;
            var endParameterDegrees = TryReadDouble(pairs, 42, out var endRadians)
                ? RadiansToDegrees(endRadians)
                : 360;
            if (!double.IsFinite(startParameterDegrees) || !double.IsFinite(endParameterDegrees)
                || Math.Abs(endParameterDegrees - startParameterDegrees) > 360.000001)
            {
                ellipse = default!;
                return false;
            }
            ellipse = new EllipseEntity(id, center, majorAxisEndPoint, minorRatio, startParameterDegrees, endParameterDegrees);
            return true;
        }

        ellipse = default!;
        return false;
    }

    private static bool TryCreatePoint(IReadOnlyList<DxfPair> pairs, EntityId id, out PointEntity point)
    {
        if (TryReadPoint(pairs, 10, 20, out var location))
        {
            point = new PointEntity(id, location);
            return true;
        }

        point = default!;
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
        var fitPoints = ReadPointSequence(pairs, 11, 21);
        var knots = ReadDoubleSequence(pairs, 40);
        var weights = ReadDoubleSequence(pairs, 41);

        if (controlPoints.Count < 2)
        {
            if (fitPoints.Count >= 2)
            {
                spline = SplineEntity.FromFitPoints(id, fitPoints);
                return true;
            }

            spline = default!;
            return false;
        }

        if (!TryReadDouble(pairs, 71, out var rawDegree))
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

        spline = new SplineEntity(id, degree, controlPoints, knots, weights, fitPoints: fitPoints);
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

    private static string? ReadString(IReadOnlyList<DxfPair> pairs, int code)
    {
        var value = pairs.FirstOrDefault(item => item.Code == code)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool TryReadInt(IReadOnlyList<DxfPair> pairs, int code, out int value)
    {
        var rawValue = pairs.FirstOrDefault(item => item.Code == code)?.Value;
        return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static string? ResolveLineTypeName(string? layerName, string? entityLineTypeName, string? layerLineTypeName)
    {
        var trimmed = string.IsNullOrWhiteSpace(entityLineTypeName) ? null : entityLineTypeName.Trim();
        if (trimmed is not null &&
            !trimmed.Equals("BYLAYER", StringComparison.OrdinalIgnoreCase) &&
            !trimmed.Equals("BYBLOCK", StringComparison.OrdinalIgnoreCase))
        {
            return NormalizeLineTypeName(trimmed, layerName);
        }

        return NormalizeLineTypeName(layerLineTypeName, layerName);
    }

    private static string? NormalizeLineTypeName(string? lineTypeName, string? layerName)
    {
        var trimmed = string.IsNullOrWhiteSpace(lineTypeName) ? null : lineTypeName.Trim();
        if (!string.IsNullOrWhiteSpace(layerName) &&
            layerName.Contains("BEND", StringComparison.OrdinalIgnoreCase) &&
            (trimmed is null || trimmed.Equals("CONTINUOUS", StringComparison.OrdinalIgnoreCase)))
        {
            return "DASHED";
        }

        return trimmed;
    }

    private static bool TryReadDouble(string value, out double result) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out result) && double.IsFinite(result);

    private static EntityId CreateId(IReadOnlyList<DxfPair> pairs, string prefix, ref int generatedId)
    {
        var handle = pairs.FirstOrDefault(pair => pair.Code == 5)?.Value;
        if (!string.IsNullOrWhiteSpace(handle))
        {
            return EntityId.Create($"{prefix}-{handle.Trim()}");
        }

        return EntityId.Create($"{prefix}-{generatedId++}");
    }

    private static IEnumerable<DxfPair> ReadPairs(string text, ICollection<DrawingDocumentWarning> warnings)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } rawCode)
        {
            var rawValue = reader.ReadLine();
            if (rawValue is null)
            {
                AddCutWarning(warnings, "Truncated DXF group-code record requires review.");
                yield break;
            }

            if (int.TryParse(rawCode.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var code))
            {
                yield return new DxfPair(code, rawValue.Trim());
            }
            else
                AddCutWarning(warnings, "Invalid DXF group-code record requires review.");
        }
    }

    private sealed record DxfPair(int Code, string Value);

    private static double RadiansToDegrees(double radians) => radians * 180.0 / Math.PI;
}

using DXFER.Blazor.Components;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Blazor.Sketching;

public static class SketchCreationEntityFactory
{
    private const double GeometryTolerance = 0.000001;
    private const int DefaultPolygonSideCount = 6;

    public static IReadOnlyList<DrawingEntity> CreateEntitiesForTool(
        string toolName,
        IReadOnlyList<Point2> points,
        Func<string, EntityId> createEntityId,
        bool isConstruction,
        IReadOnlyDictionary<string, double>? dimensionValues = null)
    {
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(createEntityId);

        var entities = new List<DrawingEntity>();
        var normalizedTool = NormalizeToolName(toolName);
        if (normalizedTool == "point" && points.Count >= 1)
        {
            entities.Add(new PointEntity(createEntityId("point"), points[0], isConstruction));
            return entities;
        }

        if (points.Count < 2)
        {
            return entities;
        }

        var first = points[0];
        var second = points[1];
        switch (normalizedTool)
        {
            case "line":
                entities.Add(new LineEntity(
                    createEntityId("line"),
                    first,
                    GetPointAtLength(first, second, GetPositiveDimensionValue(dimensionValues, "length")),
                    isConstruction));
                break;
            case "midpointline":
                var midpointEndpoint = GetPointAtLength(
                    first,
                    second,
                    GetPositiveDimensionValue(dimensionValues, "length") is { } typedLength
                        ? typedLength / 2.0
                        : null);
                var mirroredEndpoint = new Point2((2 * first.X) - midpointEndpoint.X, (2 * first.Y) - midpointEndpoint.Y);
                entities.Add(new LineEntity(createEntityId("line"), mirroredEndpoint, midpointEndpoint, isConstruction));
                break;
            case "twopointrectangle":
                var dimensionedSecond = GetAxisRectangleOppositeCorner(first, second, dimensionValues);
                entities.AddRange(CreateRectangle(
                    first,
                    new Point2(dimensionedSecond.X, first.Y),
                    dimensionedSecond,
                    new Point2(first.X, dimensionedSecond.Y),
                    createEntityId,
                    isConstruction));
                break;
            case "centerrectangle":
                var dimensionedCorner = GetCenterRectangleCorner(first, second, dimensionValues);
                var centerCorners = SketchRectangleGeometry.GetCenterRectangleCorners(first, dimensionedCorner);
                entities.AddRange(CreateRectangle(centerCorners[0], centerCorners[1], centerCorners[2], centerCorners[3], createEntityId, isConstruction));
                break;
            case "alignedrectangle" when points.Count >= 3:
                var corners = GetAlignedRectangleCorners(first, second, points[2]);
                if (corners is not null)
                {
                    entities.AddRange(CreateRectangle(corners[0], corners[1], corners[2], corners[3], createEntityId, isConstruction));
                }

                break;
            case "centercircle":
                var radius = GetPositiveDimensionValue(dimensionValues, "radius") ?? Distance(first, second);
                if (radius > GeometryTolerance)
                {
                    entities.Add(new CircleEntity(createEntityId("circle"), first, radius, isConstruction));
                }

                break;
            case "threepointcircle" when points.Count >= 3:
                var circle = SketchArcGeometry.GetThreePointCircle(first, second, points[2]);
                if (circle is not null)
                {
                    entities.Add(new CircleEntity(createEntityId("circle"), circle.Value.Center, circle.Value.Radius, isConstruction));
                }

                break;
            case "ellipse" when points.Count >= 3:
                AddEllipseEntity(entities, createEntityId("ellipse"), first, second, points[2], 0, 360, isConstruction);
                break;
            case "threepointarc" when points.Count >= 3:
                AddArcEntity(entities, createEntityId, SketchArcGeometry.GetThreePointArc(first, second, points[2]), isConstruction);
                break;
            case "tangentarc" when points.Count >= 3:
                AddArcEntity(entities, createEntityId, SketchArcGeometry.GetTangentArc(first, second, points[2]), isConstruction);
                break;
            case "centerpointarc" when points.Count >= 3:
                AddArcEntity(entities, createEntityId, SketchArcGeometry.GetCenterPointArc(first, second, points[2]), isConstruction);
                break;
            case "ellipticalarc" when points.Count >= 4:
                var endParameter = GetEllipseParameterDegrees(first, second, points[2], points[3]);
                AddEllipseEntity(entities, createEntityId("ellipse"), first, second, points[2], 0, endParameter, isConstruction);
                break;
            case "conic" when points.Count >= 3:
                entities.Add(new SplineEntity(createEntityId("conic"), 2, points.Take(3), Array.Empty<double>(), isConstruction: isConstruction));
                break;
            case "inscribedpolygon":
                entities.Add(CreatePolygon(
                    first,
                    GetPointAtLength(first, second, GetPositiveDimensionValue(dimensionValues, "radius")),
                    false,
                    GetPolygonSideCount(dimensionValues),
                    createEntityId,
                    isConstruction));
                break;
            case "circumscribedpolygon":
                entities.Add(CreatePolygon(
                    first,
                    GetPointAtLength(first, second, GetPositiveDimensionValue(dimensionValues, "apothem")),
                    true,
                    GetPolygonSideCount(dimensionValues),
                    createEntityId,
                    isConstruction));
                break;
            case "spline" when points.Count >= 2:
            case "splinecontrolpoint" when points.Count >= 2:
                entities.Add(CreateSpline(createEntityId("spline"), points.ToArray(), isConstruction));
                break;
            case "bezier" when points.Count >= 4:
                entities.Add(new SplineEntity(
                    createEntityId("bezier"),
                    3,
                    points.Take(4),
                    new[] { 0d, 0d, 0d, 0d, 1d, 1d, 1d, 1d },
                    isConstruction: isConstruction));
                break;
            case "slot" when points.Count >= 3:
                entities.AddRange(CreateSlot(first, second, points[2], createEntityId, isConstruction));
                break;
        }

        return entities;
    }

    private static IEnumerable<DrawingEntity> CreateRectangle(
        Point2 first,
        Point2 second,
        Point2 third,
        Point2 fourth,
        Func<string, EntityId> createEntityId,
        bool isConstruction)
    {
        yield return new LineEntity(createEntityId("rect"), first, second, isConstruction);
        yield return new LineEntity(createEntityId("rect"), second, third, isConstruction);
        yield return new LineEntity(createEntityId("rect"), third, fourth, isConstruction);
        yield return new LineEntity(createEntityId("rect"), fourth, first, isConstruction);
    }

    private static void AddArcEntity(
        ICollection<DrawingEntity> entities,
        Func<string, EntityId> createEntityId,
        (Point2 Center, double Radius, double StartAngleDegrees, double EndAngleDegrees)? arc,
        bool isConstruction)
    {
        if (arc is null)
        {
            return;
        }

        entities.Add(new ArcEntity(
            createEntityId("arc"),
            arc.Value.Center,
            arc.Value.Radius,
            arc.Value.StartAngleDegrees,
            arc.Value.EndAngleDegrees,
            isConstruction));
    }

    private static void AddEllipseEntity(
        ICollection<DrawingEntity> entities,
        EntityId id,
        Point2 center,
        Point2 majorPoint,
        Point2 minorPoint,
        double startParameterDegrees,
        double endParameterDegrees,
        bool isConstruction)
    {
        if (!TryGetEllipseAxes(center, majorPoint, minorPoint, out var majorAxisEndPoint, out var minorRatio))
        {
            return;
        }

        entities.Add(new EllipseEntity(
            id,
            center,
            majorAxisEndPoint,
            minorRatio,
            startParameterDegrees,
            endParameterDegrees,
            isConstruction));
    }

    private static bool TryGetEllipseAxes(
        Point2 center,
        Point2 majorPoint,
        Point2 minorPoint,
        out Point2 majorAxisEndPoint,
        out double minorRatio)
    {
        majorAxisEndPoint = new Point2(majorPoint.X - center.X, majorPoint.Y - center.Y);
        var majorLength = Distance(center, majorPoint);
        if (majorLength <= GeometryTolerance)
        {
            minorRatio = 0;
            return false;
        }

        var normal = new Point2(-majorAxisEndPoint.Y / majorLength, majorAxisEndPoint.X / majorLength);
        var minorLength = Math.Abs(((minorPoint.X - center.X) * normal.X) + ((minorPoint.Y - center.Y) * normal.Y));
        minorRatio = minorLength / majorLength;
        return minorRatio > GeometryTolerance;
    }

    private static double GetEllipseParameterDegrees(Point2 center, Point2 majorPoint, Point2 minorPoint, Point2 point)
    {
        if (!TryGetEllipseAxes(center, majorPoint, minorPoint, out var majorAxisEndPoint, out var minorRatio))
        {
            return 360;
        }

        var majorLength = Distance(center, majorPoint);
        var majorUnit = new Point2(majorAxisEndPoint.X / majorLength, majorAxisEndPoint.Y / majorLength);
        var minorUnit = new Point2(-majorUnit.Y, majorUnit.X);
        var offset = new Point2(point.X - center.X, point.Y - center.Y);
        var x = ((offset.X * majorUnit.X) + (offset.Y * majorUnit.Y)) / majorLength;
        var y = ((offset.X * minorUnit.X) + (offset.Y * minorUnit.Y)) / (majorLength * minorRatio);
        var degrees = Math.Atan2(y, x) * 180.0 / Math.PI;
        if (degrees <= GeometryTolerance)
        {
            degrees += 360.0;
        }

        return degrees;
    }

    private static DrawingEntity CreatePolygon(
        Point2 center,
        Point2 radiusPoint,
        bool circumscribed,
        int sideCount,
        Func<string, EntityId> createEntityId,
        bool isConstruction) =>
        PolygonEntity.FromCenterAndRadiusPoint(
            createEntityId(circumscribed ? "circ-poly" : "poly"),
            center,
            radiusPoint,
            circumscribed,
            sideCount,
            isConstruction);

    private static SplineEntity CreateSpline(EntityId id, IReadOnlyList<Point2> controlPoints, bool isConstruction)
    {
        var degree = Math.Min(3, controlPoints.Count - 1);
        return new SplineEntity(id, degree, controlPoints, Array.Empty<double>(), isConstruction: isConstruction);
    }

    private static IEnumerable<DrawingEntity> CreateSlot(
        Point2 startCenter,
        Point2 endCenter,
        Point2 radiusPoint,
        Func<string, EntityId> createEntityId,
        bool isConstruction)
    {
        var axisLength = Distance(startCenter, endCenter);
        if (axisLength <= GeometryTolerance)
        {
            yield break;
        }

        var radius = Math.Abs(((radiusPoint.X - startCenter.X) * -(endCenter.Y - startCenter.Y) / axisLength)
            + ((radiusPoint.Y - startCenter.Y) * (endCenter.X - startCenter.X) / axisLength));
        if (radius <= GeometryTolerance)
        {
            yield break;
        }

        var axis = new Point2((endCenter.X - startCenter.X) / axisLength, (endCenter.Y - startCenter.Y) / axisLength);
        var normal = new Point2(-axis.Y, axis.X);
        var startLeft = new Point2(startCenter.X + normal.X * radius, startCenter.Y + normal.Y * radius);
        var endLeft = new Point2(endCenter.X + normal.X * radius, endCenter.Y + normal.Y * radius);
        var startRight = new Point2(startCenter.X - normal.X * radius, startCenter.Y - normal.Y * radius);
        var endRight = new Point2(endCenter.X - normal.X * radius, endCenter.Y - normal.Y * radius);
        var axisAngle = Math.Atan2(axis.Y, axis.X) * 180.0 / Math.PI;

        yield return new LineEntity(createEntityId("slot"), startLeft, endLeft, isConstruction);
        yield return new ArcEntity(createEntityId("slot"), endCenter, radius, axisAngle - 90, axisAngle + 90, isConstruction);
        yield return new LineEntity(createEntityId("slot"), endRight, startRight, isConstruction);
        yield return new ArcEntity(createEntityId("slot"), startCenter, radius, axisAngle + 90, axisAngle + 270, isConstruction);
        yield return new LineEntity(createEntityId("slot-center"), startCenter, endCenter, true);
    }

    private static int GetPolygonSideCount(IReadOnlyDictionary<string, double>? dimensionValues)
    {
        if (dimensionValues is not null
            && dimensionValues.TryGetValue("sides", out var sides)
            && double.IsFinite(sides))
        {
            return PolygonEntity.NormalizeSideCount(sides);
        }

        return DefaultPolygonSideCount;
    }

    private static Point2[]? GetAlignedRectangleCorners(Point2 first, Point2 second, Point2 depthPoint)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        var length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length <= GeometryTolerance)
        {
            return null;
        }

        var normalX = -dy / length;
        var normalY = dx / length;
        var depth = ((depthPoint.X - second.X) * normalX) + ((depthPoint.Y - second.Y) * normalY);
        var offset = new Point2(normalX * depth, normalY * depth);
        return new[]
        {
            first,
            second,
            new Point2(second.X + offset.X, second.Y + offset.Y),
            new Point2(first.X + offset.X, first.Y + offset.Y)
        };
    }

    private static double Distance(Point2 first, Point2 second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static Point2 GetPointAtLength(Point2 anchor, Point2 directionPoint, double? length)
    {
        if (length is null)
        {
            return directionPoint;
        }

        var dx = directionPoint.X - anchor.X;
        var dy = directionPoint.Y - anchor.Y;
        var currentLength = Math.Sqrt((dx * dx) + (dy * dy));
        if (currentLength <= GeometryTolerance)
        {
            return new Point2(anchor.X + length.Value, anchor.Y);
        }

        return new Point2(
            anchor.X + dx / currentLength * length.Value,
            anchor.Y + dy / currentLength * length.Value);
    }

    private static Point2 GetAxisRectangleOppositeCorner(
        Point2 first,
        Point2 second,
        IReadOnlyDictionary<string, double>? dimensionValues)
    {
        var width = GetPositiveDimensionValue(dimensionValues, "width") ?? Math.Abs(second.X - first.X);
        var height = GetPositiveDimensionValue(dimensionValues, "height") ?? Math.Abs(second.Y - first.Y);
        var signX = second.X < first.X ? -1.0 : 1.0;
        var signY = second.Y < first.Y ? -1.0 : 1.0;
        return new Point2(first.X + width * signX, first.Y + height * signY);
    }

    private static Point2 GetCenterRectangleCorner(
        Point2 center,
        Point2 corner,
        IReadOnlyDictionary<string, double>? dimensionValues)
    {
        var halfWidth = (GetPositiveDimensionValue(dimensionValues, "width") ?? Math.Abs((corner.X - center.X) * 2.0)) / 2.0;
        var halfHeight = (GetPositiveDimensionValue(dimensionValues, "height") ?? Math.Abs((corner.Y - center.Y) * 2.0)) / 2.0;
        var signX = corner.X < center.X ? -1.0 : 1.0;
        var signY = corner.Y < center.Y ? -1.0 : 1.0;
        return new Point2(center.X + halfWidth * signX, center.Y + halfHeight * signY);
    }

    private static double? GetPositiveDimensionValue(
        IReadOnlyDictionary<string, double>? dimensionValues,
        string key)
    {
        if (dimensionValues is null)
        {
            return null;
        }

        foreach (var dimensionValue in dimensionValues)
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(dimensionValue.Key, key)
                && double.IsFinite(dimensionValue.Value)
                && dimensionValue.Value > GeometryTolerance)
            {
                return dimensionValue.Value;
            }
        }

        return null;
    }

    private static string NormalizeToolName(string toolName) =>
        toolName.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}

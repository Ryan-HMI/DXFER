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
                entities.Add(new LineEntity(createEntityId("line"), first, second, isConstruction));
                break;
            case "midpointline":
                var mirroredEndpoint = new Point2((2 * first.X) - second.X, (2 * first.Y) - second.Y);
                entities.Add(new LineEntity(createEntityId("line"), mirroredEndpoint, second, isConstruction));
                break;
            case "twopointrectangle":
                entities.AddRange(CreateRectangle(first, new Point2(second.X, first.Y), second, new Point2(first.X, second.Y), createEntityId, isConstruction));
                break;
            case "centerrectangle":
                var centerCorners = SketchRectangleGeometry.GetCenterRectangleCorners(first, second);
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
                var radius = Distance(first, second);
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
                entities.AddRange(CreatePolygon(first, second, false, GetPolygonSideCount(dimensionValues), createEntityId, isConstruction));
                break;
            case "circumscribedpolygon":
                entities.AddRange(CreatePolygon(first, second, true, GetPolygonSideCount(dimensionValues), createEntityId, isConstruction));
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

    private static IEnumerable<DrawingEntity> CreatePolygon(
        Point2 center,
        Point2 radiusPoint,
        bool circumscribed,
        int sideCount,
        Func<string, EntityId> createEntityId,
        bool isConstruction)
    {
        var constructionRadius = Distance(center, radiusPoint);
        if (constructionRadius > GeometryTolerance)
        {
            yield return new CircleEntity(
                createEntityId(circumscribed ? "circ-poly-guide" : "poly-guide"),
                center,
                constructionRadius,
                true);
        }

        var vertices = GetPolygonVertices(center, radiusPoint, circumscribed, sideCount);
        for (var index = 0; index < vertices.Count; index++)
        {
            yield return new LineEntity(
                createEntityId(circumscribed ? "circ-poly" : "poly"),
                vertices[index],
                vertices[(index + 1) % vertices.Count],
                isConstruction);
        }
    }

    private static IReadOnlyList<Point2> GetPolygonVertices(Point2 center, Point2 radiusPoint, bool circumscribed, int sideCount)
    {
        var angle = Math.Atan2(radiusPoint.Y - center.Y, radiusPoint.X - center.X);
        var radius = Distance(center, radiusPoint);
        if (circumscribed)
        {
            radius /= Math.Cos(Math.PI / sideCount);
            angle += Math.PI / sideCount;
        }

        return Enumerable.Range(0, sideCount)
            .Select(index => new Point2(
                center.X + Math.Cos(angle + index * Math.Tau / sideCount) * radius,
                center.Y + Math.Sin(angle + index * Math.Tau / sideCount) * radius))
            .ToArray();
    }

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
            return Math.Clamp((int)Math.Round(sides), 3, 64);
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

    private static string NormalizeToolName(string toolName) =>
        toolName.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();
}

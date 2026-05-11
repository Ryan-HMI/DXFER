using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Blazor.Sketching;

public static class SketchCreationConstraintFactory
{
    private const double GeometryTolerance = 0.000001;

    public static IReadOnlyList<SketchConstraint> CreateConstraintsForTool(
        string toolName,
        IReadOnlyList<DrawingEntity> createdEntities,
        Func<SketchConstraintKind, string> createConstraintId)
    {
        ArgumentNullException.ThrowIfNull(createdEntities);
        ArgumentNullException.ThrowIfNull(createConstraintId);

        var constraints = new List<SketchConstraint>();
        var normalizedTool = NormalizeToolName(toolName);
        var lines = createdEntities.OfType<LineEntity>().ToArray();

        switch (normalizedTool)
        {
            case "line":
            case "midpointline":
                if (lines.FirstOrDefault() is { } line)
                {
                    AddAxisConstraint(constraints, line, createConstraintId);
                }

                break;
            case "twopointrectangle":
            case "centerrectangle":
                AddRectangleConstraints(constraints, lines, createConstraintId, includeGlobalAxes: true);
                break;
            case "alignedrectangle":
                AddRectangleConstraints(constraints, lines, createConstraintId, includeGlobalAxes: false);
                break;
            case "slot":
                AddSlotConstraints(constraints, lines, createdEntities.OfType<ArcEntity>().ToArray(), createConstraintId);
                break;
        }

        return constraints;
    }

    public static IReadOnlyList<SketchConstraint> CreateConstraintsForInsertion(
        string toolName,
        IReadOnlyList<DrawingEntity> existingEntities,
        IReadOnlyList<DrawingEntity> createdEntities,
        Func<SketchConstraintKind, string> createConstraintId)
    {
        ArgumentNullException.ThrowIfNull(existingEntities);
        ArgumentNullException.ThrowIfNull(createdEntities);
        ArgumentNullException.ThrowIfNull(createConstraintId);

        var constraints = CreateConstraintsForTool(toolName, createdEntities, createConstraintId).ToList();
        AddPointSnapConstraints(constraints, existingEntities, createdEntities, createConstraintId);
        AddPerpendicularSnapConstraints(constraints, existingEntities, createdEntities, createConstraintId);
        AddTangentArcConstraints(constraints, existingEntities, createdEntities, createConstraintId);
        return constraints;
    }

    private static void AddRectangleConstraints(
        ICollection<SketchConstraint> constraints,
        IReadOnlyList<LineEntity> lines,
        Func<SketchConstraintKind, string> createConstraintId,
        bool includeGlobalAxes)
    {
        if (lines.Count < 4)
        {
            return;
        }

        AddCoincidentLoopConstraints(constraints, lines.Take(4).ToArray(), createConstraintId);
        AddConstraint(constraints, SketchConstraintKind.Parallel, createConstraintId, lines[0].Id.Value, lines[2].Id.Value);
        AddConstraint(constraints, SketchConstraintKind.Parallel, createConstraintId, lines[1].Id.Value, lines[3].Id.Value);
        AddConstraint(constraints, SketchConstraintKind.Perpendicular, createConstraintId, lines[0].Id.Value, lines[1].Id.Value);

        if (!includeGlobalAxes)
        {
            return;
        }

        AddAxisConstraint(constraints, lines[0], createConstraintId);
        AddAxisConstraint(constraints, lines[1], createConstraintId);
    }

    private static void AddSlotConstraints(
        ICollection<SketchConstraint> constraints,
        IReadOnlyList<LineEntity> lines,
        IReadOnlyList<ArcEntity> arcs,
        Func<SketchConstraintKind, string> createConstraintId)
    {
        if (lines.Count >= 2)
        {
            AddConstraint(constraints, SketchConstraintKind.Parallel, createConstraintId, lines[0].Id.Value, lines[1].Id.Value);
        }

        if (arcs.Count >= 2)
        {
            AddConstraint(constraints, SketchConstraintKind.Equal, createConstraintId, arcs[0].Id.Value, arcs[1].Id.Value);
        }

        if (lines.Count < 2 || arcs.Count < 2)
        {
            return;
        }

        var firstLine = lines[0];
        var secondLine = lines[1];
        var endArc = arcs[0];
        var startArc = arcs[1];

        AddConstraint(constraints, SketchConstraintKind.Coincident, createConstraintId, $"{firstLine.Id.Value}:end", $"{endArc.Id.Value}:end");
        AddConstraint(constraints, SketchConstraintKind.Coincident, createConstraintId, $"{secondLine.Id.Value}:start", $"{endArc.Id.Value}:start");
        AddConstraint(constraints, SketchConstraintKind.Coincident, createConstraintId, $"{secondLine.Id.Value}:end", $"{startArc.Id.Value}:end");
        AddConstraint(constraints, SketchConstraintKind.Coincident, createConstraintId, $"{firstLine.Id.Value}:start", $"{startArc.Id.Value}:start");
        AddConstraint(constraints, SketchConstraintKind.Tangent, createConstraintId, firstLine.Id.Value, endArc.Id.Value);
        AddConstraint(constraints, SketchConstraintKind.Tangent, createConstraintId, secondLine.Id.Value, endArc.Id.Value);
        AddConstraint(constraints, SketchConstraintKind.Tangent, createConstraintId, firstLine.Id.Value, startArc.Id.Value);
        AddConstraint(constraints, SketchConstraintKind.Tangent, createConstraintId, secondLine.Id.Value, startArc.Id.Value);
    }

    private static void AddCoincidentLoopConstraints(
        ICollection<SketchConstraint> constraints,
        IReadOnlyList<LineEntity> lines,
        Func<SketchConstraintKind, string> createConstraintId)
    {
        for (var index = 0; index < lines.Count; index++)
        {
            var current = lines[index];
            var next = lines[(index + 1) % lines.Count];
            AddConstraint(
                constraints,
                SketchConstraintKind.Coincident,
                createConstraintId,
                $"{current.Id.Value}:end",
                $"{next.Id.Value}:start");
        }
    }

    private static void AddAxisConstraint(
        ICollection<SketchConstraint> constraints,
        LineEntity line,
        Func<SketchConstraintKind, string> createConstraintId)
    {
        if (Math.Abs(line.Start.Y - line.End.Y) <= GeometryTolerance)
        {
            AddConstraint(constraints, SketchConstraintKind.Horizontal, createConstraintId, line.Id.Value);
        }
        else if (Math.Abs(line.Start.X - line.End.X) <= GeometryTolerance)
        {
            AddConstraint(constraints, SketchConstraintKind.Vertical, createConstraintId, line.Id.Value);
        }
    }

    private static void AddPointSnapConstraints(
        ICollection<SketchConstraint> constraints,
        IReadOnlyList<DrawingEntity> existingEntities,
        IReadOnlyList<DrawingEntity> createdEntities,
        Func<SketchConstraintKind, string> createConstraintId)
    {
        var existingPoints = EnumeratePointReferences(existingEntities).ToArray();
        var existingLines = EnumerateLineReferences(existingEntities).ToArray();

        foreach (var createdPoint in EnumeratePointReferences(createdEntities))
        {
            foreach (var existingPoint in existingPoints)
            {
                if (!AreClose(createdPoint.Point, existingPoint.Point))
                {
                    continue;
                }

                AddConstraintIfMissing(
                    constraints,
                    SketchConstraintKind.Coincident,
                    createConstraintId,
                    existingPoint.Reference.ToString(),
                    createdPoint.Reference.ToString());
            }

            foreach (var existingLine in existingLines)
            {
                if (!AreClose(createdPoint.Point, Midpoint(existingLine.Line)))
                {
                    continue;
                }

                AddConstraintIfMissing(
                    constraints,
                    SketchConstraintKind.Midpoint,
                    createConstraintId,
                    existingLine.Reference.ToString(),
                    createdPoint.Reference.ToString());
            }
        }
    }

    private static void AddPerpendicularSnapConstraints(
        ICollection<SketchConstraint> constraints,
        IReadOnlyList<DrawingEntity> existingEntities,
        IReadOnlyList<DrawingEntity> createdEntities,
        Func<SketchConstraintKind, string> createConstraintId)
    {
        var existingLines = EnumerateLineReferences(existingEntities).ToArray();
        foreach (var createdLine in EnumerateLineReferences(createdEntities))
        {
            foreach (var existingLine in existingLines)
            {
                if (!TouchesLineEndpointOrMidpoint(createdLine.Line, existingLine.Line)
                    || !ArePerpendicular(createdLine.Line, existingLine.Line))
                {
                    continue;
                }

                AddConstraintIfMissing(
                    constraints,
                    SketchConstraintKind.Perpendicular,
                    createConstraintId,
                    existingLine.Reference.ToString(),
                    createdLine.Reference.ToString());
            }
        }
    }

    private static void AddTangentArcConstraints(
        ICollection<SketchConstraint> constraints,
        IReadOnlyList<DrawingEntity> existingEntities,
        IReadOnlyList<DrawingEntity> createdEntities,
        Func<SketchConstraintKind, string> createConstraintId)
    {
        var existingLines = EnumerateLineReferences(existingEntities).ToArray();
        foreach (var createdArc in createdEntities.OfType<ArcEntity>())
        {
            var arcStart = PointOnArc(createdArc, createdArc.StartAngleDegrees);
            var arcEnd = PointOnArc(createdArc, createdArc.EndAngleDegrees);
            foreach (var existingLine in existingLines)
            {
                if (!LineFeatureMatches(existingLine.Line, arcStart)
                    && !LineFeatureMatches(existingLine.Line, arcEnd))
                {
                    continue;
                }

                if (!IsLineTangentToArc(existingLine.Line, createdArc))
                {
                    continue;
                }

                AddConstraintIfMissing(
                    constraints,
                    SketchConstraintKind.Tangent,
                    createConstraintId,
                    existingLine.Reference.ToString(),
                    createdArc.Id.Value);
            }
        }
    }

    private static IEnumerable<PointReference> EnumeratePointReferences(IReadOnlyList<DrawingEntity> entities)
    {
        foreach (var entity in entities)
        {
            switch (entity)
            {
                case LineEntity line:
                    yield return new PointReference(new SketchReference(line.Id.Value, SketchReferenceTarget.Start), line.Start);
                    yield return new PointReference(new SketchReference(line.Id.Value, SketchReferenceTarget.End), line.End);
                    break;
                case ArcEntity arc:
                    yield return new PointReference(new SketchReference(arc.Id.Value, SketchReferenceTarget.Start), PointOnArc(arc, arc.StartAngleDegrees));
                    yield return new PointReference(new SketchReference(arc.Id.Value, SketchReferenceTarget.End), PointOnArc(arc, arc.EndAngleDegrees));
                    yield return new PointReference(new SketchReference(arc.Id.Value, SketchReferenceTarget.Center), arc.Center);
                    break;
                case CircleEntity circle:
                    yield return new PointReference(new SketchReference(circle.Id.Value, SketchReferenceTarget.Center), circle.Center);
                    break;
                case PointEntity point:
                    yield return new PointReference(new SketchReference(point.Id.Value, SketchReferenceTarget.Entity), point.Location);
                    break;
                case PolygonEntity polygon:
                    yield return new PointReference(new SketchReference(polygon.Id.Value, SketchReferenceTarget.Center), polygon.Center);
                    break;
                case PolylineEntity polyline:
                    for (var index = 0; index < polyline.Vertices.Count - 1; index++)
                    {
                        yield return new PointReference(new SketchReference(polyline.Id.Value, SketchReferenceTarget.Start, index), polyline.Vertices[index]);
                        yield return new PointReference(new SketchReference(polyline.Id.Value, SketchReferenceTarget.End, index), polyline.Vertices[index + 1]);
                    }

                    break;
            }
        }
    }

    private static IEnumerable<LineReference> EnumerateLineReferences(IReadOnlyList<DrawingEntity> entities)
    {
        foreach (var entity in entities)
        {
            switch (entity)
            {
                case LineEntity line:
                    yield return new LineReference(new SketchReference(line.Id.Value, SketchReferenceTarget.Entity), line);
                    break;
                case PolylineEntity polyline:
                    for (var index = 0; index < polyline.Vertices.Count - 1; index++)
                    {
                        yield return new LineReference(
                            new SketchReference(polyline.Id.Value, SketchReferenceTarget.Entity, index),
                            new LineEntity(polyline.Id, polyline.Vertices[index], polyline.Vertices[index + 1], polyline.IsConstruction));
                    }

                    break;
            }
        }
    }

    private static bool TouchesLineEndpointOrMidpoint(LineEntity createdLine, LineEntity existingLine) =>
        LineFeatureMatches(existingLine, createdLine.Start)
        || LineFeatureMatches(existingLine, createdLine.End);

    private static bool LineFeatureMatches(LineEntity line, Point2 point) =>
        AreClose(point, line.Start)
        || AreClose(point, line.End)
        || AreClose(point, Midpoint(line));

    private static bool ArePerpendicular(LineEntity first, LineEntity second)
    {
        if (!TryGetDirection(first, out var firstX, out var firstY)
            || !TryGetDirection(second, out var secondX, out var secondY))
        {
            return false;
        }

        return Math.Abs((firstX * secondX) + (firstY * secondY)) <= GeometryTolerance;
    }

    private static bool IsLineTangentToArc(LineEntity line, ArcEntity arc)
    {
        return Math.Abs(DistancePointToLine(arc.Center, line) - arc.Radius) <= GeometryTolerance;
    }

    private static double DistancePointToLine(Point2 point, LineEntity line)
    {
        var deltaX = line.End.X - line.Start.X;
        var deltaY = line.End.Y - line.Start.Y;
        var denominator = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (denominator <= GeometryTolerance)
        {
            return Distance(point, line.Start);
        }

        return Math.Abs(
            (deltaY * point.X)
            - (deltaX * point.Y)
            + (line.End.X * line.Start.Y)
            - (line.End.Y * line.Start.X)) / denominator;
    }

    private static bool TryGetDirection(LineEntity line, out double x, out double y)
    {
        var deltaX = line.End.X - line.Start.X;
        var deltaY = line.End.Y - line.Start.Y;
        var length = Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
        if (length <= GeometryTolerance)
        {
            x = 0;
            y = 0;
            return false;
        }

        x = deltaX / length;
        y = deltaY / length;
        return true;
    }

    private static Point2 Midpoint(LineEntity line) =>
        new((line.Start.X + line.End.X) / 2.0, (line.Start.Y + line.End.Y) / 2.0);

    private static Point2 PointOnArc(ArcEntity arc, double angleDegrees)
    {
        var radians = angleDegrees * Math.PI / 180.0;
        return new Point2(
            arc.Center.X + arc.Radius * Math.Cos(radians),
            arc.Center.Y + arc.Radius * Math.Sin(radians));
    }

    private static double Distance(Point2 first, Point2 second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY));
    }

    private static bool AreClose(Point2 first, Point2 second) =>
        Math.Abs(first.X - second.X) <= GeometryTolerance
        && Math.Abs(first.Y - second.Y) <= GeometryTolerance;

    private static void AddConstraintIfMissing(
        ICollection<SketchConstraint> constraints,
        SketchConstraintKind kind,
        Func<SketchConstraintKind, string> createConstraintId,
        params string[] referenceKeys)
    {
        if (HasConstraint(constraints, kind, referenceKeys))
        {
            return;
        }

        AddConstraint(constraints, kind, createConstraintId, referenceKeys);
    }

    private static bool HasConstraint(
        IEnumerable<SketchConstraint> constraints,
        SketchConstraintKind kind,
        IReadOnlyList<string> referenceKeys)
    {
        return constraints.Any(constraint =>
            constraint.Kind == kind
            && ReferencesMatch(constraint.ReferenceKeys, referenceKeys));
    }

    private static bool ReferencesMatch(
        IReadOnlyList<string> existing,
        IReadOnlyList<string> candidate)
    {
        if (existing.Count != candidate.Count)
        {
            return false;
        }

        if (existing.SequenceEqual(candidate, StringComparer.Ordinal))
        {
            return true;
        }

        return candidate.Count == 2
            && StringComparer.Ordinal.Equals(existing[0], candidate[1])
            && StringComparer.Ordinal.Equals(existing[1], candidate[0]);
    }

    private static void AddConstraint(
        ICollection<SketchConstraint> constraints,
        SketchConstraintKind kind,
        Func<SketchConstraintKind, string> createConstraintId,
        params string[] referenceKeys) =>
        constraints.Add(new SketchConstraint(createConstraintId(kind), kind, referenceKeys, SketchConstraintState.Satisfied));

    private static string NormalizeToolName(string toolName) =>
        toolName.Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .ToLowerInvariant();

    private readonly record struct PointReference(SketchReference Reference, Point2 Point);

    private readonly record struct LineReference(SketchReference Reference, LineEntity Line);
}

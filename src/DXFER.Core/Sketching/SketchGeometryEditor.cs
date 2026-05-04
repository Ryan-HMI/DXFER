using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

internal static class SketchGeometryEditor
{
    internal const double Tolerance = 0.000001;

    public static bool AreClose(double first, double second) =>
        Math.Abs(first - second) <= Tolerance;

    public static bool AreClose(Point2 first, Point2 second) =>
        Distance(first, second) <= Tolerance;

    public static double Distance(Point2 first, Point2 second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }

    public static Point2 Midpoint(LineEntity line) =>
        new((line.Start.X + line.End.X) / 2.0, (line.Start.Y + line.End.Y) / 2.0);

    public static bool TryFindEntity(
        IReadOnlyList<DrawingEntity> entities,
        string entityId,
        out int index,
        out DrawingEntity entity)
    {
        for (var candidateIndex = 0; candidateIndex < entities.Count; candidateIndex++)
        {
            var candidate = entities[candidateIndex];
            if (StringComparer.Ordinal.Equals(candidate.Id.Value, entityId))
            {
                index = candidateIndex;
                entity = candidate;
                return true;
            }
        }

        index = -1;
        entity = default!;
        return false;
    }

    public static bool TryGetEntity(
        IReadOnlyList<DrawingEntity> entities,
        SketchReference reference,
        out int index,
        out DrawingEntity entity) =>
        TryFindEntity(entities, reference.EntityId, out index, out entity);

    public static bool TryGetPoint(
        IReadOnlyList<DrawingEntity> entities,
        SketchReference reference,
        out Point2 point)
    {
        if (!TryGetEntity(entities, reference, out _, out var entity))
        {
            point = default;
            return false;
        }

        switch (entity)
        {
            case LineEntity line when reference.Target == SketchReferenceTarget.Start:
                point = line.Start;
                return true;
            case LineEntity line when reference.Target == SketchReferenceTarget.End:
                point = line.End;
                return true;
            case CircleEntity circle when reference.Target == SketchReferenceTarget.Center:
                point = circle.Center;
                return true;
            case ArcEntity arc when reference.Target == SketchReferenceTarget.Center:
                point = arc.Center;
                return true;
            default:
                point = default;
                return false;
        }
    }

    public static bool TrySetPoint(
        DrawingEntity[] entities,
        SketchReference reference,
        Point2 point)
    {
        if (!TryGetEntity(entities, reference, out var index, out var entity))
        {
            return false;
        }

        switch (entity)
        {
            case LineEntity line when reference.Target == SketchReferenceTarget.Start:
                entities[index] = line with { Start = point };
                return true;
            case LineEntity line when reference.Target == SketchReferenceTarget.End:
                entities[index] = line with { End = point };
                return true;
            case CircleEntity circle when reference.Target == SketchReferenceTarget.Center:
                entities[index] = circle with { Center = point };
                return true;
            case ArcEntity arc when reference.Target == SketchReferenceTarget.Center:
                entities[index] = arc with { Center = point };
                return true;
            default:
                return false;
        }
    }

    public static bool TryGetLine(
        IReadOnlyList<DrawingEntity> entities,
        SketchReference reference,
        out int index,
        out LineEntity line)
    {
        if (reference.Target != SketchReferenceTarget.Entity
            || !TryGetEntity(entities, reference, out index, out var entity)
            || entity is not LineEntity lineEntity)
        {
            index = -1;
            line = default!;
            return false;
        }

        line = lineEntity;
        return true;
    }

    public static bool TrySetLine(
        DrawingEntity[] entities,
        SketchReference reference,
        LineEntity line)
    {
        if (!TryGetLine(entities, reference, out var index, out _))
        {
            return false;
        }

        entities[index] = line;
        return true;
    }

    public static bool TryGetCircleLike(
        IReadOnlyList<DrawingEntity> entities,
        SketchReference reference,
        out int index,
        out Point2 center,
        out double radius)
    {
        if (!TryGetEntity(entities, reference, out index, out var entity))
        {
            center = default;
            radius = default;
            return false;
        }

        if (reference.Target != SketchReferenceTarget.Entity
            && reference.Target != SketchReferenceTarget.Center)
        {
            center = default;
            radius = default;
            return false;
        }

        switch (entity)
        {
            case CircleEntity circle:
                center = circle.Center;
                radius = circle.Radius;
                return true;
            case ArcEntity arc:
                center = arc.Center;
                radius = arc.Radius;
                return true;
            default:
                center = default;
                radius = default;
                return false;
        }
    }

    public static bool TrySetCircleLikeCenter(
        DrawingEntity[] entities,
        SketchReference reference,
        Point2 center)
    {
        if (!TryGetEntity(entities, reference, out var index, out var entity))
        {
            return false;
        }

        switch (entity)
        {
            case CircleEntity circle:
                entities[index] = circle with { Center = center };
                return true;
            case ArcEntity arc:
                entities[index] = arc with { Center = center };
                return true;
            default:
                return false;
        }
    }

    public static bool TrySetCircleLikeRadius(
        DrawingEntity[] entities,
        SketchReference reference,
        double radius)
    {
        if (reference.Target != SketchReferenceTarget.Entity
            || !TryGetEntity(entities, reference, out var index, out var entity))
        {
            return false;
        }

        switch (entity)
        {
            case CircleEntity circle:
                entities[index] = circle with { Radius = radius };
                return true;
            case ArcEntity arc:
                entities[index] = arc with { Radius = radius };
                return true;
            default:
                return false;
        }
    }

    public static bool TryGetLineDirection(LineEntity line, out double unitX, out double unitY, out double length)
    {
        var deltaX = line.End.X - line.Start.X;
        var deltaY = line.End.Y - line.Start.Y;
        length = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        if (length <= Tolerance)
        {
            unitX = default;
            unitY = default;
            return false;
        }

        unitX = deltaX / length;
        unitY = deltaY / length;
        return true;
    }

    public static double NormalizeSignedDegrees(double degrees)
    {
        var normalized = degrees % 360.0;
        if (normalized > 180.0)
        {
            normalized -= 360.0;
        }
        else if (normalized <= -180.0)
        {
            normalized += 360.0;
        }

        return normalized;
    }
}

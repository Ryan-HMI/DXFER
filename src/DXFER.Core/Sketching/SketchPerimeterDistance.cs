using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

public static class SketchPerimeterDistance
{
    private const double Epsilon = 1e-7;

    public static bool TryMeasure(IReadOnlyList<DrawingEntity> entities, IReadOnlyList<string> keys,
        out double value, out Point2 start, out Point2 end)
    {
        value = default;
        start = end = default;
        if (keys.Count != 2 || !TryResolve(entities, keys[0], out var a)
            || !TryResolve(entities, keys[1], out var b) || a.Id == b.Id) return false;
        if (a is not (CircleEntity or ArcEntity))
        {
            if (b is not (CircleEntity or ArcEntity)) return false;
            return TryMeasure(entities, [keys[1], keys[0]], out value, out end, out start);
        }
        var (center, radius) = Circle(a);
        if (!double.IsFinite(radius) || radius <= 0) return false;
        if (b is CircleEntity or ArcEntity)
        {
            var (other, otherRadius) = Circle(b);
            if (!double.IsFinite(otherRadius) || otherRadius <= 0) return false;
            var distance = Distance(center, other);
            var u = Direction(center, other);
            if (distance >= radius + otherRadius - Epsilon)
            {
                start = At(center, radius, u);
                end = At(other, -otherRadius, u);
            }
            else if (distance <= Math.Abs(radius - otherRadius) + Epsilon)
            {
                var sign = radius >= otherRadius ? 1 : -1;
                start = At(center, sign * radius, u);
                end = At(other, sign * otherRadius, u);
            }
            else return false;
            if (!OnArc(b, end)) return false;
        }
        else if (b is PointEntity point)
        {
            end = point.Location;
            start = At(center, radius, Direction(center, end));
        }
        else if (b is LineEntity line)
        {
            var dx = line.End.X - line.Start.X;
            var dy = line.End.Y - line.Start.Y;
            var ll = dx * dx + dy * dy;
            if (ll <= Epsilon * Epsilon) return false;
            var t = ((center.X - line.Start.X) * dx + (center.Y - line.Start.Y) * dy) / ll;
            end = new(line.Start.X + t * dx, line.Start.Y + t * dy);
            if (Distance(center, end) < radius - Epsilon) return false;
            start = At(center, radius, Direction(center, end));
        }
        else return false;
        if (!OnArc(a, start)) return false;
        value = Distance(start, end);
        return double.IsFinite(value);
    }

    private static bool TryResolve(IReadOnlyList<DrawingEntity> entities, string key, out DrawingEntity entity)
    {
        entity = default!;
        if (!SketchReference.TryParse(key, out var reference)) return false;
        if (reference.Target == SketchReferenceTarget.Entity && !reference.SegmentIndex.HasValue)
        {
            entity = entities.FirstOrDefault(e => e.Id.Value == reference.EntityId)!;
            return entity is not null;
        }
        if (SketchDimensionSolverService.TryResolveDimensionPoint(entities, key, out var point))
        {
            entity = new PointEntity(new EntityId(key), point);
            return true;
        }
        var document = new DrawingDocument(entities);
        if (!SketchReferenceResolver.TryGetLine(document, key, out var line) || line is null) return false;
        entity = new LineEntity(new EntityId(key), line.Start, line.End);
        return true;
    }

    private static (Point2 Center, double Radius) Circle(DrawingEntity entity) => entity switch
    {
        CircleEntity c => (c.Center, c.Radius),
        ArcEntity a => (a.Center, a.Radius),
        _ => throw new ArgumentException("Expected circle or arc.")
    };
    private static double Distance(Point2 a, Point2 b) => Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
    private static Point2 Direction(Point2 a, Point2 b)
    {
        var d = Distance(a, b);
        return d > Epsilon ? new((b.X - a.X) / d, (b.Y - a.Y) / d) : new(1, 0);
    }
    private static Point2 At(Point2 center, double radius, Point2 u) => new(center.X + radius * u.X, center.Y + radius * u.Y);
    private static bool OnArc(DrawingEntity entity, Point2 p)
    {
        if (entity is not ArcEntity arc) return true;
        static double Mod(double value) => (value % 360 + 360) % 360;
        var raw = arc.EndAngleDegrees - arc.StartAngleDegrees;
        var sweep = raw >= 360 ? 360 : Mod(raw);
        var angle = Mod(Math.Atan2(p.Y - arc.Center.Y, p.X - arc.Center.X) * 180 / Math.PI - arc.StartAngleDegrees);
        return angle <= sweep + 1e-6 || 360 - angle <= 1e-6;
    }
}

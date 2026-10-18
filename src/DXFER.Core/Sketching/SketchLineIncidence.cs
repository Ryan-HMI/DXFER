using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

internal static class SketchLineIncidence
{
    public static bool IsSatisfied(IReadOnlyList<DrawingEntity> entities, SketchConstraint constraint)
    {
        if (!References(constraint, out var first, out var second)) return false;
        if (constraint.Kind == SketchConstraintKind.PointOnLine)
        {
            return PointAndLine(entities, first, second, out _, out var point, out _, out var line)
                && OnLine(point, line);
        }
        return constraint.Kind == SketchConstraintKind.Collinear
            && SketchGeometryEditor.TryGetLine(entities, first, out _, out var a)
            && SketchGeometryEditor.TryGetLine(entities, second, out _, out var b)
            && SketchGeometryEditor.TryGetLineDirection(a, out var ax, out var ay, out _)
            && SketchGeometryEditor.TryGetLineDirection(b, out var bx, out var by, out _)
            && Math.Abs(ax * by - ay * bx) <= SketchGeometryEditor.Tolerance
            && OnLine(b.Start, a) && OnLine(b.End, a);
    }

    public static bool Apply(DrawingEntity[] entities, SketchFixedReferences fixedReferences,
        SketchConstraint constraint, IReadOnlyList<DrawingEntity>? original = null)
    {
        if (IsSatisfied(entities, constraint) || !References(constraint, out var first, out var second)) return false;
        if (constraint.Kind == SketchConstraintKind.PointOnLine)
        {
            if (!PointAndLine(entities, first, second, out var pointRef, out var point, out var lineRef, out var line)
                || !Project(point, line, out var projection)) return false;
            var pointMoved = original is not null && SketchGeometryEditor.TryGetPoint(original, pointRef, out var oldPoint)
                && !SketchGeometryEditor.AreClose(oldPoint, point);
            var lineMoved = original is not null && SketchGeometryEditor.TryGetLine(original, lineRef, out _, out var oldLine)
                && (oldLine.Start != line.Start || oldLine.End != line.End);
            // Preserve an explicitly dragged point by translating its free supporting line.
            // A fixed line instead projects the point, leaving along-line freedom available.
            if ((pointMoved && !lineMoved || !fixedReferences.CanMovePoint(pointRef)) && fixedReferences.CanMoveWholeLine(lineRef))
            {
                var delta = new Point2(point.X - projection.X, point.Y - projection.Y);
                return SketchGeometryEditor.TrySetLine(entities, lineRef,
                    line with { Start = Add(line.Start, delta), End = Add(line.End, delta) });
            }
            return fixedReferences.CanMovePoint(pointRef) && SketchGeometryEditor.TrySetPoint(entities, pointRef, projection);
        }
        if (constraint.Kind != SketchConstraintKind.Collinear
            || !SketchGeometryEditor.TryGetLine(entities, first, out _, out var a)
            || !SketchGeometryEditor.TryGetLine(entities, second, out _, out var b)) return false;
        var secondMoved = original is not null && SketchGeometryEditor.TryGetLine(original, second, out _, out var oldB)
            && (oldB.Start != b.Start || oldB.End != b.End);
        var firstMoved = original is not null && SketchGeometryEditor.TryGetLine(original, first, out _, out var oldA)
            && (oldA.Start != a.Start || oldA.End != a.End);
        return secondMoved && !firstMoved
            ? Align(entities, fixedReferences, first, a, b) || Align(entities, fixedReferences, second, b, a)
            : Align(entities, fixedReferences, second, b, a) || Align(entities, fixedReferences, first, a, b);
    }

    private static bool Align(DrawingEntity[] entities, SketchFixedReferences fixedReferences,
        SketchReference moving, LineEntity line, LineEntity anchor)
    {
        if (fixedReferences.IsWholeEntityFixed(moving)
            || !SketchGeometryEditor.TryGetLineDirection(line, out var lx, out var ly, out var length)
            || !SketchGeometryEditor.TryGetLineDirection(anchor, out var ux, out var uy, out _)) return false;
        if (lx * ux + ly * uy < 0) { ux = -ux; uy = -uy; }
        var fixedStart = !fixedReferences.CanMovePoint(new(moving.EntityId, SketchReferenceTarget.Start, moving.SegmentIndex));
        var fixedEnd = !fixedReferences.CanMovePoint(new(moving.EntityId, SketchReferenceTarget.End, moving.SegmentIndex));
        if (fixedStart && fixedEnd) return false;
        Point2 start, end;
        if (fixedStart)
        {
            if (!OnLine(line.Start, anchor)) return false;
            start = line.Start; end = new(start.X + ux * length, start.Y + uy * length);
        }
        else if (fixedEnd)
        {
            if (!OnLine(line.End, anchor)) return false;
            end = line.End; start = new(end.X - ux * length, end.Y - uy * length);
        }
        else
        {
            if (!Project(new((line.Start.X + line.End.X) / 2, (line.Start.Y + line.End.Y) / 2), anchor, out var center)) return false;
            start = new(center.X - ux * length / 2, center.Y - uy * length / 2);
            end = new(center.X + ux * length / 2, center.Y + uy * length / 2);
        }
        if (SketchGeometryEditor.AreClose(start, line.Start) && SketchGeometryEditor.AreClose(end, line.End)) return false;
        return SketchGeometryEditor.TrySetLine(entities, moving, line with { Start = start, End = end });
    }

    private static bool References(SketchConstraint c, out SketchReference first, out SketchReference second)
    {
        first = second = default;
        return c.ReferenceKeys.Count == 2 && SketchReference.TryParse(c.ReferenceKeys[0], out first)
            && SketchReference.TryParse(c.ReferenceKeys[1], out second);
    }

    private static bool PointAndLine(IReadOnlyList<DrawingEntity> entities, SketchReference a, SketchReference b,
        out SketchReference pointRef, out Point2 point, out SketchReference lineRef, out LineEntity line)
    {
        pointRef = a; lineRef = b; point = default; line = default!;
        if (a.EntityId == b.EntityId) return false;
        if (SketchGeometryEditor.TryGetPoint(entities, a, out point) && SketchGeometryEditor.TryGetLine(entities, b, out _, out line)) return true;
        pointRef = b; lineRef = a;
        return SketchGeometryEditor.TryGetPoint(entities, b, out point) && SketchGeometryEditor.TryGetLine(entities, a, out _, out line);
    }

    private static bool OnLine(Point2 point, LineEntity line) => Project(point, line, out var projected)
        && SketchGeometryEditor.AreClose(point, projected);

    private static bool Project(Point2 point, LineEntity line, out Point2 projection)
    {
        projection = default;
        if (!SketchGeometryEditor.TryGetLineDirection(line, out var x, out var y, out _)) return false;
        var along = (point.X - line.Start.X) * x + (point.Y - line.Start.Y) * y;
        projection = new(line.Start.X + along * x, line.Start.Y + along * y);
        return double.IsFinite(projection.X) && double.IsFinite(projection.Y);
    }

    private static Point2 Add(Point2 a, Point2 b) => new(a.X + b.X, a.Y + b.Y);
}

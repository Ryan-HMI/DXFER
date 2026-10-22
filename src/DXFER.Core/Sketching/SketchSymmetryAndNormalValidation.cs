using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

internal static class SketchSymmetryAndNormalValidation
{
    private const double Tolerance = SketchGeometryEditor.Tolerance;

    public static bool IsSymmetric(IReadOnlyList<DrawingEntity> entities, IReadOnlyList<string> keys)
    {
        if (keys.Count < 3 || keys.Count % 2 != 1
            || !SketchReference.TryParse(keys[^1], out var axisReference)
            || !TryLine(entities, axisReference, out var axis, out var ux, out var uy)) return false;

        if (keys.Count == 3
            && SketchReference.TryParse(keys[0], out var firstCurveReference)
            && SketchReference.TryParse(keys[1], out var secondCurveReference)
            && TryCurve(entities, firstCurveReference, out var firstCurve, out var firstCenter, out var firstRadius)
            && TryCurve(entities, secondCurveReference, out var secondCurve, out var secondCenter, out var secondRadius))
        {
            if (firstCurveReference.EntityId == secondCurveReference.EntityId
                || firstCurveReference.EntityId == axisReference.EntityId || secondCurveReference.EntityId == axisReference.EntityId
                || firstCurve.GetType() != secondCurve.GetType()
                || !Close(Reflect(firstCenter, axis.Start, ux, uy), secondCenter)
                || Math.Abs(firstRadius - secondRadius) > Tolerance) return false;
            if (firstCurve is not ArcEntity firstArc) return true;
            var secondArc = (ArcEntity)secondCurve;
            return TrySweep(firstArc, out var firstSweep) && TrySweep(secondArc, out var secondSweep)
                && Math.Abs(firstSweep - secondSweep) * Math.PI / 180 * Math.Max(firstRadius, secondRadius) <= Tolerance
                && Close(Reflect(ArcPoint(firstArc, firstArc.StartAngleDegrees), axis.Start, ux, uy), ArcPoint(secondArc, secondArc.EndAngleDegrees))
                && Close(Reflect(ArcPoint(firstArc, firstArc.EndAngleDegrees), axis.Start, ux, uy), ArcPoint(secondArc, secondArc.StartAngleDegrees));
        }

        // Stored pairs are interleaved; never reelect correspondence from geometry.
        for (var index = 0; index < keys.Count - 1; index += 2)
        {
            if (!SketchReference.TryParse(keys[index], out var first)
                || !SketchReference.TryParse(keys[index + 1], out var second)
                || first.EntityId == second.EntityId
                || first.EntityId == axisReference.EntityId || second.EntityId == axisReference.EntityId
                || !TryPoint(entities, first, keys.Count > 3, out var firstPoint)
                || !TryPoint(entities, second, keys.Count > 3, out var secondPoint)
                || !Close(Reflect(firstPoint, axis.Start, ux, uy), secondPoint)) return false;
        }
        return true;
    }

    public static bool IsNormal(IReadOnlyList<DrawingEntity> entities, IReadOnlyList<string> keys)
    {
        if (keys.Count != 2 || !SketchReference.TryParse(keys[0], out var endpoint)
            || endpoint.Target is not (SketchReferenceTarget.Start or SketchReferenceTarget.End)
            || !SketchReference.TryParse(keys[1], out var curveReference)
            || endpoint.EntityId == curveReference.EntityId
            || !TryLine(entities, new SketchReference(endpoint.EntityId, SketchReferenceTarget.Entity, endpoint.SegmentIndex),
                out var line, out var ux, out var uy)
            || !TryCurve(entities, curveReference, out var curve, out var center, out var radius)) return false;
        var contact = endpoint.Target == SketchReferenceTarget.Start ? line.Start : line.End;
        var distanceToAxis = Math.Abs((center.X - line.Start.X) * uy - (center.Y - line.Start.Y) * ux);
        if (!double.IsFinite(distanceToAxis) || distanceToAxis > Tolerance
            || Math.Abs(SketchGeometryEditor.Distance(contact, center) - radius) > Tolerance) return false;
        return curve is not ArcEntity arc || OnArc(arc, contact);
    }

    private static bool TryLine(IReadOnlyList<DrawingEntity> entities, SketchReference reference,
        out LineEntity line, out double ux, out double uy)
    {
        line = default!;
        ux = uy = 0;
        if (!reference.IsEntity || !TryEntity(entities, reference, out var entity)
            || entity is not (LineEntity or PolylineEntity)
            || entity is PolylineEntity && !reference.SegmentIndex.HasValue
            || !SketchGeometryEditor.TryGetLine(entities, reference, out _, out line)
            || !Finite(line.Start) || !Finite(line.End)) return false;
        var dx = line.End.X - line.Start.X;
        var dy = line.End.Y - line.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (!double.IsFinite(length) || length <= Tolerance) return false;
        ux = dx / length;
        uy = dy / length;
        return true;
    }

    private static bool TryPoint(IReadOnlyList<DrawingEntity> entities, SketchReference reference, bool vertexOnly, out Point2 point)
    {
        point = default;
        if (reference.IsOrigin) return !vertexOnly;
        if (!TryEntity(entities, reference, out var entity)) return false;
        if (vertexOnly && (entity is not (LineEntity or PolylineEntity)
                || reference.Target is not (SketchReferenceTarget.Start or SketchReferenceTarget.End))) return false;
        if (entity is not (LineEntity or PolylineEntity or PointEntity or CircleEntity or ArcEntity)) return false;
        if (entity is PolylineEntity && !reference.SegmentIndex.HasValue) return false;
        return SketchGeometryEditor.TryGetPoint(entities, reference, out point) && Finite(point);
    }

    private static bool TryEntity(IReadOnlyList<DrawingEntity> entities, SketchReference reference, out DrawingEntity entity)
    {
        if (!SketchGeometryEditor.TryGetEntity(entities, reference, out _, out entity)) return false;
        // The general resolver permits some segment-qualified non-polylines; these
        // constraints require real, durable references, never fallback coordinates.
        return !reference.SegmentIndex.HasValue || entity is PolylineEntity polyline
            && reference.SegmentIndex.Value >= 0 && reference.SegmentIndex.Value < polyline.Vertices.Count - 1;
    }

    private static bool TryCurve(IReadOnlyList<DrawingEntity> entities, SketchReference reference,
        out DrawingEntity entity, out Point2 center, out double radius)
    {
        entity = default!;
        center = default;
        radius = 0;
        if (!reference.IsEntity || reference.SegmentIndex.HasValue || !TryEntity(entities, reference, out entity)) return false;
        switch (entity)
        {
            case CircleEntity circle: center = circle.Center; radius = circle.Radius; break;
            case ArcEntity arc when TrySweep(arc, out _): center = arc.Center; radius = arc.Radius; break;
            default: return false;
        }
        return Finite(center) && double.IsFinite(radius) && radius > Tolerance;
    }

    private static bool TrySweep(ArcEntity arc, out double sweep)
    {
        sweep = arc.EndAngleDegrees - arc.StartAngleDegrees;
        if (!double.IsFinite(arc.StartAngleDegrees) || !double.IsFinite(arc.EndAngleDegrees)
            || !double.IsFinite(sweep) || sweep > 360 || sweep <= -360) return false;
        if (sweep < 0) sweep += 360;
        return sweep > 0;
    }

    private static bool OnArc(ArcEntity arc, Point2 point)
    {
        if (!TrySweep(arc, out var sweep)) return false;
        if (sweep == 360) return true;
        var angle = Math.Atan2(point.Y - arc.Center.Y, point.X - arc.Center.X) * 180 / Math.PI;
        return Mod(angle - Mod(arc.StartAngleDegrees)) <= sweep
            || Close(point, ArcPoint(arc, arc.StartAngleDegrees)) || Close(point, ArcPoint(arc, arc.EndAngleDegrees));
    }

    private static Point2 Reflect(Point2 point, Point2 origin, double ux, double uy)
    {
        var signedDistance = (point.X - origin.X) * -uy + (point.Y - origin.Y) * ux;
        return new(point.X + 2 * signedDistance * uy, point.Y - 2 * signedDistance * ux);
    }

    private static Point2 ArcPoint(ArcEntity arc, double angle)
    {
        var radians = Mod(angle) * Math.PI / 180;
        return new(arc.Center.X + arc.Radius * Math.Cos(radians), arc.Center.Y + arc.Radius * Math.Sin(radians));
    }

    private static double Mod(double angle) => (angle % 360 + 360) % 360;
    private static bool Finite(Point2 point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    private static bool Close(Point2 a, Point2 b) => Finite(a) && Finite(b) && SketchGeometryEditor.AreClose(a, b);
}

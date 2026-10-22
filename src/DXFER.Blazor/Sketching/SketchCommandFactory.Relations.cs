using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Blazor.Sketching;

public static partial class SketchCommandFactory
{
    private static bool TryBuildOrderedRelation(DrawingDocument document, string[] picks, SketchConstraintKind kind,
        string id, out SketchConstraint constraint, out string status)
    {
        constraint = default!;
        status = kind == SketchConstraintKind.Normal
            ? "Normal requires a straight line endpoint and a circle or arc."
            : "Symmetric requires two like entities or two points, then a distinct nondegenerate straight axis. Polylines must have matching topology.";
        var keys = picks.Select(key => NormalizeRelationPick(document, key, kind == SketchConstraintKind.Normal)).ToArray();
        var references = kind == SketchConstraintKind.Normal
            ? BuildNormalReferences(document, keys)
            : BuildSymmetricReferences(document, keys);
        if (references is null) return false;
        constraint = new(id, kind, references);
        status = $"Added {kind} constraint.";
        return true;
    }

    private static string NormalizeRelationPick(DrawingDocument document, string key, bool normal)
    {
        // Perimeter coordinates are hit-test hints, never stored geometry references.
        if (SketchReference.TryParseCanvasPointCoordinates(key, out var entityId, out var label, out _)
            && TryFindEntity(document, entityId, out var entity))
        {
            if (entity is CircleEntity or ArcEntity && (normal || label != "center")) return entityId;
            if (entity is LineEntity && label == "mid") return entityId;
        }
        if (SketchReference.TryParse(key, out var reference))
        {
            if (normal && TryFindEntity(document, reference.EntityId, out var curve)
                && curve is CircleEntity or ArcEntity && !reference.SegmentIndex.HasValue) return reference.EntityId;
            return reference.ToString();
        }
        return key;
    }

    private static string[]? BuildNormalReferences(DrawingDocument document, string[] keys)
    {
        if (keys.Length != 2) return null;
        for (var index = 0; index < 2; index++)
        {
            if (!SketchReference.TryParse(keys[index], out var contact)
                || contact.Target is not (SketchReferenceTarget.Entity or SketchReferenceTarget.Start or SketchReferenceTarget.End)
                || !TryRelationLine(document, new(contact.EntityId, SketchReferenceTarget.Entity, contact.SegmentIndex), out var line)
                || !TryRelationCurve(document, keys[1 - index], out var curve, out var center, out var radius)
                || contact.EntityId == curve.Id.Value) continue;
            if (contact.IsEntity)
            {
                var startDistance = DistanceToRelationCurve(line.Start, curve, center, radius);
                var endDistance = DistanceToRelationCurve(line.End, curve, center, radius);
                contact = new(contact.EntityId, endDistance < startDistance ? SketchReferenceTarget.End : SketchReferenceTarget.Start,
                    contact.SegmentIndex);
            }
            return [contact.ToString(), curve.Id.Value];
        }
        return null;
    }

    private static string[]? BuildSymmetricReferences(DrawingDocument document, string[] keys)
    {
        // The third pick is the axis, including during the selection callback before
        // ActiveSelectionChanged arrives. Never run the generic active-last sorter.
        if (keys.Length != 3 || !SketchReference.TryParse(keys[2], out var axisPick)) return null;
        var axisReference = new SketchReference(axisPick.EntityId, SketchReferenceTarget.Entity, axisPick.SegmentIndex);
        if (!TryRelationLine(document, axisReference, out var axis)
            || !SketchReference.TryParse(keys[0], out var first) || !SketchReference.TryParse(keys[1], out var second)
            || first.EntityId == second.EntityId || first.EntityId == axisReference.EntityId || second.EntityId == axisReference.EntityId)
            return null;
        var axisKey = axisReference.ToString();
        if (TryRelationPoint(document, first, out _) && TryRelationPoint(document, second, out _))
            return [first.ToString(), second.ToString(), axisKey];
        if (TryRelationLine(document, first, out var firstLine) && TryRelationLine(document, second, out var secondLine))
        {
            var reflectedStart = ReflectRelationPoint(firstLine.Start, axis);
            var reflectedEnd = ReflectRelationPoint(firstLine.End, axis);
            var reverse = Distance(reflectedStart, secondLine.End) + Distance(reflectedEnd, secondLine.Start)
                < Distance(reflectedStart, secondLine.Start) + Distance(reflectedEnd, secondLine.End);
            return [Endpoint(first, false), Endpoint(second, reverse), Endpoint(first, true), Endpoint(second, !reverse), axisKey];
        }
        if (TryRelationCurve(document, keys[0], out var firstCurve, out _, out _)
            && TryRelationCurve(document, keys[1], out var secondCurve, out _, out _)
            && firstCurve.GetType() == secondCurve.GetType()) return [first.EntityId, second.EntityId, axisKey];
        if (first.IsEntity && !first.SegmentIndex.HasValue && second.IsEntity && !second.SegmentIndex.HasValue
            && TryFindEntity(document, first.EntityId, out var a) && a is PolylineEntity firstPoly
            && TryFindEntity(document, second.EntityId, out var b) && b is PolylineEntity secondPoly)
            return BuildPolylineSymmetry(firstPoly, secondPoly, axis, axisKey);
        return null;
    }

    private static string[]? BuildPolylineSymmetry(PolylineEntity first, PolylineEntity second, LineEntity axis, string axisKey)
    {
        var closed = first.Vertices.Count > 2 && first.Vertices[0] == first.Vertices[^1];
        if (first.Vertices.Count != second.Vertices.Count
            || closed != (second.Vertices.Count > 2 && second.Vertices[0] == second.Vertices[^1])
            || !ValidPolyline(first) || !ValidPolyline(second)) return null;
        var count = first.Vertices.Count - (closed ? 1 : 0);
        var best = double.PositiveInfinity;
        int[]? matching = null;
        // Only topology-preserving forward/reverse walks, with cyclic shifts for closed contours.
        foreach (var reverse in new[] { false, true })
        for (var offset = 0; offset < (closed ? count : 1); offset++)
        {
            var indices = Enumerable.Range(0, count).Select(i => closed
                ? (offset + (reverse ? count - i : i)) % count : reverse ? count - i - 1 : i).ToArray();
            var cost = Enumerable.Range(0, count).Sum(i => Distance(ReflectRelationPoint(first.Vertices[i], axis), second.Vertices[indices[i]]));
            if (cost < best) { best = cost; matching = indices; }
        }
        if (matching is null) return null;
        return Enumerable.Range(0, count).SelectMany(i => new[] { Vertex(first.Id.Value, i), Vertex(second.Id.Value, matching[i]) })
            .Append(axisKey).ToArray();
    }

    private static string Vertex(string id, int index) => index == 0 ? $"{id}|segment|0:start" : $"{id}|segment|{index - 1}:end";
    private static string Endpoint(SketchReference reference, bool end) =>
        new SketchReference(reference.EntityId, end ? SketchReferenceTarget.End : SketchReferenceTarget.Start, reference.SegmentIndex).ToString();
    private static bool FiniteRelationPoint(Point2 point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    private static bool ValidPolyline(PolylineEntity polyline) => polyline.Vertices.All(FiniteRelationPoint)
        && Enumerable.Range(1, polyline.Vertices.Count - 1).All(i => Distance(polyline.Vertices[i - 1], polyline.Vertices[i]) > GeometryTolerance);

    private static bool TryRelationLine(DrawingDocument document, SketchReference reference, out LineEntity line)
    {
        line = default!;
        if (!reference.IsEntity || !TryFindEntity(document, reference.EntityId, out var entity)
            || entity is not (LineEntity or PolylineEntity)
            || entity is LineEntity && reference.SegmentIndex.HasValue
            || entity is PolylineEntity poly && (!reference.SegmentIndex.HasValue || reference.SegmentIndex.Value >= poly.Vertices.Count - 1)
            || !SketchReferenceResolver.TryGetLine(document, reference.ToString(), out line)) return false;
        return FiniteRelationPoint(line.Start) && FiniteRelationPoint(line.End) && Distance(line.Start, line.End) > GeometryTolerance;
    }

    private static bool TryRelationPoint(DrawingDocument document, SketchReference reference, out Point2 point)
    {
        point = default;
        if (reference.IsOrigin) return true;
        if (!TryFindEntity(document, reference.EntityId, out var entity)
            || entity is not (PointEntity or LineEntity or PolylineEntity or CircleEntity or ArcEntity)
            || entity is not PolylineEntity && reference.SegmentIndex.HasValue
            || entity is PolylineEntity poly && (!reference.SegmentIndex.HasValue || reference.SegmentIndex.Value >= poly.Vertices.Count - 1)) return false;
        return SketchReferenceResolver.TryGetPoint(document, reference.ToString(), out point) && FiniteRelationPoint(point);
    }

    private static bool TryRelationCurve(DrawingDocument document, string key, out DrawingEntity curve, out Point2 center, out double radius)
    {
        curve = default!; center = default; radius = 0;
        if (!SketchReference.TryParse(key, out var reference) || !reference.IsEntity || reference.SegmentIndex.HasValue
            || !TryFindEntity(document, reference.EntityId, out curve)) return false;
        switch (curve)
        {
            case CircleEntity circle: center = circle.Center; radius = circle.Radius; break;
            case ArcEntity arc:
                var sweep = arc.EndAngleDegrees - arc.StartAngleDegrees;
                if (!double.IsFinite(arc.StartAngleDegrees) || !double.IsFinite(arc.EndAngleDegrees)
                    || sweep == 0 || sweep <= -360 || sweep > 360) return false;
                center = arc.Center; radius = arc.Radius; break;
            default: return false;
        }
        return FiniteRelationPoint(center) && double.IsFinite(radius) && radius > GeometryTolerance;
    }

    private static double DistanceToRelationCurve(Point2 point, DrawingEntity curve, Point2 center, double radius)
    {
        if (curve is ArcEntity arc)
        {
            var sweep = arc.EndAngleDegrees - arc.StartAngleDegrees;
            if (sweep < 0) sweep += 360;
            var angle = Math.Atan2(point.Y - center.Y, point.X - center.X) * 180 / Math.PI;
            var delta = ((angle - arc.StartAngleDegrees) % 360 + 360) % 360;
            if (delta > sweep)
            {
                Point2 At(double degrees) => new(center.X + radius * Math.Cos(degrees * Math.PI / 180), center.Y + radius * Math.Sin(degrees * Math.PI / 180));
                return Math.Min(Distance(point, At(arc.StartAngleDegrees)), Distance(point, At(arc.EndAngleDegrees)));
            }
        }
        return Math.Abs(Distance(point, center) - radius);
    }

    private static Point2 ReflectRelationPoint(Point2 point, LineEntity axis)
    {
        var dx = axis.End.X - axis.Start.X; var dy = axis.End.Y - axis.Start.Y;
        var t = ((point.X - axis.Start.X) * dx + (point.Y - axis.Start.Y) * dy) / (dx * dx + dy * dy);
        return new(2 * (axis.Start.X + t * dx) - point.X, 2 * (axis.Start.Y + t * dy) - point.Y);
    }
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Sketching;

/// <summary>Follows accepted geometry with existing label placement, without solving or changing intent.</summary>
public static class SketchDimensionAnchorService
{
    public static DrawingDocument FollowDimensionAnchors(DrawingDocument original, DrawingDocument candidate,
        bool preserveExplicitAnchorEdits = true)
    {
        ArgumentNullException.ThrowIfNull(original);
        ArgumentNullException.ThrowIfNull(candidate);
        var originals = original.Dimensions.ToDictionary(dimension => dimension.Id, StringComparer.Ordinal);
        var dimensions = candidate.Dimensions.Select(dimension =>
        {
            if (!originals.TryGetValue(dimension.Id, out var before) || !before.Anchor.HasValue
                || (preserveExplicitAnchorEdits && dimension.Anchor != before.Anchor) || dimension.Kind != before.Kind
                || !dimension.ReferenceKeys.SequenceEqual(before.ReferenceKeys))
                return dimension;
            var shifts = new List<Point2>();
            foreach (var key in before.ReferenceKeys)
            {
                if (!TryGetDimensionReferenceLocation(original, key, out var from)
                    || !TryGetDimensionReferenceLocation(candidate, key, out var to))
                    return dimension;
                shifts.Add(new Point2(to.X - from.X, to.Y - from.Y));
            }
            if (shifts.Count == 0) return dimension;
            var anchor = new Point2(before.Anchor.Value.X + shifts.Average(p => p.X),
                before.Anchor.Value.Y + shifts.Average(p => p.Y));
            if (before.Kind == SketchDimensionKind.LinearDistance && before.ReferenceKeys.Count == 2
                && SketchDimensionSolverService.TryResolveDimensionPoint(original.Entities, before.ReferenceKeys[0], out var a)
                && SketchDimensionSolverService.TryResolveDimensionPoint(original.Entities, before.ReferenceKeys[1], out var b)
                && SketchDimensionSolverService.TryResolveDimensionPoint(candidate.Entities, before.ReferenceKeys[0], out var c)
                && SketchDimensionSolverService.TryResolveDimensionPoint(candidate.Entities, before.ReferenceKeys[1], out var d))
            {
                var oldLength = SketchGeometryEditor.Distance(a, b);
                var newLength = SketchGeometryEditor.Distance(c, d);
                if (oldLength > SketchGeometryEditor.Tolerance && newLength > SketchGeometryEditor.Tolerance)
                {
                    var ux = (b.X - a.X) / oldLength;
                    var uy = (b.Y - a.Y) / oldLength;
                    var vx = (d.X - c.X) / newLength;
                    var vy = (d.Y - c.Y) / newLength;
                    var x = before.Anchor.Value.X - (a.X + b.X) / 2;
                    var y = before.Anchor.Value.Y - (a.Y + b.Y) / 2;
                    var cosine = ux * vx + uy * vy;
                    var sine = ux * vy - uy * vx;
                    anchor = new Point2((c.X + d.X) / 2 + x * cosine - y * sine,
                        (c.Y + d.Y) / 2 + x * sine + y * cosine);
                }
            }
            return new SketchDimension(dimension.Id, dimension.Kind, dimension.ReferenceKeys,
                dimension.Value, anchor, dimension.IsDriving);
        }).ToArray();
        return new DrawingDocument(candidate.Entities, dimensions, candidate.Constraints, candidate.Metadata, candidate.SolveEvidence);
    }

    private static bool TryGetDimensionReferenceLocation(DrawingDocument document, string key, out Point2 point)
    {
        point = default;
        if (SketchDimensionSolverService.TryResolveDimensionPoint(document.Entities, key, out point)) return true;
        if (!SketchReference.TryParse(key, out var reference)) return false;
        var entities = document.Entities;
        if (SketchGeometryEditor.TryGetLine(entities, reference, out _, out var line))
        {
            point = new Point2((line.Start.X + line.End.X) / 2, (line.Start.Y + line.End.Y) / 2);
            return true;
        }
        if (!reference.IsEntity || !SketchGeometryEditor.TryGetEntity(entities, reference, out _, out var entity)) return false;
        point = entity switch
        {
            CircleEntity circle => circle.Center,
            ArcEntity arc => arc.Center,
            EllipseEntity ellipse => ellipse.Center,
            PolygonEntity polygon => polygon.Center,
            _ => new Point2((entity.GetBounds().MinX + entity.GetBounds().MaxX) / 2,
                (entity.GetBounds().MinY + entity.GetBounds().MaxY) / 2)
        };
        return true;
    }
}

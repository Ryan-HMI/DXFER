using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;

namespace DXFER.Core.Operations;

public sealed record DuplicateGeometryResult(DrawingDocument Document, int RemovedCount, int ProtectedCount);

public static class ExactDuplicateGeometryService
{
    public static DuplicateGeometryResult Remove(DrawingDocument document)
    {
        if (document.Metadata.Mode == DrawingDocumentMode.ReferenceOnly)
            return new(document, 0, 0);

        var referenced = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in document.Dimensions.SelectMany(d => d.ReferenceKeys)
            .Concat(document.Constraints.SelectMany(c => c.ReferenceKeys)))
        {
            // Unknown references must not be orphaned by automatic cleanup.
            if (!SketchReference.TryParse(key, out var reference)) return new(document, 0, 0);
            referenced.Add(reference.EntityId);
        }

        var seen = new HashSet<GeometryKey>(new GeometryKeyComparer());
        var removed = new HashSet<string>(StringComparer.Ordinal);
        var protectedCount = 0;
        // Keep referenced geometry in preference to an unreferenced copy. Do not
        // remap endpoints/segments or collapse two independently constrained items.
        foreach (var entity in document.Entities.OrderByDescending(e => referenced.Contains(e.Id.Value)))
        {
            if (CutPathLengthService.IsExcluded(document, entity)) continue;
            document.Metadata.EntityStyles.TryGetValue(entity.Id.Value, out var style);
            if (style?.LayerName?.Contains("BEND", StringComparison.OrdinalIgnoreCase) == true
                || style?.LayerName?.Contains("ETCH", StringComparison.OrdinalIgnoreCase) == true) continue;
            var geometry = Coordinates(entity);
            if (geometry is null || geometry.Any(v => !double.IsFinite(v))) continue;
            if (seen.Add(new(entity.Kind, style ?? new DxfEntityStyle(), geometry))) continue;
            if (referenced.Contains(entity.Id.Value)) protectedCount++;
            else removed.Add(entity.Id.Value);
        }

        if (removed.Count == 0) return new(document, 0, protectedCount);
        var metadata = document.Metadata with
        {
            EntityStyles = document.Metadata.EntityStyles.Where(p => !removed.Contains(p.Key))
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal),
            Warnings = document.Metadata.Warnings.Where(w => w.Code != "exact-duplicates-removed")
                .Append(new DrawingDocumentWarning("exact-duplicates-removed", DrawingDocumentWarningSeverity.Info,
                    $"Removed {removed.Count} exact duplicate cut entities.")).ToArray()
        };
        return new(new DrawingDocument(document.Entities.Where(e => !removed.Contains(e.Id.Value)),
            document.Dimensions, document.Constraints, metadata), removed.Count, protectedCount);
    }

    private static double[]? Coordinates(DrawingEntity entity) => entity switch
    {
        LineEntity line when line.Start != line.End => Path([line.Start, line.End]),
        CircleEntity circle when circle.Radius > 0 => [circle.Center.X, circle.Center.Y, circle.Radius],
        ArcEntity arc when arc.Radius > 0 && ValidSweep(arc.EndAngleDegrees - arc.StartAngleDegrees) =>
            [arc.Center.X, arc.Center.Y, arc.Radius, Angle(arc.StartAngleDegrees), Sweep(arc.EndAngleDegrees - arc.StartAngleDegrees)],
        PolylineEntity polyline when polyline.Vertices.Count is >= 2 and <= 4096 => Path(polyline.Vertices),
        PolygonEntity polygon when polygon.Radius > 0 =>
            [polygon.Center.X, polygon.Center.Y, polygon.Radius, polygon.SideCount, Angle(polygon.RotationAngleDegrees)],
        EllipseEntity ellipse when ellipse.MinorRadiusRatio > 0 =>
            [ellipse.Center.X, ellipse.Center.Y, ellipse.MajorAxisEndPoint.X, ellipse.MajorAxisEndPoint.Y,
                ellipse.MinorRadiusRatio, ellipse.StartParameterDegrees, ellipse.EndParameterDegrees],
        SplineEntity spline => SplineCoordinates(spline),
        _ => null
    };

    private static double[] SplineCoordinates(SplineEntity spline)
    {
        var values = new List<double> { spline.Degree, spline.ControlPoints.Count };
        AddPoints(values, spline.ControlPoints);
        values.Add(spline.Knots.Count);
        values.AddRange(spline.Knots);
        values.Add(spline.Weights.Count);
        values.AddRange(spline.Weights);
        values.Add(spline.FitPoints.Count);
        AddPoints(values, spline.FitPoints);
        foreach (var tangent in new[] { spline.StartTangentHandle, spline.EndTangentHandle })
        {
            values.Add(tangent.HasValue ? 1 : 0);
            if (tangent is { } point) AddPoints(values, [point]);
        }
        return values.ToArray();
    }

    private static void AddPoints(List<double> values, IEnumerable<Point2> points)
    {
        foreach (var point in points) { values.Add(point.X); values.Add(point.Y); }
    }

    // Canonicalize only vertex order, never coordinates, tolerances or segmentation.
    private static double[]? Path(IReadOnlyList<Point2> points)
    {
        var closed = points.Count > 2 && points[0] == points[^1];
        var count = closed ? points.Count - 1 : points.Count;
        var start = 0;
        if (closed)
            for (var i = 1; i < count; i++)
                if (Compare(points[i], points[start]) < 0) start = i;
        // Retraced/self-touching paths need review, not quadratic canonicalization.
        if (closed && points.Take(count).Count(p => p == points[start]) != 1) return null;
        Point2[]? best = null;
        for (var i = 0; i < (closed ? count : 1); i++)
        {
            if (closed && points[i] != points[start]) continue;
            foreach (var direction in new[] { 1, -1 })
            {
                var offset = closed ? i : direction == 1 ? 0 : count - 1;
                var candidate = Enumerable.Range(0, count)
                    .Select(j => points[(offset + direction * j + count) % count]).ToArray();
                if (best is null || ComparePaths(candidate, best) < 0) best = candidate;
            }
        }
        var values = new List<double> { closed ? 1 : 0, count };
        AddPoints(values, best!);
        return values.ToArray();
    }

    private static int Compare(Point2 a, Point2 b)
    {
        var x = a.X.CompareTo(b.X);
        return x != 0 ? x : a.Y.CompareTo(b.Y);
    }
    private static int ComparePaths(Point2[] a, Point2[] b)
    {
        for (var i = 0; i < a.Length; i++)
        {
            var comparison = Compare(a[i], b[i]);
            if (comparison != 0) return comparison;
        }
        return 0;
    }

    private static bool ValidSweep(double sweep) => sweep != 0 && sweep > -360 && sweep <= 360;
    private static double Sweep(double sweep) => sweep < 0 ? sweep + 360 : sweep;
    private static double Angle(double angle)
    {
        var remainder = angle % 360;
        return remainder < 0 ? remainder + 360 : remainder;
    }
    private sealed record GeometryKey(string Kind, DxfEntityStyle Style, double[] Coordinates);
    private sealed class GeometryKeyComparer : IEqualityComparer<GeometryKey>
    {
        public bool Equals(GeometryKey? a, GeometryKey? b) => a is not null && b is not null
            && a.Kind == b.Kind && a.Style == b.Style && a.Coordinates.SequenceEqual(b.Coordinates);
        public int GetHashCode(GeometryKey key)
        {
            var hash = new HashCode();
            hash.Add(key.Kind);
            hash.Add(key.Style);
            foreach (var value in key.Coordinates) hash.Add(value);
            return hash.ToHashCode();
        }
    }
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Operations;

public sealed record CutPathLengthResult(decimal? CutLengthInches, string? CutLengthReviewReason);

public static class CutPathLengthService
{
    public const string UnsafeImportCode = "cut-length-unsafe";

    public static bool IsNonCutLayer(string? layer) =>
        string.Equals(layer?.Trim(), "GRAIN", StringComparison.OrdinalIgnoreCase)
        || string.Equals(layer?.Trim(), "CONSTRUCTION", StringComparison.OrdinalIgnoreCase);

    public static bool IsAnnotation(string type) =>
        type is "POINT" or "DIMENSION" or "TEXT" or "MTEXT" or "LEADER" or "MLEADER";

    public static CutPathLengthResult Calculate(DrawingDocument document)
    {
        var scale = document.Metadata.Units switch
        {
            DrawingUnits.Inches => 1d,
            DrawingUnits.Millimeters => 1d / 25.4,
            _ => 0d
        };
        if (scale == 0)
            return Review("Drawing units must be explicit inches or millimeters.");
        var unsafeImport = document.Metadata.Warnings.FirstOrDefault(w => w.Code == UnsafeImportCode);
        if (unsafeImport is not null)
            return Review(unsafeImport.Message);
        var unsupported = document.Metadata.UnsupportedEntityCounts
            .Where(pair => pair.Value > 0 && !IsAnnotation(pair.Key.ToUpperInvariant())).Select(pair => pair.Key).ToArray();
        if (unsupported.Length > 0)
            return Review($"Unsupported DXF entities: {string.Join(", ", unsupported)}.");

        double total = 0;
        foreach (var entity in document.Entities)
        {
            document.Metadata.EntityStyles.TryGetValue(entity.Id.Value, out var style);
            if (entity.IsConstruction || entity is PointEntity || IsNonCutLayer(style?.LayerName))
                continue;
            if (style?.LayerName?.Contains("BEND", StringComparison.OrdinalIgnoreCase) == true
                || style?.LayerName?.Contains("ETCH", StringComparison.OrdinalIgnoreCase) == true)
                return Review("Bend or etch geometry requires cut-layer review.");

            double length;
            switch (entity)
            {
                case LineEntity line when Finite(line.Start) && Finite(line.End):
                    length = MeasurementService.Measure(line.Start, line.End).Distance;
                    break;
                case CircleEntity circle when Finite(circle.Center) && Positive(circle.Radius):
                    length = Math.Tau * circle.Radius;
                    break;
                case ArcEntity arc when Finite(arc.Center) && Positive(arc.Radius)
                    && double.IsFinite(arc.StartAngleDegrees) && double.IsFinite(arc.EndAngleDegrees):
                    var sweep = arc.EndAngleDegrees - arc.StartAngleDegrees;
                    if (Math.Abs(sweep) > 360 || sweep == 0 || sweep == -360)
                        return Review("Ambiguous or multi-turn arc sweep.");
                    if (sweep < 0) sweep += 360;
                    length = arc.Radius * sweep * Math.PI / 180;
                    break;
                case PolylineEntity polyline:
                    length = PathLength(polyline.Vertices);
                    break;
                case PolygonEntity polygon when Finite(polygon.Center) && Positive(polygon.Radius)
                    && double.IsFinite(polygon.RotationAngleDegrees)
                    && polygon.SideCount is >= PolygonEntity.MinSideCount and <= PolygonEntity.MaxSideCount:
                    var vertices = polygon.GetVertices();
                    length = PathLength(vertices) + MeasurementService.Measure(vertices[^1], vertices[0]).Distance;
                    break;
                case EllipseEntity ellipse when Finite(ellipse.Center) && Finite(ellipse.MajorAxisEndPoint)
                    && Positive(ellipse.MinorRadiusRatio) && ellipse.MinorRadiusRatio <= 1
                    && double.IsFinite(ellipse.StartParameterDegrees) && double.IsFinite(ellipse.EndParameterDegrees)
                    && Math.Abs(ellipse.EndParameterDegrees - ellipse.StartParameterDegrees) <= 360.000001:
                    length = EllipseLength(ellipse, scale);
                    break;
                case SplineEntity:
                    return Review("Spline cut length requires review; existing fit/sampling has no guaranteed length tolerance.");
                default:
                    return Review($"Unsupported or invalid {entity.Kind} cut geometry.");
            }

            if (!Positive(length))
                return Review($"Invalid, degenerate, or unconverged {entity.Kind} cut geometry.");
            total += length * scale;
            if (!double.IsFinite(total) || total >= (double)decimal.MaxValue)
                return Review("Cut length exceeds the supported numeric range.");
        }
        if (total <= 0)
            return Review("No measurable cut paths.");
        var result = (decimal)total;
        return result > 0 ? new(result, null) : Review("Cut length is below the supported numeric range.");
    }

    private static double EllipseLength(EllipseEntity ellipse, double inchesPerUnit)
    {
        // Smooth ellipses converge quadratically under chord refinement. Cap both
        // eccentricity and work; display samples alone are not a quoting tolerance.
        if (ellipse.MinorRadiusRatio < 0.001) return double.NaN;
        var previous = PathLength(ellipse.GetSamplePoints(64));
        for (var count = 128; count <= 32768; count *= 2)
        {
            var current = PathLength(ellipse.GetSamplePoints(count));
            if (!Positive(current)) return double.NaN;
            if (Math.Abs(current - previous) * inchesPerUnit <= 0.000001)
                return current;
            previous = current;
        }
        return double.NaN;
    }

    private static double PathLength(IReadOnlyList<Point2> points)
    {
        if (points.Count < 2 || points.Any(point => !Finite(point))) return double.NaN;
        double length = 0;
        for (var index = 1; index < points.Count; index++)
            length += MeasurementService.Measure(points[index - 1], points[index]).Distance;
        return length;
    }

    private static bool Finite(Point2 point) => double.IsFinite(point.X) && double.IsFinite(point.Y);
    private static bool Positive(double value) => double.IsFinite(value) && value > 0;
    private static CutPathLengthResult Review(string reason) => new(null, reason);
}

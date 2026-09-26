using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using NetTopologySuite.Geometries;
using NetTopologySuite.Precision;

namespace DXFER.Core.Operations;

public sealed record ClosedContourResult(int? ClosedContourCount, string? ContourReviewReason);

public static class ClosedContourService
{
    public const double JoinToleranceInches = 0.00001;
    private const double CurveDeviationInches = JoinToleranceInches / 8;
    private const int MaximumPaths = 512;
    private const int MaximumVertices = 32768;
    private const double MaximumCoordinateInches = 1000000;

    public static ClosedContourResult Calculate(DrawingDocument document)
    {
        var entities = document.Entities.Where(entity => !CutPathLengthService.IsExcluded(document, entity))
            .Take(MaximumPaths + 1).ToArray();
        if (entities.Length > MaximumPaths || entities.OfType<PolylineEntity>().Sum(p => (long)p.Vertices.Count) > MaximumVertices)
            return Review("Contour geometry exceeds the work limit.");
        var perimeter = CutPathLengthService.Calculate(document);
        if (perimeter.CutLengthInches is null)
            return Review(perimeter.CutLengthReviewReason ?? "Cut geometry requires review.");

        var scale = document.Metadata.Units == DrawingUnits.Millimeters ? 1 / 25.4 : 1;
        var paths = new List<Coordinate[]>();
        var vertexCount = 0;
        foreach (var entity in entities)
        {
            IReadOnlyList<Point2>? points;
            switch (entity)
            {
                case LineEntity line:
                    points = [line.Start, line.End];
                    break;
                case PolylineEntity polyline:
                    points = polyline.Vertices;
                    break;
                case PolygonEntity polygon:
                    var vertices = polygon.GetVertices();
                    points = vertices.Append(vertices[0]).ToArray();
                    break;
                case CircleEntity circle:
                    points = SampleCurve(Math.Tau, circle.Radius * scale,
                        count => new EllipseEntity(circle.Id, circle.Center, new(circle.Radius, 0), 1).GetSamplePoints(count));
                    break;
                case ArcEntity arc:
                    var sweep = arc.EndAngleDegrees - arc.StartAngleDegrees;
                    if (sweep < 0) sweep += 360;
                    points = SampleCurve(sweep * Math.PI / 180, arc.Radius * scale, arc.GetSamplePoints);
                    break;
                case EllipseEntity ellipse:
                    var ellipseSweep = ellipse.EndParameterDegrees - ellipse.StartParameterDegrees;
                    if (Math.Abs(ellipseSweep) > 0.000001 && Math.Abs(ellipseSweep - 360) > 0.000001)
                        return Review("Partial ellipse contours require review.");
                    var radius = MeasurementService.Measure(new(0, 0), ellipse.MajorAxisEndPoint).Distance;
                    points = SampleCurve(Math.Tau, radius * scale, ellipse.GetSamplePoints);
                    break;
                default:
                    return Review($"Unsupported {entity.Kind} contour geometry.");
            }

            if (points is null || (vertexCount += points.Count) > MaximumVertices)
                return Review("Contour curve approximation exceeds the vertex/work limit.");
            var coordinates = points.Select(point => new Coordinate(point.X * scale, point.Y * scale)).ToArray();
            if (coordinates.Length < 2 || coordinates.Any(point => !double.IsFinite(point.X) || !double.IsFinite(point.Y)
                || Math.Abs(point.X) > MaximumCoordinateInches || Math.Abs(point.Y) > MaximumCoordinateInches))
                return Review("Contour coordinates exceed the supported range.");
            for (var index = 1; index < coordinates.Length; index++)
                if (coordinates[index - 1].Distance(coordinates[index]) <= JoinToleranceInches)
                    return Review("Degenerate or sub-tolerance contour segment requires review.");
            paths.Add(coordinates);
        }

        // Unique endpoint pairs avoid order-dependent snapping and transitive
        // clusters. Endpoints can pair with their own path to close a ring.
        var endpoints = paths.SelectMany(path => new[] { path[0], path[^1] }).ToArray();
        var partners = new int[endpoints.Length];
        for (var index = 0; index < endpoints.Length; index++)
        {
            var partner = -1;
            for (var candidate = 0; candidate < endpoints.Length; candidate++)
            {
                if (candidate == index || endpoints[index].Distance(endpoints[candidate]) > JoinToleranceInches) continue;
                if (partner != -1)
                    return Review("Branched or ambiguous contour endpoints require review.");
                partner = candidate;
            }
            if (partner == -1) return Review("Open contour endpoints require review.");
            partners[index] = partner;
        }

        for (var index = 0; index < endpoints.Length; index++)
        {
            var first = endpoints[index];
            var second = endpoints[partners[index]];
            var midpoint = new Coordinate((first.X + second.X) / 2, (first.Y + second.Y) / 2);
            var path = paths[index / 2];
            path[index % 2 == 0 ? 0 : path.Length - 1] = midpoint;
        }

        var factory = new GeometryFactory();
        var rings = new List<LineString>();
        var visited = new bool[paths.Count];
        for (var start = 0; start < paths.Count; start++)
        {
            if (visited[start]) continue;
            var coordinates = new List<Coordinate>();
            var endpoint = start * 2;
            do
            {
                var pathIndex = endpoint / 2;
                if (visited[pathIndex]) return Review("Ambiguous contour traversal requires review.");
                visited[pathIndex] = true;
                var path = paths[pathIndex];
                var oriented = endpoint % 2 == 0 ? path : path.Reverse().ToArray();
                coordinates.AddRange(coordinates.Count == 0 ? oriented : oriented.Skip(1));
                endpoint = partners[endpoint ^ 1];
            } while (endpoint != start * 2);
            if (coordinates.Count < 4) return Review("Degenerate closed contour requires review.");
            rings.Add(factory.CreateLineString(coordinates.ToArray()));
        }

        try
        {
            var linework = factory.CreateMultiLineString(rings.ToArray());
            if (!linework.IsSimple)
                return Review("Crossing, touching, duplicated or overlapping contours require review.");
            // Curve chords and endpoint snapping perturb the original geometry.
            // Reject near contacts rather than asserting topology below that margin.
            if (MinimumClearance.GetDistance(linework) <= 4 * JoinToleranceInches)
                return Review("Contour clearance is too small for a reliable count.");
        }
        catch (TopologyException)
        {
            return Review("Contour topology could not be validated.");
        }
        return rings.Count > 0 ? new(rings.Count, null) : Review("No closed cut contours.");
    }

    private static IReadOnlyList<Point2>? SampleCurve(double sweepRadians, double maximumRadiusInches,
        Func<int, IReadOnlyList<Point2>> sample)
    {
        // For ellipse/circle parametrization, |second derivative| <= major
        // radius; linear interpolation deviation is bounded by M * step^2 / 8.
        var segments = Math.Ceiling(sweepRadians * Math.Sqrt(maximumRadiusInches / (8 * CurveDeviationInches)));
        if (!double.IsFinite(segments) || segments > MaximumVertices - 1) return null;
        return sample(Math.Max(16, (int)segments));
    }

    private static ClosedContourResult Review(string reason) => new(null, reason);
}

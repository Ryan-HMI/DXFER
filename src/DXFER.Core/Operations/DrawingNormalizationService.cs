using DXFER.Core.Documents;
using DXFER.Core.Geometry;

namespace DXFER.Core.Operations;

public static class DrawingNormalizationService
{
    private const double GeometryTolerance = 0.000001;
    private const double ComparisonTolerance = 0.0001;
    private const int CircleSampleCount = 72;
    private const int ArcSampleCount = 72;
    private const int EllipseSampleCount = 96;

    public static DrawingNormalizationResult AutoNormalize(
        DrawingDocument document,
        bool manualOverride = false)
    {
        ArgumentNullException.ThrowIfNull(document);

        var originalBounds = document.GetBounds();
        var samplePoints = GetDocumentSamplePoints(document).ToArray();
        var rotation = ChooseMinimumAreaRotation(samplePoints, originalBounds);
        var center = GetBoundsCenter(originalBounds);
        var rotatedDocument = DrawingPrepService.Transform(
            document,
            Transform2.RotationDegreesAbout(rotation, center));
        var rotatedBounds = rotatedDocument.GetBounds();
        var originShiftX = CleanNearZero(-rotatedBounds.MinX);
        var originShiftY = CleanNearZero(-rotatedBounds.MinY);
        var normalizedDocument = DrawingPrepService.Transform(
            rotatedDocument,
            Transform2.Translation(originShiftX, originShiftY));
        var normalizedBounds = normalizedDocument.GetBounds();

        return new DrawingNormalizationResult(
            document,
            rotatedDocument,
            normalizedDocument,
            originalBounds,
            rotatedBounds,
            normalizedBounds,
            CleanNearZero(rotation),
            originShiftX,
            originShiftY,
            manualOverride);
    }

    private static double ChooseMinimumAreaRotation(
        IReadOnlyList<Point2> points,
        Bounds2 originalBounds)
    {
        if (points.Count < 2)
        {
            return 0;
        }

        var center = GetBoundsCenter(originalBounds);
        var best = EvaluateCandidate(points, center, 0);
        foreach (var candidate in GetCandidateRotations(points))
        {
            var evaluated = EvaluateCandidate(points, center, candidate);
            if (IsBetter(evaluated, best))
            {
                best = evaluated;
            }
        }

        return best.RotationDegrees;
    }

    private static IEnumerable<double> GetCandidateRotations(IReadOnlyList<Point2> points)
    {
        yield return 0;

        var hull = GetConvexHull(points).ToArray();
        if (hull.Length < 2)
        {
            yield break;
        }

        var seen = new List<double>();
        for (var index = 0; index < hull.Length; index++)
        {
            var start = hull[index];
            var end = hull[(index + 1) % hull.Length];
            var deltaX = end.X - start.X;
            var deltaY = end.Y - start.Y;
            if (GetDistance(deltaX, deltaY) <= GeometryTolerance)
            {
                continue;
            }

            var angle = Math.Atan2(deltaY, deltaX) * 180.0 / Math.PI;
            foreach (var rotation in new[]
            {
                NormalizeHalfTurnDegrees(-angle),
                NormalizeHalfTurnDegrees(90 - angle)
            })
            {
                if (seen.Any(value => Math.Abs(value - rotation) <= GeometryTolerance))
                {
                    continue;
                }

                seen.Add(rotation);
                yield return rotation;
            }
        }
    }

    private static CandidateRotation EvaluateCandidate(
        IReadOnlyList<Point2> points,
        Point2 center,
        double rotationDegrees)
    {
        var transform = Transform2.RotationDegreesAbout(rotationDegrees, center);
        var bounds = Bounds2.FromPoints(points.Select(point => point.Transform(transform)));
        return new CandidateRotation(
            rotationDegrees,
            Math.Abs(bounds.Width * bounds.Height),
            bounds.Width >= bounds.Height - ComparisonTolerance,
            Math.Max(Math.Abs(bounds.Width), Math.Abs(bounds.Height)));
    }

    private static bool IsBetter(CandidateRotation candidate, CandidateRotation current)
    {
        if (candidate.Area < current.Area - ComparisonTolerance)
        {
            return true;
        }

        if (candidate.Area > current.Area + ComparisonTolerance)
        {
            return false;
        }

        if (candidate.LongSideOnX != current.LongSideOnX)
        {
            return candidate.LongSideOnX;
        }

        if (candidate.MaxDimension < current.MaxDimension - ComparisonTolerance)
        {
            return true;
        }

        if (candidate.MaxDimension > current.MaxDimension + ComparisonTolerance)
        {
            return false;
        }

        return Math.Abs(candidate.RotationDegrees) < Math.Abs(current.RotationDegrees) - ComparisonTolerance;
    }

    private static IEnumerable<Point2> GetDocumentSamplePoints(DrawingDocument document)
    {
        foreach (var entity in document.Entities)
        {
            foreach (var point in GetEntitySamplePoints(entity))
            {
                yield return point;
            }
        }
    }

    private static IEnumerable<Point2> GetEntitySamplePoints(DrawingEntity entity) =>
        entity switch
        {
            LineEntity line => new[] { line.Start, line.End },
            PolylineEntity polyline => polyline.Vertices,
            PolygonEntity polygon => polygon.GetVertices(),
            CircleEntity circle => SampleCircle(circle),
            ArcEntity arc => arc.GetSamplePoints(ArcSampleCount),
            EllipseEntity ellipse => ellipse.GetSamplePoints(EllipseSampleCount),
            SplineEntity spline => spline.GetSamplePoints(),
            PointEntity point => new[] { point.Location },
            _ => Array.Empty<Point2>()
        };

    private static IReadOnlyList<Point2> SampleCircle(CircleEntity circle) =>
        Enumerable.Range(0, CircleSampleCount)
            .Select(index =>
            {
                var radians = Math.Tau * index / CircleSampleCount;
                return new Point2(
                    circle.Center.X + Math.Cos(radians) * circle.Radius,
                    circle.Center.Y + Math.Sin(radians) * circle.Radius);
            })
            .ToArray();

    private static IEnumerable<Point2> GetConvexHull(IReadOnlyList<Point2> points)
    {
        var ordered = points
            .Distinct()
            .OrderBy(point => point.X)
            .ThenBy(point => point.Y)
            .ToArray();
        if (ordered.Length <= 1)
        {
            return ordered;
        }

        var lower = new List<Point2>();
        foreach (var point in ordered)
        {
            while (lower.Count >= 2 && Cross(lower[^2], lower[^1], point) <= GeometryTolerance)
            {
                lower.RemoveAt(lower.Count - 1);
            }

            lower.Add(point);
        }

        var upper = new List<Point2>();
        for (var index = ordered.Length - 1; index >= 0; index--)
        {
            var point = ordered[index];
            while (upper.Count >= 2 && Cross(upper[^2], upper[^1], point) <= GeometryTolerance)
            {
                upper.RemoveAt(upper.Count - 1);
            }

            upper.Add(point);
        }

        lower.RemoveAt(lower.Count - 1);
        upper.RemoveAt(upper.Count - 1);
        return lower.Concat(upper);
    }

    private static double Cross(Point2 origin, Point2 a, Point2 b) =>
        ((a.X - origin.X) * (b.Y - origin.Y)) - ((a.Y - origin.Y) * (b.X - origin.X));

    private static Point2 GetBoundsCenter(Bounds2 bounds) =>
        new(
            bounds.MinX + bounds.Width / 2.0,
            bounds.MinY + bounds.Height / 2.0);

    private static double NormalizeHalfTurnDegrees(double angle)
    {
        var normalized = angle % 180.0;
        if (normalized > 90.0)
        {
            normalized -= 180.0;
        }
        else if (normalized <= -90.0)
        {
            normalized += 180.0;
        }

        return CleanNearZero(normalized);
    }

    private static double GetDistance(double x, double y) => Math.Sqrt((x * x) + (y * y));

    private static double CleanNearZero(double value) =>
        Math.Abs(value) <= GeometryTolerance ? 0 : value;

    private readonly record struct CandidateRotation(
        double RotationDegrees,
        double Area,
        bool LongSideOnX,
        double MaxDimension);
}

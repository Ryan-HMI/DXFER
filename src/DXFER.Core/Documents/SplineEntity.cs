using DXFER.Core.Geometry;

namespace DXFER.Core.Documents;

public sealed record SplineEntity : DrawingEntity
{
    private const int MinimumSampleIntervals = 24;
    private const int SamplesPerKnotSpan = 16;
    private const int MaximumSampleIntervals = 256;

    public SplineEntity(
        EntityId id,
        int degree,
        IEnumerable<Point2> controlPoints,
        IEnumerable<double> knots,
        IEnumerable<double>? weights = null,
        bool isConstruction = false)
        : base(id, isConstruction)
    {
        ArgumentNullException.ThrowIfNull(controlPoints);
        ArgumentNullException.ThrowIfNull(knots);

        if (degree < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(degree), "A spline degree must be at least one.");
        }

        var controls = controlPoints.ToArray();
        if (controls.Length < degree + 1)
        {
            throw new ArgumentException("A spline requires at least degree + 1 control points.", nameof(controlPoints));
        }

        var knotValues = NormalizeKnots(knots.ToArray(), controls.Length, degree);
        var weightValues = NormalizeWeights(weights?.ToArray(), controls.Length);

        Degree = degree;
        ControlPoints = Array.AsReadOnly(controls);
        Knots = Array.AsReadOnly(knotValues);
        Weights = Array.AsReadOnly(weightValues);
    }

    public override string Kind => "spline";

    public int Degree { get; }

    public IReadOnlyList<Point2> ControlPoints { get; }

    public IReadOnlyList<double> Knots { get; }

    public IReadOnlyList<double> Weights { get; }

    public override Bounds2 GetBounds() => Bounds2.FromPoints(GetSamplePoints());

    public override DrawingEntity Transform(Transform2 transform) =>
        new SplineEntity(
            Id,
            Degree,
            ControlPoints.Select(point => point.Transform(transform)),
            Knots,
            Weights,
            IsConstruction);

    public override DrawingEntity WithConstruction(bool isConstruction) =>
        new SplineEntity(Id, Degree, ControlPoints, Knots, Weights, isConstruction);

    public IReadOnlyList<Point2> GetSamplePoints()
    {
        var domainStart = Knots[Degree];
        var domainEnd = Knots[ControlPoints.Count];
        if (!double.IsFinite(domainStart) || !double.IsFinite(domainEnd) || domainEnd <= domainStart)
        {
            return ControlPoints;
        }

        var spanCount = CountKnotSpans();
        var sampleIntervals = Math.Clamp(spanCount * SamplesPerKnotSpan, MinimumSampleIntervals, MaximumSampleIntervals);
        var points = new List<Point2>(sampleIntervals + 1);

        for (var sampleIndex = 0; sampleIndex <= sampleIntervals; sampleIndex++)
        {
            var parameter = sampleIndex == sampleIntervals
                ? domainEnd
                : domainStart + (domainEnd - domainStart) * sampleIndex / sampleIntervals;
            AddSampledPoint(points, Evaluate(parameter));
        }

        return points.Count >= 2 ? points : ControlPoints;
    }

    private Point2 Evaluate(double parameter)
    {
        var span = FindKnotSpan(parameter);
        var weightedPoints = new WeightedPoint[Degree + 1];

        for (var index = 0; index <= Degree; index++)
        {
            var controlIndex = span - Degree + index;
            var weight = Weights[controlIndex];
            weightedPoints[index] = new WeightedPoint(
                ControlPoints[controlIndex].X * weight,
                ControlPoints[controlIndex].Y * weight,
                weight);
        }

        for (var r = 1; r <= Degree; r++)
        {
            for (var index = Degree; index >= r; index--)
            {
                var knotIndex = span - Degree + index;
                var denominator = Knots[knotIndex + Degree - r + 1] - Knots[knotIndex];
                var alpha = Math.Abs(denominator) <= double.Epsilon
                    ? 0
                    : (parameter - Knots[knotIndex]) / denominator;

                weightedPoints[index] = WeightedPoint.Lerp(weightedPoints[index - 1], weightedPoints[index], alpha);
            }
        }

        var result = weightedPoints[Degree];
        return Math.Abs(result.Weight) <= double.Epsilon
            ? new Point2(result.X, result.Y)
            : new Point2(result.X / result.Weight, result.Y / result.Weight);
    }

    private int FindKnotSpan(double parameter)
    {
        var controlPointMaxIndex = ControlPoints.Count - 1;
        if (parameter >= Knots[controlPointMaxIndex + 1])
        {
            return controlPointMaxIndex;
        }

        if (parameter <= Knots[Degree])
        {
            return Degree;
        }

        var low = Degree;
        var high = controlPointMaxIndex + 1;
        var middle = (low + high) / 2;

        while (parameter < Knots[middle] || parameter >= Knots[middle + 1])
        {
            if (parameter < Knots[middle])
            {
                high = middle;
            }
            else
            {
                low = middle;
            }

            middle = (low + high) / 2;
        }

        return middle;
    }

    private int CountKnotSpans()
    {
        var spans = 0;
        for (var index = Degree; index < ControlPoints.Count; index++)
        {
            if (Knots[index + 1] > Knots[index])
            {
                spans++;
            }
        }

        return Math.Max(1, spans);
    }

    private static double[] NormalizeKnots(IReadOnlyList<double> knots, int controlPointCount, int degree)
    {
        if (knots.Count >= controlPointCount + degree + 1
            && knots[degree] < knots[controlPointCount])
        {
            return knots.Take(controlPointCount + degree + 1).ToArray();
        }

        return CreateOpenUniformKnots(controlPointCount, degree).ToArray();
    }

    private static double[] NormalizeWeights(IReadOnlyList<double>? weights, int controlPointCount)
    {
        if (weights is { Count: var count } && count == controlPointCount)
        {
            return weights.ToArray();
        }

        return Enumerable.Repeat(1d, controlPointCount).ToArray();
    }

    private static IEnumerable<double> CreateOpenUniformKnots(int controlPointCount, int degree)
    {
        var knotCount = controlPointCount + degree + 1;
        var interiorCount = knotCount - (degree + 1) * 2;

        for (var index = 0; index <= degree; index++)
        {
            yield return 0;
        }

        for (var index = 1; index <= interiorCount; index++)
        {
            yield return (double)index / (interiorCount + 1);
        }

        for (var index = 0; index <= degree; index++)
        {
            yield return 1;
        }
    }

    private static void AddSampledPoint(ICollection<Point2> points, Point2 point)
    {
        if (points.Count > 0)
        {
            var previous = points.Last();
            if (Math.Abs(previous.X - point.X) <= 0.000001
                && Math.Abs(previous.Y - point.Y) <= 0.000001)
            {
                return;
            }
        }

        points.Add(point);
    }

    private readonly record struct WeightedPoint(double X, double Y, double Weight)
    {
        public static WeightedPoint Lerp(WeightedPoint start, WeightedPoint end, double alpha) =>
            new(
                start.X + (end.X - start.X) * alpha,
                start.Y + (end.Y - start.Y) * alpha,
                start.Weight + (end.Weight - start.Weight) * alpha);
    }
}

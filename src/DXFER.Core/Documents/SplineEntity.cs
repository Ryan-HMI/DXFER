using DXFER.Core.Geometry;

namespace DXFER.Core.Documents;

public sealed record SplineEntity : DrawingEntity
{
    private const int MinimumSampleIntervals = 24;
    private const int SamplesPerKnotSpan = 16;
    private const int MaximumSampleIntervals = 256;

    private readonly Lazy<IReadOnlyList<Point2>> _samplePoints;

    public SplineEntity(
        EntityId id,
        int degree,
        IEnumerable<Point2> controlPoints,
        IEnumerable<double> knots,
        IEnumerable<double>? weights = null,
        bool isConstruction = false,
        IEnumerable<Point2>? fitPoints = null)
        : base(id, isConstruction)
    {
        ArgumentNullException.ThrowIfNull(controlPoints);
        ArgumentNullException.ThrowIfNull(knots);

        if (degree < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(degree), "A spline degree must be at least one.");
        }

        var controls = controlPoints.ToArray();
        var fits = fitPoints?.ToArray() ?? Array.Empty<Point2>();
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
        FitPoints = Array.AsReadOnly(fits);
        _samplePoints = new Lazy<IReadOnlyList<Point2>>(BuildSamplePoints);
    }

    public override string Kind => "spline";

    public int Degree { get; }

    public IReadOnlyList<Point2> ControlPoints { get; }

    public IReadOnlyList<double> Knots { get; }

    public IReadOnlyList<double> Weights { get; }

    public IReadOnlyList<Point2> FitPoints { get; }

    public static SplineEntity FromFitPoints(
        EntityId id,
        IEnumerable<Point2> fitPoints,
        bool isConstruction = false)
    {
        ArgumentNullException.ThrowIfNull(fitPoints);

        var points = fitPoints.ToArray();
        if (points.Length < 2)
        {
            throw new ArgumentException("A fit-point spline requires at least two points.", nameof(fitPoints));
        }

        var degree = Math.Min(3, points.Length - 1);
        return new SplineEntity(
            id,
            degree,
            points,
            Array.Empty<double>(),
            isConstruction: isConstruction,
            fitPoints: points);
    }

    public override Bounds2 GetBounds() => Bounds2.FromPoints(GetSamplePoints());

    public override DrawingEntity Transform(Transform2 transform) =>
        new SplineEntity(
            Id,
            Degree,
            ControlPoints.Select(point => point.Transform(transform)),
            Knots,
            Weights,
            IsConstruction,
            FitPoints.Select(point => point.Transform(transform)));

    public override DrawingEntity WithConstruction(bool isConstruction) =>
        new SplineEntity(Id, Degree, ControlPoints, Knots, Weights, isConstruction, FitPoints);

    public IReadOnlyList<Point2> GetSamplePoints() => _samplePoints.Value;

    private IReadOnlyList<Point2> BuildSamplePoints()
    {
        if (FitPoints.Count >= 2)
        {
            return GetFitSamplePoints(FitPoints);
        }

        if (Degree == 1)
        {
            return ControlPoints.ToArray();
        }

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

    internal IEnumerable<(double Start, double End)> GetKnotParameterSpans()
    {
        if (FitPoints.Count >= 2)
        {
            yield break;
        }

        for (var index = Degree; index < ControlPoints.Count; index++)
        {
            var start = Knots[index];
            var end = Knots[index + 1];
            if (double.IsFinite(start) && double.IsFinite(end) && end > start)
            {
                yield return (start, end);
            }
        }
    }

    internal bool TryEvaluateKnotParameter(double parameter, out Point2 point)
    {
        if (FitPoints.Count >= 2 || Knots.Count <= ControlPoints.Count)
        {
            point = default;
            return false;
        }

        var domainStart = Knots[Degree];
        var domainEnd = Knots[ControlPoints.Count];
        if (!double.IsFinite(domainStart) || !double.IsFinite(domainEnd) || domainEnd <= domainStart)
        {
            point = default;
            return false;
        }

        point = Evaluate(Math.Clamp(parameter, domainStart, domainEnd));
        return true;
    }

    private static IReadOnlyList<Point2> GetFitSamplePoints(IReadOnlyList<Point2> fitPoints)
    {
        if (fitPoints.Count == 2)
        {
            return fitPoints.ToArray();
        }

        var samples = new List<Point2>(Math.Min(
            MaximumSampleIntervals + 1,
            ((fitPoints.Count - 1) * SamplesPerKnotSpan) + 1));
        for (var index = 0; index < fitPoints.Count - 1; index++)
        {
            var previous = index == 0 ? fitPoints[index] : fitPoints[index - 1];
            var start = fitPoints[index];
            var end = fitPoints[index + 1];
            var next = index + 2 < fitPoints.Count ? fitPoints[index + 2] : end;

            for (var step = 0; step <= SamplesPerKnotSpan; step++)
            {
                if (index > 0 && step == 0)
                {
                    continue;
                }

                if (step == 0)
                {
                    samples.Add(start);
                }
                else if (step == SamplesPerKnotSpan)
                {
                    samples.Add(end);
                }
                else
                {
                    samples.Add(EvaluateCatmullRom(previous, start, end, next, (double)step / SamplesPerKnotSpan));
                }
            }
        }

        return samples;
    }

    private static Point2 EvaluateCatmullRom(Point2 previous, Point2 start, Point2 end, Point2 next, double t)
    {
        var t2 = t * t;
        var t3 = t2 * t;
        return new Point2(
            0.5 * ((2 * start.X)
                + (-previous.X + end.X) * t
                + ((2 * previous.X) - (5 * start.X) + (4 * end.X) - next.X) * t2
                + (-previous.X + (3 * start.X) - (3 * end.X) + next.X) * t3),
            0.5 * ((2 * start.Y)
                + (-previous.Y + end.Y) * t
                + ((2 * previous.Y) - (5 * start.Y) + (4 * end.Y) - next.Y) * t2
                + (-previous.Y + (3 * start.Y) - (3 * end.Y) + next.Y) * t3));
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

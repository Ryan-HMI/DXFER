using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class MeasurementSpanTests
{
    private static readonly EntityId A = EntityId.Create("a");
    private static readonly EntityId B = EntityId.Create("b");

    [Fact]
    public void PointDeltasAndReversedWitnessesAgree()
    {
        var a = new PointEntity(A, new(0, 0));
        var b = new PointEntity(B, new(3, 4));
        MeasurementService.TryGetShortestSpan(a, b, out var span).Should().BeTrue();
        span.Result.Should().Be(new MeasurementResult(3, 4, 5));
        MeasurementService.TryGetShortestSpan(b, a, out var reverse).Should().BeTrue();
        reverse.Result.Should().Be(new MeasurementResult(-3, -4, 5));
        span.Approximate.Should().BeFalse();
    }

    [Theory]
    [InlineData(-10, 10, 0)]
    [InlineData(-1, 1, 4)]
    [InlineData(8, 10, 3)]
    [InlineData(0, 0, 5)]
    public void LineCircleWitnessesAreOnBothBoundaries(double start, double end, double expected)
    {
        var line = new LineEntity(A, new(start, 0), new(end, 0));
        var circle = new CircleEntity(B, new(0, 0), 5);
        MeasurementService.TryGetShortestSpan(line, circle, out var span).Should().BeTrue();
        span.Result.Distance.Should().BeApproximately(expected, 1e-9);
        span.Start.X.Should().BeInRange(start, end);
        span.Start.Y.Should().Be(0);
        MeasurementService.Measure(circle.Center, span.End).Distance.Should().BeApproximately(5, 1e-9);
        MeasurementService.TryGetShortestSpan(circle, line, out var reversed).Should().BeTrue();
        reversed.Start.Should().Be(span.End);
        reversed.End.Should().Be(span.Start);
        MeasurementService.TryMeasureShortestDistance(line, circle, out var numeric).Should().BeTrue();
        numeric.Should().Be(span.Result);
    }

    [Theory]
    [InlineData(8, 0)]
    [InlineData(12, 2)]
    [InlineData(10, 0)]
    public void CircleWitnessesRetainActualContact(double x, double expected)
    {
        var a = new CircleEntity(A, new(0, 0), 5);
        var b = new CircleEntity(B, new(x, 0), 5);
        MeasurementService.TryGetShortestSpan(a, b, out var span).Should().BeTrue();
        span.Result.Distance.Should().BeApproximately(expected, 1e-9);
        MeasurementService.Measure(a.Center, span.Start).Distance.Should().BeApproximately(5, 1e-9);
        MeasurementService.Measure(b.Center, span.End).Distance.Should().BeApproximately(5, 1e-9);
    }

    [Fact]
    public void SampledCurvesAreExplicitlyApproximate()
    {
        var arc = new ArcEntity(A, new(0, 0), 5, 0, 90);
        MeasurementService.TryGetShortestSpan(arc, new PointEntity(B, new(10, 10)), out var span).Should().BeTrue();
        span.Approximate.Should().BeTrue();
    }
}

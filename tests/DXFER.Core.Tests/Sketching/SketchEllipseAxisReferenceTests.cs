using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchEllipseAxisReferenceTests
{
    [Theory]
    [InlineData("major-start", -1, 0)]
    [InlineData("major-end", 1, 0)]
    [InlineData("minor-start", 0, -1)]
    [InlineData("MINOR-END", 0, 1)]
    [InlineData("quadrant-180", -1, 0)]
    [InlineData("quadrant-0", 1, 0)]
    [InlineData("quadrant-270", 0, -1)]
    [InlineData("QUADRANT-90", 0, 1)]
    public void AxisReferencesFollowLiveTranslatedRotatedAndResizedGeometry(string label, int major, int minor)
    {
        foreach (var ratio in new[] { 0.5, 1, 2 })
        {
            var ellipse = new EllipseEntity(EntityId.Create("shape"), new(7, -2), new(-4, 3), ratio);
            var document = new DrawingDocument([ellipse]);

            SketchReferenceResolver.TryGetPoint(document, $"shape|point|{label}|999|-999", out var point).Should().BeTrue();

            point.X.Should().BeApproximately(7 - 4 * major - 3 * ratio * minor, 1e-12);
            point.Y.Should().BeApproximately(-2 + 3 * major - 4 * ratio * minor, 1e-12);
            SketchReferenceResolver.TryGetLivePoint(document, $"shape|point|{label}|999|-999", out var live)
                .Should().BeTrue();
            live.Should().Be(point);
        }
    }

    [Theory]
    [InlineData(SketchDimensionKind.LinearDistance, 10)]
    [InlineData(SketchDimensionKind.HorizontalDistance, 6)]
    [InlineData(SketchDimensionKind.VerticalDistance, 8)]
    public void QuadrantDimensionMeasurementUsesLiveGeometryNotAcquisitionCoordinates(SketchDimensionKind kind, double expected)
    {
        var document = new DrawingDocument([new EllipseEntity(EntityId.Create("shape"), new(7, -2), new(-4, 3), 1)]);
        var dimension = new SketchDimension("minor", kind,
            ["shape|point|quadrant-90|0|2", "shape|point|quadrant-270|0|-2"], expected, isDriving: true);

        SketchDimensionSolverService.TryGetDimensionMeasurement(document, dimension, out var measured).Should().BeTrue();
        measured.Should().BeApproximately(expected, 1e-12);
    }

    public static IEnumerable<object[]> InvalidAxisKeys()
    {
        foreach (var key in new[]
        {
            "shape|point|major-missing|0|0", "shape|point|major-start|NaN|0",
            "shape|point|major-start|0|Infinity", "shape|point|major-start|-Infinity|0",
            "shape|point|major-start|1e999|0", "shape|point|major-start||0|0",
            "shape|point|major-start|0|0|", "shape|point||major-start|0|0",
            "shape|point|major-start|0", "shape|point|major-start|bad|0",
            "shape|point|major-start|extra|0|0", "missing|point|major-start|0|0",
            "shape|segment|0|point|major-start|0|0",
            "shape|point|quadrant-45|0|0", "shape|point|quadrant-360|0|0",
            "shape|point|quadrant-90|NaN|0", "shape|point|quadrant-270|0|Infinity",
            "shape|point|quadrant-90|extra|0|0", "missing|point|quadrant-90|0|0",
            "shape|segment|0|point|quadrant-90|0|0"
        }) yield return [key];
    }

    [Theory]
    [MemberData(nameof(InvalidAxisKeys))]
    public void MalformedOrMissingAxisReferenceDoesNotFallBackToStoredCoordinates(string key)
    {
        var document = new DrawingDocument([new EllipseEntity(EntityId.Create("shape"), new(7, -2), new(6, 0), 0.5)]);

        SketchReferenceResolver.TryGetPoint(document, key, out _).Should().BeFalse();
        var dimension = new SketchDimension("invalid", SketchDimensionKind.LinearDistance,
            [key, "shape|point|minor-end|7|1"], 1, isDriving: true);
        SketchDimensionSolverService.TryGetDimensionMeasurement(document, dimension, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(0, 0, 0.5)]
    [InlineData(1e-9, 0, 0.5)]
    [InlineData(double.NaN, 3, 0.5)]
    [InlineData(double.PositiveInfinity, 3, 0.5)]
    [InlineData(6, 0, 0)]
    [InlineData(6, 0, -0.5)]
    [InlineData(6, 0, 1e-9)]
    [InlineData(6, 0, double.NaN)]
    [InlineData(6, 0, double.PositiveInfinity)]
    public void InvalidLiveAxesDoNotResolveEvenWithPlausibleStoredCoordinates(double x, double y, double ratio)
    {
        var document = new DrawingDocument([new EllipseEntity(EntityId.Create("shape"), new(7, -2), new(x, y), ratio)]);
        foreach (var label in new[] { "major-start", "major-end", "minor-start", "minor-end",
            "quadrant-0", "quadrant-90", "quadrant-180", "quadrant-270" })
            SketchReferenceResolver.TryGetPoint(document, $"shape|point|{label}|1|2", out _).Should().BeFalse();
    }

    [Fact]
    public void NonfiniteCenterOrParametersDoNotResolve()
    {
        var ellipse = new EllipseEntity(EntityId.Create("shape"), new(7, -2), new(6, 0), 0.5);
        foreach (var invalid in new[]
        {
            ellipse with { Center = new(double.NaN, 0) },
            ellipse with { Center = new(0, double.PositiveInfinity) },
            ellipse with { StartParameterDegrees = double.NaN },
            ellipse with { EndParameterDegrees = double.PositiveInfinity }
        })
            SketchReferenceResolver.TryGetPoint(new DrawingDocument([invalid]),
                "shape|point|major-end|1|2", out _).Should().BeFalse();
    }
}

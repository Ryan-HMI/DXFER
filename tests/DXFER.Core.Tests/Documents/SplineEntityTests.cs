using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using FluentAssertions;

namespace DXFER.Core.Tests.Documents;

public sealed class SplineEntityTests
{
    [Fact]
    public void GetSamplePointsCachesComputedSplinePath()
    {
        var spline = SplineEntity.FromFitPoints(
            EntityId.Create("curve"),
            new[]
            {
                new Point2(0, 0),
                new Point2(5, 4),
                new Point2(10, -2),
                new Point2(15, 0)
            });

        var first = spline.GetSamplePoints();
        var second = spline.GetSamplePoints();

        ReferenceEquals(first, second).Should().BeTrue();
        first.Should().HaveCountGreaterThan(4);
    }

    [Fact]
    public void DegreeOneSplineUsesControlPointsAsSamplePath()
    {
        var points = new[]
        {
            new Point2(-10, 0),
            new Point2(-5, 4),
            new Point2(0, 0),
            new Point2(5, -4),
            new Point2(10, 0)
        };
        var spline = new SplineEntity(
            EntityId.Create("trimmed"),
            1,
            points,
            Array.Empty<double>());

        spline.GetSamplePoints().Should().Equal(points);
    }
}

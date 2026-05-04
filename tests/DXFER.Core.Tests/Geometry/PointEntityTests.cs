using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using FluentAssertions;

namespace DXFER.Core.Tests.Geometry;

public sealed class PointEntityTests
{
    [Fact]
    public void BoundsUseExactPointLocation()
    {
        var point = new PointEntity(EntityId.Create("P1"), new Point2(-2, 3));

        var bounds = point.GetBounds();

        bounds.MinX.Should().Be(-2);
        bounds.MinY.Should().Be(3);
        bounds.MaxX.Should().Be(-2);
        bounds.MaxY.Should().Be(3);
    }

    [Fact]
    public void TransformMovesPointLocation()
    {
        var point = new PointEntity(EntityId.Create("P1"), new Point2(2, 3));

        var translated = (PointEntity)point.Transform(Transform2.Translation(-5, 7));

        translated.Location.Should().Be(new Point2(-3, 10));
    }
}

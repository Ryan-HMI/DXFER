using DXFER.Blazor.Components;
using DXFER.Core.Geometry;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchRectangleGeometryTests
{
    [Fact]
    public void CenterRectangleCornersUseCenterAndCorner()
    {
        var corners = SketchRectangleGeometry.GetCenterRectangleCorners(
            new Point2(10, 10),
            new Point2(13, 14));

        corners.Should().Equal(
            new Point2(7, 6),
            new Point2(13, 6),
            new Point2(13, 14),
            new Point2(7, 14));
    }
}

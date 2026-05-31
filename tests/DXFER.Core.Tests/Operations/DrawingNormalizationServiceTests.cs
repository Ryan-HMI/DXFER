using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class DrawingNormalizationServiceTests
{
    [Fact]
    public void AutoNormalizeRotatedRectangleMinimizesBoundsAndMovesMinimumToOrigin()
    {
        var original = RectangleDocument(width: 20, height: 10, degrees: 30, offsetX: 50, offsetY: -7);

        var result = DrawingNormalizationService.AutoNormalize(original);

        result.RotationDegrees.Should().BeApproximately(-30, 0.01);
        result.NormalizedDocument.GetBounds().MinX.Should().BeApproximately(0, 0.0001);
        result.NormalizedDocument.GetBounds().MinY.Should().BeApproximately(0, 0.0001);
        result.NormalizedDocument.GetBounds().Width.Should().BeApproximately(20, 0.01);
        result.NormalizedDocument.GetBounds().Height.Should().BeApproximately(10, 0.01);
        result.ManualOverride.Should().BeFalse();
    }

    [Fact]
    public void AutoNormalizeReportsOriginShiftAppliedAfterRotation()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(12, -4), new Point2(22, -4))
        });

        var result = DrawingNormalizationService.AutoNormalize(document);

        result.OriginShiftX.Should().BeApproximately(-12, 0.0001);
        result.OriginShiftY.Should().BeApproximately(4, 0.0001);
    }

    [Fact]
    public void AutoNormalizePrefersSmallerMaxDimensionBeforeSmallerAbsoluteRotation()
    {
        var document = RectangleDocument(width: 12, height: 30, degrees: -75, offsetX: 2, offsetY: 3);

        var result = DrawingNormalizationService.AutoNormalize(document);

        result.NormalizedDocument.GetBounds().Width.Should().BeApproximately(30, 0.01);
        result.NormalizedDocument.GetBounds().Height.Should().BeApproximately(12, 0.01);
        result.RotationDegrees.Should().BeApproximately(-15, 0.01);
    }

    private static DrawingDocument RectangleDocument(
        double width,
        double height,
        double degrees,
        double offsetX,
        double offsetY)
    {
        var halfWidth = width / 2.0;
        var halfHeight = height / 2.0;
        var center = new Point2(offsetX, offsetY);
        var transform = Transform2.RotationDegreesAbout(degrees, center);
        var corners = new[]
        {
            new Point2(offsetX - halfWidth, offsetY - halfHeight).Transform(transform),
            new Point2(offsetX + halfWidth, offsetY - halfHeight).Transform(transform),
            new Point2(offsetX + halfWidth, offsetY + halfHeight).Transform(transform),
            new Point2(offsetX - halfWidth, offsetY + halfHeight).Transform(transform)
        };

        return new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(EntityId.Create("rect"), corners.Concat(new[] { corners[0] }))
        });
    }
}

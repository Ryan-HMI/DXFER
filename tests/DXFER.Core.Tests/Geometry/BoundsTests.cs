using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using FluentAssertions;

namespace DXFER.Core.Tests.Geometry;

public sealed class BoundsTests
{
    [Fact]
    public void LineBoundsIncludeBothEndpoints()
    {
        var line = new LineEntity(EntityId.Create("L1"), new Point2(-2, 3), new Point2(5, -7));

        var bounds = line.GetBounds();

        bounds.MinX.Should().Be(-2);
        bounds.MinY.Should().Be(-7);
        bounds.MaxX.Should().Be(5);
        bounds.MaxY.Should().Be(3);
        bounds.Width.Should().Be(7);
        bounds.Height.Should().Be(10);
    }

    [Fact]
    public void DocumentBoundsUnionAllEntityBounds()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("L1"), new Point2(0, 0), new Point2(10, 0)),
            new CircleEntity(EntityId.Create("C1"), new Point2(4, 5), 2)
        });

        var bounds = document.GetBounds();

        bounds.MinX.Should().Be(0);
        bounds.MinY.Should().Be(0);
        bounds.MaxX.Should().Be(10);
        bounds.MaxY.Should().Be(7);
    }

    [Fact]
    public void CircleBoundsUseCenterAndRadius()
    {
        var circle = new CircleEntity(EntityId.Create("C1"), new Point2(4, -2), 3);

        var bounds = circle.GetBounds();

        bounds.MinX.Should().Be(1);
        bounds.MinY.Should().Be(-5);
        bounds.MaxX.Should().Be(7);
        bounds.MaxY.Should().Be(1);
    }

    [Fact]
    public void PolylineBoundsIncludeAllVertices()
    {
        var polyline = new PolylineEntity(
            EntityId.Create("P1"),
            new[] { new Point2(2, 5), new Point2(-4, 1), new Point2(3, -6) });

        var bounds = polyline.GetBounds();

        bounds.MinX.Should().Be(-4);
        bounds.MinY.Should().Be(-6);
        bounds.MaxX.Should().Be(3);
        bounds.MaxY.Should().Be(5);
    }

    [Fact]
    public void ArcBoundsIncludeSampledArcExtents()
    {
        var arc = new ArcEntity(EntityId.Create("A1"), new Point2(0, 0), 5, 0, 180);

        var bounds = arc.GetBounds();

        bounds.MinX.Should().BeApproximately(-5, 0.000001);
        bounds.MinY.Should().BeApproximately(0, 0.000001);
        bounds.MaxX.Should().BeApproximately(5, 0.000001);
        bounds.MaxY.Should().BeApproximately(5, 0.000001);
    }

    [Fact]
    public void EllipseBoundsIncludeSampledEllipseExtents()
    {
        var ellipse = new EllipseEntity(EntityId.Create("E1"), new Point2(1, 2), new Point2(4, 0), 0.5);

        var bounds = ellipse.GetBounds();

        bounds.MinX.Should().BeApproximately(-3, 0.000001);
        bounds.MinY.Should().BeApproximately(0, 0.000001);
        bounds.MaxX.Should().BeApproximately(5, 0.000001);
        bounds.MaxY.Should().BeApproximately(4, 0.000001);
    }

    [Fact]
    public void TransformRotatesLineAroundOrigin()
    {
        var line = new LineEntity(EntityId.Create("L1"), new Point2(2, 0), new Point2(2, 3));

        var rotated = (LineEntity)line.Transform(Transform2.RotationDegrees(90));

        rotated.Start.X.Should().BeApproximately(0, 0.000001);
        rotated.Start.Y.Should().BeApproximately(2, 0.000001);
        rotated.End.X.Should().BeApproximately(-3, 0.000001);
        rotated.End.Y.Should().BeApproximately(2, 0.000001);
    }

    [Fact]
    public void TransformTranslatesCircleCenterWithoutChangingRadius()
    {
        var circle = new CircleEntity(EntityId.Create("C1"), new Point2(2, 3), 4);

        var translated = (CircleEntity)circle.Transform(Transform2.Translation(-5, 7));

        translated.Center.Should().Be(new Point2(-3, 10));
        translated.Radius.Should().Be(4);
    }

    [Fact]
    public void TransformPreservesConstructionState()
    {
        var line = new LineEntity(
            EntityId.Create("construction-line"),
            new Point2(0, 0),
            new Point2(1, 0),
            IsConstruction: true);

        var transformed = (LineEntity)line.Transform(Transform2.Translation(10, 5));

        transformed.IsConstruction.Should().BeTrue();
    }
}

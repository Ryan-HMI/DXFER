using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class CurveSplitServiceTests
{
    [Fact]
    public void SplitsCircleIntoTwoArcsFromTwoPoints()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("circle"), new Point2(0, 0), 5, IsConstruction: true)
        });

        var split = CurveSplitService.TrySplitCircleAtPoints(
            document,
            "circle",
            new Point2(5, 0),
            new Point2(0, 5),
            EntityId.Create("circle-split"),
            out var nextDocument);

        split.Should().BeTrue();
        nextDocument.Entities.Should().HaveCount(2);
        nextDocument.Entities[0].Should().BeOfType<ArcEntity>()
            .Which.Should().Match<ArcEntity>(arc =>
                arc.Id.Value == "circle"
                && arc.Center == new Point2(0, 0)
                && arc.Radius == 5
                && Math.Abs(arc.StartAngleDegrees) <= 0.000001
                && Math.Abs(arc.EndAngleDegrees - 90) <= 0.000001
                && arc.IsConstruction);
        nextDocument.Entities[1].Should().BeOfType<ArcEntity>()
            .Which.Id.Value.Should().Be("circle-split");
    }

    [Fact]
    public void SplitsArcAtInteriorPoint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc"), new Point2(0, 0), 5, 0, 180)
        });

        var split = CurveSplitService.TrySplitArcAtPoint(
            document,
            "arc",
            new Point2(0, 5),
            EntityId.Create("arc-split"),
            out var nextDocument);

        split.Should().BeTrue();
        nextDocument.Entities.Should().HaveCount(2);
        nextDocument.Entities[0].Should().BeOfType<ArcEntity>()
            .Which.EndAngleDegrees.Should().BeApproximately(90, 0.000001);
        nextDocument.Entities[1].Should().BeOfType<ArcEntity>()
            .Which.StartAngleDegrees.Should().BeApproximately(90, 0.000001);
    }

    [Fact]
    public void RejectsCircleSplitWhenPointsCoincide()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("circle"), new Point2(0, 0), 5)
        });

        CurveSplitService.TrySplitCircleAtPoints(
            document,
            "circle",
            new Point2(5, 0),
            new Point2(5, 0),
            EntityId.Create("circle-split"),
            out var nextDocument).Should().BeFalse();

        nextDocument.Should().BeSameAs(document);
    }
}

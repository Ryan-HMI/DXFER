using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class LineSplitServiceTests
{
    [Fact]
    public void SplitsSelectedLineAtInteriorPoint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0)),
            new CircleEntity(EntityId.Create("keep"), new Point2(20, 0), 2)
        });

        var split = LineSplitService.TrySplitLineAtPoint(
            document,
            "edge",
            new Point2(4, 0),
            EntityId.Create("edge-split"),
            out var nextDocument);

        split.Should().BeTrue();
        nextDocument.Entities.Should().HaveCount(3);
        nextDocument.Entities[0].Should().BeOfType<LineEntity>()
            .Which.Should().Be(new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(4, 0)));
        nextDocument.Entities[1].Should().BeOfType<LineEntity>()
            .Which.Should().Be(new LineEntity(EntityId.Create("edge-split"), new Point2(4, 0), new Point2(10, 0)));
        nextDocument.Entities[2].Should().BeSameAs(document.Entities[1]);
    }

    [Fact]
    public void SplitSegmentsKeepConstructionState()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0), IsConstruction: true)
        });

        var split = LineSplitService.TrySplitLineAtPoint(
            document,
            "edge",
            new Point2(4, 0),
            EntityId.Create("edge-split"),
            out var nextDocument);

        split.Should().BeTrue();
        nextDocument.Entities.Should().AllSatisfy(entity => entity.IsConstruction.Should().BeTrue());
    }

    [Fact]
    public void SplitLinePreservesSketchData()
    {
        var dimension = new SketchDimension(
            "dim-1",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            10);
        var constraint = new SketchConstraint(
            "constraint-1",
            SketchConstraintKind.Horizontal,
            new[] { "edge" });
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
            },
            new[] { dimension },
            new[] { constraint });

        var split = LineSplitService.TrySplitLineAtPoint(
            document,
            "edge",
            new Point2(4, 0),
            EntityId.Create("edge-split"),
            out var nextDocument);

        split.Should().BeTrue();
        nextDocument.Dimensions.Should().ContainSingle()
            .Which.Should().BeSameAs(dimension);
        nextDocument.Constraints.Should().ContainSingle()
            .Which.Should().BeSameAs(constraint);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 0)]
    [InlineData(4, 1)]
    public void RejectsEndpointsAndOffLinePoints(double x, double y)
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0))
        });

        var split = LineSplitService.TrySplitLineAtPoint(
            document,
            "edge",
            new Point2(x, y),
            EntityId.Create("edge-split"),
            out var nextDocument);

        split.Should().BeFalse();
        nextDocument.Should().BeSameAs(document);
    }
}

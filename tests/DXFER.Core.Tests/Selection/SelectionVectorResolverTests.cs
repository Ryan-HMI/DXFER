using DXFER.Blazor.Selection;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using FluentAssertions;

namespace DXFER.Core.Tests.Selection;

public sealed class SelectionVectorResolverTests
{
    [Fact]
    public void AcceptsSingleLineEntitySelection()
    {
        var document = CreateDocument();

        var result = SelectionVectorResolver.TryGetAlignmentVector(
            document,
            new[] { "line-a" },
            out var start,
            out var end);

        result.Should().BeTrue();
        start.Should().Be(new Point2(0, 0));
        end.Should().Be(new Point2(10, 0));
    }

    [Fact]
    public void AcceptsExactlyTwoPointSelections()
    {
        var document = CreateDocument();

        var result = SelectionVectorResolver.TryGetAlignmentVector(
            document,
            new[]
            {
                "line-a|point|start|0|0",
                "circle-a|point|quadrant-90|20|30"
            },
            out var start,
            out var end);

        result.Should().BeTrue();
        start.Should().Be(new Point2(0, 0));
        end.Should().Be(new Point2(20, 30));
    }

    [Fact]
    public void AcceptsSinglePolylineSegmentSelection()
    {
        var document = CreateDocument();

        var result = SelectionVectorResolver.TryGetAlignmentVector(
            document,
            new[] { "poly-a|segment|1" },
            out var start,
            out var end);

        result.Should().BeTrue();
        start.Should().Be(new Point2(5, 0));
        end.Should().Be(new Point2(5, 5));
    }

    [Fact]
    public void ActiveLineSelectionWinsWhenMultipleEntitiesAreSelected()
    {
        var document = CreateDocument();

        var result = SelectionVectorResolver.TryGetAlignmentVector(
            document,
            new[] { "circle-a", "line-a" },
            "line-a",
            out var start,
            out var end);

        result.Should().BeTrue();
        start.Should().Be(new Point2(0, 0));
        end.Should().Be(new Point2(10, 0));
    }

    [Fact]
    public void ActivePointSelectionDefinesVectorEndWithOneOtherSelectedPoint()
    {
        var document = CreateDocument();
        const string firstPoint = "line-a|point|start|0|0";
        const string activePoint = "circle-a|point|quadrant-90|20|30";

        var result = SelectionVectorResolver.TryGetAlignmentVector(
            document,
            new[] { firstPoint, activePoint },
            activePoint,
            out var start,
            out var end);

        result.Should().BeTrue();
        start.Should().Be(new Point2(0, 0));
        end.Should().Be(new Point2(20, 30));
    }

    [Theory]
    [InlineData("circle-a")]
    [InlineData("arc-a")]
    [InlineData("poly-a")]
    public void RejectsWholeEntitiesThatDoNotExposeAnUnambiguousVector(string selectionKey)
    {
        var document = CreateDocument();

        var result = SelectionVectorResolver.TryGetAlignmentVector(
            document,
            new[] { selectionKey },
            out _,
            out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void RejectsMixedOrExtraSelections()
    {
        var document = CreateDocument();

        var result = SelectionVectorResolver.TryGetAlignmentVector(
            document,
            new[]
            {
                "line-a",
                "circle-a"
            },
            out _,
            out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void RejectsMoreThanTwoPointSelections()
    {
        var document = CreateDocument();

        var result = SelectionVectorResolver.TryGetAlignmentVector(
            document,
            new[]
            {
                "line-a|point|start|0|0",
                "line-a|point|end|10|0",
                "circle-a|point|center|20|20"
            },
            out _,
            out _);

        result.Should().BeFalse();
    }

    private static DrawingDocument CreateDocument() =>
        new(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("line-a"), new Point2(0, 0), new Point2(10, 0)),
            new CircleEntity(EntityId.Create("circle-a"), new Point2(20, 20), 10),
            new ArcEntity(EntityId.Create("arc-a"), new Point2(40, 20), 8, 0, 180),
            new PolylineEntity(
                EntityId.Create("poly-a"),
                new[]
                {
                    new Point2(0, 0),
                    new Point2(5, 0),
                    new Point2(5, 5)
                })
        });
}

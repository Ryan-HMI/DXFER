using DXFER.Blazor.Selection;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using FluentAssertions;

namespace DXFER.Core.Tests.Selection;

public sealed class SelectionPointResolverTests
{
    [Fact]
    public void AcceptsSingleSelectedPoint()
    {
        var document = CreateDocument();

        var result = SelectionPointResolver.TryGetPointToOriginReference(
            document,
            new[] { "line-a|point|start|2|3" },
            out var point);

        result.Should().BeTrue();
        point.Should().Be(new Point2(2, 3));
    }

    [Fact]
    public void UsesCircleCenterForWholeCircleSelection()
    {
        var document = CreateDocument();

        var result = SelectionPointResolver.TryGetPointToOriginReference(
            document,
            new[] { "circle-a" },
            out var point);

        result.Should().BeTrue();
        point.Should().Be(new Point2(20, 20));
    }

    [Fact]
    public void UsesArcCenterForWholeArcSelection()
    {
        var document = CreateDocument();

        var result = SelectionPointResolver.TryGetPointToOriginReference(
            document,
            new[] { "arc-a" },
            out var point);

        result.Should().BeTrue();
        point.Should().Be(new Point2(40, 20));
    }

    [Theory]
    [InlineData("line-a")]
    [InlineData("poly-a")]
    [InlineData("poly-a|segment|0")]
    public void RejectsSelectionsWithoutASinglePointReference(string selectionKey)
    {
        var document = CreateDocument();

        var result = SelectionPointResolver.TryGetPointToOriginReference(
            document,
            new[] { selectionKey },
            out _);

        result.Should().BeFalse();
    }

    [Fact]
    public void RejectsMultipleSelections()
    {
        var document = CreateDocument();

        var result = SelectionPointResolver.TryGetPointToOriginReference(
            document,
            new[]
            {
                "circle-a",
                "arc-a"
            },
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

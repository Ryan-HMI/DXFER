using DXFER.Blazor.Selection;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using FluentAssertions;

namespace DXFER.Core.Tests.Selection;

public sealed class LineSplitSelectionResolverTests
{
    [Fact]
    public void ResolvesSelectedLineAndPersistentPointEntity()
    {
        var document = CreateDocument();

        var resolved = LineSplitSelectionResolver.TryResolveLineAndPoint(
            document,
            new[] { "edge", "split-point" },
            out var lineEntityId,
            out var point);

        resolved.Should().BeTrue();
        lineEntityId.Should().Be("edge");
        point.Should().Be(new Point2(4, 0));
    }

    [Fact]
    public void ResolvesSelectedLineAndSnapPointSelection()
    {
        var document = CreateDocument();

        var resolved = LineSplitSelectionResolver.TryResolveLineAndPoint(
            document,
            new[] { "edge", "edge|point|mid|5|0" },
            out var lineEntityId,
            out var point);

        resolved.Should().BeTrue();
        lineEntityId.Should().Be("edge");
        point.Should().Be(new Point2(5, 0));
    }

    [Fact]
    public void RejectsAmbiguousLineSelections()
    {
        var document = CreateDocument();

        var resolved = LineSplitSelectionResolver.TryResolveLineAndPoint(
            document,
            new[] { "edge", "other-edge", "split-point" },
            out _,
            out _);

        resolved.Should().BeFalse();
    }

    private static DrawingDocument CreateDocument() =>
        new(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("other-edge"), new Point2(0, 4), new Point2(10, 4)),
            new PointEntity(EntityId.Create("split-point"), new Point2(4, 0))
        });
}

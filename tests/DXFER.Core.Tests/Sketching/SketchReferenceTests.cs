using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchReferenceTests
{
    [Theory]
    [InlineData("line-a:start", "line-a:start", SketchReferenceTarget.Start)]
    [InlineData("line-a:end", "line-a:end", SketchReferenceTarget.End)]
    [InlineData("circle-a:center", "circle-a:center", SketchReferenceTarget.Center)]
    [InlineData("arc-a:center", "arc-a:center", SketchReferenceTarget.Center)]
    [InlineData("line-a", "line-a", SketchReferenceTarget.Entity)]
    [InlineData("circle-a", "circle-a", SketchReferenceTarget.Entity)]
    [InlineData("arc-a", "arc-a", SketchReferenceTarget.Entity)]
    [InlineData("line-a|point|start|0|0", "line-a:start", SketchReferenceTarget.Start)]
    [InlineData("line-a|point|end|10|0", "line-a:end", SketchReferenceTarget.End)]
    [InlineData("circle-a|point|center|20|20", "circle-a:center", SketchReferenceTarget.Center)]
    public void ParserNormalizesSupportedReferences(string key, string normalized, SketchReferenceTarget target)
    {
        var result = SketchReference.TryParse(key, out var reference);

        result.Should().BeTrue();
        reference.EntityId.Should().Be(normalized.Split(':')[0]);
        reference.Target.Should().Be(target);
        reference.ToString().Should().Be(normalized);
    }

    [Fact]
    public void ResolverResolvesSupportedPointsAndWholeEntities()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("line-a"), new Point2(0, 1), new Point2(2, 3)),
            new CircleEntity(EntityId.Create("circle-a"), new Point2(5, 6), 7),
            new ArcEntity(EntityId.Create("arc-a"), new Point2(8, 9), 10, 0, 90)
        });

        SketchReferenceResolver.TryGetPoint(document, "line-a:start", out var lineStart).Should().BeTrue();
        lineStart.Should().Be(new Point2(0, 1));

        SketchReferenceResolver.TryGetPoint(document, "line-a:end", out var lineEnd).Should().BeTrue();
        lineEnd.Should().Be(new Point2(2, 3));

        SketchReferenceResolver.TryGetPoint(document, "circle-a:center", out var circleCenter).Should().BeTrue();
        circleCenter.Should().Be(new Point2(5, 6));

        SketchReferenceResolver.TryGetPoint(document, "arc-a:center", out var arcCenter).Should().BeTrue();
        arcCenter.Should().Be(new Point2(8, 9));

        SketchReferenceResolver.TryGetEntity(document, "line-a", out var entity).Should().BeTrue();
        entity.Should().BeOfType<LineEntity>();
    }
}

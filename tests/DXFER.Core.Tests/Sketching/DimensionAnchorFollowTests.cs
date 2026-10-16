using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class DimensionAnchorFollowTests
{
    [Fact]
    public void FixedStartRotationPreservesTheLabelOffsetInTheLineFrame()
    {
        var document = new DrawingDocument(new DrawingEntity[] { new LineEntity(EntityId.Create("line"), new(0, 0), new(10, 0)) },
            new[] { new SketchDimension("length", SketchDimensionKind.LinearDistance, new[] { "line:start", "line:end" }, 10, new(5, -2), true) },
            new[] { new SketchConstraint("fixed", SketchConstraintKind.Fix, new[] { "line:start" }) });
        SketchGeometryDragService.TryApplyDrag(document, "line|point|end|10|0", new(10, 0), new(8, 6), false,
            out var next, out var status).Should().BeTrue(status);
        next.Dimensions[0].Anchor!.Value.X.Should().BeApproximately(5.2, 1e-8);
        next.Dimensions[0].Anchor!.Value.Y.Should().BeApproximately(1.4, 1e-8);
        next.Dimensions[0].Value.Should().Be(10);
        document.Dimensions[0].Anchor.Should().Be(new Point2(5, -2));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PartiallyDimensionedEndpointMovesLabelWithItsSpan(bool reversed)
    {
        var keys = reversed ? new[] { "line:end", "line:start" } : new[] { "line:start", "line:end" };
        var document = new DrawingDocument(new DrawingEntity[] {
            new LineEntity(EntityId.Create("line"), new(2, 2), new(6, 2))
        }, new[] { new SketchDimension("width", SketchDimensionKind.HorizontalDistance, keys, 4, new(4, 0), true) }, Array.Empty<SketchConstraint>());
        SketchGeometryDragService.TryApplyDrag(document, "line|point|end|6|2", new(6, 2), new(6, 4), false,
            out var next, out var status).Should().BeTrue(status);
        next.Dimensions[0].Anchor.Should().Be(new Point2(4, 1));
        next.Dimensions[0].Value.Should().Be(4);
        next.Dimensions[0].ReferenceKeys.Should().Equal(keys);
        SketchGeometryDragService.TryApplyDrag(next, "line|point|end|6|4", new(6, 4), new(6, 6), false,
            out var again, out status).Should().BeTrue(status);
        again.Dimensions[0].Anchor.Should().Be(new Point2(4, 2));
        document.Dimensions[0].Anchor.Should().Be(new Point2(4, 0));
    }
}

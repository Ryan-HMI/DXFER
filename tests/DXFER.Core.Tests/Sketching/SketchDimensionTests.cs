using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchDimensionTests
{
    [Fact]
    public void DimensionStoresStableIdentityReferencesValueAndAnchor()
    {
        var dimension = new SketchDimension(
            "dim-1",
            SketchDimensionKind.LinearDistance,
            new[] { "line-a:start", "line-b:end" },
            42.125,
            new Point2(10, 20));

        dimension.Id.Should().Be("dim-1");
        dimension.Kind.Should().Be(SketchDimensionKind.LinearDistance);
        dimension.ReferenceKeys.Should().Equal("line-a:start", "line-b:end");
        dimension.Value.Should().Be(42.125);
        dimension.Anchor.Should().Be(new Point2(10, 20));
        dimension.IsDriving.Should().BeFalse();
    }

    [Fact]
    public void DimensionReferencesAreCopiedFromConstructorInput()
    {
        var references = new[] { "line-a", "line-b" };

        var dimension = new SketchDimension(
            "dim-1",
            SketchDimensionKind.HorizontalDistance,
            references,
            25);

        references[0] = "mutated";

        dimension.ReferenceKeys.Should().Equal("line-a", "line-b");
    }
}

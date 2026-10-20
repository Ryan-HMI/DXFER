using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsGeometryIdentityTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(0.000000000001)]
    [InlineData(0.001)]
    public void FixedGeometryAcceptsSignedZeroNormalizationButNotActualDrift(double radiusDrift)
    {
        var negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
        var circle = new CircleEntity(EntityId.Create("fixed"), new(negativeZero, negativeZero), 3);
        var original = new DrawingDocument([circle], [],
            [new("fixed-circle", SketchConstraintKind.Fix, ["fixed"], SketchConstraintState.Satisfied)]);

        var result = new PlaneGcsSketchSolverAdapter(new NormalizingRuntime(radiusDrift))
            .Solve(SketchSolveRequest.FromDocument(original));

        result.Status.Should().Be(radiusDrift == 0 ? SketchSolveStatus.Solved : SketchSolveStatus.Failed);
        result.Document.Entities.Single().Should().BeSameAs(circle);
        result.Document.Metadata.Should().BeSameAs(original.Metadata);
        BitConverter.DoubleToInt64Bits(((CircleEntity)result.Document.Entities.Single()).Center.X)
            .Should().Be(long.MinValue, "the original fixed geometry must be retained exactly");
        if (radiusDrift != 0) result.Document.Should().BeSameAs(original);
    }

    private sealed class NormalizingRuntime(double radiusDrift) : IPlaneGcsRuntime
    {
        public PlaneGcsResponse Solve(PlaneGcsRequest request) =>
            new("solved", request.Document.Entities.Select(entity => entity with
            {
                Center = new(0, 0), Radius = entity.Radius + radiusDrift
            }).ToArray(), [], "PlaneGCS");
    }
}

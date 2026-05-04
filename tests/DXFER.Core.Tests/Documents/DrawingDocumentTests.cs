using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Documents;

public sealed class DrawingDocumentTests
{
    [Fact]
    public void EntityOnlyConstructorStartsWithEmptySketchData()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(1, 0))
        });

        document.Entities.Should().HaveCount(1);
        document.Dimensions.Should().BeEmpty();
        document.Constraints.Should().BeEmpty();
    }

    [Fact]
    public void ConstructorCopiesDimensionsAndConstraints()
    {
        var dimensions = new[]
        {
            new SketchDimension(
                "dim-1",
                SketchDimensionKind.LinearDistance,
                new[] { "edge:start", "edge:end" },
                10,
                isDriving: true)
        };
        var constraints = new[]
        {
            new SketchConstraint(
                "constraint-1",
                SketchConstraintKind.Horizontal,
                new[] { "edge" },
                SketchConstraintState.Satisfied)
        };

        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(1, 0))
            },
            dimensions,
            constraints);

        dimensions[0] = new SketchDimension("mutated", SketchDimensionKind.Radius, new[] { "circle" }, 5);
        constraints[0] = new SketchConstraint("mutated", SketchConstraintKind.Fix, new[] { "edge" });

        document.Dimensions.Should().ContainSingle()
            .Which.Id.Should().Be("dim-1");
        document.Constraints.Should().ContainSingle()
            .Which.Id.Should().Be("constraint-1");
    }
}

using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchCreationConstraintFactoryTests
{
    [Fact]
    public void CreatesLogicalConstraintsForAxisAlignedRectangle()
    {
        var entities = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("r1"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("r2"), new Point2(10, 0), new Point2(10, 5)),
            new LineEntity(EntityId.Create("r3"), new Point2(10, 5), new Point2(0, 5)),
            new LineEntity(EntityId.Create("r4"), new Point2(0, 5), new Point2(0, 0))
        };

        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            CreateConstraintId);

        constraints.Count(constraint => constraint.Kind == SketchConstraintKind.Coincident).Should().Be(4);
        constraints.Count(constraint => constraint.Kind == SketchConstraintKind.Parallel).Should().Be(2);
        constraints.Count(constraint => constraint.Kind == SketchConstraintKind.Perpendicular).Should().Be(1);
        constraints.Count(constraint => constraint.Kind == SketchConstraintKind.Horizontal).Should().Be(1);
        constraints.Count(constraint => constraint.Kind == SketchConstraintKind.Vertical).Should().Be(1);
        constraints.Should().Contain(constraint =>
            constraint.Kind == SketchConstraintKind.Coincident
            && constraint.ReferenceKeys.SequenceEqual(new[] { "r1:end", "r2:start" }));
    }

    [Fact]
    public void CreatesAxisConstraintOnlyWhenLineIsAlreadyAxisAligned()
    {
        var horizontal = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 2), new Point2(10, 2))
        };
        var diagonal = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("diag"), new Point2(0, 0), new Point2(10, 2))
        };

        SketchCreationConstraintFactory.CreateConstraintsForTool("line", horizontal, CreateConstraintId)
            .Should().ContainSingle()
            .Which.Kind.Should().Be(SketchConstraintKind.Horizontal);
        SketchCreationConstraintFactory.CreateConstraintsForTool("line", diagonal, CreateConstraintId)
            .Should().BeEmpty();
    }

    [Fact]
    public void LeavesAlignedRectangleFreeOfGlobalAxisConstraints()
    {
        var entities = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("a1"), new Point2(0, 0), new Point2(10, 3)),
            new LineEntity(EntityId.Create("a2"), new Point2(10, 3), new Point2(8, 8)),
            new LineEntity(EntityId.Create("a3"), new Point2(8, 8), new Point2(-2, 5)),
            new LineEntity(EntityId.Create("a4"), new Point2(-2, 5), new Point2(0, 0))
        };

        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "alignedrectangle",
            entities,
            CreateConstraintId);

        constraints.Should().Contain(constraint => constraint.Kind == SketchConstraintKind.Perpendicular);
        constraints.Should().Contain(constraint => constraint.Kind == SketchConstraintKind.Parallel);
        constraints.Any(constraint =>
                constraint.Kind == SketchConstraintKind.Horizontal
                || constraint.Kind == SketchConstraintKind.Vertical)
            .Should()
            .BeFalse();
    }

    private static string CreateConstraintId(SketchConstraintKind kind) => $"constraint-{kind}-{Guid.NewGuid():N}";
}

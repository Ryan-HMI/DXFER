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

    [Fact]
    public void CreatesCoincidentAndTangentConstraintsForSlot()
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "slot",
            new[] { new Point2(0, 0), new Point2(10, 0), new Point2(0, 2) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);

        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "slot",
            entities,
            CreateConstraintId);

        constraints.Count(constraint => constraint.Kind == SketchConstraintKind.Coincident).Should().Be(4);
        constraints.Count(constraint => constraint.Kind == SketchConstraintKind.Tangent).Should().Be(4);
        constraints.Count(constraint => constraint.Kind == SketchConstraintKind.Parallel).Should().Be(1);
        constraints.Count(constraint => constraint.Kind == SketchConstraintKind.Equal).Should().Be(1);
        constraints.Should().Contain(constraint =>
            constraint.Kind == SketchConstraintKind.Coincident
            && constraint.ReferenceKeys.SequenceEqual(new[] { "slot-1:end", "slot-2:end" }));
        constraints.Should().Contain(constraint =>
            constraint.Kind == SketchConstraintKind.Tangent
            && constraint.ReferenceKeys.SequenceEqual(new[] { "slot-1", "slot-2" }));
    }

    [Fact]
    public void GeneratedSlotConstraintsDoNotMoveCreatedGeometry()
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "slot",
            new[] { new Point2(0, 0), new Point2(10, 0), new Point2(0, 2) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "slot",
            entities,
            CreateConstraintId);
        var document = new DrawingDocument(entities);

        var solved = SketchConstraintService.ApplyConstraints(document, constraints);

        solved.Entities.Should().Equal(entities);
        solved.Constraints.Should().AllSatisfy(constraint =>
            constraint.State.Should().Be(SketchConstraintState.Satisfied));
    }

    [Fact]
    public void GeneratedCenterRectangleConstraintsDoNotMoveCreatedGeometry()
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "centerrectangle",
            new[] { new Point2(10, 10), new Point2(20, 15) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "centerrectangle",
            entities,
            CreateConstraintId);
        var document = new DrawingDocument(entities);

        var solved = SketchConstraintService.ApplyConstraints(document, constraints);

        solved.Entities.Should().Equal(entities);
    }

    [Theory]
    [InlineData(10, 10, 20, 15)]
    [InlineData(20, 15, 10, 10)]
    [InlineData(10, 15, 20, 10)]
    [InlineData(20, 10, 10, 15)]
    public void GeneratedTwoPointRectangleConstraintsDoNotMoveCreatedGeometry(
        double firstX,
        double firstY,
        double secondX,
        double secondY)
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(firstX, firstY), new Point2(secondX, secondY) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            CreateConstraintId);
        var document = new DrawingDocument(entities);

        var solved = SketchConstraintService.ApplyConstraints(document, constraints);

        solved.Entities.Should().Equal(entities);
    }

    [Fact]
    public void CreatesCoincidentConstraintForInsertedLineContinuingFromExistingEndpoint()
    {
        var existing = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("previous"), new Point2(0, 0), new Point2(10, 0))
        };
        var created = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("next"), new Point2(10, 0), new Point2(15, 0))
        };

        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion(
            "line",
            existing,
            created,
            CreateConstraintId);

        constraints.Should().Contain(constraint =>
            constraint.Kind == SketchConstraintKind.Coincident
            && constraint.State == SketchConstraintState.Satisfied
            && constraint.ReferenceKeys.SequenceEqual(new[] { "previous:end", "next:start" }));
    }

    [Fact]
    public void CreatesPerpendicularConstraintForInsertedLineFromExistingEndpoint()
    {
        var existing = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("previous"), new Point2(0, 0), new Point2(10, 0))
        };
        var created = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("next"), new Point2(10, 0), new Point2(10, 6))
        };

        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion(
            "line",
            existing,
            created,
            CreateConstraintId);

        constraints.Should().Contain(constraint =>
            constraint.Kind == SketchConstraintKind.Coincident
            && constraint.ReferenceKeys.SequenceEqual(new[] { "previous:end", "next:start" }));
        constraints.Should().Contain(constraint =>
            constraint.Kind == SketchConstraintKind.Perpendicular
            && constraint.ReferenceKeys.SequenceEqual(new[] { "previous", "next" }));
    }

    [Fact]
    public void CreatesMidpointAndPerpendicularConstraintsForInsertedLineFromExistingMidpoint()
    {
        var existing = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("baseline"), new Point2(0, 0), new Point2(10, 0))
        };
        var created = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("upright"), new Point2(5, 0), new Point2(5, 4))
        };

        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion(
            "line",
            existing,
            created,
            CreateConstraintId);

        constraints.Should().Contain(constraint =>
            constraint.Kind == SketchConstraintKind.Midpoint
            && constraint.ReferenceKeys.SequenceEqual(new[] { "baseline", "upright:start" }));
        constraints.Should().Contain(constraint =>
            constraint.Kind == SketchConstraintKind.Perpendicular
            && constraint.ReferenceKeys.SequenceEqual(new[] { "baseline", "upright" }));
    }

    [Fact]
    public void CreatesCoincidentAndTangentConstraintsForInsertedTangentArc()
    {
        var existing = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("previous"), new Point2(0, 0), new Point2(10, 0))
        };
        var created = new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc"), new Point2(10, 5), 5, -90, 0)
        };

        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion(
            "tangentarc",
            existing,
            created,
            CreateConstraintId);

        constraints.Should().Contain(constraint =>
            constraint.Kind == SketchConstraintKind.Coincident
            && constraint.ReferenceKeys.SequenceEqual(new[] { "previous:end", "arc:start" }));
        constraints.Should().Contain(constraint =>
            constraint.Kind == SketchConstraintKind.Tangent
            && constraint.ReferenceKeys.SequenceEqual(new[] { "previous", "arc" }));
    }

    private static string CreateConstraintId(SketchConstraintKind kind) => $"constraint-{kind}-{Guid.NewGuid():N}";
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class LineRelationPickOrderTests
{
    [Theory]
    [InlineData(SketchConstraintKind.Vertical)]
    [InlineData(SketchConstraintKind.Horizontal)]
    public void SmallSlantedLineCannotMasqueradeAsAxisAligned(SketchConstraintKind kind)
    {
        var line = new LineEntity(EntityId.Create("a"), new(0, 0), kind == SketchConstraintKind.Vertical
            ? new(0.0000005, 0.000002) : new(0.000002, 0.0000005));
        SketchConstraintService.ValidateConstraint(new DrawingDocument(new[] { line }),
            new("axis", kind, new[] { "a" })).State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    public static IEnumerable<object[]> Cases =>
        from vertical in new[] { false, true }
        from perpendicular in new[] { false, true }
        from reverse in new[] { false, true }
        select new object[] { vertical, perpendicular, reverse };

    [Theory]
    [MemberData(nameof(Cases))]
    public void RelationUsesFreePeerRegardlessOfPickOrder(bool vertical, bool perpendicular, bool reverse)
    {
        var a = new LineEntity(EntityId.Create("a"), new(0, 0), vertical ? new(0, 5) : new(5, 0));
        var b = new LineEntity(EntityId.Create("b"), new(4, 5), new(7, 0));
        var length = new SketchDimension("length", SketchDimensionKind.LinearDistance,
            new[] { "b:start", "b:end" }, Math.Sqrt(34), isDriving: true);
        var document = new DrawingDocument(new DrawingEntity[] { a, b }, new[] { length }, new[] {
            new SketchConstraint("axis", vertical ? SketchConstraintKind.Vertical : SketchConstraintKind.Horizontal,
                new[] { "a" }, SketchConstraintState.Satisfied) });
        var relation = new SketchConstraint("relation", perpendicular ? SketchConstraintKind.Perpendicular : SketchConstraintKind.Parallel,
            reverse ? new[] { "b", "a" } : new[] { "a", "b" });
        var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, new[] { relation }, Array.Empty<SketchDimension>()));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Entities[0].Should().Be(a);
        result.Document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        result.Document.Constraints.Single(c => c.Id == "relation").ReferenceKeys.Should().Equal(relation.ReferenceKeys);
        result.Document.Dimensions.Single().Value.Should().Be(length.Value);
        SketchDimensionSolverService.GetDimensionState(result.Document, length).Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConflictingAxisPeersRemainUnchanged(bool reverse)
    {
        var entities = new DrawingEntity[] {
            new LineEntity(EntityId.Create("a"), new(0, 0), new(0, 5)),
            new LineEntity(EntityId.Create("b"), new(4, 5), new(7, 5)) };
        var document = new DrawingDocument(entities, Array.Empty<SketchDimension>(), new[] {
            new SketchConstraint("vertical", SketchConstraintKind.Vertical, new[] { "a" }, SketchConstraintState.Satisfied),
            new SketchConstraint("horizontal", SketchConstraintKind.Horizontal, new[] { "b" }, SketchConstraintState.Satisfied) });
        var relation = new SketchConstraint("relation", SketchConstraintKind.Parallel, reverse ? new[] { "b", "a" } : new[] { "a", "b" });
        var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, new[] { relation }, Array.Empty<SketchDimension>()));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(entities);
        result.Document.Constraints.Single(c => c.Id == "relation").State.Should().Be(SketchConstraintState.Unsatisfied);
        result.Document.Constraints.Where(c => c.Id != "relation").Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        var recovered = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(
            new DrawingDocument(entities, Array.Empty<SketchDimension>(), document.Constraints.Where(c => c.Id != "horizontal")),
            new[] { relation }, Array.Empty<SketchDimension>()));
        recovered.Status.Should().Be(SketchSolveStatus.Solved);
        recovered.Document.Entities[0].Should().Be(entities[0]);
    }

    [Theory]
    [InlineData(0.00001)]
    [InlineData(10000.0)]
    public void AlternatePeerSolveIsScaleIndependent(double scale)
    {
        var document = new DrawingDocument(new DrawingEntity[] {
            new LineEntity(EntityId.Create("a"), new(0, 0), new(0, 5 * scale)),
            new LineEntity(EntityId.Create("b"), new(4 * scale, 5 * scale), new(7 * scale, 0)) },
            Array.Empty<SketchDimension>(), new[] { new SketchConstraint("axis", SketchConstraintKind.Vertical, new[] { "a" }, SketchConstraintState.Satisfied) });
        foreach (var keys in new[] { new[] { "a", "b" }, new[] { "b", "a" } })
        {
            var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document,
                new[] { new SketchConstraint("parallel", SketchConstraintKind.Parallel, keys) }, Array.Empty<SketchDimension>()));
            result.Status.Should().Be(SketchSolveStatus.Solved);
            result.Document.Entities[0].Should().Be(document.Entities[0]);
        }
    }
}

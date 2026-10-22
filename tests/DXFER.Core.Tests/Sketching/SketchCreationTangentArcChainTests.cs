using DXFER.Blazor.Interop;
using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchCreationTangentArcChainTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void QuarterArcChainHasBothTangentJoinsAndOwnCenterQuadrant(bool clockwise, bool vertical)
    {
        var sign = clockwise ? -1 : 1;
        Point2 P(double x, double y) => vertical ? new(7 - y, 11 + x) : new(7 + x, 11 + y);
        var incoming = new LineEntity(EntityId.Create("incoming"), P(0, 0), P(10, 0));
        var arc = SketchCreationEntityFactory.CreateEntitiesForTool("tangentarc",
            [P(10, 0), P(11, 0), P(15, sign * 5)], _ => EntityId.Create("arc"), false).Single();
        var snap = new CanvasCreationSnapDto(P(15, sign * 5), vertical ? "ArcQuadrantVertical" : "ArcQuadrantHorizontal", []);
        var arcConstraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion("tangentarc", [incoming], [arc], NextId,
            creationSnaps: [snap, snap]);
        AssertRelation(arcConstraints, SketchConstraintKind.Coincident, "incoming:end", clockwise ? "arc:end" : "arc:start");
        AssertRelation(arcConstraints, SketchConstraintKind.Tangent, "incoming", "arc");
        AssertRelation(arcConstraints, vertical ? SketchConstraintKind.Vertical : SketchConstraintKind.Horizontal,
            "arc:center", clockwise ? "arc:start" : "arc:end");

        var outgoing = new LineEntity(EntityId.Create("outgoing"), P(15, sign * 5), P(15, sign * 10));
        var lineConstraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion("line", [incoming, arc], [outgoing], NextId);
        AssertRelation(lineConstraints, SketchConstraintKind.Coincident, clockwise ? "arc:start" : "arc:end", "outgoing:start");
        AssertRelation(lineConstraints, SketchConstraintKind.Tangent, "arc", "outgoing");
        var document = new DrawingDocument([incoming, arc, outgoing]);
        foreach (var constraint in arcConstraints.Concat(lineConstraints))
            SketchConstraintService.ValidateConstraint(document, constraint).State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void AutomaticConstraintsOffOmitsQuadrantAndBothInferredJoins()
    {
        var incoming = new LineEntity(EntityId.Create("incoming"), new(0, 0), new(10, 0));
        var arc = new ArcEntity(EntityId.Create("arc"), new(10, 5), 5, -90, 0);
        var outgoing = new LineEntity(EntityId.Create("outgoing"), new(15, 5), new(15, 10));
        SketchCreationConstraintFactory.CreateConstraintsForInsertion("tangentarc", [incoming], [arc], NextId,
            creationSnaps: [new(new(15, 5), "ArcQuadrantHorizontal", [])], automaticSnapConstraints: false).Should().BeEmpty();
        SketchCreationConstraintFactory.CreateConstraintsForInsertion("line", [incoming, arc], [outgoing], NextId,
            automaticSnapConstraints: false).Should().BeEmpty();
    }

    [Theory]
    [InlineData("ArcQuadrantHorizontal", 10, 5)]
    [InlineData("ArcQuadrantHorizontal", 15, 6)]
    [InlineData("ArcQuadrantVertical", 15, 5)]
    [InlineData("ArcQuadrantHorizontal", double.NaN, 5)]
    public void InvalidQuadrantIntentRefusesInsteadOfInventingAReference(string kind, double x, double y)
    {
        Action create = () => SketchCreationConstraintFactory.CreateConstraintsForInsertion("tangentarc", [],
            [new ArcEntity(EntityId.Create("arc"), new(10, 5), 5, -90, 0)], NextId,
            creationSnaps: [new(new(x, y), kind, [])]);
        create.Should().Throw<InvalidOperationException>().WithMessage("*Geometry unchanged*");
    }

    [Theory]
    [InlineData("threepointarc", false)]
    [InlineData("tangentarc", true)]
    public void QuadrantIntentRejectsWrongToolOrExternalReference(string tool, bool externalReference)
    {
        Action create = () => SketchCreationConstraintFactory.CreateConstraintsForInsertion(tool, [],
            [new ArcEntity(EntityId.Create("arc"), new(10, 5), 5, -90, 0)], NextId,
            creationSnaps: [new(new(15, 5), "ArcQuadrantHorizontal", externalReference ? ["missing"] : [])]);
        create.Should().Throw<InvalidOperationException>().WithMessage("*Geometry unchanged*");
    }

    [Fact]
    public void NonTangentContinuationKeepsCoincidenceWithoutInventingTangency()
    {
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion("line",
            [new ArcEntity(EntityId.Create("arc"), new(10, 5), 5, -90, 0)],
            [new LineEntity(EntityId.Create("outgoing"), new(15, 5), new(20, 10))], NextId);
        AssertRelation(constraints, SketchConstraintKind.Coincident, "arc:end", "outgoing:start");
        constraints.Should().NotContain(c => c.Kind == SketchConstraintKind.Tangent);
    }

    private static void AssertRelation(IEnumerable<SketchConstraint> constraints, SketchConstraintKind kind, params string[] keys) =>
        constraints.Should().ContainSingle(c => c.Kind == kind && c.ReferenceKeys.SequenceEqual(keys));
    private static string NextId(SketchConstraintKind kind) => $"{kind}-{Guid.NewGuid():N}";
}

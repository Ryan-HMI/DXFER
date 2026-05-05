using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchConstraintServiceTests
{
    [Fact]
    public void AppliesCoincidentPointConstraint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("a"), new Point2(1, 2), new Point2(5, 2)),
            new LineEntity(EntityId.Create("b"), new Point2(7, 8), new Point2(9, 8))
        });

        var solved = SketchConstraintService.ApplyConstraint(
            document,
            Constraint("coincident", SketchConstraintKind.Coincident, "a:start", "b:start"));

        var b = solved.Entities[1].Should().BeOfType<LineEntity>().Subject;
        b.Start.Should().Be(new Point2(1, 2));
        solved.Constraints.Should().ContainSingle()
            .Which.State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void AppliesCoincidentConstraintToPointEntities()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PointEntity(EntityId.Create("anchor"), new Point2(1, 2)),
            new PointEntity(EntityId.Create("driven"), new Point2(5, 6))
        });

        var solved = SketchConstraintService.ApplyConstraint(
            document,
            Constraint("coincident", SketchConstraintKind.Coincident, "anchor", "driven"));

        solved.Entities.OfType<PointEntity>()
            .Single(point => point.Id.Value == "driven")
            .Location.Should().Be(new Point2(1, 2));
        solved.Constraints.Should().ContainSingle()
            .Which.State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void AppliesHorizontalLineConstraintAndPreservesConstruction()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(
                EntityId.Create("edge"),
                new Point2(1, 2),
                new Point2(5, 7),
                IsConstruction: true)
        });

        var solved = SketchConstraintService.ApplyConstraint(
            document,
            Constraint("horizontal", SketchConstraintKind.Horizontal, "edge"));

        var edge = solved.Entities[0].Should().BeOfType<LineEntity>().Subject;
        edge.End.Should().Be(new Point2(5, 2));
        edge.IsConstruction.Should().BeTrue();
    }

    [Fact]
    public void AppliesVerticalPointPairConstraint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(1, 2), new Point2(5, 7))
        });

        var solved = SketchConstraintService.ApplyConstraint(
            document,
            Constraint("vertical", SketchConstraintKind.Vertical, "edge:start", "edge:end"));

        var edge = solved.Entities[0].Should().BeOfType<LineEntity>().Subject;
        edge.End.Should().Be(new Point2(1, 7));
    }

    [Fact]
    public void AppliesParallelLineConstraint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("anchor"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("driven"), new Point2(1, 1), new Point2(1, 6))
        });

        var solved = SketchConstraintService.ApplyConstraint(
            document,
            Constraint("parallel", SketchConstraintKind.Parallel, "anchor", "driven"));

        var driven = solved.Entities[1].Should().BeOfType<LineEntity>().Subject;
        driven.Start.Should().Be(new Point2(1, 1));
        driven.End.Should().Be(new Point2(6, 1));
    }

    [Fact]
    public void AppliesPerpendicularLineConstraint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("anchor"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("driven"), new Point2(1, 1), new Point2(6, 1))
        });

        var solved = SketchConstraintService.ApplyConstraint(
            document,
            Constraint("perpendicular", SketchConstraintKind.Perpendicular, "anchor", "driven"));

        var driven = solved.Entities[1].Should().BeOfType<LineEntity>().Subject;
        driven.Start.Should().Be(new Point2(1, 1));
        driven.End.Should().Be(new Point2(1, 6));
    }

    [Fact]
    public void ValidatesTangentLineAndCircleConstraint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(-5, 0), new Point2(5, 0)),
            new CircleEntity(EntityId.Create("circle"), new Point2(0, 2), 2)
        });

        var validated = SketchConstraintService.ValidateConstraint(
            document,
            Constraint("tangent", SketchConstraintKind.Tangent, "edge", "circle"));

        validated.State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void ValidatesTangentCirclePairConstraint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("first"), new Point2(0, 0), 2),
            new ArcEntity(EntityId.Create("second"), new Point2(5, 0), 3, 0, 180)
        });

        var validated = SketchConstraintService.ValidateConstraint(
            document,
            Constraint("tangent", SketchConstraintKind.Tangent, "first", "second"));

        validated.State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void AppliesConcentricCircleAndArcConstraint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("circle"), new Point2(2, 3), 4),
            new ArcEntity(EntityId.Create("arc"), new Point2(9, 10), 5, 0, 90)
        });

        var solved = SketchConstraintService.ApplyConstraint(
            document,
            Constraint("concentric", SketchConstraintKind.Concentric, "circle", "arc"));

        solved.Entities[1].Should().BeOfType<ArcEntity>()
            .Which.Center.Should().Be(new Point2(2, 3));
    }

    [Fact]
    public void AppliesEqualLineLengthConstraint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("anchor"), new Point2(0, 0), new Point2(6, 8)),
            new LineEntity(EntityId.Create("driven"), new Point2(1, 1), new Point2(4, 1))
        });

        var solved = SketchConstraintService.ApplyConstraint(
            document,
            Constraint("equal-lines", SketchConstraintKind.Equal, "anchor", "driven"));

        var driven = solved.Entities[1].Should().BeOfType<LineEntity>().Subject;
        Distance(driven.Start, driven.End).Should().BeApproximately(10, 0.0001);
        driven.End.Should().Be(new Point2(11, 1));
    }

    [Fact]
    public void AppliesEqualCircleAndArcRadiusConstraint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("circle"), new Point2(2, 3), 7),
            new ArcEntity(EntityId.Create("arc"), new Point2(9, 10), 2, 0, 90)
        });

        var solved = SketchConstraintService.ApplyConstraint(
            document,
            Constraint("equal-radii", SketchConstraintKind.Equal, "circle", "arc"));

        solved.Entities[1].Should().BeOfType<ArcEntity>()
            .Which.Radius.Should().Be(7);
    }

    [Fact]
    public void AppliesMidpointPointToLineConstraint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 4)),
            new LineEntity(EntityId.Create("marker"), new Point2(1, 1), new Point2(2, 1))
        });

        var solved = SketchConstraintService.ApplyConstraint(
            document,
            Constraint("midpoint", SketchConstraintKind.Midpoint, "edge", "marker:start"));

        solved.Entities[1].Should().BeOfType<LineEntity>()
            .Which.Start.Should().Be(new Point2(5, 2));
    }

    [Fact]
    public void FixConstraintPreventsLaterConstraintFromMovingFixedReferenceWhereFeasible()
    {
        var fix = Constraint("fix-end", SketchConstraintKind.Fix, "edge:end");
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 4), new Point2(5, 8))
            },
            Array.Empty<SketchDimension>(),
            new[] { fix });

        var solved = SketchConstraintService.ApplyConstraint(
            document,
            Constraint("horizontal", SketchConstraintKind.Horizontal, "edge"));

        var edge = solved.Entities[0].Should().BeOfType<LineEntity>().Subject;
        edge.End.Should().Be(new Point2(5, 8));
        edge.Start.Should().Be(new Point2(0, 8));
        solved.Constraints.Select(constraint => constraint.Id)
            .Should().Equal("fix-end", "horizontal");
    }

    [Fact]
    public void ValidateConstraintReportsUnsatisfiedWithoutChangingDocument()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("a"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("b"), new Point2(0, 0), new Point2(0, 10))
        });

        var validated = SketchConstraintService.ValidateConstraint(
            document,
            Constraint("parallel", SketchConstraintKind.Parallel, "a", "b"));

        validated.State.Should().Be(SketchConstraintState.Unsatisfied);
        document.Entities[1].Should().BeOfType<LineEntity>()
            .Which.End.Should().Be(new Point2(0, 10));
    }

    private static SketchConstraint Constraint(
        string id,
        SketchConstraintKind kind,
        params string[] referenceKeys) =>
        new(id, kind, referenceKeys);

    private static double Distance(Point2 first, Point2 second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }
}

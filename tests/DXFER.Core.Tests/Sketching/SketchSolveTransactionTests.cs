using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchSolveTransactionTests
{
    private readonly ISketchSolver solver = new LegacySketchSolverAdapter();

    [Fact]
    public void HorizontalRetainsExistingDrivingLength()
    {
        var length = new SketchDimension("length", SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" }, 5, isDriving: true);
        var original = new DrawingDocument(new DrawingEntity[]
            { new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(4, 3)) },
            new[] { length }, Array.Empty<SketchConstraint>());
        var horizontal = new SketchConstraint("horizontal", SketchConstraintKind.Horizontal, new[] { "edge" });

        var result = solver.Solve(new SketchSolveRequest(original, new[] { horizontal }, Array.Empty<SketchDimension>()));

        result.Status.Should().Be(SketchSolveStatus.Solved);
        ((LineEntity)result.Document.Entities.Single()).End.Should().Be(new Point2(5, 0));
        SketchDimensionSolverService.GetDimensionState(result.Document, length).Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void MidpointPropagatesToExistingCoincidentCorner()
    {
        var original = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("bottom"), new(0, 0), new(10, 0)),
            new LineEntity(EntityId.Create("left"), new(0, 0), new(0, 10)),
            new LineEntity(EntityId.Create("target"), new(20, 20), new(24, 22))
        }, Array.Empty<SketchDimension>(), new[]
        {
            new SketchConstraint("corner", SketchConstraintKind.Coincident,
                new[] { "bottom:start", "left:start" }, SketchConstraintState.Satisfied)
        });
        var midpoint = new SketchConstraint("midpoint", SketchConstraintKind.Midpoint,
            new[] { "target", "bottom:start" });

        var result = solver.Solve(new SketchSolveRequest(original, new[] { midpoint }, Array.Empty<SketchDimension>()));

        result.Status.Should().Be(SketchSolveStatus.Solved);
        ((LineEntity)result.Document.Entities[0]).Start.Should().Be(new Point2(22, 21));
        ((LineEntity)result.Document.Entities[1]).Start.Should().Be(new Point2(22, 21));
        result.Document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void MidpointKeepsAnAxisConstrainedRectangleClosed()
    {
        var original = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("bottom"), new(0, 0), new(10, 0)),
            new LineEntity(EntityId.Create("right"), new(10, 0), new(10, 10)),
            new LineEntity(EntityId.Create("top"), new(10, 10), new(0, 10)),
            new LineEntity(EntityId.Create("left"), new(0, 10), new(0, 0)),
            new LineEntity(EntityId.Create("target"), new(20, 20), new(24, 22))
        }, Array.Empty<SketchDimension>(), new[]
        {
            new SketchConstraint("bl", SketchConstraintKind.Coincident, new[] { "bottom:start", "left:end" }),
            new SketchConstraint("br", SketchConstraintKind.Coincident, new[] { "bottom:end", "right:start" }),
            new SketchConstraint("tr", SketchConstraintKind.Coincident, new[] { "right:end", "top:start" }),
            new SketchConstraint("tl", SketchConstraintKind.Coincident, new[] { "top:end", "left:start" }),
            new SketchConstraint("bh", SketchConstraintKind.Horizontal, new[] { "bottom" }),
            new SketchConstraint("th", SketchConstraintKind.Horizontal, new[] { "top" }),
            new SketchConstraint("lv", SketchConstraintKind.Vertical, new[] { "left" }),
            new SketchConstraint("rv", SketchConstraintKind.Vertical, new[] { "right" })
        });
        var midpoint = new SketchConstraint("midpoint", SketchConstraintKind.Midpoint,
            new[] { "target", "bottom:start" });

        var result = solver.Solve(new SketchSolveRequest(original, new[] { midpoint }, Array.Empty<SketchDimension>()));

        result.Status.Should().Be(SketchSolveStatus.Solved);
        ((LineEntity)result.Document.Entities[0]).Start.Should().Be(new Point2(22, 21));
        result.Document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void LaterConstraintRevalidatesEarlierConstraintInsteadOfReportingStaleSuccess()
    {
        var a = new LineEntity(EntityId.Create("a"), new Point2(0, 0), new Point2(10, 0));
        var b = new LineEntity(EntityId.Create("b"), new Point2(0, 5), new Point2(10, 5));
        var parallel = new SketchConstraint("parallel", SketchConstraintKind.Parallel,
            new[] { "a", "b" }, SketchConstraintState.Satisfied);
        var perpendicular = new SketchConstraint("perpendicular", SketchConstraintKind.Perpendicular,
            new[] { "a", "b" });
        var original = new DrawingDocument(new DrawingEntity[] { a, b },
            Array.Empty<SketchDimension>(), new[] { parallel });

        var result = solver.Solve(new SketchSolveRequest(original, new[] { perpendicular }, Array.Empty<SketchDimension>()));

        result.Status.Should().Be(SketchSolveStatus.OverConstrained);
        result.Document.Entities.Should().Equal(original.Entities, "an unsuccessful solve must be atomic");
        result.Document.Constraints.Should().Contain(c => c.Id == "parallel" && c.State == SketchConstraintState.Satisfied);
        result.Document.Constraints.Should().Contain(c => c.Id == "perpendicular" && c.State == SketchConstraintState.Unsatisfied);
        result.AffectedDiagnostics.Should().Contain(d => d.ItemId == "perpendicular");
        result.Document.Metadata.Should().BeSameAs(original.Metadata);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContradictoryAxisConstraintsDoNotCollapseLineOrPolylineSegment(bool polyline)
    {
        DrawingEntity entity = polyline
            ? new PolylineEntity(EntityId.Create("edge"), new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 5) })
            : new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0));
        var reference = polyline ? "edge|segment|0" : "edge";
        var horizontal = new SketchConstraint("horizontal", SketchConstraintKind.Horizontal,
            new[] { reference }, SketchConstraintState.Satisfied);
        var original = new DrawingDocument(new[] { entity }, Array.Empty<SketchDimension>(), new[] { horizontal });
        var vertical = new SketchConstraint("vertical", SketchConstraintKind.Vertical, new[] { reference });

        var result = solver.Solve(new SketchSolveRequest(original, new[] { vertical }, Array.Empty<SketchDimension>()));

        result.Status.Should().Be(SketchSolveStatus.OverConstrained);
        result.Document.Entities.Should().Equal(original.Entities);
        result.Document.Constraints.Should().Contain(c => c.Id == "vertical" && c.State == SketchConstraintState.Unsatisfied);
    }

    [Fact]
    public void ConflictingDrivingDimensionsRetainRequestedValueButNotInvalidGeometry()
    {
        var length = new SketchDimension("length", SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" }, 10, isDriving: true);
        var original = new DrawingDocument(new DrawingEntity[]
            { new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0)) },
            new[] { length }, Array.Empty<SketchConstraint>());
        var conflict = new SketchDimension("conflict", SketchDimensionKind.LinearDistance,
            length.ReferenceKeys, 20, isDriving: true);

        var result = solver.Solve(new SketchSolveRequest(original, Array.Empty<SketchConstraint>(), new[] { conflict }));

        result.Status.Should().Be(SketchSolveStatus.OverConstrained);
        result.Document.Entities.Should().Equal(original.Entities);
        result.Document.Dimensions.Should().Contain(d => d.Id == "conflict" && d.Value == 20);
        result.AffectedDiagnostics.Should().Contain(d => d.ItemId == "conflict");
        SketchDimensionSolverService.GetDimensionState(result.Document, length).Should().Be(SketchConstraintState.Satisfied);

        var recovered = new DrawingDocument(result.Document.Entities,
            result.Document.Dimensions.Where(d => d.Id != "conflict"), result.Document.Constraints);
        solver.Solve(SketchSolveRequest.FromDocument(recovered)).Status.Should().Be(SketchSolveStatus.Solved);
    }

    [Fact]
    public void EditingExistingDimensionIsNotMistakenForAddingAnotherConstraint()
    {
        var originalDimension = new SketchDimension("length", SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" }, 10, isDriving: true);
        var original = new DrawingDocument(new DrawingEntity[]
            { new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0)) },
            new[] { originalDimension }, Array.Empty<SketchConstraint>());
        var update = new SketchDimension("length", originalDimension.Kind, originalDimension.ReferenceKeys, 20, isDriving: true);

        var result = solver.Solve(new SketchSolveRequest(original, Array.Empty<SketchConstraint>(), new[] { update }));

        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Dimensions.Should().ContainSingle().Which.Value.Should().Be(20);
        ((LineEntity)result.Document.Entities.Single()).End.Should().Be(new Point2(20, 0));
    }

    [Fact]
    public void SuppressedConstraintDoesNotBlockAValidEdit()
    {
        var original = new DrawingDocument(new DrawingEntity[]
            { new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 10)) },
            Array.Empty<SketchDimension>(), new[] { new SketchConstraint("horizontal", SketchConstraintKind.Horizontal,
                new[] { "edge" }, SketchConstraintState.Suppressed) });
        var vertical = new SketchConstraint("vertical", SketchConstraintKind.Vertical, new[] { "edge" });

        var result = solver.Solve(new SketchSolveRequest(original, new[] { vertical }, Array.Empty<SketchDimension>()));

        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Constraints.Should().Contain(c => c.Id == "horizontal" && c.State == SketchConstraintState.Suppressed);
    }
}

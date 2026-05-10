using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchSolveWorkflowTests
{
    [Fact]
    public void ApplyDimensionEditRoutesUpdatedDimensionThroughSolverAndAppliesSolvedDocument()
    {
        var document = CreateDimensionedLineDocument(4);
        var nextDimension = new SketchDimension(
            "distance",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            8,
            isDriving: true);
        var solvedDocument = CreateDimensionedLineDocument(8);
        var solver = new CapturingSolver(new SketchSolveResult(SketchSolveStatus.Solved, solvedDocument));

        var result = SketchSolveWorkflow.ApplyDimensionEdit(document, nextDimension, solver);

        result.Applied.Should().BeTrue();
        result.Document.Should().BeSameAs(solvedDocument);
        result.SolveResult.Status.Should().Be(SketchSolveStatus.Solved);
        solver.Request.Should().NotBeNull();
        solver.Request!.Document.Should().BeSameAs(document);
        solver.Request.Constraints.Should().Equal(document.Constraints);
        solver.Request.Dimensions.Should().ContainSingle(dimension =>
            dimension.Id == "distance"
            && dimension.Value == 8
            && dimension.ReferenceKeys.SequenceEqual(new[] { "edge:start", "edge:end" }));
    }

    [Fact]
    public void ApplyDimensionEditSendsInitialGuessesForReferencedGeometry()
    {
        var document = CreateDimensionedLineDocument(4);
        var nextDimension = new SketchDimension(
            "distance",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            8,
            isDriving: true);
        var solver = new CapturingSolver(new SketchSolveResult(SketchSolveStatus.Solved, document));

        SketchSolveWorkflow.ApplyDimensionEdit(document, nextDimension, solver);

        solver.Request.Should().NotBeNull();
        solver.Request!.InitialGuesses.Should().ContainKey("edge:start")
            .WhoseValue.Points.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, Point2>("point", new Point2(0, 0)));
        solver.Request.InitialGuesses.Should().ContainKey("edge:end")
            .WhoseValue.Points.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, Point2>("point", new Point2(4, 0)));
        solver.Request.InitialGuesses.Should().ContainKey("edge")
            .WhoseValue.Points.Should().Equal(new[]
            {
                new KeyValuePair<string, Point2>("start", new Point2(0, 0)),
                new KeyValuePair<string, Point2>("end", new Point2(4, 0))
            });
    }

    [Fact]
    public void ApplyDimensionOmitsInitialGuessesForUnresolvedReferences()
    {
        var document = CreateDimensionedLineDocument(4);
        var dimension = new SketchDimension(
            "missing",
            SketchDimensionKind.LinearDistance,
            new[] { "missing:start", "edge:end" },
            8,
            isDriving: true);
        var solver = new CapturingSolver(new SketchSolveResult(SketchSolveStatus.Solved, document));

        SketchSolveWorkflow.ApplyDimension(document, dimension, solver);

        solver.Request.Should().NotBeNull();
        solver.Request!.InitialGuesses.Should().NotContainKey("missing:start");
        solver.Request.InitialGuesses.Should().ContainKey("edge:end");
    }

    [Fact]
    public void ApplyDimensionEditRejectsFailedSolverResultAndPreservesOriginalDocument()
    {
        var document = CreateDimensionedLineDocument(4);
        var nextDimension = new SketchDimension(
            "distance",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            12,
            isDriving: true);
        var mutatedDocument = CreateDimensionedLineDocument(12);
        var solver = new CapturingSolver(new SketchSolveResult(
            SketchSolveStatus.Overconstrained,
            mutatedDocument,
            new[] { "Dimension 'distance' conflicts with fixed references." }));

        var result = SketchSolveWorkflow.ApplyDimensionEdit(document, nextDimension, solver);

        result.Applied.Should().BeFalse();
        result.Document.Should().BeSameAs(document);
        result.SolveResult.Document.Should().BeSameAs(mutatedDocument);
        result.FailureMessage.Should().Contain("Overconstrained");
        result.FailureMessage.Should().Contain("distance");
    }

    [Fact]
    public void ApplyDimensionRoutesNewDimensionThroughSolverAndAppliesUnderconstrainedDocument()
    {
        var document = CreateConstrainedLineDocument();
        var dimension = new SketchDimension(
            "distance",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            6,
            isDriving: true);
        var solvedDocument = CreateDimensionedLineDocument(6);
        var solver = new CapturingSolver(new SketchSolveResult(SketchSolveStatus.Underconstrained, solvedDocument));

        var result = SketchSolveWorkflow.ApplyDimension(document, dimension, solver);

        result.Applied.Should().BeTrue();
        result.Document.Should().BeSameAs(solvedDocument);
        result.SolveResult.Status.Should().Be(SketchSolveStatus.Underconstrained);
        solver.Request.Should().NotBeNull();
        solver.Request!.Document.Should().BeSameAs(document);
        solver.Request.Constraints.Should().Equal(document.Constraints);
        solver.Request.Dimensions.Should().ContainSingle(dimension =>
            dimension.Id == "distance"
            && dimension.Value == 6
            && dimension.ReferenceKeys.SequenceEqual(new[] { "edge:start", "edge:end" }));
    }

    [Theory]
    [InlineData(SketchSolveStatus.Failed)]
    [InlineData(SketchSolveStatus.Unavailable)]
    [InlineData(SketchSolveStatus.Overconstrained)]
    [InlineData(SketchSolveStatus.Unsupported)]
    [InlineData(SketchSolveStatus.InvalidInput)]
    public void ApplyDimensionRejectsNonApplicableSolverResultsAndPreservesOriginalDocument(SketchSolveStatus status)
    {
        var document = CreateDimensionedLineDocument(4);
        var nextDimension = new SketchDimension(
            "distance",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            12,
            isDriving: true);
        var mutatedDocument = CreateDimensionedLineDocument(12);
        var solver = new CapturingSolver(new SketchSolveResult(
            status,
            mutatedDocument,
            new[] { $"Dimension solve returned {status}." }));

        var result = SketchSolveWorkflow.ApplyDimension(document, nextDimension, solver);

        result.Applied.Should().BeFalse();
        result.Document.Should().BeSameAs(document);
        result.SolveResult.Document.Should().BeSameAs(mutatedDocument);
        result.FailureMessage.Should().Contain(status.ToString());
        result.FailureMessage.Should().Contain("Dimension solve");
    }

    [Fact]
    public void ApplyConstraintRoutesNewConstraintThroughSolverAndAppliesSolvedDocument()
    {
        var document = CreateDimensionedLineDocument(4);
        var constraint = new SketchConstraint(
            "edge-horizontal",
            SketchConstraintKind.Horizontal,
            new[] { "edge" });
        var solvedDocument = CreateConstrainedLineDocument();
        var solver = new CapturingSolver(new SketchSolveResult(SketchSolveStatus.Solved, solvedDocument));

        var result = SketchSolveWorkflow.ApplyConstraint(document, constraint, solver);

        result.Applied.Should().BeTrue();
        result.Document.Should().BeSameAs(solvedDocument);
        result.SolveResult.Status.Should().Be(SketchSolveStatus.Solved);
        solver.Request.Should().NotBeNull();
        solver.Request!.Document.Should().BeSameAs(document);
        solver.Request.Dimensions.Should().Equal(document.Dimensions);
        solver.Request.Constraints.Should().ContainSingle(constraint =>
            constraint.Id == "edge-horizontal"
            && constraint.Kind == SketchConstraintKind.Horizontal
            && constraint.ReferenceKeys.SequenceEqual(new[] { "edge" }));
    }

    [Fact]
    public void ApplyConstraintRoutesUpdatedConstraintThroughSolver()
    {
        var document = CreateConstrainedLineDocument();
        var nextConstraint = new SketchConstraint(
            "edge-horizontal",
            SketchConstraintKind.Fix,
            new[] { "edge:start" });
        var solvedDocument = CreateConstrainedLineDocument();
        var solver = new CapturingSolver(new SketchSolveResult(SketchSolveStatus.Underconstrained, solvedDocument));

        var result = SketchSolveWorkflow.ApplyConstraint(document, nextConstraint, solver);

        result.Applied.Should().BeTrue();
        solver.Request.Should().NotBeNull();
        solver.Request!.Constraints.Should().ContainSingle(constraint =>
            constraint.Id == "edge-horizontal"
            && constraint.Kind == SketchConstraintKind.Fix
            && constraint.ReferenceKeys.SequenceEqual(new[] { "edge:start" }));
    }

    [Fact]
    public void ApplyConstraintsRoutesConstraintBatchThroughSolver()
    {
        var document = CreateDimensionedLineDocument(4);
        var constraints = new[]
        {
            new SketchConstraint("edge-horizontal", SketchConstraintKind.Horizontal, new[] { "edge" }),
            new SketchConstraint("edge-fix", SketchConstraintKind.Fix, new[] { "edge:start" })
        };
        var solvedDocument = CreateConstrainedLineDocument();
        var solver = new CapturingSolver(new SketchSolveResult(SketchSolveStatus.Solved, solvedDocument));

        var result = SketchSolveWorkflow.ApplyConstraints(document, constraints, solver);

        result.Applied.Should().BeTrue();
        result.Document.Should().BeSameAs(solvedDocument);
        solver.Request.Should().NotBeNull();
        solver.Request!.Constraints.Select(constraint => constraint.Id)
            .Should().Equal("edge-horizontal", "edge-fix");
        solver.Request.InitialGuesses.Should().ContainKey("edge");
        solver.Request.InitialGuesses.Should().ContainKey("edge:start");
    }

    [Theory]
    [InlineData(SketchSolveStatus.Failed)]
    [InlineData(SketchSolveStatus.Unavailable)]
    [InlineData(SketchSolveStatus.Overconstrained)]
    [InlineData(SketchSolveStatus.Unsupported)]
    [InlineData(SketchSolveStatus.InvalidInput)]
    public void ApplyConstraintRejectsNonApplicableSolverResultsAndPreservesOriginalDocument(SketchSolveStatus status)
    {
        var document = CreateConstrainedLineDocument();
        var constraint = new SketchConstraint(
            "edge-horizontal",
            SketchConstraintKind.Horizontal,
            new[] { "edge" });
        var mutatedDocument = CreateDimensionedLineDocument(4);
        var solver = new CapturingSolver(new SketchSolveResult(
            status,
            mutatedDocument,
            new[] { $"Constraint solve returned {status}." }));

        var result = SketchSolveWorkflow.ApplyConstraint(document, constraint, solver);

        result.Applied.Should().BeFalse();
        result.Document.Should().BeSameAs(document);
        result.SolveResult.Document.Should().BeSameAs(mutatedDocument);
        result.FailureMessage.Should().Contain(status.ToString());
        result.FailureMessage.Should().Contain("Constraint solve");
    }

    private static DrawingDocument CreateDimensionedLineDocument(double length)
    {
        var dimension = new SketchDimension(
            "distance",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            length,
            isDriving: true);
        return new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(length, 0))
            },
            new[] { dimension },
            Array.Empty<SketchConstraint>());
    }

    private static DrawingDocument CreateConstrainedLineDocument()
    {
        var constraint = new SketchConstraint(
            "edge-horizontal",
            SketchConstraintKind.Horizontal,
            new[] { "edge" });
        return new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(4, 0))
            },
            Array.Empty<SketchDimension>(),
            new[] { constraint });
    }

    private sealed class CapturingSolver : ISketchSolver
    {
        private readonly SketchSolveResult _result;

        public CapturingSolver(SketchSolveResult result)
        {
            _result = result;
        }

        public SketchSolveRequest? Request { get; private set; }

        public SketchSolveResult Solve(SketchSolveRequest request)
        {
            Request = request;
            return _result;
        }
    }
}

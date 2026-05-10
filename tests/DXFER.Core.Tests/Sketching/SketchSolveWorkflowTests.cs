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

using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchSolverAbstractionTests
{
    [Fact]
    public void FallbackSolverPreservesExistingConstraintAndDimensionBehavior()
    {
        var fix = new SketchConstraint(
            "fix-start",
            SketchConstraintKind.Fix,
            new[] { "edge:start" },
            SketchConstraintState.Satisfied);
        var horizontal = new SketchConstraint(
            "horizontal",
            SketchConstraintKind.Horizontal,
            new[] { "edge" });
        var distance = new SketchDimension(
            "distance",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            10,
            isDriving: true);
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(3, 4))
            },
            Array.Empty<SketchDimension>(),
            new[] { fix });

        var expected = SketchDimensionSolverService.ApplyDimensions(
            SketchConstraintService.ApplyConstraints(document, new[] { horizontal }),
            new[] { distance });

        ISketchSolver solver = new LegacySketchSolverAdapter();

        var result = solver.Solve(new SketchSolveRequest(document, new[] { horizontal }, new[] { distance }));

        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(expected.Entities);
        result.Document.Dimensions.Should().BeEquivalentTo(expected.Dimensions);
        result.Document.Constraints.Should().BeEquivalentTo(expected.Constraints);
    }

    [Fact]
    public void SolveRequestCanRepresentDocumentEntitiesConstraintsAndDimensions()
    {
        var constraint = new SketchConstraint(
            "horizontal",
            SketchConstraintKind.Horizontal,
            new[] { "edge" },
            SketchConstraintState.Satisfied);
        var dimension = new SketchDimension(
            "distance",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            7,
            isDriving: true);
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(7, 0))
            },
            new[] { dimension },
            new[] { constraint });

        var request = SketchSolveRequest.FromDocument(document);

        request.Document.Should().BeSameAs(document);
        request.Entities.Should().Equal(document.Entities);
        request.Constraints.Should().Equal(document.Constraints);
        request.Dimensions.Should().Equal(document.Dimensions);
    }

    [Fact]
    public void SolveStatusCanRepresentFeatureScriptLifecycleOutcomes()
    {
        Enum.GetNames<SketchSolveStatus>().Should().Contain(new[]
        {
            nameof(SketchSolveStatus.Solved),
            nameof(SketchSolveStatus.Unavailable),
            nameof(SketchSolveStatus.Failed),
            "Underconstrained",
            "Overconstrained",
            "Unsupported",
            "InvalidInput"
        });
    }

    [Fact]
    public void SolveRequestCarriesDefensiveCopyOfInitialGuessesByStableReference()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(5, 0))
        });
        var guesses = new Dictionary<string, SketchInitialGuess>
        {
            ["edge:start"] = SketchInitialGuess.Point(new Point2(1, 2)),
            ["edge"] = SketchInitialGuess.Entity(new[]
            {
                new KeyValuePair<string, Point2>("start", new Point2(1, 2)),
                new KeyValuePair<string, Point2>("end", new Point2(6, 2))
            })
        };

        var request = new SketchSolveRequest(
            document,
            Array.Empty<SketchConstraint>(),
            Array.Empty<SketchDimension>(),
            guesses);
        guesses["edge:start"] = SketchInitialGuess.Point(new Point2(99, 99));

        request.InitialGuesses.Should().HaveCount(2);
        request.InitialGuesses["edge:start"].Points.Should().ContainSingle()
            .Which.Should().Be(new KeyValuePair<string, Point2>("point", new Point2(1, 2)));
        request.InitialGuesses["edge"].Points.Should().Equal(new[]
        {
            new KeyValuePair<string, Point2>("start", new Point2(1, 2)),
            new KeyValuePair<string, Point2>("end", new Point2(6, 2))
        });
    }

    [Fact]
    public void PlaneGcsAdapterIsReplaceableAndReportsUnavailableUntilWasmIsWired()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("circle"), new Point2(2, 3), 4)
        });
        ISketchSolver solver = new PlaneGcsSketchSolverAdapter();

        var result = solver.Solve(SketchSolveRequest.FromDocument(document));

        solver.Should().BeAssignableTo<ISketchSolver>();
        result.Status.Should().Be(SketchSolveStatus.Unavailable);
        result.Document.Should().BeSameAs(document);
        result.Diagnostics.Should().ContainSingle()
            .Which.Should().Contain("PlaneGCS");
    }

    [Fact]
    public void FallbackSolverReportsAffectedReferencesForUnsatisfiedDimensions()
    {
        var fixStart = new SketchConstraint(
            "fix-start",
            SketchConstraintKind.Fix,
            new[] { "edge:start" },
            SketchConstraintState.Satisfied);
        var fixEnd = new SketchConstraint(
            "fix-end",
            SketchConstraintKind.Fix,
            new[] { "edge:end" },
            SketchConstraintState.Satisfied);
        var distance = new SketchDimension(
            "distance",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            10,
            isDriving: true);
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(4, 0))
            },
            Array.Empty<SketchDimension>(),
            new[] { fixStart, fixEnd });
        ISketchSolver solver = new LegacySketchSolverAdapter();

        var result = solver.Solve(new SketchSolveRequest(document, new[] { fixStart, fixEnd }, new[] { distance }));

        result.Status.Should().Be(SketchSolveStatus.Failed);
        result.AffectedDiagnostics.Should().ContainSingle()
            .Which.Should().Match<SketchSolveDiagnostic>(diagnostic =>
                diagnostic.ItemId == "distance"
                && diagnostic.ItemKind == "dimension"
                && diagnostic.ReferenceKeys.SequenceEqual(new[] { "edge:start", "edge:end" }));
        result.Diagnostics.Should().ContainSingle()
            .Which.Should().Contain("distance");
    }
}

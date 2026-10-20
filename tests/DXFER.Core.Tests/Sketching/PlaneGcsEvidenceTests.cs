using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsEvidenceTests
{
    [Fact]
    public void NativeRedundancyEvidenceSurvivesSaveReopenAndHistorySnapshots()
    {
        var baseline = Axes();
        var result = RejectDiagonal(baseline);

        AssertRejected(result.Document);
        result.Document.Entities.Single().Should().BeSameAs(baseline.Entities.Single());
        result.Document.Metadata.Should().BeSameAs(baseline.Metadata);
        result.Document.SolveEvidence!.EngineStatus.Should().Be("solved");
        var reopened = DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(result.Document));
        AssertRejected(reopened);
        SketchSolveDiagnostics.FromDocument(reopened).Should().Contain(d =>
            d.ItemId == "diagonal" && d.Reason == SketchSolveDiagnosticReason.RedundantDrivingDimension);
        baseline.Dimensions.Should().HaveCount(2, "undo restores the original snapshot without attempted intent");
        PlaneGcsDiagnostics.FromDocument(baseline).Should().BeEmpty();
        AssertRejected(result.Document); // Redo restores the failed snapshot, not a reclassified document.
    }

    [Fact]
    public void ExistingNativeRedundantBasisIsPersistedAsUnknownNotFalseRedOrCleanAcceptance()
    {
        var original = Axes(includeDiagonal: true);
        SketchDimensionSolverService.GetDimensionState(original, Diagonal()).Should().Be(SketchConstraintState.Unsatisfied);
        var result = new PlaneGcsSketchSolverAdapter(new ReportingRuntime(["diagonal"]))
            .Solve(SketchSolveRequest.FromDocument(original));

        result.Status.Should().Be(SketchSolveStatus.Solved);
        var view = PlaneGcsDiagnostics.Read(result.Document);
        view.HasCurrentEvidence.Should().BeTrue();
        view.HasUnvalidatedIntent.Should().BeTrue();
        view.GetDimensionState(Diagonal()).Should().Be(SketchConstraintState.Unknown);
        view.Observations.Should().ContainSingle(d => d.ItemId == "diagonal"
            && d.Reason == SketchSolveDiagnosticReason.RedundantDrivingDimension);
        SketchDimensionSolverService.GetDimensionState(result.Document, Diagonal()).Should().Be(SketchConstraintState.Unknown);
        SketchSolveDiagnostics.FromDocument(result.Document).Should().BeEmpty();
        var reopened = DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(result.Document));
        SketchDimensionSolverService.GetDimensionState(reopened, Diagonal()).Should().Be(SketchConstraintState.Unknown);
        PlaneGcsDiagnostics.Read(reopened).Observations.Should().ContainSingle(d => d.ItemId == "diagonal");
        SketchSolveDiagnostics.FromDocument(reopened).Should().BeEmpty();
    }

    [Theory]
    [InlineData("geometry")]
    [InlineData("value")]
    [InlineData("references")]
    [InlineData("layer")]
    [InlineData("units")]
    public void SemanticChangesMakeEvidenceUnknownInsteadOfFallingBackToLegacyRank(string change)
    {
        var original = RejectDiagonal(Axes()).Document;
        var entities = original.Entities;
        var dimensions = original.Dimensions;
        var metadata = original.Metadata;
        if (change == "geometry") entities = [new PointEntity(EntityId.Create("point"), new(6, 8))];
        if (change == "value") dimensions = original.Dimensions.Select(d => d.Id == "diagonal"
            ? new SketchDimension(d.Id, d.Kind, d.ReferenceKeys, 6, d.Anchor, true) : d).ToArray();
        if (change == "references") dimensions = original.Dimensions.Select(d => d.Id == "diagonal"
            ? new SketchDimension(d.Id, d.Kind, d.ReferenceKeys.Reverse(), d.Value, d.Anchor, true) : d).ToArray();
        if (change == "layer") metadata = metadata with { Layers = new Dictionary<string, DxfEntityStyle> { ["0"] = new("0", IsLocked: true) } };
        if (change == "units") metadata = metadata with { Units = DrawingUnits.Millimeters };
        var changed = PlaneGcsDiagnostics.CarryEvidence(original, new DrawingDocument(entities, dimensions, original.Constraints, metadata));
        var reopened = DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(changed));
        var view = PlaneGcsDiagnostics.Read(reopened);

        view.HasCurrentEvidence.Should().BeFalse();
        view.HasUnvalidatedIntent.Should().BeTrue();
        view.Diagnostics.Should().BeEmpty("stale evidence must not manufacture red intent");
        view.GetDimensionState(reopened.Dimensions.Single(d => d.Id == "diagonal")).Should().Be(SketchConstraintState.Unknown);
        SketchDimensionSolverService.GetDimensionState(reopened, reopened.Dimensions.Single(d => d.Id == "diagonal"))
            .Should().Be(SketchConstraintState.Unknown);
    }

    [Fact]
    public void AnchorAndSourceProvenanceChangesDoNotInvalidateSemanticEvidence()
    {
        var original = RejectDiagonal(Axes()).Document;
        var dimensions = original.Dimensions.Select(d => new SketchDimension(d.Id, d.Kind, d.ReferenceKeys,
            d.Value, new(42, -7), d.IsDriving));
        var changed = PlaneGcsDiagnostics.CarryEvidence(original, new DrawingDocument(original.Entities, dimensions,
            original.Constraints, original.Metadata with { SourceFileName = "renamed.dxf", TrustedSource = false }));

        AssertRejected(changed);
        AssertRejected(DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(changed)));
    }

    [Fact]
    public void RejectedDrivingIdRemainsPendingUntilNativeReevaluationClearsIt()
    {
        var original = RejectDiagonal(Axes()).Document;
        var refusing = new ReportingRuntime(["diagonal"]);
        var retried = new PlaneGcsSketchSolverAdapter(refusing).Solve(SketchSolveRequest.FromDocument(original));
        refusing.Request!.AttemptedDrivingDimensionIds.Should().Contain("diagonal");
        retried.Status.Should().Be(SketchSolveStatus.Failed);
        retried.Document.Should().BeSameAs(original, "an unchanged failed retry adds no history");
        AssertRejected(retried.Document);

        var independent = new DrawingDocument(original.Entities, [Diagonal()], [], original.Metadata, original.SolveEvidence);
        var accepting = new ReportingRuntime([]);
        var solved = new PlaneGcsSketchSolverAdapter(accepting).Solve(SketchSolveRequest.FromDocument(independent));
        accepting.Request!.AttemptedDrivingDimensionIds.Should().Contain("diagonal");
        solved.Status.Should().Be(SketchSolveStatus.Solved);
        PlaneGcsDiagnostics.GetDimensionState(solved.Document, Diagonal()).Should().Be(SketchConstraintState.Satisfied);
        PlaneGcsDiagnostics.FromDocument(solved.Document).Should().BeEmpty();
    }

    [Fact]
    public void MissingEvidenceIsUnknownInExplicitPlaneGcsMode()
    {
        var original = Axes(includeDiagonal: true);
        PlaneGcsDiagnostics.GetDimensionState(original, Diagonal()).Should().Be(SketchConstraintState.Unknown);
        SketchSolveDiagnostics.FromDocument(original, usePlaneGcs: true).Should().BeEmpty();
        PlaneGcsDiagnostics.Read(original).HasUnvalidatedIntent.Should().BeTrue();
    }

    [Fact]
    public void MissingNativeRankReportDoesNotAssertCleanDrivingEvidence()
    {
        var result = new PlaneGcsSketchSolverAdapter(new ReportingRuntime(null))
            .Solve(SketchSolveRequest.FromDocument(Axes()));

        result.Status.Should().Be(SketchSolveStatus.Solved);
        var view = PlaneGcsDiagnostics.Read(result.Document);
        view.HasUnvalidatedIntent.Should().BeTrue();
        view.Observations.Should().HaveCount(2);
        result.Document.Dimensions.Should().OnlyContain(d => view.GetDimensionState(d) == SketchConstraintState.Unknown);
    }

    [Fact]
    public void TransportFailureDoesNotClaimNativeEngineEvidenceAndFailedDragKeepsSnapshot()
    {
        var original = Axes();
        var unavailable = new PlaneGcsSketchSolverAdapter();
        var failed = unavailable.Solve(new(original, [], [Diagonal()]));
        failed.Status.Should().Be(SketchSolveStatus.Unavailable);
        failed.Document.SolveEvidence!.EngineStatus.Should().BeNull();
        PlaneGcsDiagnostics.GetDimensionState(failed.Document, Diagonal()).Should().Be(SketchConstraintState.Unsatisfied);

        unavailable.TryApplyDrag(failed.Document, "point:center", new(3, 4), new(4, 5), false,
            out var unchanged, out _).Should().BeFalse();
        unchanged.Should().BeSameAs(failed.Document);
    }

    [Fact]
    public void RemovingSolvedTemporaryConstraintPreservesEvidenceWithoutAnotherSolve()
    {
        var document = Axes();
        document = new(document.Entities, document.Dimensions,
            [new("temporary", SketchConstraintKind.Fix, ["point"], SketchConstraintState.Satisfied)], document.Metadata);
        var solved = new PlaneGcsSketchSolverAdapter(new ReportingRuntime([])).Solve(SketchSolveRequest.FromDocument(document));
        solved.Status.Should().Be(SketchSolveStatus.Solved);

        var stripped = PlaneGcsDiagnostics.RemoveSolvedConstraints(solved.Document, ["temporary"]);

        stripped.Constraints.Should().BeEmpty();
        stripped.Entities.Single().Should().BeSameAs(solved.Document.Entities.Single());
        stripped.Metadata.Should().BeSameAs(solved.Document.Metadata);
        var view = PlaneGcsDiagnostics.Read(stripped);
        view.HasCurrentEvidence.Should().BeTrue();
        view.HasUnvalidatedIntent.Should().BeFalse();
        view.Diagnostics.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptedDragFollowsAnchorButPreservesExplicitPlacement(bool explicitAnchor)
    {
        var dimension = new SketchDimension("length", SketchDimensionKind.LinearDistance,
            ["edge:start", "edge:end"], 4, new(2, 1));
        var original = new DrawingDocument([new LineEntity(EntityId.Create("edge"), new(0, 0), new(4, 0))], [dimension], []);
        var update = new SketchDimension(dimension.Id, dimension.Kind, dimension.ReferenceKeys, dimension.Value, new(9, 9));
        var result = new PlaneGcsSketchSolverAdapter(new TranslatingRuntime()).Solve(
            new(original, [], explicitAnchor ? [update] : []),
            new PlaneGcsDragRequest("edge", new(0, 0), new(2, 3), false));

        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        result.Document.Dimensions.Single().Anchor.Should().Be(explicitAnchor ? new Point2(9, 9) : new Point2(4, 4));
        PlaneGcsDiagnostics.Read(result.Document).HasCurrentEvidence.Should().BeTrue();
        original.Dimensions.Single().Anchor.Should().Be(new Point2(2, 1));
    }

    [Fact]
    public void ActualRuntimeRedundantDrivingEvidenceSurvivesNativeRoundTrip()
    {
        using var runtime = CreateRuntime();
        var result = new PlaneGcsSketchSolverAdapter(runtime).Solve(new(Axes(), [], [Diagonal()]));

        result.Status.Should().Be(SketchSolveStatus.Failed, string.Join(" ", result.Diagnostics));
        result.Document.SolveEvidence!.EngineStatus.Should().Be("solved");
        AssertRejected(result.Document);
        AssertRejected(DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(result.Document)));
    }

    [Fact]
    public void ActualRuntimeImportedRedundantSystemKeepsNativeBasisObservationAfterReopen()
    {
        using var runtime = CreateRuntime();
        var result = new PlaneGcsSketchSolverAdapter(runtime).Solve(SketchSolveRequest.FromDocument(Axes(includeDiagonal: true)));

        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        var reopened = DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(result.Document));
        var view = PlaneGcsDiagnostics.Read(reopened);
        view.HasCurrentEvidence.Should().BeTrue();
        view.HasUnvalidatedIntent.Should().BeTrue();
        view.Observations.Should().NotBeEmpty().And.OnlyContain(d =>
            d.Reason == SketchSolveDiagnosticReason.RedundantDrivingDimension);
        view.Diagnostics.Should().BeEmpty("a selected redundant basis does not identify a rejected edit");
    }

    private static void AssertRejected(DrawingDocument document)
    {
        var view = PlaneGcsDiagnostics.Read(document);
        view.HasCurrentEvidence.Should().BeTrue();
        view.GetDimensionState(Diagonal()).Should().Be(SketchConstraintState.Unsatisfied);
        view.Diagnostics.Should().Contain(d => d.ItemId == "diagonal"
            && d.Reason == SketchSolveDiagnosticReason.RedundantDrivingDimension);
        SketchDimensionSolverService.TryGetDimensionMeasurement(document, Diagonal(), out var measured).Should().BeTrue();
        measured.Should().Be(5, "compatible redundancy cannot be inferred from residual failure");
    }

    private static SketchSolveResult RejectDiagonal(DrawingDocument document) =>
        new PlaneGcsSketchSolverAdapter(new ReportingRuntime(["diagonal"])).Solve(new(document, [], [Diagonal()]));

    private static SketchDimension Diagonal() =>
        new("diagonal", SketchDimensionKind.LinearDistance, ["$origin:center", "point:center"], 5, new(2, 3), true);

    private static DrawingDocument Axes(bool includeDiagonal = false)
    {
        var dimensions = new List<SketchDimension>
        {
            new("x", SketchDimensionKind.HorizontalDistance, ["$origin:center", "point:center"], 3, isDriving: true),
            new("y", SketchDimensionKind.VerticalDistance, ["$origin:center", "point:center"], 4, isDriving: true)
        };
        if (includeDiagonal) dimensions.Add(Diagonal());
        return new([new PointEntity(EntityId.Create("point"), new(3, 4))], dimensions, [],
            new DrawingDocumentMetadata { SourceFileName = "evidence.dxf", Units = DrawingUnits.Inches, TrustedSource = true });
    }

    private sealed class ReportingRuntime(IReadOnlyList<string>? redundant) : IPlaneGcsRuntime
    {
        public PlaneGcsRequest? Request { get; private set; }
        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            Request = request;
            return new("solved", request.Document.Entities, [], "PlaneGCS", redundant);
        }
    }

    private sealed class TranslatingRuntime : IPlaneGcsRuntime
    {
        public PlaneGcsResponse Solve(PlaneGcsRequest request) =>
            new("solved", request.Document.Entities.Select(e => e with
            {
                Points = e.Points.Select(p => new PlaneGcsPoint(p.X + 2, p.Y + 3)).ToArray()
            }).ToArray(), [], "PlaneGCS", []);
    }

    private static PlaneGcsNodeRuntime CreateRuntime()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs")))
            root = root.Parent;
        if (root is null) throw new InvalidOperationException("The repository's real PlaneGCS assets are required.");
        var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.GetFullPath(Path.Combine(directory, executable))).FirstOrDefault(File.Exists);
        if (node is null) throw new InvalidOperationException("Node is required; no mock or legacy solver is permitted.");
        return new(node, Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"),
            Path.Combine(root.FullName, "src/DXFER.Blazor/wwwroot/planeGcsSolver.js"));
    }
}

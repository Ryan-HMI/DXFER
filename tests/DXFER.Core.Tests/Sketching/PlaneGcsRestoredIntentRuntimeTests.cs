using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsRestoredIntentRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedCenteredLengthCanReturnToAcceptedValueAfterConflict(bool reopen)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var adapter = new PlaneGcsSketchSolverAdapter(observed);
        var accepted = AcceptFixedCenteredLength(adapter);
        var failed = adapter.Solve(new(accepted, [], [Length(14)]));

        failed.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Response!.Status.Should().Be("failed");
        failed.Document.Entities.Should().Equal(accepted.Entities);
        failed.Document.SolveEvidence!.LastAcceptedSnapshotSignature.Should().Be(accepted.SolveEvidence!.SnapshotSignature);
        var retained = reopen ? RoundTrip(failed.Document) : failed.Document;
        var restored = adapter.Solve(new(retained, [], [Length(12)]));

        AssertSolved(restored);
        observed.Response!.Status.Should().Be("solved");
        observed.Request!.AttemptedDrivingDimensionIds.Should().BeEmpty();
        restored.Document.SolveEvidence!.SnapshotSignature.Should().Be(accepted.SolveEvidence!.SnapshotSignature);
        PlaneGcsDiagnostics.FromDocument(restored.Document).Should().BeEmpty();
        PlaneGcsDiagnostics.FromDocument(RoundTrip(restored.Document)).Should().BeEmpty();
        failed.Document.Dimensions.Single().Value.Should().Be(14, "failed history remains immutable");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AcceptedPerpendicularAngleRestoresAndDoesNotPoisonLaterLengthEdit(bool reopen)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var adapter = new PlaneGcsSketchSolverAdapter(observed);
        var document = new DrawingDocument([
            new LineEntity(EntityId.Create("edge"), new(0, 0), new(12, 0)),
            new LineEntity(EntityId.Create("upright"), new(0, 0), new(0, 8))
        ], [], []);
        var angle = adapter.Solve(new(document, [], [Angle(90)]));
        AssertSolved(angle);
        var perpendicular = adapter.Solve(new(angle.Document,
            [new("perpendicular", SketchConstraintKind.Perpendicular, ["edge", "upright"])], []));
        AssertSolved(perpendicular);
        var failed = adapter.Solve(new(perpendicular.Document, [], [Angle(60)]));
        failed.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Response!.Status.Should().Be("failed");

        var retained = reopen ? RoundTrip(failed.Document) : failed.Document;
        var restored = adapter.Solve(new(retained, [], [Angle(90)]));
        AssertSolved(restored);
        observed.Request!.AttemptedDrivingDimensionIds.Should().BeEmpty();
        PlaneGcsDiagnostics.FromDocument(restored.Document).Should().BeEmpty();

        var length = adapter.Solve(new(restored.Document, [], [Length(15)]));
        AssertSolved(length);
        observed.Request!.AttemptedDrivingDimensionIds.Should().Equal("length");
        var resized = adapter.Solve(new(length.Document, [], [Length(18)]));
        AssertSolved(resized);
        PlaneGcsDiagnostics.FromDocument(resized.Document).Should().BeEmpty();
        SketchDimensionSolverService.TryGetDimensionMeasurement(resized.Document,
            resized.Document.Dimensions.Single(d => d.Id == "length"), out var measured).Should().BeTrue();
        measured.Should().BeApproximately(18, 1e-6);
    }

    [Fact]
    public void RepeatedFailuresAndFailedRecoveryKeepTheAcceptedSnapshotUntilNativeSuccess()
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var adapter = new PlaneGcsSketchSolverAdapter(observed);
        var accepted = AcceptFixedCenteredLength(adapter);
        var failed = adapter.Solve(new(accepted, [], [Length(14)]));
        var failedAgain = adapter.Solve(new(failed.Document, [], [Length(16)]));
        failedAgain.Status.Should().Be(SketchSolveStatus.Failed);
        var retried = adapter.Solve(SketchSolveRequest.FromDocument(failedAgain.Document));
        retried.Status.Should().Be(SketchSolveStatus.Failed);
        retried.Document.Should().BeSameAs(failedAgain.Document);
        observed.Request!.AttemptedDrivingDimensionIds.Should().Equal("length");

        var unavailable = new PlaneGcsSketchSolverAdapter().Solve(new(retried.Document, [], [Length(12)]));
        unavailable.Status.Should().Be(SketchSolveStatus.Unavailable);
        unavailable.Document.SolveEvidence!.LastAcceptedSnapshotSignature.Should().Be(accepted.SolveEvidence!.SnapshotSignature);
        PlaneGcsDiagnostics.GetDimensionState(unavailable.Document, Length(12)).Should().Be(SketchConstraintState.Unsatisfied);

        var restored = adapter.Solve(SketchSolveRequest.FromDocument(RoundTrip(unavailable.Document)));
        AssertSolved(restored);
        observed.Request!.AttemptedDrivingDimensionIds.Should().BeEmpty();
        PlaneGcsDiagnostics.FromDocument(restored.Document).Should().BeEmpty();
        restored.Document.SolveEvidence!.LastAcceptedSnapshotSignature.Should().BeNull();
    }

    [Theory]
    [InlineData("units")]
    [InlineData("geometry")]
    [InlineData("references")]
    [InlineData("constraint")]
    [InlineData("dimension")]
    [InlineData("request-lock")]
    public void RecoveryDoesNotExemptChangesOutsideTheAcceptedSnapshot(string change)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var adapter = new PlaneGcsSketchSolverAdapter(observed);
        var accepted = AcceptFixedCenteredLength(adapter);
        var failed = adapter.Solve(new(accepted, [], [Length(14)]));
        var retained = failed.Document;
        if (change == "units")
            retained = new(retained.Entities, retained.Dimensions, retained.Constraints,
                retained.Metadata with { Units = DrawingUnits.Millimeters }, retained.SolveEvidence);
        if (change == "geometry")
            retained = new(retained.Entities.Append(new LineEntity(EntityId.Create("other"), new(20, 0), new(25, 0))),
                retained.Dimensions, retained.Constraints, retained.Metadata, retained.SolveEvidence);
        var restore = change == "references"
            ? new SketchDimension("length", SketchDimensionKind.LinearDistance, ["edge:end", "edge:start"], 12, isDriving: true)
            : Length(12);
        var result = adapter.Solve(new(retained,
            change == "constraint" ? [new("extra-horizontal", SketchConstraintKind.Horizontal, ["edge"])] : [],
            change == "dimension" ? [restore, Length(12, "duplicate")] : [restore],
            fixedReferenceKeys: change == "request-lock" ? ["edge:start"] : []));

        result.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Response!.Status.Should().Be("solved", "these changes are compatible but do not prove restoration");
        observed.Request!.AttemptedDrivingDimensionIds.Should().Contain("length");
        result.AffectedDiagnostics.Should().Contain(d => d.ItemId == "length"
            && d.Reason == SketchSolveDiagnosticReason.RedundantDrivingDimension);
        result.Document.Entities.Should().Equal(retained.Entities);
        if (change is "units" or "geometry")
            result.Document.SolveEvidence!.LastAcceptedSnapshotSignature.Should().BeNull("stale evidence cannot certify history");
    }

    [Fact]
    public void DisplayOnlyChangesDoNotPreventRecovery()
    {
        using var runtime = CreateRuntime();
        var adapter = new PlaneGcsSketchSolverAdapter(runtime);
        var accepted = AcceptFixedCenteredLength(adapter);
        var failed = adapter.Solve(new(accepted, [], [Length(14)]));
        var retained = new DrawingDocument(failed.Document.Entities, failed.Document.Dimensions,
            failed.Document.Constraints, failed.Document.Metadata with { SourceFileName = "renamed.dxf" }, failed.Document.SolveEvidence);
        var labelEdit = new SketchDimension("length", SketchDimensionKind.LinearDistance,
            ["edge:start", "edge:end"], 12, new(30, 40), true);

        var restored = adapter.Solve(new(RoundTrip(retained), [], [labelEdit]));

        AssertSolved(restored);
        restored.Document.Dimensions.Single().Anchor.Should().Be(new Point2(30, 40));
        PlaneGcsDiagnostics.FromDocument(restored.Document).Should().BeEmpty();
    }

    [Fact]
    public void NeverAcceptedRedundantIntentCannotBecomeAcceptedByChangingAndRestoringItsValue()
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var adapter = new PlaneGcsSketchSolverAdapter(observed);
        var accepted = AcceptFixedCenteredLength(adapter);
        var redundant = adapter.Solve(new(accepted, [], [Length(12, "duplicate")]));
        redundant.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Response!.Status.Should().Be("solved");
        var conflicting = adapter.Solve(new(redundant.Document, [], [Length(14, "duplicate")]));
        conflicting.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Response!.Status.Should().Be("failed");
        var restored = adapter.Solve(new(RoundTrip(conflicting.Document), [], [Length(12, "duplicate")]));

        restored.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Response!.Status.Should().Be("solved");
        observed.Request!.AttemptedDrivingDimensionIds.Should().Equal("duplicate");
        restored.AffectedDiagnostics.Should().ContainSingle(d => d.ItemId == "duplicate"
            && d.Reason == SketchSolveDiagnosticReason.RedundantDrivingDimension);
        var retried = adapter.Solve(SketchSolveRequest.FromDocument(restored.Document));
        retried.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Request!.AttemptedDrivingDimensionIds.Should().Equal("duplicate");
    }

    [Theory]
    [InlineData("missing-history")]
    [InlineData("stale-snapshot")]
    [InlineData("unsupported-version")]
    public void MissingOrStaleAcceptedEvidenceCannotAuthorizeRecovery(string change)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var adapter = new PlaneGcsSketchSolverAdapter(observed);
        var accepted = AcceptFixedCenteredLength(adapter);
        var failed = adapter.Solve(new(accepted, [], [Length(14)]));
        var evidence = failed.Document.SolveEvidence!;
        var changed = new PlaneGcsSolveEvidence(evidence.Engine,
            change == "unsupported-version" ? evidence.Version + 1 : evidence.Version,
            change == "stale-snapshot" ? new string('A', 64) : evidence.SnapshotSignature,
            evidence.Status, evidence.EngineStatus, evidence.Intents,
            change == "missing-history" ? null : evidence.LastAcceptedSnapshotSignature);
        var retained = new DrawingDocument(failed.Document.Entities, failed.Document.Dimensions,
            failed.Document.Constraints, failed.Document.Metadata, changed);

        var restored = adapter.Solve(new(RoundTrip(retained), [], [Length(12)]));

        restored.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Response!.Status.Should().Be("solved");
        observed.Request!.AttemptedDrivingDimensionIds.Should().Equal("length");
        restored.AffectedDiagnostics.Should().Contain(d => d.ItemId == "length"
            && d.Reason == SketchSolveDiagnosticReason.RedundantDrivingDimension);
    }

    [Fact]
    public void MalformedAcceptedSignatureIsRejectedByNativeDocumentValidation()
    {
        var evidence = new PlaneGcsSolveEvidence("PlaneGCS", PlaneGcsSolveEvidence.CurrentVersion,
            new string('A', 64), SketchSolveStatus.Failed, "failed", [], "not-a-signature");
        var document = new DrawingDocument([], [], [], DrawingDocumentMetadata.Empty, evidence);

        PlaneGcsDiagnostics.IsWellFormed(evidence).Should().BeFalse();
        var write = () => DxferDocumentSerializer.Write(document);
        write.Should().Throw<FormatException>();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExactRestorationStillRequiresNativeSuccessAndValidGeometry(bool invalidGeometry)
    {
        using var runtime = CreateRuntime();
        var adapter = new PlaneGcsSketchSolverAdapter(runtime);
        var accepted = AcceptFixedCenteredLength(adapter);
        var failed = adapter.Solve(new(accepted, [], [Length(14)]));
        var observed = new ObservedRuntime(new InvalidRecoveryRuntime(runtime, invalidGeometry));

        var restored = new PlaneGcsSketchSolverAdapter(observed).Solve(new(failed.Document, [], [Length(12)]));

        restored.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Request.Should().NotBeNull("recovery must invoke the native runtime");
        observed.Request!.AttemptedDrivingDimensionIds.Should().BeEmpty();
        restored.Document.Entities.Should().Equal(accepted.Entities);
        PlaneGcsDiagnostics.GetDimensionState(restored.Document, Length(12)).Should().Be(SketchConstraintState.Unsatisfied);
        restored.Document.SolveEvidence!.LastAcceptedSnapshotSignature.Should().Be(accepted.SolveEvidence!.SnapshotSignature);
        AssertSolved(adapter.Solve(SketchSolveRequest.FromDocument(restored.Document)));
    }

    private static DrawingDocument AcceptFixedCenteredLength(PlaneGcsSketchSolverAdapter adapter)
    {
        var document = new DrawingDocument([
            new LineEntity(EntityId.Create("edge"), new(-6, 0), new(6, 0))
        ], [], [
            new("horizontal", SketchConstraintKind.Horizontal, ["edge"]),
            new("midpoint", SketchConstraintKind.Midpoint, ["edge", SketchReference.OriginKey])
        ]);
        var sized = adapter.Solve(new(document, [], [Length(12)]));
        AssertSolved(sized);
        var fixedEnd = adapter.Solve(new(sized.Document,
            [new("fix-end", SketchConstraintKind.Fix, ["edge:end"])], []));
        AssertSolved(fixedEnd);
        return fixedEnd.Document;
    }

    private static SketchDimension Length(double value, string id = "length") =>
        new(id, SketchDimensionKind.LinearDistance, ["edge:start", "edge:end"], value, isDriving: true);

    private static SketchDimension Angle(double value) =>
        new("angle", SketchDimensionKind.Angle, ["edge", "upright"], value, isDriving: true);

    private static DrawingDocument RoundTrip(DrawingDocument document) =>
        DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(document));

    private static void AssertSolved(SketchSolveResult result) =>
        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));

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

    private sealed class ObservedRuntime(IPlaneGcsRuntime inner) : IPlaneGcsRuntime
    {
        public PlaneGcsRequest? Request { get; private set; }
        public PlaneGcsResponse? Response { get; private set; }

        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            Request = request;
            return Response = inner.Solve(request);
        }
    }

    private sealed class InvalidRecoveryRuntime(IPlaneGcsRuntime inner, bool invalidGeometry) : IPlaneGcsRuntime
    {
        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            var response = inner.Solve(request);
            response.Status.Should().Be("solved");
            return invalidGeometry
                ? response with { Entities = [response.Entities!.Single() with { Points = [new(-7, 0), new(7, 0)] }] }
                : response with { Status = "failed", Diagnostics = ["Injected native refusal after solving the exact snapshot."] };
        }
    }
}

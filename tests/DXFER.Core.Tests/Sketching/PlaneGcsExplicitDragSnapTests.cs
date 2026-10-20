using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsExplicitDragSnapTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void SnapIsSolvedAsHardIntentAndOnlyPersistsWhenEnabled(bool midpoint, bool automatic)
    {
        using var runtime = CreateRuntime();
        var original = Document();
        var intent = Snap(midpoint);
        new PlaneGcsSketchSolverAdapter(runtime).TryApplyDrag(original, "moving:end",
            new(3, 1), new(8, 2), false, out var result, out var status, automatic, intent).Should().BeTrue(status);
        var moving = (LineEntity)result.Entities[0];
        var source = (LineEntity)result.Entities[1];
        var target = midpoint
            ? new Point2((source.Start.X + source.End.X) / 2, (source.Start.Y + source.End.Y) / 2)
            : source.Start;
        moving.End.X.Should().BeApproximately(target.X, 1e-7);
        moving.End.Y.Should().BeApproximately(target.Y, 1e-7);
        result.Constraints.Count.Should().Be(automatic ? 1 : 0);
        if (automatic)
            result.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        PlaneGcsDiagnostics.Read(result).HasCurrentEvidence.Should().BeTrue();
        result.SolveEvidence!.Intents.Where(i => i.ItemKind == "constraint").Select(i => i.ItemId)
            .Should().BeEquivalentTo(result.Constraints.Select(c => c.Id));
        original.Constraints.Should().BeEmpty();
        ((LineEntity)original.Entities[0]).End.Should().Be(new Point2(3, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AutoOffRemovesOnlyTemporarySnapAndPreservesNativeEvidenceForExistingIntent(bool midpoint)
    {
        using var runtime = CreateRuntime();
        var recording = new RecordingRuntime(runtime);
        var adapter = new PlaneGcsSketchSolverAdapter(recording);
        var document = new DrawingDocument(Document().Entities,
            [new("anchor-length", SketchDimensionKind.LinearDistance, ["anchor:start", "anchor:end"], 4, isDriving: true)],
            [new("anchor-horizontal", SketchConstraintKind.Horizontal, ["anchor"], SketchConstraintState.Satisfied)]);
        var initial = adapter.Solve(SketchSolveRequest.FromDocument(document));
        initial.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", initial.Diagnostics));
        var original = initial.Document;
        var beforeCalls = recording.Calls;

        adapter.TryApplyDrag(original, "moving:end", new(3, 1), new(8, 2), false,
            out var result, out var status, false, Snap(midpoint)).Should().BeTrue(status);

        recording.Calls.Should().Be(beforeCalls + 1, "removing temporary intent does not require another solve");
        var temporaryId = recording.LastRequest!.Constraints.Should().ContainSingle().Which.Id;
        result.Constraints.Should().ContainSingle().Which.Id.Should().Be("anchor-horizontal");
        result.Dimensions.Should().ContainSingle().Which.Id.Should().Be("anchor-length");
        result.Metadata.Should().BeSameAs(original.Metadata);
        result.SolveEvidence!.Intents.Should().NotContain(i => i.ItemId == temporaryId);
        result.SolveEvidence.Intents.Should().HaveCount(2);
        foreach (var snapshot in new[] { result, DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(result)) })
        {
            var view = PlaneGcsDiagnostics.Read(snapshot);
            view.HasCurrentEvidence.Should().BeTrue();
            view.HasUnvalidatedIntent.Should().BeFalse();
            view.GetDimensionState(snapshot.Dimensions.Single()).Should().Be(SketchConstraintState.Satisfied);
            view.GetConstraintState(snapshot.Constraints.Single()).Should().Be(SketchConstraintState.Satisfied);
            view.Diagnostics.Should().BeEmpty();
        }
        original.Constraints.Should().ContainSingle().Which.Id.Should().Be("anchor-horizontal");
        PlaneGcsDiagnostics.Read(original).HasCurrentEvidence.Should().BeTrue();
    }

    [Fact]
    public void ConflictingSnapPreservesGeometryAndDoesNotStoreFailedDragIntent()
    {
        using var runtime = CreateRuntime();
        var baseDocument = Document();
        var original = new DrawingDocument(baseDocument.Entities, [], [
            new("fixed-moving", SketchConstraintKind.Fix, ["moving"], SketchConstraintState.Satisfied),
            new("fixed-target", SketchConstraintKind.Fix, ["anchor"], SketchConstraintState.Satisfied)
        ]);
        new PlaneGcsSketchSolverAdapter(runtime).TryApplyDrag(original, "moving:end",
            new(3, 1), new(8, 2), false, out var result, out _, true, Snap(false)).Should().BeFalse();
        result.Should().BeSameAs(original);
    }

    [Fact]
    public void ExplicitOriginSnapUsesFixedOriginWithoutCreatingAnEntity()
    {
        using var runtime = CreateRuntime();
        var original = new DrawingDocument([
            new LineEntity(EntityId.Create("moving"), new(-3, -1), new(3, 1))
        ]);
        var snap = new PlaneGcsConstraint("client", "Coincident", ["moving:end", SketchReference.OriginKey], "Satisfied");
        new PlaneGcsSketchSolverAdapter(runtime).TryApplyDrag(original, "moving:end",
            new(3, 1), new(0, 0), false, out var result, out var status, true, snap).Should().BeTrue(status);
        result.Entities.Should().HaveCount(original.Entities.Count);
        var endpoint = ((LineEntity)result.Entities[0]).End;
        endpoint.X.Should().BeApproximately(0, 1e-7);
        endpoint.Y.Should().BeApproximately(0, 1e-7);
        result.Constraints.Should().ContainSingle().Which.ReferenceKeys.Should()
            .Contain(SketchReference.OriginKey);
    }

    [Theory]
    [InlineData("Horizontal", "moving:end", "anchor:start")]
    [InlineData("Coincident", "anchor:start", "anchor:end")]
    [InlineData("Coincident", "moving:end", "missing:start")]
    [InlineData("Coincident", "moving:end", "moving:start")]
    public void InvalidOrUnrelatedSnapIsRejectedBeforeRuntime(string kind, string first, string second)
    {
        var runtime = new MustNotRun();
        var original = Document();
        var intent = new PlaneGcsConstraint("ignored", kind, [first, second], "Satisfied");
        new PlaneGcsSketchSolverAdapter(runtime).TryApplyDrag(original, "moving:end",
            new(3, 1), new(8, 2), false, out var result, out _, true, intent).Should().BeFalse();
        result.Should().BeSameAs(original);
        runtime.Calls.Should().Be(0);
    }

    [Fact]
    public void ExistingSnapIsNotDuplicatedOrRemovedWhenAutomaticCreationIsOff()
    {
        using var runtime = CreateRuntime();
        var original = new DrawingDocument([
            new LineEntity(EntityId.Create("moving"), new(0, 0), new(8, 2)),
            new LineEntity(EntityId.Create("anchor"), new(8, 2), new(12, 2))
        ], [], [new("existing", SketchConstraintKind.Coincident, ["anchor:start", "moving:end"],
            SketchConstraintState.Satisfied)]);
        new PlaneGcsSketchSolverAdapter(runtime).TryApplyDrag(original, "moving:end",
            new(8, 2), new(9, 3), false, out var result, out var status, false, Snap(false)).Should().BeTrue(status);
        result.Constraints.Should().ContainSingle().Which.Id.Should().Be("existing");
        PlaneGcsDiagnostics.Read(result).HasCurrentEvidence.Should().BeTrue();
        ((LineEntity)result.Entities[0]).End.Should().Be(((LineEntity)result.Entities[1]).Start);
    }

    private static DrawingDocument Document() => new([
        new LineEntity(EntityId.Create("moving"), new(0, 0), new(3, 1)),
        new LineEntity(EntityId.Create("anchor"), new(8, 2), new(12, 2))
    ]);

    private static PlaneGcsConstraint Snap(bool midpoint) => new("client-id-is-not-trusted",
        midpoint ? "Midpoint" : "Coincident", ["moving:end", midpoint ? "anchor" : "anchor:start"], "Satisfied");

    private static PlaneGcsNodeRuntime CreateRuntime()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs")))
            root = root.Parent;
        if (root is null) throw new InvalidOperationException("Real PlaneGCS assets are required.");
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Path.Combine(path, OperatingSystem.IsWindows() ? "node.exe" : "node")))
            .First(File.Exists);
        return new(node, Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"),
            Path.Combine(root.FullName, "src/DXFER.Blazor/wwwroot/planeGcsSolver.js"));
    }

    private sealed class MustNotRun : IPlaneGcsRuntime
    {
        public int Calls { get; private set; }
        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            Calls++;
            throw new InvalidOperationException("Invalid snap reached the runtime.");
        }
    }

    private sealed class RecordingRuntime(IPlaneGcsRuntime runtime) : IPlaneGcsRuntime
    {
        public int Calls { get; private set; }
        public PlaneGcsRequest? LastRequest { get; private set; }

        public PlaneGcsResponse Solve(PlaneGcsRequest request)
        {
            Calls++;
            LastRequest = request;
            return runtime.Solve(request);
        }
    }
}

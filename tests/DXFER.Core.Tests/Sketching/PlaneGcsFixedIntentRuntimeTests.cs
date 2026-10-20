using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsFixedIntentRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealRuntimeSuppressedOrRemappedFixDoesNotRetainOldWholeEntityLock(bool remap)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var original = Document();
        var edits = FixEdits(remap);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(new(original, edits, [Size(10)]));

        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        observed.Response!.Engine.Should().Be("PlaneGCS");
        observed.Response.Status.Should().Be("solved");
        observed.Request!.FixedReferenceKeys.Should().NotContain("edge");
        observed.Request.FixedReferenceKeys.Should().Contain(new SketchReference("edge", SketchReferenceTarget.Start).ToString());
        var line = result.Document.Entities.Single().Should().BeOfType<LineEntity>().Which;
        line.Start.Should().Be(new Point2(0, 0));
        line.End.Should().NotBe(new Point2(3, 4));
        SketchDimensionSolverService.TryGetDimensionMeasurement(result.Document,
            result.Document.Dimensions.Single(), out var length).Should().BeTrue();
        length.Should().BeApproximately(10, 1e-6);
        var changedFix = result.Document.Constraints.Single(c => c.Id == "fixed-edge");
        changedFix.State.Should().Be(remap ? SketchConstraintState.Satisfied : SketchConstraintState.Suppressed);
        changedFix.ReferenceKeys.Should().Equal(remap ? new[] { "edge:start" } : new[] { "edge" });
        result.Document.Metadata.Should().BeSameAs(original.Metadata);
        original.Constraints.Single().State.Should().Be(SketchConstraintState.Satisfied);
        original.Dimensions.Single().Value.Should().Be(5);
        ((LineEntity)original.Entities.Single()).End.Should().Be(new Point2(3, 4));
    }

    [Fact]
    public void ExplicitRequestFixedKeyStillProtectsEntityAfterFixSuppression()
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var original = Document();
        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(
            new(original, FixEdits(false), [Size(10)], fixedReferenceKeys: ["edge"]));

        result.Status.Should().Be(SketchSolveStatus.Failed);
        observed.Request!.FixedReferenceKeys.Should().Contain("edge");
        observed.Response!.Engine.Should().Be("PlaneGCS");
        observed.Response.Status.Should().Be("failed");
        result.Document.Entities.Single().Should().BeSameAs(original.Entities.Single());
        result.Document.Metadata.Should().BeSameAs(original.Metadata);
        result.Document.Dimensions.Single().Value.Should().Be(10);
        result.Document.Constraints.Single(c => c.Id == "fixed-edge").State.Should().Be(SketchConstraintState.Suppressed);
    }

    private static SketchConstraint[] FixEdits(bool remap) => remap
        ? [new("fixed-edge", SketchConstraintKind.Fix, ["edge:start"], SketchConstraintState.Satisfied)]
        : [
            new("fixed-edge", SketchConstraintKind.Fix, ["edge"], SketchConstraintState.Suppressed),
            new("anchor-start", SketchConstraintKind.Fix, ["edge:start"], SketchConstraintState.Satisfied)
        ];

    private static SketchDimension Size(double value) =>
        new("size", SketchDimensionKind.LinearDistance, ["edge:start", "edge:end"], value, isDriving: true);

    private static DrawingDocument Document() =>
        new([new LineEntity(EntityId.Create("edge"), new(0, 0), new(3, 4))], [Size(5)],
            [new("fixed-edge", SketchConstraintKind.Fix, ["edge"], SketchConstraintState.Satisfied)],
            new DrawingDocumentMetadata { SourceFileName = "fixed-intent-fixture.dxf" });

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
}

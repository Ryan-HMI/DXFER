using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

[Collection("PlaneGCS resource budget")]
public sealed class PlaneGcsSymmetricNormalRuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualEngineMirrorsCirclesAndRetainsSymmetryAfterRadiusEdit(bool reverse)
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var original = new DrawingDocument([Axis(), new CircleEntity(Id("a"), new(-4, 2), 2), new CircleEntity(Id("b"), new(6, 4), 3)],
            [], [], new DrawingDocumentMetadata { SourceFileName = "symmetry.dxf", Units = DrawingUnits.Inches });
        var intent = new SketchConstraint("mirror", SketchConstraintKind.Symmetric, reverse ? ["b", "a", "axis"] : ["a", "b", "axis"]);
        var result = solver.Solve(new(original, [intent], [], fixedReferenceKeys: ["axis", "a"]));
        AssertSolved(result, "mirror");
        result.Document.Entities.Take(2).Should().Equal(original.Entities.Take(2));
        var b = result.Document.Entities.OfType<CircleEntity>().Single(c => c.Id.Value == "b");
        b.Center.X.Should().BeApproximately(4, 1e-6);
        b.Center.Y.Should().BeApproximately(2, 1e-6);
        b.Radius.Should().BeApproximately(2, 1e-6);
        result.Document.Metadata.Should().BeSameAs(original.Metadata);
        var edited = solver.Solve(new(result.Document, [],
            [new SketchDimension("radius", SketchDimensionKind.Radius, ["a"], 4, isDriving: true)], fixedReferenceKeys: ["axis"]));
        AssertSolved(edited, "mirror");
        edited.Document.Entities.OfType<CircleEntity>().Should().OnlyContain(c => Math.Abs(c.Radius - 4) < 1e-6);
        original.Constraints.Should().BeEmpty();
        ((CircleEntity)original.Entities[2]).Radius.Should().Be(3);
    }

    [Fact]
    public void ActualEngineMirrorsArcWithReversedEndpointCorrespondence()
    {
        using var runtime = CreateRuntime();
        var original = new DrawingDocument([Axis(),
            new ArcEntity(Id("a"), new(-4, 0), 2, 0, 90), new ArcEntity(Id("b"), new(5, 1), 3, 80, 170)]);
        var result = new PlaneGcsSketchSolverAdapter(runtime).Solve(new(original,
            [new SketchConstraint("mirror", SketchConstraintKind.Symmetric, ["a", "b", "axis"])], [], fixedReferenceKeys: ["axis", "a"]));
        AssertSolved(result, "mirror");
        result.Document.Entities.Take(2).Should().Equal(original.Entities.Take(2));
        var arc = result.Document.Entities.OfType<ArcEntity>().Single(a => a.Id.Value == "b");
        arc.Center.X.Should().BeApproximately(4, 1e-6);
        arc.Center.Y.Should().BeApproximately(0, 1e-6);
        arc.Radius.Should().BeApproximately(2, 1e-6);
        arc.StartAngleDegrees.Should().BeApproximately(90, 1e-6);
        arc.EndAngleDegrees.Should().BeApproximately(180, 1e-6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ActualEngineMirrorsPairedLineOrPolylineVertices(bool polyline)
    {
        using var runtime = CreateRuntime();
        DrawingEntity a = polyline ? new PolylineEntity(Id("a"), [new(1, 3), new(2, 6)])
            : new LineEntity(Id("a"), new(1, 3), new(2, 6));
        DrawingEntity b = polyline ? new PolylineEntity(Id("b"), [new(7, 0), new(5, 2)])
            : new LineEntity(Id("b"), new(7, 0), new(5, 2));
        var original = new DrawingDocument([new LineEntity(Id("axis"), new(-10, -10), new(10, 10)), a, b]);
        var segment = polyline ? "|segment|0" : "";
        var result = new PlaneGcsSketchSolverAdapter(runtime).Solve(new(original,
            [new SketchConstraint("mirror", SketchConstraintKind.Symmetric,
                [$"a{segment}:start", $"b{segment}:end", $"a{segment}:end", $"b{segment}:start", "axis"])], [],
            fixedReferenceKeys: ["axis", "a"]));
        AssertSolved(result, "mirror");
        result.Document.Entities.Take(2).Should().Equal(original.Entities.Take(2));
        SketchReferenceResolver.TryGetPoint(result.Document, $"b{segment}:start", out var start).Should().BeTrue();
        SketchReferenceResolver.TryGetPoint(result.Document, $"b{segment}:end", out var end).Should().BeTrue();
        start.X.Should().BeApproximately(6, 1e-6);
        start.Y.Should().BeApproximately(2, 1e-6);
        end.X.Should().BeApproximately(3, 1e-6);
        end.Y.Should().BeApproximately(1, 1e-6);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void ActualEngineMakesEndpointNormalWithoutMovingFixedCurve(bool arc, bool polyline)
    {
        using var runtime = CreateRuntime();
        DrawingEntity curve = arc ? new ArcEntity(Id("c"), new(0, 0), 3, 0, 180) : new CircleEntity(Id("c"), new(0, 0), 3);
        DrawingEntity line = polyline ? new PolylineEntity(Id("l"), [new(4, 2), new(7, 3)]) : new LineEntity(Id("l"), new(4, 2), new(7, 3));
        var original = new DrawingDocument([curve, line]);
        var key = polyline ? "l|segment|0:start" : "l:start";
        var result = new PlaneGcsSketchSolverAdapter(runtime).Solve(new(original,
            [new SketchConstraint("normal", SketchConstraintKind.Normal, [key, "c"])], [], fixedReferenceKeys: ["c"]));
        AssertSolved(result, "normal");
        result.Document.Entities[0].Should().Be(curve);
        SketchReferenceResolver.TryGetPoint(result.Document, key, out var contact).Should().BeTrue();
        Math.Sqrt(contact.X * contact.X + contact.Y * contact.Y).Should().BeApproximately(3, 1e-6);
        original.Entities[1].Should().BeSameAs(line);
    }

    [Theory]
    [InlineData(SketchConstraintKind.Normal)]
    [InlineData(SketchConstraintKind.Symmetric)]
    public void ActualEngineFixedConflictPreservesGeometryAndFailedIntent(SketchConstraintKind kind)
    {
        using var runtime = CreateRuntime();
        var original = kind == SketchConstraintKind.Normal
            ? new DrawingDocument([new CircleEntity(Id("c"), new(0, 0), 3), new LineEntity(Id("l"), new(4, 2), new(7, 3))])
            : new DrawingDocument([Axis(), new CircleEntity(Id("a"), new(-4, 2), 2), new CircleEntity(Id("b"), new(6, 4), 3)]);
        var intent = new SketchConstraint("intent", kind, kind == SketchConstraintKind.Normal ? ["l:start", "c"] : ["a", "b", "axis"]);
        var result = new PlaneGcsSketchSolverAdapter(runtime).Solve(new(original, [intent], [],
            fixedReferenceKeys: original.Entities.Select(e => e.Id.Value)));
        result.Status.Should().Be(SketchSolveStatus.Failed, string.Join(" ", result.Diagnostics));
        result.Document.Entities.Should().Equal(original.Entities);
        result.Document.Constraints.Single(c => c.Id == intent.Id).State.Should().Be(SketchConstraintState.Unsatisfied);
        original.Constraints.Should().BeEmpty();
    }

    private static void AssertSolved(SketchSolveResult result, string id)
    {
        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        var constraint = result.Document.Constraints.Single(c => c.Id == id);
        SketchConstraintService.ValidateConstraint(result.Document, constraint).State.Should().Be(SketchConstraintState.Satisfied);
        PlaneGcsDiagnostics.Read(result.Document).HasCurrentEvidence.Should().BeTrue();
        var reopened = DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(result.Document));
        SketchConstraintService.ValidateConstraint(reopened, reopened.Constraints.Single(c => c.Id == id)).State.Should().Be(SketchConstraintState.Satisfied);
    }

    private static EntityId Id(string id) => EntityId.Create(id);
    private static LineEntity Axis() => new(Id("axis"), new(0, -10), new(0, 10));
    private static PlaneGcsNodeRuntime CreateRuntime()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("The repository's real PlaneGCS assets are required.");
        var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.GetFullPath(Path.Combine(directory, executable))).FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("Node is required; no mock or legacy solver is permitted.");
        return new(node, Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"),
            Path.Combine(root.FullName, "src/DXFER.Blazor/wwwroot/planeGcsSolver.js"));
    }
}

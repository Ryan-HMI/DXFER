using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

[Collection("PlaneGCS resource budget")]
public sealed class PlaneGcsCollapsedSymmetryRuntimeTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void CollapsedCircleSymmetryAllowsDiameterEditAndNativeReopen(bool oblique, bool midpoint)
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var axis = new LineEntity(Id("axis"), oblique ? new(-10, -10) : new(0, -10), oblique ? new(10, 10) : new(0, 10));
        var document = new DrawingDocument([axis, new CircleEntity(Id("a"), new(0, 0), 3), new CircleEntity(Id("b"), new(0, 0), 3)],
            [Diameter(6)], [FixedAxis(), Mirror(), midpoint
                ? new("align", SketchConstraintKind.Midpoint, ["a:center", "axis"])
                : new("align", SketchConstraintKind.Vertical, ["a:center", "b:center"])]);
        var accepted = solver.Solve(SketchSolveRequest.FromDocument(document));
        AssertSolved(accepted);
        WriteFixture("collapsed-circles", accepted.Document, !oblique);
        var edited = solver.Solve(new(accepted.Document, [], [Diameter(8)]));
        AssertSolved(edited);
        edited.Document.Entities[0].Should().Be(axis);
        edited.Document.Entities.OfType<CircleEntity>().Should().OnlyContain(c => Math.Abs(c.Radius - 4) < 1e-6);
        var reopened = RoundTrip(edited.Document);
        var next = solver.Solve(new(reopened, [], [Diameter(10)]));
        AssertSolved(next);
        next.Document.Entities.OfType<CircleEntity>().Should().OnlyContain(c => Math.Abs(c.Radius - 5) < 1e-6);
    }

    [Fact]
    public void CollapsedPointSymmetryAllowsSeparationDragAfterNativeReopen()
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var document = new DrawingDocument([Axis(), new PointEntity(Id("a"), new(0, 2)), new PointEntity(Id("b"), new(0, 2))],
            [], [FixedAxis(), Mirror()]);
        var accepted = solver.Solve(SketchSolveRequest.FromDocument(document));
        AssertSolved(accepted);
        WriteFixture("collapsed-points", accepted.Document);
        solver.TryApplyDrag(RoundTrip(accepted.Document), "a:center", new(0, 2), new(-3, 4), false,
            out var edited, out var status).Should().BeTrue(status);
        SketchReferenceResolver.TryGetPoint(edited, "a:center", out var a).Should().BeTrue();
        SketchReferenceResolver.TryGetPoint(edited, "b:center", out var b).Should().BeTrue();
        a.X.Should().BeApproximately(-3, 1e-6); a.Y.Should().BeApproximately(4, 1e-6);
        b.X.Should().BeApproximately(3, 1e-6); b.Y.Should().BeApproximately(4, 1e-6);
    }

    [Fact]
    public void SymmetricLineLengthRemainsEditableWhenStartPointsCoincide()
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        SketchDimension Length(double value) => new("length", SketchDimensionKind.LinearDistance,
            ["a:start", "a:end"], value, isDriving: true);
        var document = new DrawingDocument([Axis(), new LineEntity(Id("a"), new(0, 0), new(-6, 0)),
            new LineEntity(Id("b"), new(0, 0), new(6, 0))], [Length(6)], [FixedAxis(),
            new("mirror", SketchConstraintKind.Symmetric, ["a:start", "b:start", "a:end", "b:end", "axis"])]);
        var accepted = solver.Solve(SketchSolveRequest.FromDocument(document));
        AssertSolved(accepted); WriteFixture("collapsed-lines", accepted.Document);
        var edited = solver.Solve(new(RoundTrip(accepted.Document), [], [Length(8)]));
        AssertSolved(edited);
        SketchDimensionSolverService.TryGetDimensionMeasurement(edited.Document,
            edited.Document.Dimensions.Single(), out var measured).Should().BeTrue();
        measured.Should().BeApproximately(8, 1e-6);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EquivalentEllipseFramesDoNotChangeAlreadyMirroredGeometry(bool reverse)
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var document = new DrawingDocument([Axis(), new EllipseEntity(Id("a"), new(-5, 1), new(3, 0), .5),
            new EllipseEntity(Id("b"), new(5, 1), new(3, 0), .5)], [], [FixedAxis()]);
        WriteFixture("same-direction-ellipses", document, !reverse);
        var result = solver.Solve(new(document, [new("mirror", SketchConstraintKind.Symmetric,
            reverse ? ["b", "a", "axis"] : ["a", "b", "axis"])], []));
        AssertSolved(result);
        foreach (var (before, after) in document.Entities.OfType<EllipseEntity>().Zip(result.Document.Entities.OfType<EllipseEntity>()))
        {
            after.Center.X.Should().BeApproximately(before.Center.X, 1e-6);
            after.Center.Y.Should().BeApproximately(before.Center.Y, 1e-6);
            after.MajorAxisEndPoint.X.Should().BeApproximately(before.MajorAxisEndPoint.X, 1e-6);
            after.MajorAxisEndPoint.Y.Should().BeApproximately(before.MajorAxisEndPoint.Y, 1e-6);
            after.MinorRadiusRatio.Should().BeApproximately(before.MinorRadiusRatio, 1e-6);
        }
        AssertSolved(solver.Solve(SketchSolveRequest.FromDocument(RoundTrip(result.Document))));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void IndependentMinorRadiusRemainsEditableWithRedundantMajorDimension(int sign)
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var majorKey = FormattableString.Invariant($"b|point|major-end|{5 + sign * 3}|0");
        var minorKey = FormattableString.Invariant($"b|point|minor-end|5|{sign * 1.5}");
        SketchDimension Minor(double value) => new("minor", SketchDimensionKind.LinearDistance,
            ["b:center", minorKey], value, isDriving: true);
        var document = new DrawingDocument([Axis(), new EllipseEntity(Id("a"), new(-5, 0), new(3, 0), .5),
            new EllipseEntity(Id("b"), new(5, 0), new(sign * 3, 0), .5),
            new PointEntity(Id("target"), new(5 + sign * 3, 0))],
            [new("major", SketchDimensionKind.LinearDistance, ["b:center", majorKey], 3, isDriving: true), Minor(1.5)],
            [FixedAxis(), Mirror(), new("anchor", SketchConstraintKind.Fix, ["a:center", "a|point|major-end|-2|0", "target"]),
                new("endpoint", SketchConstraintKind.Coincident, [majorKey, "target"])]);
        var before = DxferDocumentSerializer.Write(document);

        var edited = solver.Solve(new(document, [], [Minor(2)]));

        AssertSolved(edited);
        foreach (var ellipse in edited.Document.Entities.OfType<EllipseEntity>())
        {
            var major = Math.Sqrt(ellipse.MajorAxisEndPoint.X * ellipse.MajorAxisEndPoint.X
                + ellipse.MajorAxisEndPoint.Y * ellipse.MajorAxisEndPoint.Y);
            major.Should().BeApproximately(3, 1e-6);
            (major * ellipse.MinorRadiusRatio).Should().BeApproximately(2, 1e-6);
        }
        edited.Document.Dimensions.Single(d => d.Id == "major").Value.Should().Be(3);
        AssertSolved(solver.Solve(new(RoundTrip(edited.Document), [], [Minor(2.5)])));
        DxferDocumentSerializer.Write(document).Should().Be(before);
    }

    private static void AssertSolved(SketchSolveResult result)
    {
        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        result.Document.Constraints.Should().OnlyContain(c =>
            SketchConstraintService.ValidateConstraint(result.Document, c).State == SketchConstraintState.Satisfied);
    }
    private static DrawingDocument RoundTrip(DrawingDocument document) => DxfDocumentReader.Read(
        DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true)));
    private static void WriteFixture(string name, DrawingDocument document, bool enabled = true)
    {
        var output = Environment.GetEnvironmentVariable("DXFER_COLLAPSED_SYMMETRY_FIXTURES");
        if (!enabled || string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, $"{name}.dxf"), DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true)));
    }
    private static EntityId Id(string id) => EntityId.Create(id);
    private static LineEntity Axis() => new(Id("axis"), new(0, -10), new(0, 10));
    private static SketchConstraint FixedAxis() => new("fixed", SketchConstraintKind.Fix, ["axis"]);
    private static SketchConstraint Mirror() => new("mirror", SketchConstraintKind.Symmetric, ["a", "b", "axis"]);
    private static SketchDimension Diameter(double value) => new("diameter", SketchDimensionKind.Diameter, ["a"], value, isDriving: true);
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

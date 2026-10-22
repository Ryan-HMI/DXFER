using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

[Collection("PlaneGCS resource budget")]
public sealed class PlaneGcsSymmetricNormalRuntimeTests
{
    public static IEnumerable<object[]> CurvePairs()
    {
        foreach (var reverse in new[] { false, true })
        foreach (var kind in new[] { "spline", "fit", "rational", "ellipse", "round-ellipse", "partial-ellipse", "polygon" })
            yield return [kind, reverse];
    }

    [Theory]
    [MemberData(nameof(CurvePairs))]
    public void WholeCurveSymmetryUsesExactNativeGeometryAndSurvivesReopenAndEdit(string kind, bool reverse)
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        DrawingEntity Curve(string id, double shift)
        {
            Point2[] points = shift == 0
                ? [new(-6, 0), new(-5, 2), new(-4, -1), new(-3, 1)]
                : [new(7, .5), new(6, 2.5), new(5, -.5), new(4, 1.5)];
            return kind switch
            {
                "fit" => SplineEntity.FromFitPoints(Id(id), points),
                "spline" or "rational" => new SplineEntity(Id(id), 3, points, [0, 0, 0, 0, 1, 1, 1, 1],
                    kind == "rational" ? [1, 2, 3, 1] : null),
                "polygon" => new PolygonEntity(Id(id), new(-5 + shift, 1), shift == 0 ? 3 : 2, shift == 0 ? 20 : 130, 5),
                _ => new EllipseEntity(Id(id), new(-5 + shift, 1), new(shift == 0 ? 3 : -2, 1),
                    kind == "round-ellipse" ? 1 : 0.5, kind == "partial-ellipse" ? 20 : 0, kind == "partial-ellipse" ? 100 : 360)
            };
        }
        var original = new DrawingDocument([Axis(), Curve("a", 0), Curve("b", 10)], [],
            [new SketchConstraint("axis-fixed", SketchConstraintKind.Fix, ["axis"], SketchConstraintState.Satisfied)],
            new DrawingDocumentMetadata { SourceFileName = $"symmetry-{kind}.dxf", Units = DrawingUnits.Inches });
        SketchCommandFactory.TryBuildConstraint(original, reverse ? ["b", "a", "axis"] : ["a", "b", "axis"],
            SketchConstraintKind.Symmetric, "mirror", out var intent, out var status).Should().BeTrue(status);
        var result = solver.Solve(new(original, [intent], [], fixedReferenceKeys: ["a"]));
        AssertSolved(result, "mirror");
        result.Document.Entities[1].Should().Be(original.Entities[1]);
        var refusal = solver.Solve(new(original, [intent], [], fixedReferenceKeys: ["a", "b"]));
        refusal.Status.Should().Be(SketchSolveStatus.Failed);
        refusal.Document.Entities.Should().Equal(original.Entities);
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(result.Document, new DxfWriteOptions(IncludeNativeDocument: true)));
        var key = kind is "spline" or "rational" ? "a|point|control-1|-5|2" : kind == "fit" ? "a|point|fit-1|-5|2" : "a:center";
        Point2 start;
        if (reopened.Entities[1] is SplineEntity spline) start = (kind == "fit" ? spline.FitPoints : spline.ControlPoints)[1];
        else SketchReferenceResolver.TryGetPoint(reopened, key, out start).Should().BeTrue();
        solver.TryApplyDrag(reopened, key, start, new(start.X - 1, start.Y + 0.5), false,
            out var edited, out status).Should().BeTrue(status);
        SketchConstraintService.ValidateConstraint(edited, edited.Constraints.Single(c => c.Id == "mirror"))
            .State.Should().Be(SketchConstraintState.Satisfied);
        if (edited.Entities[1] is SplineEntity a && edited.Entities[2] is SplineEntity b)
        {
            a.Degree.Should().Be(b.Degree); a.Knots.Should().Equal(b.Knots); a.Weights.Should().Equal(b.Weights);
            a.ControlPoints.Zip(b.ControlPoints).Should().OnlyContain(pair => Math.Abs(pair.First.X + pair.Second.X) < 1e-6
                && Math.Abs(pair.First.Y - pair.Second.Y) < 1e-6);
        }
        var output = Environment.GetEnvironmentVariable("DXFER_SYMMETRY_FIXTURES");
        if (!string.IsNullOrWhiteSpace(output) && !reverse)
        {
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, $"{kind}-before.dxf"), DxfDocumentWriter.Write(original, new DxfWriteOptions(IncludeNativeDocument: true)));
            Point2 Body(DrawingEntity entity) => entity switch {
                SplineEntity s => s.GetSamplePoints()[s.GetSamplePoints().Count / 3],
                EllipseEntity e => e.PointAtParameterDegrees(kind == "partial-ellipse" ? 53 : 38),
                PolygonEntity p => new(p.GetVertices()[0].X * .63 + p.GetVertices()[1].X * .37,
                    p.GetVertices()[0].Y * .63 + p.GetVertices()[1].Y * .37),
                _ => throw new InvalidOperationException()
            };
            File.WriteAllText(Path.Combine(output, $"{kind}-picks.json"), System.Text.Json.JsonSerializer.Serialize(
                new[] { Body(original.Entities[1]), Body(original.Entities[2]), new Point2(0, 8) },
                new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase }));
        }
    }

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

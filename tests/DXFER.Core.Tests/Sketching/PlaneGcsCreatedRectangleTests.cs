using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsCreatedRectangleTests
{
    private const string TwoPoint = "twopointrectangle";
    private const string ThreePoint = "alignedrectangle";
    private const double Tolerance = 0.000001;

    public static IEnumerable<object[]> CornerDirections()
    {
        foreach (var tool in new[] { TwoPoint, ThreePoint })
        foreach (var corner in Enumerable.Range(0, 4))
        foreach (var direction in new (double X, double Y)[]
                 { (1, 0), (-1, 0), (0, 1), (0, -1), (1, 1), (1, -1), (-1, 1), (-1, -1) })
            yield return [tool, corner, direction.X, direction.Y];
    }

    [Fact]
    public void CapturedUiRectangleCornerDragPreservesFactoryParallelRelations()
    {
        // Exact geometry/IDs and pointer target from created-rectangle-before.dxf.
        // Keep this independent of the machine-local /tmp browser evidence.
        var original = CreateRectangle(TwoPoint);
        var edge = (LineEntity)original.Entities[1];
        edge.End.X.Should().Be(3.458333333333334);
        edge.End.Y.Should().Be(-2.1666666666666665);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);

        solver.TryApplyDrag(original, $"{edge.Id.Value}|point|end|3.458333|-2.166667",
            edge.End, new(5.125, -4.25), false, out var result, out var status,
            automaticSnapConstraints: false).Should().BeTrue(status);

        AssertEngine(observed);
        AssertPoint(Line(result, edge.Id.Value).End, new(5.125, -4.25));
        AssertIntent(original, result);
    }

    [Theory]
    [MemberData(nameof(CornerDirections))]
    public void ActualFactoryRectangleCornersFollowAllDragDirections(string tool, int corner, double dx, double dy)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var generated = CreateRectangle(tool);
        // Match Workbench creation: full geometry with newly inserted factory relations.
        var created = solver.Solve(new(new DrawingDocument(generated.Entities, generated.Dimensions, [],
            generated.Metadata), generated.Constraints, []));
        created.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", created.Diagnostics));
        AssertIntent(generated, created.Document);
        var edge = (LineEntity)created.Document.Entities[corner];
        var target = new Point2(edge.Start.X + dx * 0.75, edge.Start.Y + dy * 0.75);

        solver.TryApplyDrag(created.Document, $"{edge.Id.Value}:start", edge.Start, target, false,
            out var result, out var status, automaticSnapConstraints: false).Should().BeTrue(status);

        AssertEngine(observed);
        AssertPoint(Line(result, edge.Id.Value).Start, target);
        AssertIntent(created.Document, result);
    }

    [Theory]
    [InlineData(TwoPoint, 0)]
    [InlineData(TwoPoint, 1)]
    [InlineData(TwoPoint, 2)]
    [InlineData(TwoPoint, 3)]
    [InlineData(ThreePoint, 0)]
    [InlineData(ThreePoint, 1)]
    [InlineData(ThreePoint, 2)]
    [InlineData(ThreePoint, 3)]
    public void EntityRelationAndReferenceOrderDoNotBreakCreatedRectangleDrag(string tool, int order)
    {
        var seed = CreateRectangle(tool);
        var entities = (order & 1) != 0 ? seed.Entities.Reverse() : seed.Entities;
        var relations = (order & 2) != 0 ? seed.Constraints.Reverse() : seed.Constraints;
        if (order == 3)
            relations = relations.Select(c => new SketchConstraint(c.Id, c.Kind, c.ReferenceKeys.Reverse(), c.State));
        var original = new DrawingDocument(entities, seed.Dimensions, relations, seed.Metadata);
        var edge = (LineEntity)seed.Entities[1];
        var target = new Point2(edge.End.X + 1.25, edge.End.Y - 1.5);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);

        new PlaneGcsSketchSolverAdapter(observed).TryApplyDrag(original, $"{edge.Id.Value}:end",
            edge.End, target, false, out var result, out var status).Should().BeTrue(status);

        AssertEngine(observed);
        AssertPoint(Line(result, edge.Id.Value).End, target);
        AssertIntent(original, result);
    }

    [Theory]
    [InlineData(TwoPoint)]
    [InlineData(ThreePoint)]
    public void DrivingRectangleDimensionsSurviveCornerDrag(string tool)
    {
        var original = CreateRectangle(tool, driving: true);
        original.Dimensions.Should().HaveCount(2).And.OnlyContain(d => d.IsDriving);
        var edge = (LineEntity)original.Entities[2];
        var target = new Point2(edge.Start.X + 1.25, edge.Start.Y - 1.5);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);

        new PlaneGcsSketchSolverAdapter(observed).TryApplyDrag(original, $"{edge.Id.Value}:start",
            edge.Start, target, false, out var result, out var status).Should().BeTrue(status);

        AssertEngine(observed);
        AssertPoint(Line(result, edge.Id.Value).Start, target);
        AssertIntent(original, result);
    }

    [Theory]
    [InlineData(TwoPoint, false)]
    [InlineData(TwoPoint, true)]
    [InlineData(ThreePoint, false)]
    [InlineData(ThreePoint, true)]
    public void FreeOppositeCornerMovesWithoutMovingFixedOrLockedEdge(string tool, bool locked)
    {
        var original = ProtectFirstEdge(CreateRectangle(tool), locked);
        var fixedEdge = (LineEntity)original.Entities[0];
        var dragged = (LineEntity)original.Entities[2];
        var dx = fixedEdge.End.X - fixedEdge.Start.X;
        var dy = fixedEdge.End.Y - fixedEdge.Start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var target = new Point2(dragged.Start.X - dy / length, dragged.Start.Y + dx / length);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);

        new PlaneGcsSketchSolverAdapter(observed).TryApplyDrag(original, $"{dragged.Id.Value}:start",
            dragged.Start, target, false, out var result, out var status).Should().BeTrue(status);

        AssertEngine(observed);
        observed.Request!.FixedReferenceKeys.Should().Contain(fixedEdge.Id.Value);
        Line(result, fixedEdge.Id.Value).Should().Be(fixedEdge);
        AssertPoint(Line(result, dragged.Id.Value).Start, target);
        AssertIntent(original, result);
    }

    [Theory]
    [InlineData(TwoPoint, false)]
    [InlineData(TwoPoint, true)]
    [InlineData(ThreePoint, false)]
    [InlineData(ThreePoint, true)]
    public void FixedOrLockedDrivingRectangleRefusesIncompatibleFreeCornerDrag(string tool, bool locked)
    {
        var original = ProtectFirstEdge(CreateRectangle(tool, driving: true), locked);
        var edge = (LineEntity)original.Entities[2];
        var snapshot = DxfDocumentWriter.Write(original, new DxfWriteOptions(IncludeNativeDocument: true));
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);

        new PlaneGcsSketchSolverAdapter(observed).TryApplyDrag(original, $"{edge.Id.Value}:start",
            edge.Start, new(edge.Start.X + 1, edge.Start.Y + 1), false,
            out var result, out var status).Should().BeFalse();

        observed.Request.Should().NotBeNull("the free corner requires a real constrained solve");
        observed.Response!.Engine.Should().Be("PlaneGCS");
        status.Should().NotBeNullOrWhiteSpace();
        result.Should().BeSameAs(original);
        DxfDocumentWriter.Write(result, new DxfWriteOptions(IncludeNativeDocument: true)).Should().Be(snapshot);
    }

    [Theory]
    [InlineData(TwoPoint)]
    [InlineData(ThreePoint)]
    public void RepeatedDragsAndNativeReopenRetainCompleteFactoryIntent(string tool)
    {
        var document = CreateRectangle(tool);
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        foreach (var step in Enumerable.Range(0, 8))
        {
            var edge = (LineEntity)document.Entities[step % 4];
            var target = new Point2(edge.Start.X + (step % 2 == 0 ? 0.4 : -0.3),
                edge.Start.Y + (step % 3 == 0 ? -0.6 : 0.5));
            solver.TryApplyDrag(document, $"{edge.Id.Value}:start", edge.Start, target, false,
                out var next, out var status).Should().BeTrue($"step {step}: {status}");
            AssertEngine(observed);
            AssertPoint(Line(next, edge.Id.Value).Start, target);
            AssertIntent(document, next);
            var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(next,
                new DxfWriteOptions(IncludeNativeDocument: true)));
            reopened.Entities.Should().BeEquivalentTo(next.Entities);
            AssertIntent(next, reopened);
            document = reopened;
        }
    }

    private static DrawingDocument CreateRectangle(string tool, bool driving = false)
    {
        Point2[] points = tool == TwoPoint
            ? [new(-3.625, 2.4166666666666665), new(3.458333333333334, -2.1666666666666665)]
            : [new(-3.625, 2.4166666666666665), new(3.458333333333334, 4.8), new(2, -2.1666666666666665)];
        var values = new Dictionary<string, double>();
        var entityIndex = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(tool, points,
            _ => EntityId.Create($"rect-1a15a36b3a7-{++entityIndex}"), false, values);
        if (driving)
        {
            values[tool == TwoPoint ? "width" : "length"] = Length((LineEntity)entities[0]);
            values[tool == TwoPoint ? "height" : "depth"] = Length((LineEntity)entities[1]);
        }
        var relationIndex = 0;
        var relations = SketchCreationConstraintFactory.CreateConstraintsForInsertion(tool, [], entities,
            kind => $"{kind.ToString().ToLowerInvariant()}-1a15a36b3a7-{++relationIndex}",
            automaticSnapConstraints: true);
        var dimensionIndex = 0;
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(tool, entities, values,
            () => $"dimension-{++dimensionIndex}");
        relations.Should().HaveCount(tool == TwoPoint ? 9 : 7);
        relations.Count(c => c.Kind == SketchConstraintKind.Coincident).Should().Be(4);
        relations.Count(c => c.Kind == SketchConstraintKind.Parallel).Should().Be(2);
        relations.Count(c => c.Kind == SketchConstraintKind.Perpendicular).Should().Be(1);
        return DrawingLayerService.AssignNewEntities(new DrawingDocument([]),
            new DrawingDocument(entities, dimensions, relations));
    }

    private static DrawingDocument ProtectFirstEdge(DrawingDocument document, bool locked)
    {
        var id = document.Entities[0].Id.Value;
        if (!locked)
            return new(document.Entities, document.Dimensions, document.Constraints.Append(
                new SketchConstraint("fixed-edge", SketchConstraintKind.Fix, [id], SketchConstraintState.Satisfied)),
                document.Metadata);
        return new(document.Entities, document.Dimensions, document.Constraints, document.Metadata with
        {
            EntityStyles = new Dictionary<string, DxfEntityStyle>(document.Metadata.EntityStyles) { [id] = new("locked") },
            Layers = new Dictionary<string, DxfEntityStyle> { ["0"] = new("0"), ["locked"] = new("locked", IsLocked: true) }
        });
    }

    private static void AssertIntent(DrawingDocument before, DrawingDocument after)
    {
        after.Entities.Select(e => e.Id).Should().Equal(before.Entities.Select(e => e.Id));
        after.Constraints.Should().BeEquivalentTo(before.Constraints);
        after.Constraints.Should().OnlyContain(c =>
            SketchConstraintService.ValidateConstraint(after, c).State == SketchConstraintState.Satisfied);
        foreach (var dimension in before.Dimensions)
        {
            var actual = after.Dimensions.Single(d => d.Id == dimension.Id);
            actual.Kind.Should().Be(dimension.Kind);
            actual.ReferenceKeys.Should().Equal(dimension.ReferenceKeys);
            actual.IsDriving.Should().Be(dimension.IsDriving);
            actual.Value.Should().Be(dimension.Value);
            SketchDimensionSolverService.IsDimensionSatisfied(after, actual).Should().BeTrue();
        }
        var lines = after.Entities.Cast<LineEntity>().ToDictionary(e => e.Id.Value);
        foreach (var relation in after.Constraints.Where(c => c.Kind == SketchConstraintKind.Coincident))
        {
            SketchReference.TryParse(relation.ReferenceKeys[0], out var first).Should().BeTrue();
            SketchReference.TryParse(relation.ReferenceKeys[1], out var second).Should().BeTrue();
            AssertPoint(first.Target == SketchReferenceTarget.Start ? lines[first.EntityId].Start : lines[first.EntityId].End,
                second.Target == SketchReferenceTarget.Start ? lines[second.EntityId].Start : lines[second.EntityId].End);
        }
        lines.Values.Should().OnlyContain(edge => Length(edge) > 0.01, "a collapsed rectangle is not a valid drag result");
    }

    private static LineEntity Line(DrawingDocument document, string id) =>
        (LineEntity)document.Entities.Single(e => e.Id.Value == id);
    private static double Length(LineEntity line) =>
        Math.Sqrt(Math.Pow(line.End.X - line.Start.X, 2) + Math.Pow(line.End.Y - line.Start.Y, 2));
    private static void AssertPoint(Point2 actual, Point2 expected)
    {
        actual.X.Should().BeApproximately(expected.X, Tolerance);
        actual.Y.Should().BeApproximately(expected.Y, Tolerance);
    }
    private static void AssertEngine(ObservedRuntime runtime)
    {
        runtime.Request.Should().NotBeNull();
        runtime.Response!.Engine.Should().Be("PlaneGCS");
        runtime.Response.Status.Should().Be("solved", string.Join(" ", runtime.Response.Diagnostics ?? []));
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

using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;
using Xunit.Abstractions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsHorizontalCircleDragTests(ITestOutputHelper output)
{
    private const double Tolerance = 0.00001;
    private static readonly string[] Peers =
        ["origin", "fixed-line", "fixed-horizontal-line", "factory-rectangle", "fixed-factory-rectangle"];

    public static IEnumerable<object[]> DragCases()
    {
        foreach (var peer in Peers)
        foreach (var diameter in new[] { false, true })
        foreach (var body in new[] { false, true })
        foreach (var diagonal in new[] { false, true })
            yield return [peer, diameter, body, diagonal];
    }

    public static IEnumerable<object[]> FixedCases()
    {
        foreach (var peer in Peers)
        foreach (var diameter in new[] { false, true })
        foreach (var body in new[] { false, true })
            yield return [peer, diameter, body];
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ExactFixedHorizontalLineMidpointReproductionAllowsCircleDrag(bool diameter, bool body)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var seed = Fixture("fixed-horizontal-line", diameter);
        var original = new DrawingDocument(seed.Entities, seed.Dimensions, seed.Constraints.Append(
            new SketchConstraint("horizontal-center", SketchConstraintKind.Horizontal,
                ["circle:center", "peer:mid"], SketchConstraintState.Satisfied)), seed.Metadata);
        var start = body ? new Point2(14, 0) : new Point2(12, 0);
        var end = body ? new Point2(19, 0) : new Point2(17, 3);

        solver.TryApplyDrag(original, body ? "circle" : "circle:center", start, end, false,
            out var result, out var status, automaticSnapConstraints: false)
            .Should().BeTrue(Describe(observed, [status]));

        AssertEngine(observed);
        AssertAlignmentAndIntent(result, "fixed-horizontal-line");
        AssertFixedPeers(original, result);
        if (!body || diameter)
        {
            AssertPoint(Circle(result).Center, new(17, 0));
            Circle(result).Radius.Should().BeApproximately(2, Tolerance);
        }
        else
            Circle(result).Radius.Should().BeGreaterThan(2, "the undimensioned body grip may resize");
    }

    [Theory]
    [MemberData(nameof(DragCases))]
    public void HorizontalCirclePreviewReleaseReopenAndDiameterEditRemainMovable(
        string peer, bool diameter, bool body, bool diagonal)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var seed = Fixture(peer, diameter);
        // Use the public constraint-edit path, not a hand-marked satisfied relation.
        var alignment = new SketchConstraint("horizontal-center", SketchConstraintKind.Horizontal,
            ["circle:center", PeerReference(peer)]);
        var applied = solver.Solve(new(seed, [alignment], []));
        applied.Status.Should().Be(SketchSolveStatus.Solved, Describe(observed, applied.Diagnostics));
        var original = applied.Document;
        AssertAlignmentAndIntent(original, peer);
        var before = Circle(original);
        var key = body ? "circle" : "circle:center";
        var start = body ? new Point2(before.Center.X + before.Radius, before.Center.Y) : before.Center;
        var end = new Point2(start.X + 1.5, start.Y + (diagonal ? 0.75 : 0));

        solver.TryApplyDrag(original, key, start, end, false, out var preview, out var previewStatus,
            automaticSnapConstraints: false).Should().BeTrue(Describe(observed, [previewStatus]));
        AssertEngine(observed);
        solver.TryApplyDrag(original, key, start, end, false, out var released, out var releaseStatus,
            automaticSnapConstraints: false).Should().BeTrue(Describe(observed, [releaseStatus]));
        AssertEngine(observed);
        AssertPoint(Circle(released).Center, Circle(preview).Center);
        Circle(released).Radius.Should().BeApproximately(Circle(preview).Radius, Tolerance);
        AssertAlignmentAndIntent(released, peer);
        released.Entities.Select(e => e.Id).Should().Equal(original.Entities.Select(e => e.Id));
        released.Constraints.Should().BeEquivalentTo(original.Constraints);
        AssertFixedPeers(original, released);

        var moved = Circle(released);
        if (!body)
            moved.Center.X.Should().BeApproximately(before.Center.X + 1.5, Tolerance,
                "Horizontal alignment constrains Y, not horizontal center motion");
        if (!body || diameter)
            moved.Radius.Should().BeApproximately(before.Radius, Tolerance,
                "center translation and a driving diameter must retain radius");
        if (body && diameter)
            Math.Abs(moved.Center.X - before.Center.X).Should().BeGreaterThan(0.01,
                "a dimensioned circle must move to follow the body pick rather than become locked");
        // Without diameter, a body grip is a radius edit, not a promised rigid translation.
        moved.Radius.Should().BeGreaterThan(0);
        if (peer != "factory-rectangle")
            moved.Center.Y.Should().BeApproximately(before.Center.Y, Tolerance);

        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(released,
            new DxfWriteOptions(IncludeNativeDocument: true)));
        AssertPoint(Circle(reopened).Center, moved.Center);
        Circle(reopened).Radius.Should().BeApproximately(moved.Radius, Tolerance);
        reopened.Constraints.Should().BeEquivalentTo(released.Constraints);
        reopened.Dimensions.Should().BeEquivalentTo(released.Dimensions);
        AssertAlignmentAndIntent(reopened, peer);
        var reopenedCircle = Circle(reopened);
        solver.TryApplyDrag(reopened, "circle:center", reopenedCircle.Center,
            new(reopenedCircle.Center.X - 0.5, reopenedCircle.Center.Y), false,
            out var draggedAgain, out var againStatus, automaticSnapConstraints: false)
            .Should().BeTrue(Describe(observed, [againStatus]));
        AssertPoint(Circle(draggedAgain).Center, new(reopenedCircle.Center.X - 0.5, reopenedCircle.Center.Y));
        Circle(draggedAgain).Radius.Should().BeApproximately(reopenedCircle.Radius, Tolerance);
        AssertAlignmentAndIntent(draggedAgain, peer);

        var nextDiameter = Circle(draggedAgain).Radius * 2 + 1;
        var edited = solver.Solve(new(draggedAgain, [],
            [new SketchDimension("diameter", SketchDimensionKind.Diameter, ["circle"],
                nextDiameter, new(18, -4), isDriving: true)]));
        edited.Status.Should().Be(SketchSolveStatus.Solved, Describe(observed, edited.Diagnostics));
        AssertEngine(observed);
        Circle(edited.Document).Radius.Should().BeApproximately(nextDiameter / 2, Tolerance);
        AssertAlignmentAndIntent(edited.Document, peer);
        AssertFixedPeers(original, edited.Document);
        output.WriteLine($"{peer}, diameter={diameter}, body={body}, diagonal={diagonal}: "
            + $"center {before.Center} -> {moved.Center}; radius {before.Radius:G17} -> {moved.Radius:G17}; reopen/edit passed");
    }

    [Theory]
    [MemberData(nameof(FixedCases))]
    public void ExplicitlyFixedHorizontalCircleRejectsCenterAndBodyMovement(string peer, bool diameter, bool body)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var seed = Fixture(peer, diameter);
        var applied = solver.Solve(new(seed,
            [new SketchConstraint("horizontal-center", SketchConstraintKind.Horizontal, ["circle:center", PeerReference(peer)]),
                new SketchConstraint("circle-fixed", SketchConstraintKind.Fix, ["circle"])], []));
        applied.Status.Should().Be(SketchSolveStatus.Solved, Describe(observed, applied.Diagnostics));
        var original = applied.Document;
        var circle = Circle(original);
        var start = body ? new Point2(circle.Center.X + circle.Radius, circle.Center.Y) : circle.Center;
        var snapshot = DxfDocumentWriter.Write(original, new DxfWriteOptions(IncludeNativeDocument: true));

        solver.TryApplyDrag(original, body ? "circle" : "circle:center", start,
            new(start.X + 1.5, start.Y + 0.75), false, out var result, out var status,
            automaticSnapConstraints: false).Should().BeFalse("whole-circle Fix must remain authoritative");

        observed.Request!.FixedReferenceKeys.Should().Contain("circle");
        observed.Response!.Engine.Should().Be("PlaneGCS");
        result.Should().BeSameAs(original);
        status.Should().NotBeNullOrWhiteSpace();
        DxfDocumentWriter.Write(result, new DxfWriteOptions(IncludeNativeDocument: true)).Should().Be(snapshot);
        AssertAlignmentAndIntent(result, peer);
        output.WriteLine($"fixed {peer}, diameter={diameter}, body={body}: {status}");
    }

    private static DrawingDocument Fixture(string peer, bool diameter)
    {
        var entities = new List<DrawingEntity>();
        var constraints = new List<SketchConstraint>();
        var y = 0.0;
        if (peer is "fixed-line" or "fixed-horizontal-line")
        {
            var explicitHorizontal = peer == "fixed-horizontal-line";
            entities.Add(explicitHorizontal
                ? new LineEntity(EntityId.Create("peer"), new(-3, 0), new(3, 0))
                : new LineEntity(EntityId.Create("peer"), new(-4, 3), new(4, 3)));
            constraints.Add(new("fixed-peer", SketchConstraintKind.Fix, ["peer"], SketchConstraintState.Satisfied));
            if (explicitHorizontal)
                constraints.Add(new("horizontal-peer", SketchConstraintKind.Horizontal, ["peer"], SketchConstraintState.Satisfied));
            y = explicitHorizontal ? 0 : 3;
        }
        else if (peer.Contains("rectangle", StringComparison.Ordinal))
        {
            var sequence = 0;
            var rectangle = SketchCreationEntityFactory.CreateEntitiesForTool("twopointrectangle",
                [new(-3.625, 2.4166666666666665), new(3.458333333333334, -2.1666666666666665)],
                _ => EntityId.Create($"rect-{++sequence}"), false);
            entities.AddRange(rectangle);
            sequence = 0;
            constraints.AddRange(SketchCreationConstraintFactory.CreateConstraintsForInsertion("twopointrectangle",
                [], rectangle, kind => $"rect-{kind}-{++sequence}"));
            constraints.Should().HaveCount(9, "use the actual factory relation combination, not synthetic four-axis intent");
            if (peer == "fixed-factory-rectangle")
                constraints.AddRange(rectangle.Select(e =>
                    new SketchConstraint($"fixed-{e.Id.Value}", SketchConstraintKind.Fix,
                        [e.Id.Value], SketchConstraintState.Satisfied)));
            y = ((LineEntity)rectangle[0]).Start.Y;
        }
        var centerX = peer == "fixed-horizontal-line" ? 12 : 14;
        var circle = SketchCreationEntityFactory.CreateEntitiesForTool("centercircle",
            [new(centerX, y), new(centerX + 2, y)], _ => EntityId.Create("circle"), false);
        entities.AddRange(circle);
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool("centercircle", circle,
            diameter ? new Dictionary<string, double> { ["diameter"] = 4 } : new Dictionary<string, double>(),
            () => "diameter");
        return new(entities, dimensions, constraints);
    }

    private static string PeerReference(string peer) => peer switch
    {
        "origin" => SketchReference.OriginKey,
        "fixed-line" or "fixed-horizontal-line" => "peer:mid",
        _ => "rect-1:mid"
    };

    private static void AssertAlignmentAndIntent(DrawingDocument document, string peer)
    {
        var y = peer == "origin" ? 0
            : document.Entities.OfType<LineEntity>().Where(e => e.Id.Value ==
                (peer is "fixed-line" or "fixed-horizontal-line" ? "peer" : "rect-1"))
                .Select(e => (e.Start.Y + e.End.Y) / 2).Single();
        Circle(document).Center.Y.Should().BeApproximately(y, Tolerance);
        document.Constraints.Should().OnlyContain(c =>
            SketchConstraintService.ValidateConstraint(document, c).State == SketchConstraintState.Satisfied);
        foreach (var dimension in document.Dimensions.Where(d => d.IsDriving))
        {
            dimension.Kind.Should().Be(SketchDimensionKind.Diameter);
            (Circle(document).Radius * 2).Should().BeApproximately(dimension.Value, Tolerance);
        }
    }

    private static void AssertFixedPeers(DrawingDocument original, DrawingDocument candidate)
    {
        foreach (var relation in original.Constraints.Where(c => c.Kind == SketchConstraintKind.Fix))
        foreach (var id in relation.ReferenceKeys)
            candidate.Entities.Single(e => e.Id.Value == id).Should().Be(original.Entities.Single(e => e.Id.Value == id));
    }

    private static CircleEntity Circle(DrawingDocument document) =>
        document.Entities.OfType<CircleEntity>().Single(e => e.Id.Value == "circle");

    private static void AssertPoint(Point2 actual, Point2 expected)
    {
        actual.X.Should().BeApproximately(expected.X, Tolerance);
        actual.Y.Should().BeApproximately(expected.Y, Tolerance);
    }

    private static void AssertEngine(ObservedRuntime runtime)
    {
        runtime.Response!.Engine.Should().Be("PlaneGCS");
        runtime.Response.Status.Should().Be("solved", string.Join(" ", runtime.Response.Diagnostics ?? []));
    }

    private static string Describe(ObservedRuntime runtime, IEnumerable<string> status) =>
        string.Join(" ", status.Concat(runtime.Response?.Diagnostics ?? []));

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

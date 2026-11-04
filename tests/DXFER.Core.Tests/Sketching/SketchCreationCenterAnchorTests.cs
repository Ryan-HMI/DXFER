using DXFER.Blazor.Interop;
using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchCreationCenterAnchorTests
{
    private const string Rectangle = "centerrectangle";
    private const string MidpointLine = "midpointline";
    private const double Tolerance = 0.000001;

    [Theory]
    [InlineData(Rectangle)]
    [InlineData(MidpointLine)]
    [InlineData("center-rectangle")]
    [InlineData("MIDPOINT_LINE")]
    public void OriginSnapAnchorsLogicalCenterExactlyOnceWithoutHelperGeometry(string tool)
    {
        var entities = Create(tool, new(0, 0));
        var constraints = Constraints(tool, entities, origin: true,
            snaps: [new(new(0, 0), "Coincident", ["$origin:center"])]);

        AssertAnchor(tool, constraints.Single(c => c.ReferenceKeys.Contains("$origin:center")), "$origin:center");
        constraints.Should().HaveCount(IsRectangle(tool) ? 10 : 2);
        constraints.Should().NotContain(c => c.Kind == SketchConstraintKind.Fix);
        entities.Should().HaveCount(IsRectangle(tool) ? 4 : 1).And.OnlyContain(e => e is LineEntity);
        AssertValid(new(entities, [], constraints));
    }

    [Theory]
    [InlineData(Rectangle, "point", false)]
    [InlineData(Rectangle, "point", true)]
    [InlineData(Rectangle, "endpoint", true)]
    [InlineData(Rectangle, "circle", true)]
    [InlineData(Rectangle, "midpoint", false)]
    [InlineData(Rectangle, "midpoint", true)]
    [InlineData(MidpointLine, "point", false)]
    [InlineData(MidpointLine, "point", true)]
    [InlineData(MidpointLine, "endpoint", true)]
    [InlineData(MidpointLine, "circle", true)]
    [InlineData(MidpointLine, "midpoint", false)]
    [InlineData(MidpointLine, "midpoint", true)]
    public void ExistingPointSnapPersistsAtNonOriginCenters(string tool, string sourceKind, bool explicitSnap)
    {
        var center = new Point2(17, -9);
        var (source, key) = Source(sourceKind, center);
        var entities = Create(tool, center);
        var kind = sourceKind == "midpoint" ? "Midpoint" : "Coincident";
        var constraints = Constraints(tool, entities, [source], snaps: explicitSnap ? [new(center, kind, [key])] : null);

        var anchor = constraints.Should().ContainSingle(c => c.ReferenceKeys.Any(k => k.StartsWith("source"))).Which;
        if (sourceKind == "midpoint")
        {
            anchor.Kind.Should().Be(SketchConstraintKind.Midpoint);
            anchor.ReferenceKeys.Should().Equal(tool == Rectangle
                ? ["source:mid", "created-1:start", "created-3:start"]
                : ["source", "created-1:mid"]);
        }
        else AssertAnchor(tool, anchor, key);
        AssertValid(new(new[] { source }.Concat(entities), [], constraints));
    }

    [Theory]
    [InlineData(Rectangle, false)]
    [InlineData(Rectangle, true)]
    [InlineData(MidpointLine, false)]
    [InlineData(MidpointLine, true)]
    public void AutoOffKeepsPlacementAndTopologyWithoutCenterAnchor(string tool, bool existingPoint)
    {
        var center = existingPoint ? new Point2(17, -9) : new Point2(0, 0);
        var entities = Create(tool, center);
        var source = new PointEntity(EntityId.Create("source"), center);
        var constraints = Constraints(tool, entities, existingPoint ? [source] : [], origin: !existingPoint,
            automatic: false, snaps: [new(center, "Coincident", [existingPoint ? "source" : "$origin:center"])]);

        constraints.Should().HaveCount(tool == Rectangle ? 9 : 0);
        constraints.Should().NotContain(c => c.ReferenceKeys.Any(k => k.StartsWith("source") || k.StartsWith("$origin")));
        AssertPoint(Center(entities), center);
        AssertValid(new(entities, [], constraints));
    }

    [Theory]
    [InlineData(Rectangle)]
    [InlineData(MidpointLine)]
    public void CenterAtOriginWithoutSnapIntentDoesNotAnchor(string tool)
    {
        Constraints(tool, Create(tool, new(0, 0))).Should().HaveCount(tool == Rectangle ? 9 : 1);
        Constraints(tool, Create(tool, new(17, -9)), origin: true).Should().HaveCount(tool == Rectangle ? 9 : 1);
    }

    [Theory]
    [InlineData(Rectangle)]
    [InlineData(MidpointLine)]
    public void InvalidExplicitCenterSourceRefusesWithoutChangingGeometry(string tool)
    {
        var entities = Create(tool, new(17, -9));
        var before = DxferDocumentSerializer.Write(new(entities));
        var act = () => Constraints(tool, entities, snaps: [new(new(17, -9), "Coincident", ["missing:center"])]);

        act.Should().Throw<InvalidOperationException>().WithMessage("*snap*");
        DxferDocumentSerializer.Write(new(entities)).Should().Be(before);
    }

    [Theory]
    [InlineData(Rectangle, 0)]
    [InlineData(Rectangle, 1)]
    [InlineData(Rectangle, 2)]
    [InlineData(Rectangle, 3)]
    [InlineData(MidpointLine, 0)]
    public void PlaneGcsResizesAboutOriginAndRetainsNativeHistorySnapshots(string tool, int corner)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var entities = Create(tool, new(0, 0));
        var before = new DrawingDocument(entities);
        var beforeSnapshot = DxferDocumentSerializer.Write(before);
        var inserted = solver.Solve(new(before, Constraints(tool, entities, origin: true), []));
        inserted.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", inserted.Diagnostics));
        AssertEngine(observed);
        inserted.Document.Entities.Should().BeEquivalentTo(entities);
        inserted.Document.Dimensions.Should().BeEmpty();
        var document = Reopen(inserted.Document);
        var insertedSnapshot = DxferDocumentSerializer.Write(document);
        var edge = (LineEntity)document.Entities[corner];
        var target = new Point2(edge.Start.X * 1.5, edge.Start.Y * 2);

        solver.TryApplyDrag(document, $"{edge.Id.Value}:start", edge.Start, target, false,
            out var resized, out var status, automaticSnapConstraints: false).Should().BeTrue(status);

        AssertEngine(observed);
        AssertPoint(((LineEntity)resized.Entities[corner]).Start, target);
        AssertPoint(Center(resized.Entities), new(0, 0));
        resized.Constraints.Should().BeEquivalentTo(document.Constraints);
        AssertValid(resized);
        var redo = Reopen(resized);
        redo.Entities.Should().BeEquivalentTo(resized.Entities);
        redo.Constraints.Should().BeEquivalentTo(resized.Constraints);
        AssertValid(redo);
        DxferDocumentSerializer.Write(before).Should().Be(beforeSnapshot, "undo before insertion retains the original snapshot");
        DxferDocumentSerializer.Write(document).Should().Be(insertedSnapshot, "undo after resizing retains the anchored snapshot");
        AssertPoint(Center(DxferDocumentSerializer.Read(insertedSnapshot).Entities), new(0, 0));
    }

    [Theory]
    [InlineData(Rectangle, false)]
    [InlineData(Rectangle, true)]
    [InlineData(MidpointLine, false)]
    [InlineData(MidpointLine, true)]
    public void PlaneGcsExistingPointAnchorFollowsSourceAfterNativeReopen(string tool, bool midpointSource)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var center = new Point2(17, -9);
        var (source, key) = Source(midpointSource ? "midpoint" : "point", center);
        var entities = Create(tool, center);
        var inserted = solver.Solve(new(new DrawingDocument(new[] { source }.Concat(entities)),
            Constraints(tool, entities, [source], snaps: [new(center, midpointSource ? "Midpoint" : "Coincident", [key])]), []));
        inserted.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", inserted.Diagnostics));
        var document = Reopen(inserted.Document);
        var target = new Point2(21, -6);

        solver.TryApplyDrag(document, midpointSource ? "source" : "source:center", center, target, false,
            out var moved, out var status, automaticSnapConstraints: false).Should().BeTrue(status);

        AssertEngine(observed);
        SketchReferenceResolver.TryGetPoint(moved, midpointSource ? "source:mid" : "source", out var sourcePoint).Should().BeTrue();
        AssertPoint(sourcePoint, target);
        AssertPoint(Center(moved.Entities.Where(e => e.Id.Value != "source").ToArray()), target);
        moved.Constraints.Should().BeEquivalentTo(document.Constraints);
        AssertValid(moved);
    }

    [Theory]
    [InlineData(Rectangle)]
    [InlineData(MidpointLine)]
    public void PlaneGcsAutoOffAllowsCenterToTranslate(string tool)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var entities = Create(tool, new(0, 0));
        var document = new DrawingDocument(entities, [], Constraints(tool, entities, origin: true, automatic: false));
        var edge = (LineEntity)entities[0];
        var target = new Point2(edge.Start.X + 3, edge.Start.Y + 2);

        solver.TryApplyDrag(document, edge.Id.Value, edge.Start, target, false,
            out var moved, out var status, automaticSnapConstraints: false).Should().BeTrue(status);

        AssertEngine(observed);
        Center(moved.Entities).Should().NotBe(new Point2(0, 0));
        moved.Constraints.Should().BeEquivalentTo(document.Constraints);
        AssertValid(moved);
    }

    [Theory]
    [InlineData("width")]
    [InlineData("height")]
    public void PlaneGcsPartlyKeyedRectangleRetainsOnlyTheUnkeyedResizeFreedom(string keyed)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var values = new Dictionary<string, double> { [keyed] = keyed == "width" ? 10 : 6 };
        var document = Reopen(InsertKeyed(solver, Rectangle, values));
        var edge = (LineEntity)document.Entities[0];
        var target = new Point2(edge.Start.X * (keyed == "width" ? 1 : 1.5),
            edge.Start.Y * (keyed == "height" ? 1 : 2));

        solver.TryApplyDrag(document, $"{edge.Id.Value}:start", edge.Start, target, false,
            out var resized, out var status, automaticSnapConstraints: false).Should().BeTrue(status);

        AssertEngine(observed);
        AssertPoint(((LineEntity)resized.Entities[0]).Start, target);
        AssertPoint(Center(resized.Entities), new(0, 0));
        resized.Dimensions.Should().ContainSingle();
        resized.Constraints.Should().BeEquivalentTo(document.Constraints);
        AssertValid(resized);
    }

    [Theory]
    [InlineData(Rectangle)]
    [InlineData(MidpointLine)]
    public void PlaneGcsFullyKeyedOriginShapeRefusesIncompatibleDragAndCanEditSize(string tool)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var values = tool == Rectangle
            ? new Dictionary<string, double> { ["width"] = 10, ["height"] = 6 }
            : new Dictionary<string, double> { ["length"] = 10 };
        var document = Reopen(InsertKeyed(solver, tool, values));
        var snapshot = DxferDocumentSerializer.Write(document);
        var edge = (LineEntity)document.Entities[0];

        solver.TryApplyDrag(document, $"{edge.Id.Value}:start", edge.Start,
            new(edge.Start.X - 2, edge.Start.Y - 2), false,
            out var rejected, out var status, automaticSnapConstraints: false).Should().BeFalse();

        observed.Response!.Engine.Should().Be("PlaneGCS");
        status.Should().NotBeNullOrWhiteSpace();
        rejected.Should().BeSameAs(document);
        DxferDocumentSerializer.Write(rejected).Should().Be(snapshot);
        var dimension = document.Dimensions[0];
        var edited = solver.Solve(new(document, [], [new SketchDimension(dimension.Id, dimension.Kind,
            dimension.ReferenceKeys, 14, dimension.Anchor, true)]));

        edited.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", edited.Diagnostics));
        AssertEngine(observed);
        AssertPoint(Center(edited.Document.Entities), new(0, 0));
        edited.Document.Dimensions.Single(d => d.Id == dimension.Id).Value.Should().Be(14);
        edited.Document.Constraints.Should().BeEquivalentTo(document.Constraints);
        AssertValid(edited.Document);
        AssertValid(Reopen(edited.Document));
        DxferDocumentSerializer.Write(document).Should().Be(snapshot);
    }

    [Theory]
    [InlineData(Rectangle)]
    [InlineData(MidpointLine)]
    public void PlaneGcsKeyedAutoOffPreservesDimensionsButAllowsTranslation(string tool)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var values = tool == Rectangle
            ? new Dictionary<string, double> { ["width"] = 10, ["height"] = 6 }
            : new Dictionary<string, double> { ["length"] = 10 };
        var document = Reopen(InsertKeyed(solver, tool, values, automatic: false));
        var edge = (LineEntity)document.Entities[0];

        solver.TryApplyDrag(document, edge.Id.Value, edge.Start, new(edge.Start.X + 3, edge.Start.Y + 2), false,
            out var moved, out var status, automaticSnapConstraints: false).Should().BeTrue(status);

        AssertEngine(observed);
        Center(moved.Entities).Should().NotBe(new Point2(0, 0));
        moved.Constraints.Should().BeEquivalentTo(document.Constraints);
        moved.Dimensions.Select(d => d.Value).Should().Equal(document.Dimensions.Select(d => d.Value));
        AssertValid(moved);
    }

    [Fact]
    public void PlaneGcsKeyedMidpointLineRetainsRotationAboutOriginWithoutAnAxisSnap()
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var solver = new PlaneGcsSketchSolverAdapter(observed);
        var values = new Dictionary<string, double> { ["length"] = 10 };
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(MidpointLine, [new(0, 0), new(3, 4)],
            _ => EntityId.Create("created-1"), false, values);
        var constraints = Constraints(MidpointLine, entities, origin: true);
        constraints.Should().ContainSingle().Which.Kind.Should().Be(SketchConstraintKind.Coincident);
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(MidpointLine, entities, values, () => "length");
        var inserted = solver.Solve(new(new DrawingDocument(entities), constraints, dimensions));
        inserted.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", inserted.Diagnostics));
        var document = Reopen(inserted.Document);
        var edge = (LineEntity)document.Entities[0];

        solver.TryApplyDrag(document, "created-1:end", edge.End, new(4, 3), false,
            out var rotated, out var status, automaticSnapConstraints: false).Should().BeTrue(status);

        AssertEngine(observed);
        AssertPoint(((LineEntity)rotated.Entities[0]).End, new(4, 3));
        AssertPoint(Center(rotated.Entities), new(0, 0));
        rotated.Dimensions.Single().Value.Should().Be(10);
        rotated.Constraints.Should().BeEquivalentTo(document.Constraints);
        AssertValid(rotated);
    }

    [Fact]
    public void OrdinaryLineDoesNotAcquireItsUnpickedMidpointAndCircleCenterStillAnchors()
    {
        var line = new LineEntity(EntityId.Create("line"), new(-5, 0), new(5, 0));
        Constraints("line", [line], origin: true).Should().ContainSingle().Which.Kind.Should().Be(SketchConstraintKind.Horizontal);
        var circle = new CircleEntity(EntityId.Create("circle"), new(0, 0), 5);
        var constraints = Constraints("centercircle", [circle], origin: true);
        constraints.Should().ContainSingle().Which.ReferenceKeys.Should().Equal("$origin:center", "circle:center");
        AssertValid(new([circle], [], constraints));
    }

    [Theory]
    [InlineData("Horizontal")]
    [InlineData("Vertical")]
    [InlineData("PointOnLine")]
    public void UnrepresentableRectangleCenterSnapRefusesInsteadOfSilentlyDroppingIntent(string kind)
    {
        var entities = Create(Rectangle, new(17, -9));
        var act = () => Constraints(Rectangle, entities,
            snaps: [new(new(17, -9), kind, ["source:start"])]);
        act.Should().Throw<InvalidOperationException>().WithMessage("*derived center*");
    }

    private static DrawingDocument InsertKeyed(PlaneGcsSketchSolverAdapter solver, string tool,
        IReadOnlyDictionary<string, double> values, bool automatic = true)
    {
        var entities = Create(tool, new(0, 0), values);
        var id = 0;
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(tool, entities, values,
            () => $"dimension-{++id}");
        dimensions.Should().HaveCount(values.Count).And.OnlyContain(d => d.IsDriving);
        var result = solver.Solve(new(new DrawingDocument(entities),
            Constraints(tool, entities, origin: true, automatic: automatic), dimensions));
        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        result.Document.Entities.Should().BeEquivalentTo(entities);
        AssertPoint(Center(result.Document.Entities), new(0, 0));
        AssertValid(result.Document);
        return result.Document;
    }

    private static IReadOnlyList<DrawingEntity> Create(string tool, Point2 center,
        IReadOnlyDictionary<string, double>? values = null)
    {
        var id = 0;
        return SketchCreationEntityFactory.CreateEntitiesForTool(tool,
            [center, new(center.X + 5, center.Y + (IsRectangle(tool) ? 3 : 0))],
            _ => EntityId.Create($"created-{++id}"), false, values);
    }

    private static IReadOnlyList<SketchConstraint> Constraints(string tool, IReadOnlyList<DrawingEntity> entities,
        IReadOnlyList<DrawingEntity>? existing = null, bool origin = false, bool automatic = true,
        IReadOnlyList<CanvasCreationSnapDto>? snaps = null)
    {
        var id = 0;
        return SketchCreationConstraintFactory.CreateConstraintsForInsertion(tool, existing ?? [], entities,
            _ => $"constraint-{++id}", originSnapUsed: origin, creationSnaps: snaps, automaticSnapConstraints: automatic);
    }

    private static (DrawingEntity Entity, string Key) Source(string kind, Point2 center) => kind switch
    {
        "endpoint" => (new LineEntity(EntityId.Create("source"), center, new(center.X + 2, center.Y + 4)), "source:start"),
        "circle" => (new CircleEntity(EntityId.Create("source"), center, 1), "source:center"),
        "midpoint" => (new LineEntity(EntityId.Create("source"), new(center.X - 2, center.Y - 4), new(center.X + 2, center.Y + 4)), "source"),
        _ => (new PointEntity(EntityId.Create("source"), center), "source")
    };

    private static bool IsRectangle(string tool) => tool.Contains("rectangle", StringComparison.OrdinalIgnoreCase);

    private static void AssertAnchor(string tool, SketchConstraint anchor, string key)
    {
        anchor.Kind.Should().Be(IsRectangle(tool) ? SketchConstraintKind.Midpoint : SketchConstraintKind.Coincident);
        anchor.ReferenceKeys.Should().Equal(IsRectangle(tool)
            ? [key, "created-1:start", "created-3:start"] : [key, "created-1:mid"]);
    }

    private static Point2 Center(IReadOnlyList<DrawingEntity> entities)
    {
        var first = (LineEntity)entities[0];
        var opposite = entities.Count == 1 ? first.End : ((LineEntity)entities[2]).Start;
        return new((first.Start.X + opposite.X) / 2, (first.Start.Y + opposite.Y) / 2);
    }

    private static DrawingDocument Reopen(DrawingDocument document)
    {
        var native = DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(document));
        return DxfDocumentReader.Read(DxfDocumentWriter.Write(native, new(IncludeNativeDocument: true)));
    }

    private static void AssertPoint(Point2 actual, Point2 expected)
    {
        actual.X.Should().BeApproximately(expected.X, Tolerance);
        actual.Y.Should().BeApproximately(expected.Y, Tolerance);
    }

    private static void AssertValid(DrawingDocument document)
    {
        foreach (var constraint in document.Constraints)
            SketchConstraintService.ValidateConstraint(document, constraint).State.Should().Be(SketchConstraintState.Satisfied);
        foreach (var dimension in document.Dimensions)
            SketchDimensionSolverService.IsDimensionSatisfied(document, dimension).Should().BeTrue();
    }

    private static void AssertEngine(ObservedRuntime runtime)
    {
        runtime.Response.Should().NotBeNull();
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
        public PlaneGcsResponse? Response { get; private set; }
        public PlaneGcsResponse Solve(PlaneGcsRequest request) => Response = inner.Solve(request);
    }
}

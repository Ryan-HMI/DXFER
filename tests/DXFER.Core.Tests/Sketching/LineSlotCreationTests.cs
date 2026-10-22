using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.IO;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

[Collection("PlaneGCS resource budget")]
public sealed class LineSlotCreationTests
{
    private static DrawingDocument Source(double x = 10, double y = 0) => new(
        [new LineEntity(EntityId.Create("source"), default, new(x, y))], [],
        [new("fixed-start", SketchConstraintKind.Fix, ["source:start"], SketchConstraintState.Satisfied)]);

    private static DrawingDocument Build(DrawingDocument document, double width = 2, bool keyed = true, string key = "source")
    {
        var entity = 0;
        var constraint = 0;
        return LineSlotCreation.Create(document, key, width, keyed,
            prefix => EntityId.Create(prefix + "-" + ++entity), kind => kind + "-" + ++constraint, () => "slot-width");
    }

    [Fact]
    public void CandidatePreservesSourceIdentityAndIntentWithoutAddingAnotherCenterline()
    {
        var original = Source();
        var snapshot = DxferDocumentSerializer.Write(original);
        var candidate = Build(original);
        candidate.Entities.Should().HaveCount(5);
        var source = candidate.Entities.OfType<LineEntity>().Single(e => e.Id.Value == "source");
        source.Should().Be(((LineEntity)original.Entities[0]) with { IsConstruction = true });
        candidate.Entities.Count(e => e.IsConstruction).Should().Be(1);
        candidate.Entities.OfType<ArcEntity>().Should().HaveCount(2);
        candidate.Constraints[0].Should().BeSameAs(original.Constraints[0]);
        candidate.Constraints.Count(c => c.Kind == SketchConstraintKind.Coincident).Should().Be(6);
        candidate.Constraints.Count(c => c.Kind == SketchConstraintKind.Tangent).Should().Be(4);
        candidate.Constraints.Count(c => c.Kind == SketchConstraintKind.Equal).Should().Be(1);
        candidate.Constraints.Should().NotContain(c => c.Kind == SketchConstraintKind.Parallel);
        candidate.Dimensions.Should().ContainSingle().Which.Kind.Should().Be(SketchDimensionKind.PointToLineDistance);
        candidate.Dimensions.Single().IsDriving.Should().BeTrue();
        candidate.Dimensions.Single().Value.Should().Be(2);
        DxferDocumentSerializer.Write(original).Should().Be(snapshot);
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(0, 10)]
    [InlineData(8, 6)]
    [InlineData(-8, -6)]
    public void BoundaryMatchesExistingSlotFactoryWithoutItsExtraCenterline(double x, double y)
    {
        var original = Source(x, y);
        var candidate = Build(original);
        var length = Math.Sqrt(x * x + y * y);
        var sequence = 0;
        var factory = SketchCreationEntityFactory.CreateEntitiesForTool("slot", [default, new(x, y), new(-y / length, x / length)],
            prefix => EntityId.Create(prefix + "-" + ++sequence), isConstruction: false);
        candidate.Entities.Skip(1).Should().Equal(factory.Take(4));
        candidate.Constraints.Should().OnlyContain(c => SketchConstraintService.ValidateConstraint(candidate, c).State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void PointerWidthHasNoDrivingDimensionAndAddedEdgesUseSourceLayer()
    {
        var original = Source();
        original = new DrawingDocument(original.Entities, original.Dimensions, original.Constraints, original.Metadata with
        {
            EntityStyles = new Dictionary<string, DxfEntityStyle> { ["source"] = new("source-layer", TrueColor: 123) },
            ActiveLayerName = "locked", Layers = new Dictionary<string, DxfEntityStyle>
            {
                ["source-layer"] = new("source-layer"), ["locked"] = new("locked", IsLocked: true)
            }
        });
        var candidate = Build(original, keyed: false);
        candidate.Dimensions.Should().BeEmpty();
        candidate.Entities.Should().OnlyContain(e => DrawingLayerService.LayerName(candidate, e.Id.Value) == "source-layer");
        DrawingLayerService.AssignNewEntities(original, candidate).Should().BeSameAs(candidate);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.Epsilon)]
    public void InvalidWidthRefusesWithoutMutation(double width)
    {
        var original = Source();
        var snapshot = DxferDocumentSerializer.Write(original);
        Action create = () => Build(original, width);
        create.Should().Throw<InvalidOperationException>();
        DxferDocumentSerializer.Write(original).Should().Be(snapshot);
    }

    [Theory]
    [InlineData("source:start")]
    [InlineData("source|segment|0")]
    [InlineData("missing")]
    [InlineData("")]
    public void OnlyWholeExistingLineCanBeSource(string key)
    {
        Action create = () => Build(Source(), key: key);
        create.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("locked")]
    [InlineData("hidden")]
    [InlineData("readonly")]
    [InlineData("generated")]
    [InlineData("degenerate")]
    [InlineData("nonfinite")]
    [InlineData("circle")]
    public void ProtectedGeneratedOrInvalidSourceRefuses(string scenario)
    {
        var original = Source();
        var key = "source";
        if (scenario is "locked" or "hidden" or "readonly")
            original = new DrawingDocument(original.Entities, original.Dimensions, original.Constraints, original.Metadata with
            {
                Mode = scenario == "readonly" ? DrawingDocumentMode.ReferenceOnly : DrawingDocumentMode.Editable,
                Layers = new Dictionary<string, DxfEntityStyle> { ["0"] = new("0", IsHidden: scenario == "hidden", IsLocked: scenario == "locked") }
            });
        else if (scenario == "generated")
        {
            DrawingPatternService.TryCreate(original, new PatternDefinition { Id = "array", SourceEntityIds = ["source"], YPitch = 2, XPitch = 20 },
                out original, out var reason).Should().BeTrue(reason);
            key = original.Metadata.Patterns.Single().Members[0].EntityId;
        }
        else
        {
            DrawingEntity source = scenario == "circle"
                ? new CircleEntity(EntityId.Create("source"), default, 1)
                : new LineEntity(EntityId.Create("source"), default, scenario == "degenerate" ? default : new(double.NaN, 1));
            original = new DrawingDocument([source]);
        }
        Action create = () => Build(original, key: key);
        create.Should().Throw<InvalidOperationException>();
        original.Entities.First().IsConstruction.Should().BeFalse();
    }

    [Theory]
    [InlineData("entity")]
    [InlineData("constraint")]
    [InlineData("dimension")]
    public void DuplicateAllocatedIdentityRefuses(string kind)
    {
        var original = Source();
        original = new DrawingDocument(original.Entities,
            [new("existing-dimension", SketchDimensionKind.LinearDistance, ["source"], 10)], original.Constraints, original.Metadata);
        var entity = 0;
        var constraint = 0;
        Action create = () => LineSlotCreation.Create(original, "source", 2, true,
            _ => EntityId.Create(kind == "entity" ? "source" : "entity-" + ++entity),
            _ => kind == "constraint" ? "fixed-start" : "constraint-" + ++constraint,
            () => kind == "dimension" ? "existing-dimension" : "width");
        create.Should().Throw<InvalidOperationException>();
        original.Entities.Single().IsConstruction.Should().BeFalse();
    }

    [Theory]
    [InlineData(10, 0)]
    [InlineData(0, 10)]
    [InlineData(8, 6)]
    [InlineData(-8, -6)]
    public void RealPlaneGcsCreationWidthEditAndSourceEditRemainLinked(double x, double y)
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var original = Source(x, y);
        var candidate = Build(original);
        var created = solver.Solve(new(candidate, [], [], fixedReferenceKeys: original.Entities.Select(e => e.Id.Value)));
        created.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", created.Diagnostics));
        SourceLine(created.Document).Should().Be(SourceLine(candidate));
        AssertSlot(created.Document, 2);
        var width = created.Document.Dimensions.Single();
        var wider = solver.Solve(new(created.Document, [],
            [new(width.Id, width.Kind, width.ReferenceKeys, 4, width.Anchor, true)], fixedReferenceKeys: ["source"]));
        wider.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", wider.Diagnostics));
        AssertSlot(wider.Document, 4);
        SourceLine(wider.Document).Should().Be(SourceLine(candidate));
        var target = new Point2(x + 3, y + 2);
        solver.TryApplyDrag(wider.Document, "source:end", new(x, y), target, false, out var moved, out var status)
            .Should().BeTrue(status);
        SourceLine(moved).End.X.Should().BeApproximately(target.X, 1e-6);
        SourceLine(moved).End.Y.Should().BeApproximately(target.Y, 1e-6);
        AssertSlot(moved, 4);
    }

    [Fact]
    public void RealPlaneGcsNativeRoundTripSupportsAnotherWidthAndSourceEdit()
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var original = Source(8, 6);
        var candidate = Build(original);
        var created = solver.Solve(new(candidate, [], [], fixedReferenceKeys: ["source"]));
        created.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", created.Diagnostics));
        var native = DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(created.Document));
        var embedded = DxfDocumentReader.Read(DxfDocumentWriter.Write(created.Document, new(IncludeNativeDocument: true)), out var restored);
        restored.Should().BeTrue();
        foreach (var reopened in new[] { native, embedded })
        {
            var dimension = reopened.Dimensions.Single();
            var changed = solver.Solve(new(reopened, [], [new(dimension.Id, dimension.Kind, dimension.ReferenceKeys, 3, dimension.Anchor, true)],
                fixedReferenceKeys: ["source"]));
            changed.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", changed.Diagnostics));
            solver.TryApplyDrag(changed.Document, "source:end", new(8, 6), new(9, 8), false, out var moved, out var status)
                .Should().BeTrue(status);
            SourceLine(moved).End.X.Should().BeApproximately(9, 1e-6);
            SourceLine(moved).End.Y.Should().BeApproximately(8, 1e-6);
            AssertSlot(moved, 3);
            DxferDocumentSerializer.Read(DxferDocumentSerializer.Write(moved)).Constraints.Select(c => c.Id)
                .Should().Equal(reopened.Constraints.Select(c => c.Id));
        }
    }

    [Theory]
    [InlineData(false, 10, 0)]
    [InlineData(true, 10, 0)]
    [InlineData(false, 0, 10)]
    [InlineData(true, 0, 10)]
    [InlineData(false, 8, 6)]
    [InlineData(true, 8, 6)]
    [InlineData(false, -8, -6)]
    [InlineData(true, -8, -6)]
    public void RealPlaneGcsUiWidthEditUsesNoTemporarySourceFix(bool fixedStart, double x, double y)
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var original = Source(x, y);
        if (!fixedStart) original = new DrawingDocument(original.Entities);
        var candidate = Build(original);
        var created = solver.Solve(new(candidate, [], [], fixedReferenceKeys: original.Entities.Select(e => e.Id.Value)));
        created.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", created.Diagnostics));
        var baseline = created.Document;
        var width = baseline.Dimensions.Single();
        var changed = solver.Solve(new(baseline, [], [new(width.Id, width.Kind, width.ReferenceKeys, 4, width.Anchor, true)]));
        changed.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", changed.Diagnostics));
        AssertSlot(changed.Document, 4);
        var source = SourceLine(changed.Document);
        source.Start.X.Should().BeApproximately(0, 1e-6);
        source.Start.Y.Should().BeApproximately(0, 1e-6);
        source.End.X.Should().BeApproximately(x, 1e-6);
        source.End.Y.Should().BeApproximately(y, 1e-6);
        changed.Document.Constraints.Select(c => c.Id).Should().Equal(baseline.Constraints.Select(c => c.Id));
        SourceLine(baseline).Should().Be(SourceLine(candidate));
    }

    [Fact]
    public void FixedSourceCanCreateSlotButCannotBeDraggedAfterwards()
    {
        using var runtime = CreateRuntime();
        var original = Source();
        original = new DrawingDocument(original.Entities, [], [new("whole-fixed", SketchConstraintKind.Fix, ["source"], SketchConstraintState.Satisfied)]);
        var candidate = Build(original);
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var created = solver.Solve(new(candidate, [], []));
        created.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", created.Diagnostics));
        solver.TryApplyDrag(created.Document, "source:end", new(10, 0), new(12, 1), false, out var unchanged, out _).Should().BeFalse();
        unchanged.Should().BeSameAs(created.Document);
    }

    [Fact]
    public void RealPlaneGcsSourceLengthDimensionPropagatesThroughPermanentCenterLinks()
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var original = Source(8, 6);
        var candidate = Build(original);
        var created = solver.Solve(new(candidate, [], [], fixedReferenceKeys: ["source"]));
        created.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", created.Diagnostics));
        var length = new SketchDimension("source-length", SketchDimensionKind.LinearDistance, ["source:start", "source:end"], 13, isDriving: true);
        var changed = solver.Solve(new(created.Document, [], [length]));
        changed.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", changed.Diagnostics));
        var line = SourceLine(changed.Document);
        Math.Sqrt(Math.Pow(line.End.X - line.Start.X, 2) + Math.Pow(line.End.Y - line.Start.Y, 2))
            .Should().BeApproximately(13, 1e-6);
        AssertSlot(changed.Document, 2);
    }

    [Fact]
    public void RealPlaneGcsPreservesExistingSourceDrivingIntentThroughWidthAndLengthEdits()
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var original = Source();
        var sourceLength = new SketchDimension("existing-length", SketchDimensionKind.LinearDistance, ["source:start", "source:end"], 10, isDriving: true);
        original = new DrawingDocument(original.Entities, [sourceLength], original.Constraints.Append(
            new SketchConstraint("existing-horizontal", SketchConstraintKind.Horizontal, ["source"], SketchConstraintState.Satisfied)), original.Metadata);
        var candidate = Build(original);
        candidate.Dimensions.First().Should().BeSameAs(sourceLength);
        candidate.Constraints.Take(original.Constraints.Count).Should().Equal(original.Constraints);
        var created = solver.Solve(new(candidate, [], [], fixedReferenceKeys: original.Entities.Select(e => e.Id.Value)));
        created.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", created.Diagnostics));
        var width = created.Document.Dimensions.Single(d => d.Id == "slot-width");
        var wider = solver.Solve(new(created.Document, [], [new(width.Id, width.Kind, width.ReferenceKeys, 4, width.Anchor, true)]));
        wider.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", wider.Diagnostics));
        AssertSlot(wider.Document, 4);
        var longer = solver.Solve(new(wider.Document, [],
            [new(sourceLength.Id, sourceLength.Kind, sourceLength.ReferenceKeys, 13, isDriving: true)]));
        longer.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", longer.Diagnostics));
        SourceLine(longer.Document).End.X.Should().BeApproximately(13, 1e-6);
        SourceLine(longer.Document).End.Y.Should().BeApproximately(0, 1e-6);
        AssertSlot(longer.Document, 4);
        longer.Document.Constraints.Select(c => c.Id).Should().Equal(candidate.Constraints.Select(c => c.Id));
    }

    [Fact]
    public void RealPlaneGcsPointerWidthCanCreateAndFollowSourceWithoutPermanentWidthIntent()
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var candidate = Build(Source(8, 6), keyed: false);
        var created = solver.Solve(new(candidate, [], [], fixedReferenceKeys: ["source"]));
        created.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", created.Diagnostics));
        created.Document.Dimensions.Should().BeEmpty();
        solver.TryApplyDrag(created.Document, "source:end", new(8, 6), new(11, 8), false, out var moved, out var status)
            .Should().BeTrue(status);
        SourceLine(moved).End.X.Should().BeApproximately(11, 1e-6);
        SourceLine(moved).End.Y.Should().BeApproximately(8, 1e-6);
        moved.Dimensions.Should().BeEmpty();
        foreach (var constraint in moved.Constraints)
            SketchConstraintService.ValidateConstraint(moved, constraint).State.Should().Be(SketchConstraintState.Satisfied, constraint.Id);
    }

    [Fact]
    public void RealPlaneGcsConflictingWidthEditDoesNotMoveGeometryOrMutateBaseline()
    {
        using var runtime = CreateRuntime();
        var solver = new PlaneGcsSketchSolverAdapter(runtime);
        var candidate = Build(Source());
        var created = solver.Solve(new(candidate, [], [], fixedReferenceKeys: ["source"]));
        created.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", created.Diagnostics));
        var arc = created.Document.Entities.OfType<ArcEntity>().First();
        var baseline = new DrawingDocument(created.Document.Entities, created.Document.Dimensions,
            created.Document.Constraints.Append(new SketchConstraint("fixed-cap", SketchConstraintKind.Fix, [arc.Id.Value], SketchConstraintState.Satisfied)),
            created.Document.Metadata);
        var snapshot = DxferDocumentSerializer.Write(baseline);
        var width = baseline.Dimensions.Single();
        var rejected = solver.Solve(new(baseline, [], [new(width.Id, width.Kind, width.ReferenceKeys, 4, width.Anchor, true)]));
        rejected.Status.Should().Be(SketchSolveStatus.Failed);
        rejected.Document.Entities.Should().Equal(baseline.Entities);
        rejected.Document.Constraints.Single(c => c.Id == "fixed-cap").Should().Be(baseline.Constraints.Last());
        DxferDocumentSerializer.Write(baseline).Should().Be(snapshot);
    }

    private static LineEntity SourceLine(DrawingDocument document) => document.Entities.OfType<LineEntity>().Single(e => e.Id.Value == "source");

    private static void AssertSlot(DrawingDocument document, double width)
    {
        var source = SourceLine(document);
        source.IsConstruction.Should().BeTrue();
        document.Entities.Should().HaveCount(5);
        var arcs = document.Entities.OfType<ArcEntity>().ToArray();
        arcs.Should().HaveCount(2);
        arcs[0].Center.X.Should().BeApproximately(source.End.X, 1e-6);
        arcs[0].Center.Y.Should().BeApproximately(source.End.Y, 1e-6);
        arcs[1].Center.X.Should().BeApproximately(source.Start.X, 1e-6);
        arcs[1].Center.Y.Should().BeApproximately(source.Start.Y, 1e-6);
        arcs.Should().OnlyContain(a => Math.Abs(a.Radius - width / 2) < 1e-6);
        foreach (var constraint in document.Constraints)
            SketchConstraintService.ValidateConstraint(document, constraint).State.Should().Be(SketchConstraintState.Satisfied, constraint.Id);
        SketchDimensionSolverService.TryGetDimensionMeasurement(document, document.Dimensions.Single(d => d.Id == "slot-width"), out var measured).Should().BeTrue();
        measured.Should().BeApproximately(width, 1e-6);
    }

    private static PlaneGcsNodeRuntime CreateRuntime()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"))) root = root.Parent;
        if (root is null) throw new InvalidOperationException("Real PlaneGCS repository assets are required.");
        var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Path.GetFullPath(Path.Combine(p, executable))).FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("Node is required for real PlaneGCS tests.");
        return new(node, Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs"),
            Path.Combine(root.FullName, "src/DXFER.Blazor/wwwroot/planeGcsSolver.js"));
    }
}

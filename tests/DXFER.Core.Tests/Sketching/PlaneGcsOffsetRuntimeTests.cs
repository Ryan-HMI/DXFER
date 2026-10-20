using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsOffsetRuntimeTests
{
    [Theory]
    [InlineData(1e-12, true)]
    [InlineData(1e-5, false)]
    public void OffsetValidationToleratesRoundoffButNotBrokenSourceJoins(double separation, bool valid)
    {
        DrawingEntity[] entities =
        [
            new LineEntity(EntityId.Create("a"), new(0, 0), new(8, 0)),
            new LineEntity(EntityId.Create("b"), new(8 + separation, 0), new(8, 6)),
            new LineEntity(EntityId.Create("c"), new(8, 6), new(0, 6)),
            new LineEntity(EntityId.Create("d"), new(0, 6), new(0, 0)),
            Rectangle("dependent", 1)
        ];
        var originalEndpoint = ((LineEntity)entities[1]).Start;
        SketchOffsetRelation.IsSatisfied(entities, ["a", "b", "c", "d", "dependent"]).Should().Be(valid);
        ((LineEntity)entities[1]).Start.Should().Be(originalEndpoint, "validation cannot repair or mutate source geometry");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealRuntimeUpdatesOffsetOfFourJoinedLines(bool reversed)
    {
        DrawingEntity[] lines =
        [
            new LineEntity(EntityId.Create("a"), new(-4, -2), new(-4, 8)),
            new LineEntity(EntityId.Create("b"), new(-4, 8), new(18, 8)),
            new LineEntity(EntityId.Create("c"), new(18, 8), new(18, -2)),
            new LineEntity(EntityId.Create("d"), new(18, -2), new(-4, -2))
        ];
        if (reversed) Array.Reverse(lines);
        var baseline = new DrawingDocument(lines);
        SelectionOffsetService.TryOffset(baseline, lines.Select(e => e.Id.Value), new(-5, 3),
            _ => EntityId.Create("dependent"), out var original, out _, out _, out _, out _,
            requestedDistance: 1, retainIntent: true).Should().BeTrue();
        var gap = original.Dimensions.Single();
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(
            new(original, [], [new(gap.Id, gap.Kind, gap.ReferenceKeys, 2, gap.Anchor, gap.IsDriving)]));

        observed.Response!.Status.Should().Be("solved");
        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        foreach (var line in lines.Cast<LineEntity>())
        {
            var current = result.Document.Entities.OfType<LineEntity>().Single(e => e.Id == line.Id);
            current.Start.X.Should().BeApproximately(line.Start.X, 1e-7);
            current.Start.Y.Should().BeApproximately(line.Start.Y, 1e-7);
            current.End.X.Should().BeApproximately(line.End.X, 1e-7);
            current.End.Y.Should().BeApproximately(line.End.Y, 1e-7);
        }
        var outline = result.Document.Entities.OfType<PolylineEntity>().Single();
        outline.Vertices.Should().HaveCount(5);
        outline.Vertices[0].Should().Be(outline.Vertices[^1]);
        var bounds = outline.GetBounds();
        bounds.MinX.Should().BeApproximately(-6, 1e-7);
        bounds.MinY.Should().BeApproximately(-4, 1e-7);
        bounds.MaxX.Should().BeApproximately(20, 1e-7);
        bounds.MaxY.Should().BeApproximately(10, 1e-7);
        SketchOffsetRelation.IsSatisfied(result.Document.Entities,
            original.Constraints.Single().ReferenceKeys).Should().BeTrue();
    }

    [Theory]
    [InlineData(false, 0.5)]
    [InlineData(false, 2)]
    [InlineData(true, 0.5)]
    [InlineData(true, 2)]
    public void RealRuntimeUpdatesGapWithStableIdentityMetadataAndProtectedGeometry(bool rectangle, double gap)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var original = Document(rectangle);
        var update = Gap(gap);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(new(original, [], [update]));

        result.Status.Should().Be(SketchSolveStatus.Solved, string.Join(" ", result.Diagnostics));
        observed.Response!.Engine.Should().Be("PlaneGCS");
        observed.Response.Status.Should().Be("solved");
        observed.Request!.AttemptedDrivingDimensionIds.Should().Equal("gap");
        observed.Request.FixedReferenceKeys.Should().Contain(["source", "protected"]);
        observed.Request.Document.Entities.Should().HaveCount(3, "hidden protected geometry must still be sent");
        AssertIdentityAndMetadata(original, result.Document);
        result.Document.Entities[0].Should().BeSameAs(original.Entities[0], "the whole source is fixed");
        result.Document.Entities[2].Should().BeSameAs(original.Entities[2], "unrelated hidden/locked geometry is protected");
        result.Document.Dimensions.Single().Value.Should().Be(gap);
        result.Document.Constraints.Should().OnlyContain(c =>
            SketchConstraintService.ValidateConstraint(result.Document, c).State == SketchConstraintState.Satisfied);
        SketchDimensionSolverService.TryGetDimensionMeasurement(result.Document,
            result.Document.Dimensions.Single(), out var measured).Should().BeTrue();
        measured.Should().BeApproximately(gap, 1e-6);

        if (rectangle)
        {
            var dependent = result.Document.Entities[1].Should().BeOfType<PolylineEntity>().Which;
            var expected = Rectangle("dependent", gap);
            dependent.Vertices.Should().HaveCount(expected.Vertices.Count);
            for (var index = 0; index < expected.Vertices.Count; index++)
            {
                dependent.Vertices[index].X.Should().BeApproximately(expected.Vertices[index].X, 1e-6);
                dependent.Vertices[index].Y.Should().BeApproximately(expected.Vertices[index].Y, 1e-6);
            }
            dependent.Vertices[0].Should().Be(dependent.Vertices[^1]);
            ((PolylineEntity)original.Entities[1]).Vertices.Should().Equal(Rectangle("dependent", 1).Vertices);
        }
        else
        {
            var dependent = result.Document.Entities[1].Should().BeOfType<CircleEntity>().Which;
            dependent.Center.X.Should().BeApproximately(0, 1e-6);
            dependent.Center.Y.Should().BeApproximately(0, 1e-6);
            dependent.Radius.Should().BeApproximately(2 + gap, 1e-6);
            ((CircleEntity)original.Entities[1]).Radius.Should().Be(3);
        }
        original.Dimensions.Single().Value.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RealRuntimeRefusesGapEditWhenBothMembersFixedWithoutChangingGeometry(bool rectangle)
    {
        using var runtime = CreateRuntime();
        var observed = new ObservedRuntime(runtime);
        var original = Document(rectangle, fixDependent: true);

        var result = new PlaneGcsSketchSolverAdapter(observed).Solve(new(original, [], [Gap(2)]));

        result.Status.Should().Be(SketchSolveStatus.Failed, string.Join(" ", result.Diagnostics));
        observed.Response!.Engine.Should().Be("PlaneGCS");
        observed.Response.Status.Should().Be("failed", "the actual engine must refuse the incompatible fixed gap");
        AssertIdentityAndMetadata(original, result.Document);
        for (var index = 0; index < original.Entities.Count; index++)
            result.Document.Entities[index].Should().BeSameAs(original.Entities[index]);
        result.Document.Dimensions.Single().Value.Should().Be(2, "failed editable intent remains visible");
        result.AffectedDiagnostics.Should().Contain(d => d.ItemId == "gap" && d.ItemKind == "dimension");
        original.Dimensions.Single().Value.Should().Be(1);
    }

    private static void AssertIdentityAndMetadata(DrawingDocument original, DrawingDocument result)
    {
        result.Metadata.Should().BeSameAs(original.Metadata);
        result.Entities.Select(e => e.Id).Should().Equal(original.Entities.Select(e => e.Id));
        result.Entities.Select(e => e.IsConstruction).Should().Equal(original.Entities.Select(e => e.IsConstruction));
        result.Constraints.Select(c => c.Id).Should().Equal(original.Constraints.Select(c => c.Id));
        for (var index = 0; index < original.Constraints.Count; index++)
        {
            result.Constraints[index].Kind.Should().Be(original.Constraints[index].Kind);
            result.Constraints[index].ReferenceKeys.Should().Equal(original.Constraints[index].ReferenceKeys);
        }
        var dimension = result.Dimensions.Should().ContainSingle().Which;
        dimension.Id.Should().Be("gap");
        dimension.Kind.Should().Be(SketchDimensionKind.Offset);
        dimension.ReferenceKeys.Should().Equal("source", "dependent");
        dimension.Anchor.Should().Be(original.Dimensions.Single().Anchor);
        dimension.IsDriving.Should().BeTrue();
    }

    private static DrawingDocument Document(bool rectangle, bool fixDependent = false)
    {
        DrawingEntity source = rectangle ? Rectangle("source", 0) : new CircleEntity(EntityId.Create("source"), new(0, 0), 2);
        DrawingEntity dependent = rectangle ? Rectangle("dependent", 1) : new CircleEntity(EntityId.Create("dependent"), new(0, 0), 3);
        var constraints = new List<SketchConstraint>
        {
            new("offset", SketchConstraintKind.Offset, ["source", "dependent"], SketchConstraintState.Satisfied),
            new("fixed-source", SketchConstraintKind.Fix, ["source"], SketchConstraintState.Satisfied)
        };
        if (fixDependent)
            constraints.Add(new("fixed-dependent", SketchConstraintKind.Fix, ["dependent"], SketchConstraintState.Satisfied));
        var metadata = new DrawingDocumentMetadata
        {
            SourceFileName = "offset-engine-fixture.dxf",
            SourceSha256 = "retained-source-hash",
            Units = DrawingUnits.Inches,
            TrustedSource = true,
            EntityStyles = new Dictionary<string, DxfEntityStyle>
            {
                ["source"] = new("0", ColorNumber: 2),
                ["dependent"] = new("0", ColorNumber: 3),
                ["protected"] = new("protected", ColorNumber: 4)
            },
            Layers = new Dictionary<string, DxfEntityStyle>
            {
                ["0"] = new("0"),
                ["protected"] = new("protected", IsHidden: true, IsLocked: true)
            }
        };
        return new([source, dependent,
            new LineEntity(EntityId.Create("protected"), new(20, 20), new(24, 20), true)],
            [Gap(1)], constraints, metadata);
    }

    private static SketchDimension Gap(double value) =>
        new("gap", SketchDimensionKind.Offset, ["source", "dependent"], value, new(9, -3), true);

    private static PolylineEntity Rectangle(string id, double gap) =>
        new(EntityId.Create(id), [
            new(-gap, -gap), new(8 + gap, -gap), new(8 + gap, 6 + gap),
            new(-gap, 6 + gap), new(-gap, -gap)
        ]);

    private static PlaneGcsNodeRuntime CreateRuntime()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src/DXFER.Web/Solver/planeGcsHost.mjs")))
            root = root.Parent;
        if (root is null) throw new InvalidOperationException("The repository's real PlaneGCS assets are required.");
        var executable = OperatingSystem.IsWindows() ? "node.exe" : "node";
        var node = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
            .Where(directory => !string.IsNullOrWhiteSpace(directory))
            .Select(directory => Path.GetFullPath(Path.Combine(directory, executable))).FirstOrDefault(File.Exists);
        if (node is null) throw new InvalidOperationException("Node is required; this test cannot use a mock or legacy solver.");
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

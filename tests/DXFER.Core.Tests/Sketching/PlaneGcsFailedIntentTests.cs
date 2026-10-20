using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsFailedIntentTests
{
    [Theory]
    [InlineData("failed", SketchSolveStatus.Failed)]
    [InlineData("unsupported", SketchSolveStatus.Failed)]
    [InlineData("throw", SketchSolveStatus.Unavailable)]
    [InlineData("missing", SketchSolveStatus.Unavailable)]
    public void FailedConstraintCreationRetainsRedIntentAndOriginalGeometry(string mode, SketchSolveStatus expected)
    {
        var original = Document();
        var attempted = new SketchConstraint("vertical", SketchConstraintKind.Vertical, ["edge"]);
        var result = Adapter(mode).Solve(new(original, [attempted], []));

        result.Status.Should().Be(expected);
        result.Document.Entities[0].Should().BeSameAs(original.Entities[0]);
        result.Document.Metadata.Should().BeSameAs(original.Metadata);
        result.Document.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Unsatisfied);
        result.Document.Constraints[0].Id.Should().Be("vertical");
        result.AffectedDiagnostics.Should().Contain(d => d.ItemId == "vertical" && d.ItemKind == "constraint");
        original.Constraints.Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedDimensionAdditionOrEditRetainsValueWithoutDuplicatingIdentity(bool edit)
    {
        var size = new SketchDimension("size", SketchDimensionKind.LinearDistance, ["edge:start", "edge:end"], 10, new(5, 2), true);
        var original = Document(edit ? [size] : []);
        var attempted = new SketchDimension("size", size.Kind, size.ReferenceKeys, 20, size.Anchor, true);
        var result = Adapter("failed").Solve(new(original, [], [attempted]));

        result.Status.Should().Be(SketchSolveStatus.Failed);
        result.Document.Entities[0].Should().BeSameAs(original.Entities[0]);
        result.Document.Dimensions.Should().ContainSingle().Which.Should().BeSameAs(attempted);
        result.Document.Metadata.Should().BeSameAs(original.Metadata);
        result.AffectedDiagnostics.Should().Contain(d => d.ItemId == "size" && d.ItemKind == "dimension");
        SketchDimensionSolverService.TryGetDimensionMeasurement(result.Document, attempted, out var measured).Should().BeTrue();
        measured.Should().Be(10);
        if (edit) original.Dimensions.Single().Value.Should().Be(10);
    }

    [Fact]
    public void FailedConstraintEditPreservesUnrelatedStatesAndSuppression()
    {
        var horizontal = new SketchConstraint("axis", SketchConstraintKind.Horizontal, ["edge"], SketchConstraintState.Satisfied);
        var suppressed = new SketchConstraint("suppressed", SketchConstraintKind.Vertical, ["edge"], SketchConstraintState.Suppressed);
        var original = new DrawingDocument(Document().Entities, [], [horizontal, suppressed]);
        var update = new SketchConstraint("axis", SketchConstraintKind.Vertical, ["edge"]);
        var result = Adapter("failed").Solve(new(original, [update], []));

        result.Document.Constraints.Should().HaveCount(2);
        result.Document.Constraints[0].Kind.Should().Be(SketchConstraintKind.Vertical);
        result.Document.Constraints[0].State.Should().Be(SketchConstraintState.Unsatisfied);
        result.Document.Constraints[1].Should().BeSameAs(suppressed);
        original.Constraints[0].Should().BeSameAs(horizontal);
    }

    [Fact]
    public void RetryWithoutNewIntentAndFailedDragReturnExactBaseline()
    {
        var original = Document([new("size", SketchDimensionKind.LinearDistance, ["edge"], 10, isDriving: true)]);
        var adapter = Adapter("failed");
        adapter.Solve(SketchSolveRequest.FromDocument(original)).Document.Should().BeSameAs(original);
        adapter.TryApplyDrag(original, "edge:end", new(10, 0), new(10, 3), false,
            out var afterDrag, out _, automaticSnapConstraints: true).Should().BeFalse();
        afterDrag.Should().BeSameAs(original);

        var withIntent = adapter.Solve(new(original, [new("axis", SketchConstraintKind.Vertical, ["edge"])], []),
            new PlaneGcsDragRequest("edge:end", new(10, 0), new(10, 3), false));
        withIntent.Document.Should().BeSameAs(original);
    }

    [Fact]
    public void InvalidNumericIntentIsNotPersisted()
    {
        var original = Document();
        var result = Adapter("failed").Solve(new(original, [],
            [new("invalid", SketchDimensionKind.LinearDistance, ["edge"], double.NaN, isDriving: true)]));
        result.Document.Should().BeSameAs(original);
    }

    [Fact]
    public void DuplicateIntentIdsAreNotPersisted()
    {
        var original = Document();
        var result = Adapter("failed").Solve(new(original, [
            new("axis", SketchConstraintKind.Horizontal, ["edge"]),
            new("axis", SketchConstraintKind.Vertical, ["edge"])
        ], []));
        result.Document.Should().BeSameAs(original);
    }

    [Fact]
    public void ReadOnlyAndLockedIntentEditsRemainUnchanged()
    {
        var attempted = new SketchConstraint("axis", SketchConstraintKind.Vertical, ["edge"]);
        var readOnly = new DrawingDocument(Document().Entities, [], [],
            new DrawingDocumentMetadata { Mode = DrawingDocumentMode.ReferenceOnly });
        Adapter("failed").Solve(new(readOnly, [attempted], [])).Document.Should().BeSameAs(readOnly);

        var dimension = new SketchDimension("size", SketchDimensionKind.LinearDistance,
            ["edge:start", "edge:end"], 10, isDriving: true);
        var locked = new DrawingDocument(Document().Entities, [dimension], [], new DrawingDocumentMetadata
        {
            Layers = new Dictionary<string, DxfEntityStyle> { ["0"] = new("0", IsLocked: true) }
        });
        var edit = new SketchDimension("size", dimension.Kind, dimension.ReferenceKeys, 20, isDriving: true);
        Adapter("failed").Solve(new(locked, [], [edit])).Document.Should().BeSameAs(locked);
    }

    private static DrawingDocument Document(SketchDimension[]? dimensions = null) =>
        new([new LineEntity(EntityId.Create("edge"), new(0, 0), new(10, 0))], dimensions ?? [], []);

    private static PlaneGcsSketchSolverAdapter Adapter(string mode) =>
        mode == "missing" ? new() : new(new RefusingRuntime(mode));

    private sealed class RefusingRuntime(string mode) : IPlaneGcsRuntime
    {
        public PlaneGcsResponse Solve(PlaneGcsRequest request) => mode == "throw"
            ? throw new TimeoutException("Test runtime unavailable.")
            : new(mode, null, ["Test refusal."], "PlaneGCS");
    }
}

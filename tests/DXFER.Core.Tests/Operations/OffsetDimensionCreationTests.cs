using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class OffsetDimensionCreationTests
{
    private static DrawingDocument Create(bool keyed, bool circle = false)
    {
        DrawingEntity source = circle
            ? new CircleEntity(EntityId.Create("source"), new(0, 0), 3)
            : new LineEntity(EntityId.Create("source"), new(0, 0), new(10, 0));
        SelectionOffsetService.TryOffset(new DrawingDocument([source]), ["source"],
            circle ? new(4, 0) : new(5, 1), _ => EntityId.Create("copy"),
            out var document, out _, out _, out _, out _, keyed ? 1 : null, retainIntent: true)
            .Should().BeTrue();
        return document;
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DimensioningLinkedGapUsesOffsetIntentAndCanEditAfterReopen(bool keyed, bool reversed)
    {
        var document = Create(keyed);
        string[] keys = reversed ? ["copy", "source"] : ["source", "copy"];
        SketchCommandFactory.TryBuildDimension(document, keys, "new-gap", out var dimension, out _,
            anchorOverride: new(5, 0.5)).Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.Offset);
        dimension.ReferenceKeys.Should().Equal("source", "copy");
        dimension.Id.Should().Be(keyed ? document.Dimensions.Single().Id : "new-gap");
        var placed = new LegacySketchSolverAdapter().Solve(new(document, [], [dimension]));
        placed.Status.Should().Be(SketchSolveStatus.Solved);
        placed.Document.Dimensions.Should().ContainSingle();
        placed.Document.Entities.Should().Equal(document.Entities);
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(placed.Document, new(IncludeNativeDocument: true)));
        var edited = new LegacySketchSolverAdapter().Solve(new(reopened, [],
            [new(dimension.Id, dimension.Kind, dimension.ReferenceKeys, 2, dimension.Anchor, true)]));
        edited.Status.Should().Be(SketchSolveStatus.Solved);
        edited.Document.Entities.OfType<LineEntity>().Last().Start.Y.Should().Be(2);
        edited.Document.Entities.First().Should().Be(document.Entities.First());
        edited.Document.Dimensions.Should().ContainSingle();
        edited.Document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData("copy:start")]
    [InlineData("copy|point|midpoint|5|1")]
    public void PointToSourceGapAlsoUsesOffsetIntent(string pointKey)
    {
        var document = Create(false);
        SketchCommandFactory.TryBuildDimension(document, ["source", pointKey], "gap", out var dimension, out _)
            .Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.Offset);
    }

    [Fact]
    public void RoundPairUsesOffsetButSingleCircleKeepsDiameter()
    {
        var document = Create(false, circle: true);
        SketchCommandFactory.TryBuildDimension(document, ["source", "copy"], "gap", out var dimension, out _)
            .Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.Offset);
        SketchCommandFactory.TryBuildDimension(document, ["copy"], "diam", out dimension, out _).Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.Diameter);
    }

    [Fact]
    public void ExistingFailedGapKeepsItsRequestedValueWhenPlacedAgain()
    {
        var document = Create(true);
        var original = document.Dimensions.Single();
        document = new(document.Entities,
            [new(original.Id, original.Kind, original.ReferenceKeys, 0, original.Anchor, true)], document.Constraints);
        SketchCommandFactory.TryBuildDimension(document, ["source", "copy"], "new", out var dimension, out _)
            .Should().BeTrue();
        dimension.Id.Should().Be(original.Id);
        dimension.Value.Should().Be(0, "placing a label must not silently repair failed driving intent");
    }

    [Fact]
    public void PostCreationGapDoesNotOverrideFixedDependent()
    {
        var document = Create(false);
        document = new(document.Entities, document.Dimensions, document.Constraints.Append(
            new SketchConstraint("fixed", SketchConstraintKind.Fix, ["copy"], SketchConstraintState.Satisfied)));
        SketchCommandFactory.TryBuildDimension(document, ["source", "copy"], "gap", out var dimension, out _)
            .Should().BeTrue();
        var result = new LegacySketchSolverAdapter().Solve(new(document, [],
            [new(dimension.Id, dimension.Kind, dimension.ReferenceKeys, 2, dimension.Anchor, true)]));
        result.Status.Should().NotBe(SketchSolveStatus.Solved);
        result.Document.Entities.Should().Equal(document.Entities);
        result.Document.Dimensions.Should().ContainSingle().Which.Value.Should().Be(2);
    }

    [Fact]
    public void UnlinkedParallelLinesStillGetOrdinaryDistance()
    {
        var linked = Create(false);
        var document = new DrawingDocument(linked.Entities);
        SketchCommandFactory.TryBuildDimension(document, ["source", "copy"], "gap", out var dimension, out _)
            .Should().BeTrue();
        dimension.Kind.Should().Be(SketchDimensionKind.PointToLineDistance);
    }
}

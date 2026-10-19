using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using DXFER.CadIO;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public class DimensionModeTests
{
    private static DrawingDocument Line(bool driving = true) => new(
        [new LineEntity(EntityId.Create("line"), new(0, 0), new(10, 0))],
        [new("length", SketchDimensionKind.LinearDistance, ["line:start", "line:end"], 10, new(5, 2), driving)], []);

    [Fact]
    public void ModeChangesPreserveGeometryIdentityAnchorAndNativeIntent()
    {
        var original = Line();
        SketchDimensionModeService.TrySetDriving(original, "length", false, out var driven, out _).Should().BeTrue();
        driven.Entities.Should().Equal(original.Entities);
        driven.Dimensions.Single().IsDriving.Should().BeFalse();
        driven.Dimensions.Single().Anchor.Should().Be(original.Dimensions.Single().Anchor);
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(driven, new(IncludeNativeDocument: true)));
        reopened.Dimensions.Single().IsDriving.Should().BeFalse();
        SketchDimensionModeService.TrySetDriving(reopened, "length", true, out var restored, out _).Should().BeTrue();
        restored.Dimensions.Single().IsDriving.Should().BeTrue();
        restored.Entities.Should().BeEquivalentTo(original.Entities);
    }

    [Fact]
    public void DrivenMeasurementFollowsGeometryAndPromotionUsesCurrentMeasurement()
    {
        var original = Line(false);
        var changed = new DrawingDocument([new LineEntity(EntityId.Create("line"), new(0, 0), new(14, 0))], original.Dimensions, []);
        SketchDimensionSolverService.IsDimensionSatisfied(changed, changed.Dimensions.Single()).Should().BeTrue();
        var refreshed = SketchDimensionModeService.RefreshMeasurements(changed);
        refreshed.Dimensions.Single().Value.Should().Be(14);
        SketchDimensionModeService.TrySetDriving(changed, "length", true, out var result, out _).Should().BeTrue();
        result.Entities.Should().Equal(changed.Entities);
        result.Dimensions.Single().Value.Should().Be(14);
    }

    [Fact]
    public void RedundantPromotionRefusesWithoutChangingModeOrGeometry()
    {
        var line = new LineEntity(EntityId.Create("line"), new(0, 0), new(3, 4));
        var document = new DrawingDocument([line], [
            new("x", SketchDimensionKind.HorizontalDistance, ["line:start", "line:end"], 3, null, true),
            new("y", SketchDimensionKind.VerticalDistance, ["line:start", "line:end"], 4, null, true),
            new("diagonal", SketchDimensionKind.LinearDistance, ["line:start", "line:end"], 5, null, false)], []);
        SketchDimensionModeService.TrySetDriving(document, "diagonal", true, out var result, out var reason).Should().BeFalse();
        result.Should().BeSameAs(document);
        reason.Should().NotBeEmpty();
    }

    [Fact]
    public void MakingFailedDrivingIntentDrivenUsesActualGeometry()
    {
        var original = Line();
        var failed = new DrawingDocument(original.Entities,
            [new("length", SketchDimensionKind.LinearDistance, ["line:start", "line:end"], 12, new(5,2), true)], []);
        SketchDimensionModeService.TrySetDriving(failed,"length",false,out var result,out _).Should().BeTrue();
        result.Dimensions.Single().Value.Should().Be(10);
        result.Entities.Should().Equal(original.Entities);
        SketchDimensionSolverService.IsDimensionSatisfied(result,result.Dimensions.Single()).Should().BeTrue();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProtectedDocumentsAndLayersCannotToggle(bool referenceOnly)
    {
        var original = Line();
        var metadata = original.Metadata with { Mode = referenceOnly ? DrawingDocumentMode.ReferenceOnly : DrawingDocumentMode.Editable };
        if (!referenceOnly) metadata = metadata with {
            Layers = new Dictionary<string,DxfEntityStyle> { ["0"] = new("0", IsLocked:true) }
        };
        var document = new DrawingDocument(original.Entities,original.Dimensions,[],metadata);
        SketchDimensionModeService.TrySetDriving(document,"length",false,out var result,out _).Should().BeFalse();
        result.Should().BeSameAs(document);
    }

    [Fact]
    public void DrivenDragDoesNotRetainTheOldDrivingLength()
    {
        var document=Line();
        SketchDimensionModeService.TrySetDriving(document,"length",false,out document,out _).Should().BeTrue();
        SketchGeometryDragService.TryApplyDrag(document,"line|point|end|10|0",new(10,0),new(12,0),false,out var changed,out _,false).Should().BeTrue();
        changed.Dimensions.Single().Value.Should().Be(12);
        changed.Dimensions.Single().IsDriving.Should().BeFalse();
    }
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using DXFER.CadIO;
using DXFER.Core.Sketching;

namespace DXFER.Core.Tests.Operations;

public class DrawingLayerTests
{
    private static DrawingDocument Fixture() => new(new DrawingEntity[] { new LineEntity(new("line"), new(0, 0), new(10, 0)) });

    [Fact]
    public void MoveRenameAndDeletePreserveGeometry()
    {
        var original = Fixture();
        var next = DrawingLayerService.SetLayer(original, new("CUT", TrueColor: 0x123456));
        next = DrawingLayerService.Move(next, new[] { "line" }, "CUT");
        Assert.Throws<ArgumentException>(() => DrawingLayerService.DeleteEmpty(next, "CUT"));
        next = DrawingLayerService.SetLayer(next, new("OUTLINE", TrueColor: 0x123456), "CUT");
        Assert.Equal("OUTLINE", DrawingLayerService.LayerName(next, "line"));
        Assert.Equal(original.Entities, next.Entities);
        next = DrawingLayerService.Move(next, new[] { "line" }, "0");
        next = DrawingLayerService.DeleteEmpty(next, "OUTLINE");
        Assert.Single(DrawingLayerService.GetLayers(next));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void HiddenOrLockedGeometryCannotMoveOrDisappear(bool hidden, bool locked)
    {
        var doc = DrawingLayerService.SetLayer(Fixture(), new("0", IsHidden: hidden, IsLocked: locked), "0");
        Assert.False(DrawingLayerService.CanEdit(doc, "line"));
        var changed = new DrawingDocument(Array.Empty<DrawingEntity>(), doc.Dimensions, doc.Constraints, doc.Metadata);
        Assert.False(DrawingLayerService.PreservesProtectedGeometry(doc, changed));
        Assert.True(DrawingLayerService.PreservesProtectedGeometry(doc, doc));
        Assert.Throws<ArgumentException>(() => DrawingLayerService.Move(doc, new[] { "line" }, "0"));
    }

    [Fact]
    public void DefaultAndDuplicateNamesAreProtected()
    {
        var doc = Fixture();
        Assert.Throws<ArgumentException>(() => DrawingLayerService.DeleteEmpty(doc, "0"));
        Assert.Throws<ArgumentException>(() => DrawingLayerService.SetLayer(doc, new("BAD/NAME")));
        doc = DrawingLayerService.SetLayer(doc, new("CUT"));
        Assert.Throws<ArgumentException>(() => DrawingLayerService.SetLayer(doc, new("cut")));
        Assert.Throws<ArgumentException>(() => DrawingLayerService.SetLayer(doc, new("OTHER"), "0"));
    }

    [Fact]
    public void PlainDxfRoundTripRetainsEmptyAndHiddenLockedTrueColorLayer()
    {
        var doc = DrawingLayerService.SetLayer(Fixture(), new("EMPTY", TrueColor: 0x123456, IsHidden: true, IsLocked: true));
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(doc, new() { IncludeNativeDocument = false }));
        var layer = DrawingLayerService.GetLayers(reopened)["EMPTY"];
        Assert.True(layer.IsHidden);
        Assert.True(layer.IsLocked);
        Assert.Equal(0x123456, layer.TrueColor);
    }

    [Fact]
    public void NewEntitiesUseCurrentLayerAndLockedCurrentRefusesCreation()
    {
        var before = DrawingLayerService.SetLayer(Fixture(), new("CUT"));
        before = DrawingLayerService.WithMetadata(before, before.Metadata with { ActiveLayerName = "CUT" });
        var after = new DrawingDocument(before.Entities.Append(new CircleEntity(new("circle"), new(4, 4), 1)), before.Dimensions, before.Constraints, before.Metadata);
        var assigned = DrawingLayerService.AssignNewEntities(before, after);
        Assert.Equal("CUT", DrawingLayerService.LayerName(assigned, "circle"));
        Assert.Equal("0", DrawingLayerService.LayerName(assigned, "line"));
        var locked = DrawingLayerService.SetLayer(after, new("CUT", IsLocked: true), "CUT");
        Assert.Throws<ArgumentException>(() => DrawingLayerService.AssignNewEntities(before, locked));
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(assigned, new() { IncludeNativeDocument = false }));
        Assert.Equal("CUT", reopened.Metadata.ActiveLayerName);
        Assert.Contains(reopened.Metadata.EntityStyles.Values, s => s.LayerName == "CUT");
    }

    [Fact]
    public void LockedDimensionsAndConstraintsCannotBeEditedOrDeleted()
    {
        var doc = new DrawingDocument(Fixture().Entities,
            new[] { new SketchDimension("d", SketchDimensionKind.LinearDistance, new[] { "line|start", "line|end" }, 10, isDriving: true) },
            new[] { new SketchConstraint("c", SketchConstraintKind.Horizontal, new[] { "line" }) });
        doc = DrawingLayerService.SetLayer(doc, new("0", IsLocked: true), "0");
        Assert.False(DrawingLayerService.PreservesProtectedGeometry(doc, new(doc.Entities, Array.Empty<SketchDimension>(), doc.Constraints, doc.Metadata)));
        Assert.False(DrawingLayerService.PreservesProtectedGeometry(doc, new(doc.Entities, doc.Dimensions, Array.Empty<SketchConstraint>(), doc.Metadata)));
        var moved = new DrawingDocument(new[] { new LineEntity(new("line"), new(0, 1), new(10, 1)) }, doc.Dimensions, doc.Constraints, doc.Metadata);
        Assert.False(DrawingLayerService.PreservesProtectedGeometry(doc, moved));
        var unlocked = DrawingLayerService.SetLayer(doc, new("0", IsLocked: false), "0");
        Assert.True(DrawingLayerService.PreservesProtectedGeometry(doc, unlocked));
    }

    [Fact]
    public void RenameWithColorUsesByLayerAndNativeRoundTripKeepsState()
    {
        var doc = DrawingLayerService.SetLayer(Fixture(), new("CUT"));
        doc = DrawingLayerService.Move(doc, new[] { "line" }, "CUT");
        doc = DrawingLayerService.SetLayer(doc, new("EDGE", TrueColor: 0xff8800), "CUT");
        Assert.Equal(256, doc.Metadata.EntityStyles["line"].ColorNumber);
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(doc, new() { IncludeNativeDocument = true }));
        Assert.Equal("EDGE", DrawingLayerService.LayerName(reopened, "line"));
        Assert.Equal("#FF8800", DrawingLayerService.Color(DrawingLayerService.GetLayers(reopened)["EDGE"]));
    }
}

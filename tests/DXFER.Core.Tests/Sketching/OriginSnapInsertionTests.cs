using DXFER.Blazor.Sketching;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class OriginSnapInsertionTests
{
    [Theory]
    [InlineData("line")]
    [InlineData("centercircle")]
    [InlineData("point")]
    public void ExplicitOriginSnapPersistsWithoutPinningUnsnappedCreation(string tool)
    {
        DrawingEntity entity = tool switch {
            "line" => new LineEntity(EntityId.Create("created"), new(0, 0), new(5, 3)),
            "centercircle" => new CircleEntity(EntityId.Create("created"), new(0, 0), 3),
            _ => new PointEntity(EntityId.Create("created"), new(0, 0)) };
        var created = new[] { entity };
        var count = 0;
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion(tool, Array.Empty<DrawingEntity>(), created,
            _ => $"c{count++}", originSnapUsed: true);
        constraints.Should().ContainSingle(c => c.Kind == SketchConstraintKind.Coincident && c.ReferenceKeys.Contains("$origin:center"));
        var document = SketchConstraintService.ApplyConstraints(new DrawingDocument(created), constraints);
        document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true)));
        reopened.Constraints.Should().BeEquivalentTo(document.Constraints);
        SketchCreationConstraintFactory.CreateConstraintsForInsertion(tool, Array.Empty<DrawingEntity>(), created,
            _ => $"c{count++}").Should().NotContain(c => c.ReferenceKeys.Contains("$origin:center"));
    }

    [Fact]
    public void DragToOriginStoresCoincidentAndRejectsSubsequentAnchorMovement()
    {
        var document = new DrawingDocument(new[] { new LineEntity(EntityId.Create("line"), new(2, 1), new(5, 3)) });
        SketchGeometryDragService.TryApplyDrag(document, "line|point|start|2|1", new(2, 1), new(0, 0), false,
            out var snapped, out var status).Should().BeTrue(status);
        snapped.Constraints.Should().ContainSingle(c => c.Kind == SketchConstraintKind.Coincident && c.ReferenceKeys.Contains("$origin:center"));
        SketchGeometryDragService.TryApplyDrag(snapped, "line|point|start|0|0", new(0, 0), new(1, 2), false,
            out var unchanged, out _).Should().BeFalse();
        unchanged.Entities.Should().Equal(snapped.Entities);
    }
}

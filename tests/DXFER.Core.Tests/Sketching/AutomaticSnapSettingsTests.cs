using DXFER.Blazor.Sketching;
using DXFER.Blazor.Interop;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class AutomaticSnapSettingsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TangentAndPerpendicularCaptureRespectSetting(bool enabled)
    {
        var line = new LineEntity(EntityId.Create("source"), new(0, 0), new(10, 0));
        var next = new LineEntity(EntityId.Create("next"), new(5, 0), new(5, 3));
        var circle = new CircleEntity(EntityId.Create("circle"), new(5, 2), 2);
        var id = 0;
        var perpendicular = SketchCreationConstraintFactory.CreateConstraintsForInsertion("line", [line], [next],
            _ => $"c{id++}", automaticSnapConstraints: enabled);
        perpendicular.Any(c => c.Kind == SketchConstraintKind.Perpendicular).Should().Be(enabled);
        perpendicular.Any(c => c.Kind == SketchConstraintKind.Midpoint).Should().Be(enabled);
        var tangent = SketchCreationConstraintFactory.CreateConstraintsForInsertion("centercircle", [line], [circle],
            _ => $"c{id++}", tangentReferenceKey: "source", automaticSnapConstraints: enabled);
        tangent.Any(c => c.Kind == SketchConstraintKind.Tangent).Should().Be(enabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TypedProjectionIntentRespectsSetting(bool enabled)
    {
        var source = new PointEntity(EntityId.Create("source"), new(2, 3));
        var point = new PointEntity(EntityId.Create("point"), new(2, 7));
        var id = 0;
        var result = SketchCreationConstraintFactory.CreateConstraintsForInsertion("point", [source], [point],
            _ => $"c{id++}", creationSnaps: [new CanvasCreationSnapDto(new(2, 7), "Vertical", ["source"])],
            automaticSnapConstraints: enabled);
        result.Any(c => c.Kind == SketchConstraintKind.Vertical).Should().Be(enabled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CreationSettingControlsOriginAndAxisInference(bool enabled)
    {
        var line = new LineEntity(EntityId.Create("line"), new(0, 0), new(5, 0));
        var id = 0;
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion("line", [], [line],
            _ => $"c{id++}", originSnapUsed: true, automaticSnapConstraints: enabled);
        if (enabled)
        {
            constraints.Should().Contain(c => c.Kind == SketchConstraintKind.Coincident);
            constraints.Should().Contain(c => c.Kind == SketchConstraintKind.Horizontal);
        }
        else constraints.Should().BeEmpty();
    }

    [Fact]
    public void DisabledInferencePreservesRectangleTopology()
    {
        DrawingEntity[] lines = [
            new LineEntity(EntityId.Create("a"), new(0, 0), new(5, 0)),
            new LineEntity(EntityId.Create("b"), new(5, 0), new(5, 3)),
            new LineEntity(EntityId.Create("c"), new(5, 3), new(0, 3)),
            new LineEntity(EntityId.Create("d"), new(0, 3), new(0, 0))];
        var id = 0;
        var intrinsic = SketchCreationConstraintFactory.CreateConstraintsForTool("twopointrectangle", lines, _ => $"c{id++}");
        id = 0;
        SketchCreationConstraintFactory.CreateConstraintsForInsertion("twopointrectangle", [], lines,
            _ => $"c{id++}", originSnapUsed: true, automaticSnapConstraints: false).Should().BeEquivalentTo(intrinsic);
    }

    [Fact]
    public void DisabledDragCaptureKeepsSnappedCoordinatesAndExistingConstraints()
    {
        var document = new DrawingDocument([new LineEntity(EntityId.Create("line"), new(2, 1), new(5, 3))]);
        SketchGeometryDragService.TryApplyDrag(document, "line|point|start|2|1", new(2, 1), new(0, 0), false,
            out var free, out var status, automaticSnapConstraints: false).Should().BeTrue(status);
        free.Constraints.Should().BeEmpty();
        ((LineEntity)free.Entities[0]).Start.Should().Be(new Point2(0, 0));
        SketchGeometryDragService.TryApplyDrag(document, "line|point|start|2|1", new(2, 1), new(0, 0), false,
            out var anchored, out status).Should().BeTrue(status);
        anchored.Constraints.Should().NotBeEmpty();
        SketchGeometryDragService.TryApplyDrag(anchored, "line|point|start|0|0", new(0, 0), new(1, 2), false,
            out var unchanged, out _, automaticSnapConstraints: false).Should().BeFalse();
        unchanged.Should().BeSameAs(anchored);
    }
}

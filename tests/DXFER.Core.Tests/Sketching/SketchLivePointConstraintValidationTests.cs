using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchLivePointConstraintValidationTests
{
    [Theory]
    [InlineData(SketchConstraintKind.Coincident, "line|point|unknown|99|99")]
    [InlineData(SketchConstraintKind.Horizontal, "line|point|unknown|99|99")]
    [InlineData(SketchConstraintKind.Vertical, "line|point|unknown|99|99")]
    [InlineData(SketchConstraintKind.Horizontal, "line|point|unknown|NaN|99")]
    [InlineData(SketchConstraintKind.Vertical, "line|point|unknown|99|NaN")]
    [InlineData(SketchConstraintKind.Coincident, "circle|point|unknown|99|99")]
    [InlineData(SketchConstraintKind.Coincident, "target|point|unknown|99|99")]
    public void UnknownIdentitiesCannotValidateFromAcquisitionCoordinates(SketchConstraintKind kind, string key)
    {
        var constraint = new SketchConstraint("relation", kind, [key, "target"]);
        var document = new DrawingDocument([
            new LineEntity(EntityId.Create("line"), new(0, 0), new(2, 0)),
            new CircleEntity(EntityId.Create("circle"), new(0, 0), 1),
            new PointEntity(EntityId.Create("target"), new(99, 99))], [], [constraint]);

        SketchConstraintService.ValidateConstraint(document, constraint).State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Theory]
    [InlineData(SketchConstraintKind.Coincident)]
    [InlineData(SketchConstraintKind.Horizontal)]
    [InlineData(SketchConstraintKind.Vertical)]
    public void RecognizedIdentityUsesLivePointNotAcquisitionCoordinates(SketchConstraintKind kind)
    {
        var constraint = new SketchConstraint("relation", kind, ["line|point|start|99|99", "target"]);
        var line = new LineEntity(EntityId.Create("line"), new(0, 0), new(2, 0));
        var point = new PointEntity(EntityId.Create("target"), new(0, 0));
        var document = new DrawingDocument([line, point], [], [constraint]);
        SketchConstraintService.ValidateConstraint(document, constraint).State.Should().Be(SketchConstraintState.Satisfied);

        var moved = new DrawingDocument([line with { Start = new(1, 1) }, point], [], [constraint]);
        SketchConstraintService.ValidateConstraint(moved, constraint).State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Theory]
    [InlineData(SketchConstraintKind.Horizontal, double.NaN, 0)]
    [InlineData(SketchConstraintKind.Vertical, 0, double.NaN)]
    public void AxisValidationRejectsNonfiniteUnusedCoordinate(SketchConstraintKind kind, double x, double y)
    {
        var constraint = new SketchConstraint("relation", kind, ["line:start", "target"]);
        var document = new DrawingDocument([
            new LineEntity(EntityId.Create("line"), new(x, y), new(2, 0)),
            new PointEntity(EntityId.Create("target"), new(0, 0))], [], [constraint]);
        SketchConstraintService.ValidateConstraint(document, constraint).State.Should().Be(SketchConstraintState.Unsatisfied);
    }
}

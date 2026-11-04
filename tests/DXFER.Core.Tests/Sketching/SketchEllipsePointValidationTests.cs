using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchEllipsePointValidationTests
{
    [Theory]
    [InlineData(SketchConstraintKind.Coincident, "major-end", 8, 1)]
    [InlineData(SketchConstraintKind.Coincident, "minor-end", 5, 2.5)]
    [InlineData(SketchConstraintKind.Horizontal, "major-end", 0, 1)]
    [InlineData(SketchConstraintKind.Vertical, "minor-end", 5, 0)]
    public void ValidationUsesLiveEllipseAxisRatherThanAcquisitionCoordinates(
        SketchConstraintKind kind, string label, double x, double y)
    {
        var ellipse = new EllipseEntity(EntityId.Create("ellipse"), new(5, 1), new(3, 0), .5);
        var point = new PointEntity(EntityId.Create("point"), new(x, y));
        var relation = new SketchConstraint("relation", kind, [$"ellipse|point|{label}|999|999", "point"]);
        var document = new DrawingDocument([ellipse, point], [], [relation]);
        SketchConstraintService.ValidateConstraint(document, relation).State.Should().Be(SketchConstraintState.Satisfied);

        var moved = new DrawingDocument([ellipse with { Center = new(7, 4) }, point], [], [relation]);
        SketchConstraintService.ValidateConstraint(moved, relation).State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Theory]
    [InlineData("ellipse|point|unknown|8|1")]
    [InlineData("ellipse|point|major-end|NaN|1")]
    [InlineData("missing|point|major-end|8|1")]
    public void InvalidEllipseAxisCannotValidateFromStaleCoordinates(string key)
    {
        var relation = new SketchConstraint("relation", SketchConstraintKind.Coincident, [key, "point"]);
        var document = new DrawingDocument([
            new EllipseEntity(EntityId.Create("ellipse"), new(5, 1), new(3, 0), .5),
            new PointEntity(EntityId.Create("point"), new(8, 1))], [], [relation]);
        SketchConstraintService.ValidateConstraint(document, relation).State.Should().Be(SketchConstraintState.Unsatisfied);
    }
}

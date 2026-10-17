using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class CoupledTangentCreationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RectangleCanBecomeTangentToAlreadyTangentCircle(bool reverse)
    {
        var rectangle = new DrawingEntity[] {
            new LineEntity(EntityId.Create("bottom"), new(-4, 7), new(-1, 7)),
            new LineEntity(EntityId.Create("right"), new(-1, 7), new(-1, 10)),
            new LineEntity(EntityId.Create("top"), new(-1, 10), new(-4, 10)),
            new LineEntity(EntityId.Create("left"), new(-4, 10), new(-4, 7)) };
        var sequence = 0;
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool("twopointrectangle", rectangle, k => $"{k}-{++sequence}")
            .Concat(new[] {
                new SketchConstraint("origin", SketchConstraintKind.Coincident, new[] { "$origin:center", "large:center" }),
                new SketchConstraint("circle-tangent", SketchConstraintKind.Tangent, new[] { "small", "large" }) }).ToArray();
        var document = SketchConstraintService.ApplyConstraints(new DrawingDocument(new DrawingEntity[] {
            new CircleEntity(EntityId.Create("large"), new(0, 0), 3),
            new CircleEntity(EntityId.Create("small"), new(-4, 3), 2) }.Concat(rectangle)), constraints);
        document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        var keys = reverse ? new[] { "small", "bottom" } : new[] { "bottom", "small" };
        SketchCommandFactory.TryBuildConstraint(document, keys, SketchConstraintKind.Tangent, "new-tangent", out var relation, out var status)
            .Should().BeTrue(status);
        var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, new[] { relation }, Array.Empty<SketchDimension>()));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        result.Document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        ((CircleEntity)result.Document.Entities[0]).Center.Should().Be(new Point2(0, 0));
    }
}

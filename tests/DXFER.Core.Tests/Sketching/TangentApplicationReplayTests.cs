using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class TangentApplicationReplayTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ApplyingTangentToDimensionedCircleCommitsSatisfiedGeometryAndRemainsSatisfiedAfterDrag(bool twoCircles, bool reverse)
    {
        DrawingEntity peer = twoCircles
            ? new CircleEntity(EntityId.Create("peer"), new Point2(0, 0), 5)
            : new LineEntity(EntityId.Create("peer"), new Point2(-20, 0), new Point2(20, 0));
        var circle = new CircleEntity(EntityId.Create("circle"), new Point2(10, 12), 3);
        var document = new DrawingDocument(new[] {peer, circle}, new[]
        {
            new SketchDimension("diameter", SketchDimensionKind.Diameter, new[] {"circle"}, 6, new Point2(16, 12), true)
        }, Array.Empty<SketchConstraint>());
        var keys = reverse ? new[] {"circle", "peer"} : new[] {"peer", "circle"};
        var constraint = new SketchConstraint("tangent", SketchConstraintKind.Tangent, keys);
        var result = new LegacySketchSolverAdapter().Solve(new SketchSolveRequest(document, new[] {constraint}, Array.Empty<SketchDimension>()));
        result.Status.Should().Be(SketchSolveStatus.Solved);
        var solved = result.Document;
        SketchConstraintService.ValidateConstraint(solved, solved.Constraints.Single()).State.Should().Be(SketchConstraintState.Satisfied);
        SketchDimensionSolverService.IsDimensionSatisfied(solved, solved.Dimensions.Single()).Should().BeTrue();
        var movedCircle = solved.Entities.OfType<CircleEntity>().Single(c => c.Id.Value == "circle");
        var key = FormattableString.Invariant($"circle|point|center|{movedCircle.Center.X:R}|{movedCircle.Center.Y:R}");
        SketchGeometryDragService.TryApplyDrag(solved, key, movedCircle.Center,
            new Point2(movedCircle.Center.X + 1, movedCircle.Center.Y), false, out var moved, out var status).Should().BeTrue(status);
        moved.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }
}

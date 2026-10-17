using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class CurveOriginAndPickTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ConcentricOriginMovesOnlyFreeCurveCenter(bool reversed, bool arc)
    {
        DrawingEntity curve = arc ? new ArcEntity(EntityId.Create("curve"), new(4, 5), 3, 0, 90)
            : new CircleEntity(EntityId.Create("curve"), new(4, 5), 3);
        var document = new DrawingDocument(new[] { curve });
        var keys = reversed ? new[] { "$origin:center", "curve" } : new[] { "curve", "$origin:center" };
        SketchCommandFactory.TryBuildConstraint(document, keys, SketchConstraintKind.Concentric,
            "origin", out var constraint, out var status).Should().BeTrue(status);
        constraint.Kind.Should().Be(SketchConstraintKind.Concentric);
        var solved = SketchConstraintService.ApplyConstraint(document, constraint);
        solved.Constraints.Single().State.Should().Be(SketchConstraintState.Satisfied);
        var center = arc ? ((ArcEntity)solved.Entities[0]).Center : ((CircleEntity)solved.Entities[0]).Center;
        center.Should().Be(new Point2(0, 0));
        var radius = arc ? ((ArcEntity)solved.Entities[0]).Radius : ((CircleEntity)solved.Entities[0]).Radius;
        radius.Should().Be(3);
        solved.Entities.Should().HaveCount(1);
        if (!arc)
        {
            SketchGeometryDragService.TryApplyDrag(solved, "curve|point|center|0|0", new(0, 0), new(2, 2), false,
                out var unchanged, out _).Should().BeFalse();
            unchanged.Entities.Should().Equal(solved.Entities);
            SketchGeometryDragService.TryApplyDrag(solved, "curve|point|quadrant-0|3|0", new(3, 0), new(4, 0), false,
                out var resized, out var resizeStatus).Should().BeTrue(resizeStatus);
            ((CircleEntity)resized.Entities[0]).Center.Should().Be(new Point2(0, 0));
            ((CircleEntity)resized.Entities[0]).Radius.Should().Be(4);
        }

        var fixedDocument = SketchConstraintService.ApplyConstraint(document,
            new("fix", SketchConstraintKind.Fix, new[] { "curve" }));
        var refused = SketchConstraintService.ApplyConstraint(fixedDocument, constraint);
        refused.Entities.Should().Equal(document.Entities);
        refused.Constraints.Single(c => c.Id == "origin").State.Should().Be(SketchConstraintState.Unsatisfied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TangentCircleQuadrantPicksMatchBodyPicks(bool reversed)
    {
        var document = new DrawingDocument(new DrawingEntity[] {
            new CircleEntity(EntityId.Create("a"), new(0, 0), 3),
            new CircleEntity(EntityId.Create("b"), new(10, 0), 3)
        });
        var keys = new[] { "a|point|quadrant-90|0|3", "b|point|quadrant-90|10|3" };
        SketchCommandFactory.TryBuildConstraint(document, reversed ? keys.Reverse() : keys,
            SketchConstraintKind.Tangent, "tangent", out var constraint, out var status).Should().BeTrue(status);
        constraint.ReferenceKeys.Should().OnlyContain(k => !k.Contains("|point|"));
        var solved = SketchConstraintService.ApplyConstraint(document, constraint);
        solved.Constraints.Single().State.Should().Be(SketchConstraintState.Satisfied);
        var a = (CircleEntity)solved.Entities[0]; var b = (CircleEntity)solved.Entities[1];
        a.Radius.Should().Be(3); b.Radius.Should().Be(3);
        Math.Sqrt(Math.Pow(a.Center.X - b.Center.X, 2) + Math.Pow(a.Center.Y - b.Center.Y, 2)).Should().BeApproximately(6, 1e-9);
    }
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class EllipseDragMatrixTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EllipseCenterWeldPropagatesOrRefusesFixedPeer(bool locked, bool reverse)
    {
        var constraints = new List<SketchConstraint> { new("weld", SketchConstraintKind.Coincident, new[] { "ellipse:center", "peer:start" }) };
        if (locked) constraints.Add(new("fix", SketchConstraintKind.Fix, new[] { "peer:start" }));
        if (reverse) constraints.Reverse();
        var original = new DrawingDocument(new DrawingEntity[] {
            new EllipseEntity(EntityId.Create("ellipse"), new(0, 0), new(4, 0), 0.5),
            new LineEntity(EntityId.Create("peer"), new(0, 0), new(-2, 0)) }, Array.Empty<SketchDimension>(), constraints);
        SketchGeometryDragService.TryApplyDrag(original, "ellipse|point|center|0|0", new(0, 0), new(1, 1), false,
            out var next, out var status).Should().Be(!locked, status);
        if (locked) next.Should().BeSameAs(original);
        else
        {
            ((EllipseEntity)next.Entities[0]).Center.Should().Be(new Point2(1, 1));
            ((LineEntity)next.Entities[1]).Start.Should().Be(new Point2(1, 1));
            next.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1e-9)]
    public void DegenerateMajorAxisRefusesThenNegativeSideRecovers(double offset)
    {
        var original = new DrawingDocument(new DrawingEntity[] {
            new EllipseEntity(EntityId.Create("ellipse"), new(0, 0), new(4, 0), 0.5) });
        SketchGeometryDragService.TryApplyDrag(original, "ellipse|point|quadrant-0|4|0", new(4, 0), new(offset, offset), false,
            out var refused, out _).Should().BeFalse();
        refused.Should().BeSameAs(original);
        SketchGeometryDragService.TryApplyDrag(original, "ellipse|point|quadrant-0|4|0", new(4, 0), new(-6, 0), false,
            out var next, out _).Should().BeTrue();
        var ellipse = (EllipseEntity)next.Entities[0];
        ellipse.MajorAxisEndPoint.Should().Be(new Point2(-6, 0));
        ellipse.MinorRadiusRatio.Should().BeApproximately(1.0 / 3, 1e-8);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void FreeAxisDragPreservesOrthogonalDrivingDiameterAndFixedCenter(bool major, bool bothDriving)
    {
        var axis = major ? "minor" : "major";
        var dimensions = new List<SketchDimension> { new("diameter", SketchDimensionKind.LinearDistance,
            new[] { $"ellipse|point|{axis}-start|0|0", $"ellipse|point|{axis}-end|0|0" }, major ? 4 : 8, isDriving: true) };
        if (bothDriving)
        {
            var other = major ? "major" : "minor";
            dimensions.Add(new("other", SketchDimensionKind.LinearDistance,
                new[] { $"ellipse|point|{other}-start|0|0", $"ellipse|point|{other}-end|0|0" }, major ? 8 : 4, isDriving: true));
        }
        var original = new DrawingDocument(new DrawingEntity[] {
            new EllipseEntity(EntityId.Create("ellipse"), new(0, 0), new(4, 0), 0.5) },
            dimensions,
            new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { "ellipse:center" }) });
        SketchGeometryDragService.TryApplyDrag(original, major ? "ellipse|point|quadrant-0|4|0" : "ellipse|point|quadrant-90|0|2",
            major ? new(4, 0) : new(0, 2), major ? new(6, 0) : new(0, 3), false, out var next, out var status).Should().Be(!bothDriving, status);
        if (bothDriving)
        {
            next.Should().BeSameAs(original);
            return;
        }
        var ellipse = (EllipseEntity)next.Entities[0];
        ellipse.Center.Should().Be(new Point2(0, 0));
        ellipse.MajorAxisEndPoint.Should().Be(new Point2(major ? 6 : 4, 0));
        (ellipse.MajorAxisEndPoint.X * ellipse.MinorRadiusRatio).Should().BeApproximately(major ? 2 : 3, 1e-8);
        next.Dimensions.Should().OnlyContain(d => SketchDimensionSolverService.IsDimensionSatisfied(next, d));
    }
}

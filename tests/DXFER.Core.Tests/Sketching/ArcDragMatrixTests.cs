using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class ArcDragMatrixTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1e-9)]
    public void ShiftArcEndpointAtCenterHasNoDirectionAndMustRefuse(double offset)
    {
        var original = new DrawingDocument(new DrawingEntity[] { new ArcEntity(EntityId.Create("arc"), new(0, 0), 3, 0, 90) });
        SketchGeometryDragService.TryApplyDrag(original, "arc|point|end|0|3", new(0, 3), new(offset, offset), true,
            out var next, out _).Should().BeFalse();
        next.Should().BeSameAs(original);
        SketchGeometryDragService.TryApplyDrag(original, "arc|point|end|0|3", new(0, 3), new(1e-4, 1e-4), true,
            out var recovered, out _).Should().BeTrue();
        ((ArcEntity)recovered.Entities[0]).EndAngleDegrees.Should().BeApproximately(45, 1e-8);
        ((ArcEntity)recovered.Entities[0]).Radius.Should().Be(3);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DrivingRadiusAndSweepKeepWeldedEndpointOrRefuseFixedPeer(bool locked, bool reverse)
    {
        var constraints = new List<SketchConstraint> { new("weld", SketchConstraintKind.Coincident, new[] { "arc:end", "peer:start" }) };
        if (locked) constraints.Add(new("fix", SketchConstraintKind.Fix, new[] { "peer:start" }));
        if (reverse) constraints.Reverse();
        var dimensions = new[] { new SketchDimension("radius", SketchDimensionKind.Radius, new[] { "arc" }, 3, isDriving: true),
            new SketchDimension("sweep", SketchDimensionKind.Angle, new[] { "arc" }, 90, isDriving: true) };
        var original = new DrawingDocument(new DrawingEntity[] { new ArcEntity(EntityId.Create("arc"), new(0, 0), 3, 0, 90),
            new LineEntity(EntityId.Create("peer"), new(0, 3), new(-2, 3)) }, reverse ? dimensions.Reverse() : dimensions, constraints);
        SketchGeometryDragService.TryApplyDrag(original, "arc|point|end|0|3", new(0, 3), new(1, 4), false,
            out var next, out var status).Should().Be(!locked, status);
        if (locked) next.Should().BeSameAs(original);
        else
        {
            var arc = (ArcEntity)next.Entities[0];
            arc.Center.Should().Be(new Point2(1, 1)); arc.Radius.Should().Be(3);
            arc.StartAngleDegrees.Should().Be(0); arc.EndAngleDegrees.Should().Be(90);
            ((LineEntity)next.Entities[1]).Start.X.Should().BeApproximately(1, 1e-8);
            ((LineEntity)next.Entities[1]).Start.Y.Should().BeApproximately(4, 1e-8);
            next.Dimensions.Should().OnlyContain(d => SketchDimensionSolverService.IsDimensionSatisfied(next, d));
        }
    }

    [Fact]
    public void FixedCenterDrivingRadiusAllowsShiftSweepButNotRadiusChange()
    {
        var original = new DrawingDocument(new DrawingEntity[] { new ArcEntity(EntityId.Create("arc"), new(0, 0), 3, 0, 90) },
            new[] { new SketchDimension("radius", SketchDimensionKind.Radius, new[] { "arc" }, 3, isDriving: true) },
            new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { "arc:center" }) });
        SketchGeometryDragService.TryApplyDrag(original, "arc|point|mid|2.12132|2.12132", new(Math.Sqrt(4.5), Math.Sqrt(4.5)), new(4, 4), false,
            out var refused, out _).Should().BeFalse();
        refused.Should().BeSameAs(original);
        SketchGeometryDragService.TryApplyDrag(original, "arc|point|end|0|3", new(0, 3), new(4, 4), true,
            out var next, out var status).Should().BeTrue(status);
        var arc = (ArcEntity)next.Entities[0]; arc.Center.Should().Be(new Point2(0, 0)); arc.Radius.Should().Be(3);
        arc.EndAngleDegrees.Should().BeApproximately(45, 1e-8);
    }
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class AnchoredTangentDragTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrivingSmallRadiusTranslatesAndResizesFreeLargePeer(bool inside)
    {
        var x = inside ? 7d : 13d;
        var document = new DrawingDocument(new DrawingEntity[] {
            new CircleEntity(EntityId.Create("large"), new(0, 0), 10),
            new CircleEntity(EntityId.Create("small"), new(x, 0), 3) }, new[] {
            new SketchDimension("small-radius", SketchDimensionKind.Radius, new[] { "small" }, 3, new(x, -4), true) }, new[] {
            new SketchConstraint("anchor", SketchConstraintKind.Concentric, new[] { "large", "$origin:center" }, SketchConstraintState.Satisfied),
            new SketchConstraint("tangent", SketchConstraintKind.Tangent, new[] { "small", "large" }, SketchConstraintState.Satisfied) });
        SketchGeometryDragService.TryApplyDrag(document, $"small|point|quadrant-90|{x}|3", new(x, 3), new(x, 4), false,
            out var changed, out var status).Should().BeTrue(status);
        ((CircleEntity)changed.Entities[0]).Center.Should().Be(new Point2(0, 0));
        ((CircleEntity)changed.Entities[0]).Radius.Should().BeApproximately(Math.Sqrt(x * x + 1) + (inside ? 3 : -3), 1e-8);
        ((CircleEntity)changed.Entities[1]).Center.Should().Be(new Point2(x, 1));
        ((CircleEntity)changed.Entities[1]).Radius.Should().Be(3);
        changed.Dimensions.Single().Value.Should().Be(3);
        changed.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrivingPeerRadiusRefusesIncompatibleResizeAndRecovers(bool wholeFix)
    {
        var entities = new DrawingEntity[] {
            new CircleEntity(EntityId.Create("large"), new(0, 0), 10),
            new CircleEntity(EntityId.Create("small"), new(7, 0), 3) };
        var constraints = new[] {
            new SketchConstraint("anchor", SketchConstraintKind.Fix, new[] { wholeFix ? "large" : "large:center" }, SketchConstraintState.Satisfied),
            new SketchConstraint("tangent", SketchConstraintKind.Tangent, new[] { "small", "large" }, SketchConstraintState.Satisfied) };
        var dimensions = wholeFix ? Array.Empty<SketchDimension>() : new[] {
            new SketchDimension("radius", SketchDimensionKind.Radius, new[] { "large" }, 10, new(-7, 7), true) };
        var document = new DrawingDocument(entities, dimensions, constraints);
        SketchGeometryDragService.TryApplyDrag(document, "small|point|quadrant-90|7|3", new(7, 3), new(7, 4), false,
            out var unchanged, out _).Should().BeFalse();
        unchanged.Entities.Should().Equal(entities);
        unchanged.Dimensions.Should().Equal(dimensions);
        var free = new DrawingDocument(entities, Array.Empty<SketchDimension>(), new[] {
            new SketchConstraint("anchor", SketchConstraintKind.Fix, new[] { "large:center" }, SketchConstraintState.Satisfied), constraints[1] });
        SketchGeometryDragService.TryApplyDrag(free, "small|point|quadrant-90|7|3", new(7, 3), new(7, 4), false,
            out var recovered, out var status).Should().BeTrue(status);
        ((CircleEntity)recovered.Entities[0]).Radius.Should().Be(11);
    }

    [Theory]
    [InlineData(0.00001)]
    [InlineData(1.0)]
    [InlineData(10000.0)]
    public void InternalTangencyKeepsContainingCircleWhenDraggedPastOldBranchBoundary(double scale)
    {
        var document = new DrawingDocument(new DrawingEntity[] {
            new CircleEntity(EntityId.Create("large"), new(0, 0), 10 * scale),
            new CircleEntity(EntityId.Create("small"), new(7 * scale, 0), 3 * scale) },
            Array.Empty<SketchDimension>(), new[] {
                new SketchConstraint("anchor", SketchConstraintKind.Fix, new[] { "large:center" }, SketchConstraintState.Satisfied),
                new SketchConstraint("tangent", SketchConstraintKind.Tangent, new[] { "small", "large" }, SketchConstraintState.Satisfied) });
        SketchGeometryDragService.TryApplyDrag(document, $"small|point|center|{7 * scale}|0", new(7 * scale, 0), new(14 * scale, 0), false,
            out var changed, out var status).Should().BeTrue(status);
        ((CircleEntity)changed.Entities[0]).Radius.Should().BeApproximately(17 * scale, 1e-8);
    }

    public static IEnumerable<object[]> Cases =>
        from anchor in new[] { "Fix", "Coincident", "Concentric" }
        from inside in new[] { false, true }
        from center in new[] { false, true }
        from reverse in new[] { false, true }
        select new object[] { anchor, inside, center, reverse };

    [Theory]
    [MemberData(nameof(Cases))]
    public void SmallCircleDragResizesFreeAnchoredPeer(string anchor, bool inside, bool center, bool reverse)
    {
        var x = inside ? 7d : 13d;
        var anchorConstraint = anchor == "Fix"
            ? new SketchConstraint("anchor", SketchConstraintKind.Fix, new[] { "large:center" })
            : new SketchConstraint("anchor", Enum.Parse<SketchConstraintKind>(anchor),
                new[] { anchor == "Concentric" ? "large" : "large:center", "$origin:center" });
        var tangent = new SketchConstraint("tangent", SketchConstraintKind.Tangent, new[] { "small", "large" });
        var constraints = reverse ? new[] { tangent, anchorConstraint } : new[] { anchorConstraint, tangent };
        var document = SketchConstraintService.ApplyConstraints(new DrawingDocument(new DrawingEntity[] {
            new CircleEntity(EntityId.Create("large"), new(0, 0), 10),
            new CircleEntity(EntityId.Create("small"), new(x, 0), 3)
        }), constraints);
        document.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
        ((CircleEntity)document.Entities[0]).Radius.Should().Be(10);
        ((CircleEntity)document.Entities[1]).Center.Should().Be(new Point2(x, 0));
        var start = new Point2(center ? x : x + 3, 0);
        var end = new Point2(start.X + 1, 0);
        var key = center ? $"small|point|center|{x}|0" : $"small|point|quadrant-0|{x + 3}|0";
        SketchGeometryDragService.TryApplyDrag(document, key, start, end, false,
            out var changed, out var status).Should().BeTrue(status);
        var large = (CircleEntity)changed.Entities[0]; var small = (CircleEntity)changed.Entities[1];
        large.Center.Should().Be(new Point2(0, 0));
        large.Radius.Should().BeApproximately(inside || center ? 11 : 9, 1e-8);
        small.Center.Should().Be(new Point2(center ? x + 1 : x, 0));
        small.Radius.Should().Be(center ? 3 : 4);
        changed.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }
}

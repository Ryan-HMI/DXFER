using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class AnchoredTangentDragTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void CenterDragOrbitsSizedAnchoredPeer(bool inside, bool reverse)
    {
        var x = inside ? 7d : 13d;
        var tangent = new SketchConstraint("tangent", SketchConstraintKind.Tangent,
            reverse ? new[] { "large", "small" } : new[] { "small", "large" }, SketchConstraintState.Satisfied);
        var document = new DrawingDocument(new DrawingEntity[] {
            new CircleEntity(EntityId.Create("large"), new(0, 0), 10),
            new CircleEntity(EntityId.Create("small"), new(x, 0), 3) }, new[] {
            new SketchDimension("radius", SketchDimensionKind.Radius, new[] { "large" }, 10, new(-7, 7), true) }, new[] {
            new SketchConstraint("anchor", SketchConstraintKind.Coincident, new[] { "large:center", "$origin:center" }, SketchConstraintState.Satisfied), tangent });
        SketchGeometryDragService.TryApplyDrag(document, $"small|point|center|{x}|0", new(x, 0), new(x + 1, 2), false,
            out var changed, out var status).Should().BeTrue(status);
        changed.Entities[0].Should().Be(document.Entities[0]);
        var small = (CircleEntity)changed.Entities[1];
        var length = Math.Sqrt((x + 1) * (x + 1) + 4);
        small.Center.X.Should().BeApproximately((x + 1) / length * x, 1e-8);
        small.Center.Y.Should().BeApproximately(2 / length * x, 1e-8);
        small.Radius.Should().Be(3);
        changed.Dimensions.Single().Value.Should().Be(10);
        changed.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(0.00001, false)]
    [InlineData(1, false)]
    [InlineData(10000, false)]
    [InlineData(1, true)]
    public void OrbitPreservesFixedPeerAndDrivingSmallRadiusAndRespectsFixedCenter(double scale, bool inside)
    {
        var x = (inside ? 7d : 13d) * scale;
        var entities = new DrawingEntity[] {
            new CircleEntity(EntityId.Create("large"), new(0, 0), 10 * scale),
            new CircleEntity(EntityId.Create("small"), new(x, 0), 3 * scale) };
        var dimensions = new[] {
            new SketchDimension("radius", SketchDimensionKind.Radius, new[] { "small" }, 3 * scale, new(x, -4 * scale), true) };
        var constraints = new[] {
            new SketchConstraint("anchor", SketchConstraintKind.Fix, new[] { "large" }, SketchConstraintState.Satisfied),
            new SketchConstraint("tangent", SketchConstraintKind.Tangent, new[] { "large", "small" }, SketchConstraintState.Satisfied),
            new SketchConstraint("small-anchor", SketchConstraintKind.Fix, new[] { "small:center" }, SketchConstraintState.Satisfied) };
        var document = new DrawingDocument(entities, dimensions, constraints);
        var start = new Point2(x, 3 * scale);
        var end = new Point2(x + scale, 5 * scale);
        var key = $"small|point|quadrant-90|{x}|{3 * scale}";
        SketchGeometryDragService.TryApplyDrag(document, key, start, end, false,
            out var unchanged, out _).Should().BeFalse();
        unchanged.Entities.Should().Equal(entities);
        unchanged.Dimensions.Should().Equal(dimensions);
        var free = new DrawingDocument(entities, dimensions, constraints.Take(2).ToArray());
        SketchGeometryDragService.TryApplyDrag(free, key, start, end, false,
            out var changed, out var status).Should().BeTrue(status);
        changed.Entities[0].Should().Be(entities[0]);
        var small = (CircleEntity)changed.Entities[1];
        small.Radius.Should().Be(3 * scale);
        Math.Sqrt(small.Center.X * small.Center.X + small.Center.Y * small.Center.Y).Should().BeApproximately(x, 1e-8);
        small.Center.Y.Should().BeGreaterThan(0);
        changed.Dimensions.Single().Value.Should().Be(3 * scale);
        changed.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(-10, 17, true)]
    [InlineData(0, 0, false)]
    [InlineData(-22.8, 30.4, false)]
    public void OffAxisOrbitAndDegenerateRequestsPreserveSizedPeer(double x, double y, bool accepted)
    {
        var document = new DrawingDocument(new DrawingEntity[] {
            new CircleEntity(EntityId.Create("large"), new(0, 0), 10),
            new CircleEntity(EntityId.Create("small"), new(-11.4, 15.2), 9) }, new[] {
            new SketchDimension("radius", SketchDimensionKind.Radius, new[] { "large" }, 10, new(11, -4), true) }, new[] {
            new SketchConstraint("anchor", SketchConstraintKind.Coincident, new[] { "large:center", "$origin:center" }, SketchConstraintState.Satisfied),
            new SketchConstraint("tangent", SketchConstraintKind.Tangent, new[] { "small", "large" }, SketchConstraintState.Satisfied) });
        SketchGeometryDragService.TryApplyDrag(document, "small|point|center|-11.4|15.2", new(-11.4, 15.2), new(x, y), false,
            out var changed, out _).Should().Be(accepted);
        changed.Entities[0].Should().Be(document.Entities[0]);
        if (!accepted) changed.Entities.Should().Equal(document.Entities);
        else
        {
            var small = (CircleEntity)changed.Entities[1];
            Math.Sqrt(small.Center.X * small.Center.X + small.Center.Y * small.Center.Y).Should().BeApproximately(19, 1e-8);
            small.Radius.Should().Be(9);
        }
    }

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

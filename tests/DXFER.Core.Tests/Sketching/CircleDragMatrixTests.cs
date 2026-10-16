using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class CircleDragMatrixTests
{
    private static readonly string[] Fixtures = { "free", "fixed", "weld-free", "weld-fixed", "radius-free", "radius-center-fixed" };

    public static IEnumerable<object[]> DragCases()
    {
        foreach (var fixture in Fixtures)
            foreach (var grip in new[] { "center", "quadrant-0" })
                foreach (var reverse in new[] { false, true })
                    yield return new object[] { fixture, grip, reverse };
    }

    [Theory]
    [MemberData(nameof(DragCases))]
    public void CircleDragPreservesDrivingRadiusOrUpdatesReferenceAndRecoversAfterFixRemoval(string fixture, string grip, bool reverse)
    {
        var original = CreateDocument(fixture, reverse);
        var driving = original.Dimensions[0].IsDriving;
        var delta = !driving && grip != "center" ? new Point2(1, 0) : new Point2(1, 1);
        var locked = fixture is "fixed" or "weld-fixed" || fixture == "radius-center-fixed" && grip == "center";
        var before = original.Entities.ToArray();
        Drag(original, grip, delta, out var next, out var status).Should().Be(!locked, status);
        if (locked)
        {
            next.Should().BeSameAs(original);
            Drag(RemoveFix(next), grip, delta, out next, out status).Should().BeTrue(status);
        }
        var translates = driving || grip == "center";
        AssertResult(original, next, translates ? delta : new(0, 0), translates ? 3 : 4);
        // Pick the current grip again to detect accumulated anchor or stale-coordinate errors.
        Drag(next, grip, delta, out var again, out status).Should().BeTrue(status);
        AssertResult(original, again, translates ? new(2 * delta.X, 2 * delta.Y) : new(0, 0), translates ? 3 : 5);
        original.Entities.Should().Equal(before);
        original.Dimensions[0].Value.Should().Be(3);
        original.Dimensions[0].Anchor.Should().Be(new Point2(4, -1));
    }

    public static IEnumerable<object[]> DegenerateCases()
    {
        foreach (var fixture in Fixtures)
            foreach (var offset in new[] { 0.0, 1e-9 })
                foreach (var reverse in new[] { false, true })
                    yield return new object[] { fixture, offset, reverse };
    }

    [Theory]
    [MemberData(nameof(DegenerateCases))]
    public void CollapsedRadiusAndNegligibleCenterMovementRefuseThenRecover(string fixture, double offset, bool reverse)
    {
        var original = CreateDocument(fixture, reverse);
        Drag(original, "quadrant-0", new(offset - 3, 0), out var refused, out _).Should().BeFalse();
        refused.Should().BeSameAs(original);
        Drag(original, "center", new(offset, offset), out refused, out _).Should().BeFalse();
        refused.Should().BeSameAs(original);
        var unlocked = RemoveFix(original);
        Drag(unlocked, "quadrant-0", new(-7, 0), out var recovered, out var status).Should().BeTrue(status);
        AssertResult(original, recovered, original.Dimensions[0].IsDriving ? new(-7, 0) : new(0, 0),
            original.Dimensions[0].IsDriving ? 3 : 4);
        Drag(unlocked, "center", new(1e-4, 1e-4), out recovered, out status).Should().BeTrue(status);
        AssertResult(original, recovered, new(1e-4, 1e-4), 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WeldPropagationRefreshesPeerReferenceMeasurementWithoutChangingDrivingRadius(bool reverse)
    {
        var original = CreateDocument("weld-free", reverse);
        original = new DrawingDocument(original.Entities, original.Dimensions.Concat(new[] {
            new SketchDimension("peer-length", SketchDimensionKind.LinearDistance, new[] { "peer:start", "peer:end" }, 2, new(-1, -1))
        }), original.Constraints);
        Drag(original, "center", new(1, 1), out var next, out var status).Should().BeTrue(status);
        next.Dimensions.Single(d => d.Id == "radius").Value.Should().Be(3);
        var peer = next.Dimensions.Single(d => d.Id == "peer-length");
        peer.Value.Should().BeApproximately(Math.Sqrt(10), 1e-8);
        peer.Anchor!.Value.X.Should().BeApproximately(-0.5 + 1 / Math.Sqrt(10), 1e-8);
        peer.Anchor.Value.Y.Should().BeApproximately(0.5 - 3 / Math.Sqrt(10), 1e-8);
        peer.IsDriving.Should().BeFalse();
    }

    private static DrawingDocument CreateDocument(string fixture, bool reverse)
    {
        var entities = new List<DrawingEntity> { new CircleEntity(EntityId.Create("circle"), new(0, 0), 3) };
        var constraints = new List<SketchConstraint>();
        if (fixture.StartsWith("weld", StringComparison.Ordinal))
        {
            entities.Add(new LineEntity(EntityId.Create("peer"), new(0, 0), new(-2, 0)));
            constraints.Add(new("weld", SketchConstraintKind.Coincident,
                reverse ? new[] { "peer:start", "circle:center" } : new[] { "circle:center", "peer:start" }));
        }
        var fixedKey = fixture switch { "fixed" => "circle", "weld-fixed" => "peer:start", "radius-center-fixed" => "circle:center", _ => null };
        if (fixedKey != null) constraints.Add(new("fix", SketchConstraintKind.Fix, new[] { fixedKey }));
        if (reverse) constraints.Reverse();
        return new DrawingDocument(entities, new[] {
            new SketchDimension("radius", SketchDimensionKind.Radius, new[] { "circle" }, 3, new(4, -1),
                !fixture.StartsWith("radius", StringComparison.Ordinal))
        }, constraints);
    }

    private static DrawingDocument RemoveFix(DrawingDocument original) => new(original.Entities, original.Dimensions,
        original.Constraints.Where(c => c.Kind != SketchConstraintKind.Fix), original.Metadata);

    private static bool Drag(DrawingDocument document, string grip, Point2 delta, out DrawingDocument next, out string status)
    {
        var circle = (CircleEntity)document.Entities[0];
        var start = grip == "center" ? circle.Center : new Point2(circle.Center.X + circle.Radius, circle.Center.Y);
        var key = string.Create(CultureInfo.InvariantCulture, $"circle|point|{grip}|{start.X:R}|{start.Y:R}");
        return SketchGeometryDragService.TryApplyDrag(document, key, start, new(start.X + delta.X, start.Y + delta.Y),
            false, out next, out status);
    }

    private static void AssertResult(DrawingDocument original, DrawingDocument next, Point2 center, double radius)
    {
        var circle = (CircleEntity)next.Entities[0];
        AssertPoint(circle.Center, center);
        circle.Radius.Should().Be(radius);
        var dimension = next.Dimensions.Should().ContainSingle().Which;
        dimension.Value.Should().Be(radius);
        dimension.IsDriving.Should().Be(original.Dimensions[0].IsDriving);
        dimension.Kind.Should().Be(SketchDimensionKind.Radius);
        dimension.ReferenceKeys.Should().Equal("circle");
        AssertPoint(dimension.Anchor!.Value, new(4 + center.X, -1 + center.Y));
        SketchDimensionSolverService.IsDimensionSatisfied(next, dimension).Should().BeTrue();
        foreach (var constraint in next.Constraints) constraint.State.Should().Be(SketchConstraintState.Satisfied);
        if (next.Entities.Count == 2)
        {
            AssertPoint(((LineEntity)next.Entities[1]).Start, center);
            ((LineEntity)next.Entities[1]).End.Should().Be(new Point2(-2, 0));
            next.Constraints.Single(c => c.Id == "weld").ReferenceKeys.Should()
                .Equal(original.Constraints.Single(c => c.Id == "weld").ReferenceKeys);
        }
    }

    private static void AssertPoint(Point2 actual, Point2 expected)
    {
        actual.X.Should().BeApproximately(expected.X, 1e-8);
        actual.Y.Should().BeApproximately(expected.Y, 1e-8);
    }
}

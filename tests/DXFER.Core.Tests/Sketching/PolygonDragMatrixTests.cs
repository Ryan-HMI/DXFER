using System.Globalization;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PolygonDragMatrixTests
{
    public static IEnumerable<object[]> TranslationCases()
    {
        foreach (var grip in new[] { "center", "mid-0", "whole" })
        foreach (var fixture in new[] { "free", "fixed", "weld-free", "weld-fixed", "weld-fixed-entity" })
        foreach (var circumscribed in new[] { false, true })
        foreach (var reverse in new[] { false, true })
            yield return new object[] { grip, fixture, circumscribed, reverse };
    }

    [Theory]
    [MemberData(nameof(TranslationCases))]
    public void DrivingPolygonTranslationPreservesIntentOrRefusesThenRecovers(
        string grip, string fixture, bool circumscribed, bool reverse)
    {
        var original = CreateDocument(fixture, circumscribed, reverse);
        var beforeEntities = original.Entities.ToArray();
        var beforeDimensions = original.Dimensions.ToArray();
        var beforeConstraints = original.Constraints.ToArray();
        var locked = fixture == "fixed" || fixture.StartsWith("weld-fixed", StringComparison.Ordinal);

        var accepted = Drag(original, grip, new(1, 1), out var next, out var status);
        accepted.Should().Be(!locked, status);
        if (locked)
        {
            next.Should().BeSameAs(original);
            status.Should().NotBeNullOrWhiteSpace();
            var unlocked = new DrawingDocument(next.Entities, next.Dimensions,
                next.Constraints.Where(c => c.Kind != SketchConstraintKind.Fix), next.Metadata);
            Drag(unlocked, grip, new(1, 1), out next, out status).Should().BeTrue(status);
        }

        AssertTranslated(original, next, new(1, 1));
        // A subsequent drag must move live anchors once, not reuse coordinates from the first pick.
        Drag(next, grip, new(-1, 2), out var again, out status).Should().BeTrue(status);
        AssertTranslated(original, again, new(0, 3));
        original.Entities.Should().Equal(beforeEntities);
        original.Dimensions.Should().Equal(beforeDimensions);
        original.Constraints.Should().Equal(beforeConstraints);
    }

    [Theory]
    [InlineData("center", SketchDimensionKind.Radius)]
    [InlineData("mid-0", SketchDimensionKind.Radius)]
    [InlineData("whole", SketchDimensionKind.Radius)]
    [InlineData("center", SketchDimensionKind.Count)]
    [InlineData("mid-0", SketchDimensionKind.Count)]
    [InlineData("whole", SketchDimensionKind.Count)]
    public void InvalidDrivingIntentRefusesWithoutRewritingValues(string grip, SketchDimensionKind kind)
    {
        var valid = CreateDocument("weld-free", false, false);
        var invalid = new DrawingDocument(valid.Entities, valid.Dimensions.Select(d =>
            new SketchDimension(d.Id, d.Kind, d.ReferenceKeys, d.Value + (d.Kind == kind ? 1 : 0), d.Anchor, true)), valid.Constraints);
        Drag(invalid, grip, new(1, 1), out var next, out _).Should().BeFalse();
        next.Should().BeSameAs(invalid);
        Drag(valid, grip, new(1, 1), out next, out var status).Should().BeTrue(status);
        AssertTranslated(valid, next, new(1, 1));
    }

    public static IEnumerable<object[]> NegligibleTranslationCases()
    {
        foreach (var grip in new[] { "center", "mid-0", "whole" })
        foreach (var offset in new[] { 0.0, 1e-9 })
        foreach (var locked in new[] { false, true })
        foreach (var reverse in new[] { false, true })
            yield return new object[] { grip, offset, locked, reverse };
    }

    [Theory]
    [MemberData(nameof(NegligibleTranslationCases))]
    public void NegligibleTranslationRefusesThenValidMovementRecovers(string grip, double offset, bool locked, bool reverse)
    {
        var original = CreateDocument(locked ? "weld-fixed" : "weld-free", false, reverse);
        Drag(original, grip, new(offset, offset), out var refused, out _).Should().BeFalse();
        refused.Should().BeSameAs(original);
        var unlocked = new DrawingDocument(refused.Entities, refused.Dimensions,
            refused.Constraints.Where(c => c.Kind != SketchConstraintKind.Fix), refused.Metadata);
        Drag(unlocked, grip, new(1e-4, 1e-4), out var recovered, out var status).Should().BeTrue(status);
        AssertTranslated(original, recovered, new(1e-4, 1e-4));
    }

    private static DrawingDocument CreateDocument(string fixture, bool circumscribed, bool reverse)
    {
        var entities = new List<DrawingEntity> {
            new PolygonEntity(EntityId.Create("polygon"), new(0, 0), 5, 30, 6, circumscribed)
        };
        var constraints = new List<SketchConstraint>();
        if (fixture.StartsWith("weld", StringComparison.Ordinal))
        {
            entities.Add(new LineEntity(EntityId.Create("peer"), new(0, 0), new(-2, 0)));
            constraints.Add(new("weld", SketchConstraintKind.Coincident,
                reverse ? new[] { "peer:start", "polygon:center" } : new[] { "polygon:center", "peer:start" }));
        }
        if (fixture.StartsWith("weld-fixed", StringComparison.Ordinal))
            constraints.Add(new("fix", SketchConstraintKind.Fix,
                new[] { fixture == "weld-fixed-entity" ? "peer" : "peer:start" }));
        if (fixture == "fixed")
            constraints.Add(new("fix", SketchConstraintKind.Fix, new[] { "polygon" }));
        var dimensions = new List<SketchDimension> {
            new("radius", SketchDimensionKind.Radius, new[] { "polygon" }, 5, new(6, -2), true),
            new("count", SketchDimensionKind.Count, new[] { "polygon" }, 6, new(0, -6), true)
        };
        if (reverse)
        {
            constraints.Reverse();
            dimensions.Reverse();
        }
        return new DrawingDocument(entities, dimensions, constraints);
    }

    private static bool Drag(DrawingDocument document, string grip, Point2 delta,
        out DrawingDocument next, out string status)
    {
        var polygon = (PolygonEntity)document.Entities[0];
        var vertices = polygon.GetVertices();
        var start = grip switch {
            "center" => polygon.Center,
            "mid-0" => new Point2((vertices[0].X + vertices[1].X) / 2, (vertices[0].Y + vertices[1].Y) / 2),
            _ => new Point2(0.7 * vertices[0].X + 0.3 * vertices[1].X, 0.7 * vertices[0].Y + 0.3 * vertices[1].Y)
        };
        var key = grip == "whole" ? "polygon"
            : string.Create(CultureInfo.InvariantCulture, $"polygon|point|{grip}|{start.X:R}|{start.Y:R}");
        return SketchGeometryDragService.TryApplyDrag(document, key, start,
            new(start.X + delta.X, start.Y + delta.Y), false, out next, out status);
    }

    private static void AssertTranslated(DrawingDocument original, DrawingDocument next, Point2 delta)
    {
        var before = (PolygonEntity)original.Entities[0];
        var polygon = (PolygonEntity)next.Entities[0];
        AssertPoint(polygon.Center, new(before.Center.X + delta.X, before.Center.Y + delta.Y));
        polygon.Radius.Should().Be(before.Radius);
        polygon.SideCount.Should().Be(before.SideCount);
        polygon.RotationAngleDegrees.Should().Be(before.RotationAngleDegrees);
        polygon.Circumscribed.Should().Be(before.Circumscribed);
        var vertices = polygon.GetVertices();
        foreach (var (vertex, index) in before.GetVertices().Select((point, index) => (point, index)))
            AssertPoint(vertices[index], new(vertex.X + delta.X, vertex.Y + delta.Y));
        next.Dimensions.Should().HaveCount(original.Dimensions.Count);
        foreach (var dimension in next.Dimensions)
        {
            var previous = original.Dimensions.Single(d => d.Id == dimension.Id);
            dimension.Value.Should().Be(previous.Value);
            dimension.Kind.Should().Be(previous.Kind);
            dimension.ReferenceKeys.Should().Equal(previous.ReferenceKeys);
            dimension.IsDriving.Should().BeTrue();
            AssertPoint(dimension.Anchor!.Value, new(previous.Anchor!.Value.X + delta.X, previous.Anchor.Value.Y + delta.Y));
            SketchDimensionSolverService.IsDimensionSatisfied(next, dimension).Should().BeTrue();
        }
        foreach (var constraint in next.Constraints)
            constraint.State.Should().Be(SketchConstraintState.Satisfied);
        next.Constraints.Select(c => c.Id).Should().Equal(original.Constraints.Where(c => c.Kind != SketchConstraintKind.Fix).Select(c => c.Id));
        if (original.Entities.Count > 1)
        {
            var peer = (LineEntity)next.Entities[1];
            AssertPoint(peer.Start, polygon.Center);
            peer.End.Should().Be(new Point2(-2, 0));
            var weld = next.Constraints.Single(c => c.Id == "weld");
            weld.ReferenceKeys.Should().Equal(original.Constraints.Single(c => c.Id == "weld").ReferenceKeys);
        }
    }

    private static void AssertPoint(Point2 actual, Point2 expected)
    {
        actual.X.Should().BeApproximately(expected.X, 1e-8);
        actual.Y.Should().BeApproximately(expected.Y, 1e-8);
    }
}

using System.Globalization;
using DXFER.CadIO;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

// Bounded circle/straight-line sliding contract, not a general nonlinear solver test.
public sealed class DimensionedTangentCircleDragTests
{
    private const double Radius = 7.5;
    private static readonly string[] Grips = { "center", "quadrant-0", "perimeter" };

    public static IEnumerable<object[]> RectangleCases()
    {
        foreach (var grip in Grips)
        foreach (var reverse in new[] { false, true })
            yield return new object[] { grip, reverse };
    }

    [Theory]
    [MemberData(nameof(RectangleCases))]
    public void MixedDragSlidesDiameter15CircleAlongDimensioned20By10Rectangle(string grip, bool reverse)
    {
        var original = RectangleDocument(reverse);
        AssertSatisfied(original);
        original.Constraints.Should().NotContain(c => c.Kind == SketchConstraintKind.Fix);
        original.Dimensions.Should().HaveCount(3);
        var before = Native(original);

        Drag(original, grip, new(3, 2), out var next, out var status).Should().BeTrue(status);

        AssertSlide(original, next, new(13, 17.5));
        Native(original).Should().Be(before, "a drag candidate must not mutate its input/history snapshot");
        // A second current-grip pick catches stale circle coordinates and accumulated radius changes.
        Drag(next, grip, new(-2, -1), out var again, out status).Should().BeTrue(status);
        AssertSlide(next, again, new(11, 17.5));
        var reopened = DxfDocumentReader.Read(Native(again));
        AssertSlide(original, reopened, new(11, 17.5));
    }

    public static IEnumerable<object[]> FixedLineCases()
    {
        foreach (var orientation in new[] { "horizontal", "vertical", "diagonal" })
        foreach (var grip in Grips)
        foreach (var reverse in new[] { false, true })
        foreach (var kind in new[] { SketchDimensionKind.Diameter, SketchDimensionKind.Radius })
            yield return new object[] { orientation, grip, reverse, kind };
    }

    [Theory]
    [MemberData(nameof(FixedLineCases))]
    public void MixedDragSlidesAlongFixedLineWithoutChangingRadialIntent(
        string orientation, string grip, bool reverse, SketchDimensionKind kind)
    {
        var (original, tangent, normal) = FixedLineDocument(orientation, reverse, kind);
        AssertSatisfied(original);
        var circle = Circle(original);
        // Retain two units along the tangent; discard one unit normal to the fixed line.
        var delta = new Point2(2 * tangent.X + normal.X, 2 * tangent.Y + normal.Y);
        var expected = new Point2(circle.Center.X + 2 * tangent.X, circle.Center.Y + 2 * tangent.Y);
        var before = Native(original);

        Drag(original, grip, delta, out var next, out var status).Should().BeTrue(status);

        AssertSlide(original, next, expected);
        Native(original).Should().Be(before);
    }

    [Theory]
    [MemberData(nameof(RectangleCases))]
    public void FixedTangentCircleStillRefusesMixedDragWithoutChangingNativeDocument(string grip, bool reverse)
    {
        var rectangle = RectangleDocument(reverse);
        var original = new DrawingDocument(rectangle.Entities, rectangle.Dimensions,
            rectangle.Constraints.Append(new SketchConstraint("fixed-circle", SketchConstraintKind.Fix,
                new[] { "circle" }, SketchConstraintState.Satisfied)));
        AssertSatisfied(original);
        var before = Native(original);

        Drag(original, grip, new(3, 2), out var next, out _).Should().BeFalse();

        next.Should().BeSameAs(original);
        Native(next).Should().Be(before);
    }

    [Theory]
    [MemberData(nameof(RectangleCases))]
    public void FixedCenterAlsoRefusesMixedDragWithDrivingDiameter(string grip, bool reverse)
    {
        var (document, _, _) = FixedLineDocument("horizontal", reverse, SketchDimensionKind.Diameter);
        var original = new DrawingDocument(document.Entities, document.Dimensions,
            document.Constraints.Append(new SketchConstraint("fixed-center", SketchConstraintKind.Fix,
                new[] { "circle:center" }, SketchConstraintState.Satisfied)));
        var before = Native(original);

        Drag(original, grip, new(3, 2), out var next, out _).Should().BeFalse();

        next.Should().BeSameAs(original);
        Native(next).Should().Be(before);
    }

    [Fact]
    public void CenterPositionDimensionStillBlocksSlideAndRemovingItRestoresFreedom()
    {
        var (free, _, _) = FixedLineDocument("horizontal", false, SketchDimensionKind.Diameter);
        var locked = new DrawingDocument(free.Entities, free.Dimensions.Append(
            new SketchDimension("center-x", SketchDimensionKind.HorizontalDistance,
                new[] { SketchReference.OriginKey, "circle:center" }, 10, isDriving: true)), free.Constraints);
        Drag(locked, "center", new(3, 2), out var unchanged, out _).Should().BeFalse();
        unchanged.Should().BeSameAs(locked);
        Drag(free, "center", new(3, 2), out var moved, out var status).Should().BeTrue(status);
        AssertSlide(free, moved, new(13, 7.5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1e-10)]
    public void NegligibleAlongEdgeMovementDoesNotCreateAnEdit(double along)
    {
        var (original, _, _) = FixedLineDocument("horizontal", false, SketchDimensionKind.Diameter);
        Drag(original, "center", new(along, 2), out var next, out _).Should().BeFalse();
        next.Should().BeSameAs(original);
        Drag(original, "center", new(.0001, 2), out next, out var status).Should().BeTrue(status);
        AssertSlide(original, next, new(10.0001, 7.5));
    }

    private static DrawingDocument RectangleDocument(bool reverse)
    {
        var entities = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("bottom"), new(0, 0), new(20, 0)),
            new LineEntity(EntityId.Create("right"), new(20, 0), new(20, 10)),
            new LineEntity(EntityId.Create("top"), new(20, 10), new(0, 10)),
            new LineEntity(EntityId.Create("left"), new(0, 10), new(0, 0)),
            new CircleEntity(EntityId.Create("circle"), new(10, 17.5), Radius)
        };
        var constraints = new[]
        {
            Relation("corner0", SketchConstraintKind.Coincident, "bottom:end", "right:start", reverse),
            Relation("corner1", SketchConstraintKind.Coincident, "right:end", "top:start", reverse),
            Relation("corner2", SketchConstraintKind.Coincident, "top:end", "left:start", reverse),
            Relation("corner3", SketchConstraintKind.Coincident, "left:end", "bottom:start", reverse),
            Relation("parallel0", SketchConstraintKind.Parallel, "bottom", "top", reverse),
            Relation("parallel1", SketchConstraintKind.Parallel, "left", "right", reverse),
            Relation("perpendicular", SketchConstraintKind.Perpendicular, "bottom", "right", reverse),
            Relation("tangent", SketchConstraintKind.Tangent, "top", "circle", reverse)
        };
        var dimensions = new[]
        {
            new SketchDimension("width", SketchDimensionKind.LinearDistance, new[] { "bottom:start", "bottom:end" }, 20, isDriving: true),
            new SketchDimension("height", SketchDimensionKind.LinearDistance, new[] { "right:start", "right:end" }, 10, isDriving: true),
            RadialDimension(SketchDimensionKind.Diameter)
        };
        return new DrawingDocument(reverse ? entities.Reverse() : entities,
            reverse ? dimensions.Reverse() : dimensions, reverse ? constraints.Reverse() : constraints);
    }

    private static (DrawingDocument Document, Point2 Tangent, Point2 Normal) FixedLineDocument(
        string orientation, bool reverse, SketchDimensionKind kind)
    {
        var tangent = orientation switch
        {
            "vertical" => new Point2(0, 1),
            "diagonal" => new Point2(.6, .8),
            _ => new Point2(1, 0)
        };
        var normal = new Point2(-tangent.Y, tangent.X);
        var center = new Point2(10 * tangent.X + Radius * normal.X, 10 * tangent.Y + Radius * normal.Y);
        var end = new Point2(20 * tangent.X, 20 * tangent.Y);
        var entities = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), reverse ? end : new(0, 0), reverse ? new(0, 0) : end),
            new CircleEntity(EntityId.Create("circle"), center, Radius)
        };
        var constraints = new[]
        {
            new SketchConstraint("fixed-edge", SketchConstraintKind.Fix, new[] { "edge" }, SketchConstraintState.Satisfied),
            Relation("tangent", SketchConstraintKind.Tangent, "edge", "circle", reverse)
        };
        return (new DrawingDocument(reverse ? entities.Reverse() : entities,
            new[] { RadialDimension(kind) }, reverse ? constraints.Reverse() : constraints), tangent, normal);
    }

    private static SketchDimension RadialDimension(SketchDimensionKind kind) =>
        new("radial-intent", kind, new[] { "circle" }, kind == SketchDimensionKind.Diameter ? Radius * 2 : Radius, isDriving: true);

    private static SketchConstraint Relation(string id, SketchConstraintKind kind, string first, string second, bool reverse) =>
        new(id, kind, reverse ? new[] { second, first } : new[] { first, second }, SketchConstraintState.Satisfied);

    private static CircleEntity Circle(DrawingDocument document) => document.Entities.OfType<CircleEntity>().Single();
    private static string Native(DrawingDocument document) => DxfDocumentWriter.Write(document, new DxfWriteOptions(IncludeNativeDocument: true));

    private static bool Drag(DrawingDocument document, string grip, Point2 delta, out DrawingDocument next, out string status)
    {
        var circle = Circle(document);
        var start = grip == "center" ? circle.Center : new Point2(circle.Center.X + circle.Radius, circle.Center.Y);
        var key = grip == "perimeter" ? "circle"
            : string.Create(CultureInfo.InvariantCulture, $"circle|point|{grip}|{start.X:R}|{start.Y:R}");
        return SketchGeometryDragService.TryApplyDrag(document, key, start,
            new(start.X + delta.X, start.Y + delta.Y), false, out next, out status);
    }

    private static void AssertSlide(DrawingDocument original, DrawingDocument next, Point2 expectedCenter)
    {
        var circle = Circle(next);
        circle.Center.X.Should().BeApproximately(expectedCenter.X, 1e-8);
        circle.Center.Y.Should().BeApproximately(expectedCenter.Y, 1e-8);
        circle.Radius.Should().Be(Radius);
        next.Entities.Where(e => e.Id.Value != "circle").Should().Equal(original.Entities.Where(e => e.Id.Value != "circle"));
        next.Dimensions.Should().HaveCount(original.Dimensions.Count);
        foreach (var dimension in original.Dimensions)
        {
            var after = next.Dimensions.Single(d => d.Id == dimension.Id);
            after.Value.Should().Be(dimension.Value);
            after.Kind.Should().Be(dimension.Kind);
            after.ReferenceKeys.Should().Equal(dimension.ReferenceKeys);
            after.IsDriving.Should().BeTrue();
        }
        next.Constraints.Should().HaveCount(original.Constraints.Count);
        foreach (var constraint in original.Constraints)
        {
            var after = next.Constraints.Single(c => c.Id == constraint.Id);
            after.Kind.Should().Be(constraint.Kind);
            after.ReferenceKeys.Should().Equal(constraint.ReferenceKeys);
        }
        AssertSatisfied(next);
    }

    private static void AssertSatisfied(DrawingDocument document)
    {
        foreach (var constraint in document.Constraints)
            SketchConstraintService.ValidateConstraint(document, constraint).State.Should().Be(SketchConstraintState.Satisfied,
                $"{constraint.Id} must be satisfied by geometry, not just carry a stale state");
        foreach (var dimension in document.Dimensions)
            SketchDimensionSolverService.IsDimensionSatisfied(document, dimension).Should().BeTrue(dimension.Id);
    }
}

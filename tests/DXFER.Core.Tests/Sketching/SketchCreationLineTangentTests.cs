using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchCreationLineTangentTests
{
    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 0)]
    [InlineData(true, 1)]
    [InlineData(true, 2)]
    public void InfersTangentAtStartEndOrInteriorWithoutMovingExactGeometry(bool arc, int contact)
    {
        DrawingEntity curve = arc
            ? new ArcEntity(EntityId.Create("curve"), new Point2(10, 10), 5, 0, 90)
            : Circle();
        var line = contact switch
        {
            0 => Line(13, 14, 9, 17),
            1 => Line(9, 17, 13, 14),
            _ => Line(17, 11, 9, 17)
        };
        var entities = new[] { curve, line };

        var constraints = Infer(new[] { curve }, new[] { line }, contact == 2 ? "midpointline" : "line");

        constraints.Should().ContainSingle().Which.ReferenceKeys.Should().Equal("curve", "new-line");
        constraints[0].Kind.Should().Be(SketchConstraintKind.Tangent);
        constraints[0].State.Should().Be(SketchConstraintState.Satisfied);
        var solved = SketchConstraintService.ApplyConstraints(new DrawingDocument(entities), constraints);
        solved.Entities.Should().Equal(entities);
        solved.Constraints.Should().ContainSingle().Which.State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0.0000005, true)]
    [InlineData(-0.0000005, true)]
    [InlineData(0.000002, false)]
    [InlineData(-0.000002, false)]
    [InlineData(0.001, false)]
    public void UsesAbsoluteGeometryToleranceNotVisualProximity(double offset, bool expected)
    {
        var curve = Circle();
        var line = Line(5, 15 + offset, 15, 15 + offset);
        var tangents = Tangents(Infer(new[] { curve }, new[] { line }));
        tangents.Should().HaveCount(expected ? 1 : 0);
        if (expected)
            SketchConstraintService.ValidateConstraint(new DrawingDocument(new DrawingEntity[] { curve, line }), tangents[0])
                .State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(0.0000005, true)]
    [InlineData(0.000002, false)]
    [InlineData(5, false)]
    public void RequiresContactOnFiniteSegmentWithWorldSpaceEndpointTolerance(double gap, bool expected)
    {
        Tangents(Infer(new[] { Circle() }, new[] { Line(10 + gap, 15, 20, 15) }))
            .Should().HaveCount(expected ? 1 : 0);
        Tangents(Infer(new[] { Circle() }, new[] { Line(20, 15, 10 + gap, 15) }))
            .Should().HaveCount(expected ? 1 : 0);
    }

    [Theory]
    [InlineData(0, 90, 0, true)]
    [InlineData(0, 90, 90, true)]
    [InlineData(0, 90, 45, true)]
    [InlineData(0, 90, 180, false)]
    [InlineData(300, 60, 0, true)]
    [InlineData(300, 60, 180, false)]
    [InlineData(-60, 60, 0, true)]
    [InlineData(360, 450, 45, true)]
    [InlineData(0, 360, 180, true)]
    [InlineData(0, 0, 0, false)]
    [InlineData(0, 0, 180, false)]
    [InlineData(0, 90, -0.000005, true)]
    [InlineData(0, 90, -0.00003, false)]
    [InlineData(0, 90, 90.000005, true)]
    [InlineData(0, 90, 90.00003, false)]
    public void RequiresContactWithinArcSweepIncludingWrappedAngles(double start, double end, double contact, bool expected)
    {
        var curve = new ArcEntity(EntityId.Create("curve"), new Point2(10, 10), 5, start, end);
        var angle = contact * Math.PI / 180;
        var x = 10 + 5 * Math.Cos(angle);
        var y = 10 + 5 * Math.Sin(angle);
        var line = Line(x + 4 * Math.Sin(angle), y - 4 * Math.Cos(angle),
            x - 4 * Math.Sin(angle), y + 4 * Math.Cos(angle));
        Tangents(Infer(new[] { curve }, new[] { line })).Should().HaveCount(expected ? 1 : 0);
    }

    [Theory]
    [InlineData(10, 15, 10, 15)]
    [InlineData(10, 15, 10.0000005, 15)]
    [InlineData(0, 10, 20, 10)]
    [InlineData(10, 15, 10, 20)]
    [InlineData(double.NaN, 15, 20, 15)]
    [InlineData(10, 15, double.PositiveInfinity, 15)]
    public void RejectsDegenerateSecantRadialAndNonfiniteLines(double x1, double y1, double x2, double y2)
    {
        Tangents(Infer(new[] { Circle() }, new[] { Line(x1, y1, x2, y2) })).Should().BeEmpty();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(0.0000005)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void RejectsInvalidOrDegenerateCurveRadius(double radius)
    {
        Tangents(Infer(new[] { Circle() with { Radius = radius } }, new[] { Line(5, 15, 15, 15) }))
            .Should().BeEmpty();
    }

    [Fact]
    public void LargeGeometryDoesNotScaleUpContactTolerance()
    {
        var largeCircle = Circle() with { Center = new Point2(0, 0), Radius = 1000000 };
        Tangents(Infer(new[] { largeCircle }, new[] { Line(-10, 1000000.001, 10, 1000000.001) }))
            .Should().BeEmpty();
        Tangents(Infer(new[] { Circle() }, new[] { Line(10.01, 15, 100000010, 15) }))
            .Should().BeEmpty();
        var largeArc = new ArcEntity(EntityId.Create("curve"), new Point2(0, 0), 1000000, 0, 90);
        var angle = -0.000005 * Math.PI / 180;
        var x = largeArc.Radius * Math.Cos(angle);
        var y = largeArc.Radius * Math.Sin(angle);
        Tangents(Infer(new[] { largeArc }, new[] { Line(x + Math.Sin(angle), y - Math.Cos(angle),
            x - Math.Sin(angle), y + Math.Cos(angle)) })).Should().BeEmpty();
    }

    [Theory]
    [InlineData(double.NaN, 90)]
    [InlineData(0, double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity, 90)]
    public void RejectsNonfiniteArcAngles(double start, double end)
    {
        var arc = new ArcEntity(EntityId.Create("curve"), new Point2(10, 10), 5, start, end);
        Tangents(Infer(new[] { arc }, new[] { Line(5, 15, 15, 15) })).Should().BeEmpty();
    }

    [Theory]
    [InlineData(double.NaN, 10)]
    [InlineData(10, double.PositiveInfinity)]
    public void RejectsNonfiniteCurveCenters(double x, double y)
    {
        Tangents(Infer(new[] { Circle() with { Center = new Point2(x, y) } }, new[] { Line(5, 15, 15, 15) }))
            .Should().BeEmpty();
    }

    [Fact]
    public void DeduplicatesRepeatedPairsAndRetainsDistinctCurvesRegardlessOfEnumerationOrder()
    {
        var first = Circle();
        var second = first with { Id = EntityId.Create("other-curve"), Center = new Point2(20, 10) };
        var line = Line(0, 15, 30, 15);
        foreach (var curves in new[] { new[] { first, second, first }, new[] { second, first, second } })
        {
            var calls = 0;
            var constraints = SketchCreationConstraintFactory.CreateConstraintsForInsertion("line", curves,
                new[] { line, line }, kind => $"{kind}-{++calls}");
            var tangents = Tangents(constraints);
            tangents.Should().HaveCount(2);
            tangents.Select(c => c.ReferenceKeys[0]).Should().BeEquivalentTo("curve", "other-curve");
            tangents.Should().OnlyContain(c => c.ReferenceKeys[1] == "new-line");
            constraints.Select(c => c.Id).Should().OnlyHaveUniqueItems();
            calls.Should().Be(constraints.Count);
        }
    }

    [Fact]
    public void ArcEndpointContinuationKeepsCoincidentAndAddsOneTangent()
    {
        var arc = new ArcEntity(EntityId.Create("curve"), new Point2(10, 10), 5, 0, 90);
        var constraints = Infer(new[] { arc }, new[] { Line(10, 15, 5, 15) });
        Tangents(constraints).Should().ContainSingle();
        constraints.Should().Contain(c => c.Kind == SketchConstraintKind.Coincident
            && c.ReferenceKeys.SequenceEqual(new[] { "curve:end", "new-line:start" }));
    }

    [Fact]
    public void DoesNotExpandInferenceToNewCirclesOrPolylineSegments()
    {
        var circle = Circle();
        var line = Line(5, 15, 15, 15);
        Tangents(Infer(new[] { line }, new[] { circle }, "centercircle")).Should().BeEmpty();
        var polyline = new PolylineEntity(EntityId.Create("polyline"), new[] { line.Start, line.End });
        Tangents(Infer(new[] { circle }, new[] { polyline }, "polyline")).Should().BeEmpty();
    }

    private static CircleEntity Circle() => new(EntityId.Create("curve"), new Point2(10, 10), 5);
    private static LineEntity Line(double x1, double y1, double x2, double y2) =>
        new(EntityId.Create("new-line"), new Point2(x1, y1), new Point2(x2, y2));
    private static IReadOnlyList<SketchConstraint> Infer(IReadOnlyList<DrawingEntity> existing,
        IReadOnlyList<DrawingEntity> created, string tool = "line") =>
        SketchCreationConstraintFactory.CreateConstraintsForInsertion(tool, existing, created,
            kind => $"{kind}-{Guid.NewGuid():N}");
    private static SketchConstraint[] Tangents(IEnumerable<SketchConstraint> constraints) =>
        constraints.Where(c => c.Kind == SketchConstraintKind.Tangent).ToArray();
}

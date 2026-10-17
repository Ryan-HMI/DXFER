using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SplineEndpointWeldTests
{
    public static IEnumerable<object[]> EndpointCases() =>
        from kind in new[] { "fit", "weighted", "linear" }
        from start in new[] { false, true }
        from reverse in new[] { false, true }
        from delta in new[] { 1d, 0.00001 }
        select new object[] { kind, start, reverse, delta };

    [Theory]
    [MemberData(nameof(EndpointCases))]
    public void SupportedEndpointsPropagateAndKeepRepresentation(string kind, bool start, bool reverse, double delta)
    {
        var points = new[] { new Point2(0, 0), new(1, 0), new(3, 1), new(4, 1) };
        var spline = kind == "fit" ? SplineEntity.FromFitPoints(EntityId.Create("spline"), points)
            : new SplineEntity(EntityId.Create("spline"), kind == "linear" ? 1 : 3, points,
                kind == "linear" ? new double[] { 0, 0, 1, 2, 3, 3 } : new double[] { 0, 0, 0, 0, 1, 1, 1, 1 },
                kind == "weighted" ? new double[] { 1, 2, 3, 1 } : null);
        var from = start ? points[0] : points[^1];
        var to = new Point2(from.X + delta, from.Y + delta);
        var endpoint = start ? "start" : "end";
        var keys = new[] { $"spline:{endpoint}", "point" };
        var constraint = new SketchConstraint("weld", SketchConstraintKind.Coincident, reverse ? keys.Reverse() : keys);
        var original = new DrawingDocument(new DrawingEntity[] { spline, new PointEntity(EntityId.Create("point"), from) },
            Array.Empty<SketchDimension>(), new[] { constraint });
        SketchGeometryDragService.TryApplyDrag(original, "point", from, to, false, out var next, out var status).Should().BeTrue(status);
        var edited = (SplineEntity)next.Entities[0];
        edited.Degree.Should().Be(spline.Degree);
        edited.Knots.Should().Equal(spline.Knots);
        edited.Weights.Should().Equal(spline.Weights);
        var current = kind == "fit" ? edited.FitPoints : edited.ControlPoints;
        current.Should().Equal(points.Select((p, i) => i == (start ? 0 : 3) ? to : p));
        if (kind == "fit")
        {
            var before = (start ? spline.StartTangentHandle : spline.EndTangentHandle)!.Value;
            var after = (start ? edited.StartTangentHandle : edited.EndTangentHandle)!.Value;
            after.X.Should().BeApproximately(before.X + delta, 1e-12);
            after.Y.Should().BeApproximately(before.Y + delta, 1e-12);
        }
        next.Constraints.Single().State.Should().Be(SketchConstraintState.Satisfied);
    }

    [Fact]
    public void UnclampedEndpointDragAndPeerMovementRefuseUnchanged()
    {
        var spline = new SplineEntity(EntityId.Create("spline"), 3,
            new[] { new Point2(0, 0), new(1, 0), new(3, 1), new(4, 1) }, new double[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        var from = spline.GetSamplePoints()[^1];
        var original = new DrawingDocument(new DrawingEntity[] { spline, new PointEntity(EntityId.Create("point"), from) },
            Array.Empty<SketchDimension>(), new[] { new SketchConstraint("weld", SketchConstraintKind.Coincident, new[] { "spline:end", "point" }) });
        foreach (var key in new[] { "spline|point|end|4|1", "point" })
        {
            SketchGeometryDragService.TryApplyDrag(original, key, from, new(from.X + 1, from.Y + 1), false,
                out var next, out _).Should().BeFalse();
            next.Should().BeSameAs(original);
        }
    }

    [Fact]
    public void EndpointPickOffsetDoesNotShiftTheWeld()
    {
        SketchGeometryDragService.TryApplyDrag(Fixture(false), "spline|point|end|4|1", new(4.1, 1.1), new(5.1, 2.1), false,
            out var next, out var status).Should().BeTrue(status);
        ((SplineEntity)next.Entities[0]).ControlPoints[^1].Should().Be(new Point2(5, 2));
        ((LineEntity)next.Entities[1]).Start.Should().Be(new Point2(5, 2));
    }

    [Fact]
    public void WeldedSplineDrivingSpanSurvivesSupportedTranslation()
    {
        var free = Fixture(false);
        var dimension = new SketchDimension("span", SketchDimensionKind.LinearDistance,
            new[] { "spline:start", "spline:end" }, Math.Sqrt(17), new(2, -1), true);
        var original = new DrawingDocument(free.Entities, new[] { dimension }, free.Constraints);
        SketchGeometryDragService.TryApplyDrag(original, "spline|point|end|4|1", new(4, 1), new(5, 2), false,
            out var next, out var status).Should().BeTrue(status);
        SketchDimensionSolverService.IsDimensionSatisfied(next, next.Dimensions.Single()).Should().BeTrue();
        next.Dimensions.Single().Value.Should().Be(Math.Sqrt(17));
        var spline = (SplineEntity)next.Entities[0];
        spline.ControlPoints.Should().Equal(((SplineEntity)original.Entities[0]).ControlPoints.Select(p => new Point2(p.X + 1, p.Y + 1)));
        ((LineEntity)next.Entities[1]).Start.Should().Be(new Point2(5, 2));
        next.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void ClampedEndpointCanBeConstrainedToOriginWithoutFlattening()
    {
        var original = Fixture(false);
        var constraint = new SketchConstraint("origin", SketchConstraintKind.Coincident, new[] { SketchReference.OriginKey, "spline:end" });
        var next = SketchConstraintService.ApplyConstraint(original, constraint);
        var spline = (SplineEntity)next.Entities[0];
        spline.ControlPoints[^1].Should().Be(new Point2(0, 0));
        spline.Degree.Should().Be(3);
        spline.Knots.Should().Equal(((SplineEntity)original.Entities[0]).Knots);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void EndpointWeldPropagatesInEitherDirection(bool dragSpline, bool reverse)
    {
        var original = Fixture(reverse);
        var key = dragSpline ? "spline|point|end|4|1" : "line|point|start|4|1";
        SketchGeometryDragService.TryApplyDrag(original, key, new(4, 1), new(5, 2), false,
            out var next, out var status).Should().BeTrue(status);
        var spline = (SplineEntity)next.Entities[0];
        spline.ControlPoints.Should().Equal(new Point2(0, 0), new(1, 0), new(3, 1), new(5, 2));
        spline.Degree.Should().Be(3);
        spline.Knots.Should().Equal(((SplineEntity)original.Entities[0]).Knots);
        ((LineEntity)next.Entities[1]).Start.Should().Be(new Point2(5, 2));
        ((LineEntity)next.Entities[1]).End.Should().Be(new Point2(8, 1));
        ((PointEntity)next.Entities[2]).Location.Should().Be(new Point2(5, 2));
        next.Constraints.Should().OnlyContain(c => c.State == SketchConstraintState.Satisfied);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FixedWeldRefusesAndRemovingFixRecovers(bool reverse)
    {
        var free = Fixture(reverse);
        var original = new DrawingDocument(free.Entities, free.Dimensions,
            free.Constraints.Append(new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { "point" })));
        SketchGeometryDragService.TryApplyDrag(original, "spline|point|end|4|1", new(4, 1), new(5, 2), false,
            out var next, out _).Should().BeFalse();
        next.Should().BeSameAs(original);
        SketchGeometryDragService.TryApplyDrag(free, "spline|point|end|4|1", new(4, 1), new(5, 2), false,
            out _, out var status).Should().BeTrue(status);
    }

    private static DrawingDocument Fixture(bool reverse)
    {
        var constraints = new[] {
            new SketchConstraint("join", SketchConstraintKind.Coincident, new[] { "spline:end", "line:start" }),
            new SketchConstraint("join-point", SketchConstraintKind.Coincident, new[] { "line:start", "point" }) };
        return new DrawingDocument(new DrawingEntity[] {
            new SplineEntity(EntityId.Create("spline"), 3,
                new[] { new Point2(0, 0), new(1, 0), new(3, 1), new(4, 1) },
                new double[] { 0, 0, 0, 0, 1, 1, 1, 1 }),
            new LineEntity(EntityId.Create("line"), new(4, 1), new(8, 1)),
            new PointEntity(EntityId.Create("point"), new(4, 1)) },
            Array.Empty<SketchDimension>(), reverse ? constraints.Reverse() : constraints);
    }
}

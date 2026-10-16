using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class DimensionedSplineDragTests
{
    [Fact]
    public void UnclampedEndpointsResolveCurveEndpointsRatherThanControlVertices()
    {
        var spline = new SplineEntity(EntityId.Create("spline"), 3, Cubic().ControlPoints,
            new double[] { 0, 1, 2, 3, 4, 5, 6, 7 });
        var document = new DrawingDocument(new DrawingEntity[] { spline });
        SketchReferenceResolver.TryGetPoint(document, "spline|point|start|0|0", out var start).Should().BeTrue();
        SketchReferenceResolver.TryGetPoint(document, "spline|point|end|4|1", out var end).Should().BeTrue();
        start.Should().Be(spline.GetSamplePoints()[0]);
        end.Should().Be(spline.GetSamplePoints()[^1]);
        start.Should().NotBe(spline.ControlPoints[0]);
        SketchReferenceResolver.TryGetPoint(document, "spline|point|tangent-start|.25|0", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("control-99")]
    [InlineData("fit-1")]
    [InlineData("tangent-unknown")]
    public void MissingOrUnsupportedGripDoesNotResolveStoredCoordinates(string label)
    {
        var spline = Cubic();
        var dimension = Dimension("control-0", label, 1);
        var original = new DrawingDocument(new DrawingEntity[] { spline }, new[] { dimension }, Array.Empty<SketchConstraint>());
        SketchReferenceResolver.TryGetPoint(original, dimension.ReferenceKeys[1], out _).Should().BeFalse();
        SketchDimensionSolverService.IsDimensionSatisfied(original, dimension).Should().BeFalse();
        SketchGeometryDragService.TryApplyDrag(original, "spline|point|control-1|1|0", new(1, 0), new(1, 1), false,
            out var next, out _).Should().BeFalse();
        next.Should().BeSameAs(original);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DrivingTangentHandlePreservesValueAndFixedSplineRefuses(bool fixedSpline)
    {
        var spline = Cubic();
        var dimension = Dimension("control-0", "tangent-start", 0.25);
        var original = new DrawingDocument(new DrawingEntity[] { spline }, new[] { dimension },
            fixedSpline ? new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { "spline" }) } : Array.Empty<SketchConstraint>());
        SketchGeometryDragService.TryApplyDrag(original, "spline|point|tangent-start|0.25|0", new(.25, 0), new(.25, .5), false,
            out var next, out var status).Should().Be(!fixedSpline, status);
        if (fixedSpline) next.Should().BeSameAs(original);
        else
        {
            var edited = (SplineEntity)next.Entities[0];
            edited.ControlPoints[1].X.Should().BeApproximately(edited.ControlPoints[0].X + 1, 1e-8);
            edited.ControlPoints[1].Y.Should().BeApproximately(edited.ControlPoints[0].Y, 1e-8);
            edited.Knots.Should().Equal(spline.Knots);
            edited.Degree.Should().Be(3);
            SketchDimensionSolverService.IsDimensionSatisfied(next, next.Dimensions[0]).Should().BeTrue();
        }
    }

    private static SplineEntity Cubic() => new(EntityId.Create("spline"), 3,
        new[] { new Point2(0, 0), new Point2(1, 0), new Point2(3, 1), new Point2(4, 1) }, new double[] { 0, 0, 0, 0, 1, 1, 1, 1 });
    private static SketchDimension Dimension(string first, string second, double value) => new("length", SketchDimensionKind.LinearDistance,
        new[] { $"spline|point|{first}|0|0", $"spline|point|{second}|1|0" }, value, new(.5, -1), true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexedSplineDimensionMeasuresLiveGeometryDuringDrag(bool fit)
    {
        var points = new[] { new Point2(0, 0), new Point2(1, 0), new Point2(3, 1), new Point2(4, 1) };
        var spline = fit ? SplineEntity.FromFitPoints(EntityId.Create("spline"), points)
            : new SplineEntity(EntityId.Create("spline"), 3, points, new double[] { 0, 0, 0, 0, 1, 1, 1, 1 });
        var prefix = fit ? "fit" : "control";
        var dimension = new SketchDimension("length", SketchDimensionKind.LinearDistance,
            new[] { $"spline|point|{prefix}-0|0|0", $"spline|point|{prefix}-1|1|0" }, 1, new(0.5, -1), true);
        var original = new DrawingDocument(new DrawingEntity[] { spline }, new[] { dimension }, Array.Empty<SketchConstraint>());
        SketchGeometryDragService.TryApplyDrag(original, $"spline|point|{prefix}-1|1|0", new(1, 0), new(1, 1), false,
            out var next, out var status).Should().BeTrue(status);
        var edited = (SplineEntity)next.Entities[0];
        var current = fit ? edited.FitPoints : edited.ControlPoints;
        Math.Sqrt(Math.Pow(current[1].X - current[0].X, 2) + Math.Pow(current[1].Y - current[0].Y, 2))
            .Should().BeApproximately(1, 1e-8);
        next.Dimensions[0].Value.Should().Be(1);
        SketchReferenceResolver.TryGetPoint(next, dimension.ReferenceKeys[0], out var resolved).Should().BeTrue();
        resolved.Should().Be(current[0]);
    }
}

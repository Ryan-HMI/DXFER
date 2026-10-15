using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using DXFER.Blazor.Interop;
using DXFER.CadIO;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SplineTangentDragTests
{
    [Theory]
    [InlineData(true, 0.4)]
    [InlineData(false, 0.4)]
    [InlineData(true, 0.5)]
    [InlineData(false, 0.5)]
    public void ControlSplineTangentDragMovesOnlyAdjacentControl(bool start, double dy)
    {
        var spline = Cubic();
        var document = new DrawingDocument(new DrawingEntity[] { spline });
        var from = start ? new Point2(0.25, 0) : new Point2(3.75, 1);
        var to = new Point2(from.X, from.Y + dy);
        var label = start ? "tangent-start" : "tangent-end";
        SketchGeometryDragService.TryApplyDrag(document, $"spline|point|{label}|{from.X}|{from.Y}",
            from, to, false, out var next, out var status).Should().BeTrue(status);
        var result = next.Entities.Single().Should().BeOfType<SplineEntity>().Subject;
        var expected = spline.ControlPoints.ToArray();
        var index = start ? 1 : 2;
        expected[index] = new Point2(expected[index].X, expected[index].Y + 4 * dy);
        for (var i = 0; i < expected.Length; i++)
        {
            if (i == index)
            {
                result.ControlPoints[i].X.Should().BeApproximately(expected[i].X, 1e-12);
                result.ControlPoints[i].Y.Should().BeApproximately(expected[i].Y, 1e-12);
            }
            else result.ControlPoints[i].Should().Be(spline.ControlPoints[i]);
        }
        result.Degree.Should().Be(3);
        result.Knots.Should().Equal(spline.Knots);
        result.Weights.Should().Equal(spline.Weights);
        result.FitPoints.Should().BeEmpty();
        result.StartTangentHandle.Should().BeNull();
        result.EndTangentHandle.Should().BeNull();
        result.GetSamplePoints().Should().NotEqual(spline.GetSamplePoints());
        spline.ControlPoints.Should().Equal(new Point2(0, 0), new Point2(1, 0), new Point2(3, 1), new Point2(4, 1));
        var reopened = DxfDocumentReader.Read(DxfDocumentWriter.Write(next)).Entities.OfType<SplineEntity>().Single();
        reopened.ControlPoints.Should().BeEquivalentTo(expected, options => options.WithStrictOrdering()
            .Using<double>(context => context.Subject.Should().BeApproximately(context.Expectation, 1e-12)).WhenTypeIs<double>());
        reopened.Degree.Should().Be(3);
        reopened.Knots.Should().Equal(spline.Knots);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void FixedSplineTangentRejectsWithoutChangingDocument(bool start, bool fit)
    {
        var spline = fit ? SplineEntity.FromFitPoints(EntityId.Create("spline"), Cubic().ControlPoints) : Cubic();
        var document = new DrawingDocument(new DrawingEntity[] { spline }, Array.Empty<SketchDimension>(),
            new[] { new SketchConstraint("fix", SketchConstraintKind.Fix, new[] { "spline" }) });
        SketchGeometryDragService.TryApplyDrag(document, $"spline|point|tangent-{(start ? "start" : "end")}|0.25|0",
            new Point2(0.25, 0), new Point2(0.25, 0.4), false, out var next, out var status).Should().BeFalse();
        next.Should().BeSameAs(document);
        status.Should().Contain("constrained");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void FitTangentPreservesAllFitPointsAndOppositeHandle(bool start)
    {
        var spline = SplineEntity.FromFitPoints(EntityId.Create("spline"), Cubic().ControlPoints,
            startTangentHandle: new Point2(0.25, 0), endTangentHandle: new Point2(3.75, 1));
        var from = start ? spline.StartTangentHandle!.Value : spline.EndTangentHandle!.Value;
        var to = new Point2(from.X, from.Y + 0.4);
        var document = new DrawingDocument(new DrawingEntity[] { spline });
        SketchGeometryDragService.TryApplyDrag(document, $"spline|point|tangent-{(start ? "start" : "end")}|0|0",
            from, to, false, out var next, out _).Should().BeTrue();
        var result = (SplineEntity)next.Entities.Single();
        result.FitPoints.Should().Equal(spline.FitPoints);
        result.ControlPoints.Should().Equal(spline.ControlPoints);
        result.StartTangentHandle.Should().Be(start ? to : spline.StartTangentHandle);
        result.EndTangentHandle.Should().Be(start ? spline.EndTangentHandle : to);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RoundAdjacentControlDragIsNotScaledLikeTheSquareHandle(bool start)
    {
        var spline = Cubic();
        var index = start ? 1 : 2;
        var from = spline.ControlPoints[index];
        var to = new Point2(from.X, from.Y + 0.4);
        var document = new DrawingDocument(new DrawingEntity[] { spline });
        SketchGeometryDragService.TryApplyDrag(document, $"spline|point|control-{index}|0|0", from, to,
            false, out var next, out _).Should().BeTrue();
        var expected = spline.ControlPoints.ToArray();
        expected[index] = to;
        ((SplineEntity)next.Entities.Single()).ControlPoints.Should().Equal(expected);
    }

    [Fact]
    public void RationalMultiSpanSplineRetainsDegreeKnotsWeightsAndUnrelatedEntities()
    {
        var spline = new SplineEntity(EntityId.Create("spline"), 3,
            new[] { new Point2(0, 0), new Point2(1, 0), new Point2(2, 1), new Point2(3, 1), new Point2(4, 0) },
            new double[] { 2, 2, 2, 2, 3, 4, 4, 4, 4 }, new double[] { 1, 2, 3, 2, 1 }, isConstruction: true);
        var other = new PointEntity(EntityId.Create("other"), new Point2(9, 9));
        var document = new DrawingDocument(new DrawingEntity[] { spline, other });
        SketchGeometryDragService.TryApplyDrag(document, "spline|point|tangent-start|0.25|0",
            new Point2(0.25, 0), new Point2(0.25, 0.4), false, out var next, out _).Should().BeTrue();
        var result = (SplineEntity)next.Entities[0];
        result.ControlPoints[1].Should().Be(new Point2(1, 1.6));
        result.ControlPoints.Skip(2).Should().Equal(spline.ControlPoints.Skip(2));
        result.Degree.Should().Be(spline.Degree);
        result.Knots.Should().Equal(spline.Knots);
        result.Weights.Should().Equal(spline.Weights);
        result.IsConstruction.Should().BeTrue();
        result.GetSamplePoints().Should().HaveCount(33);
        result.GetSamplePoints()[16].X.Should().BeApproximately(2, 1e-12);
        result.GetSamplePoints()[16].Y.Should().BeApproximately(1.12, 1e-12);
        next.Entities[1].Should().BeSameAs(other);
        var canvas = CanvasDocumentDto.FromDocument(next).Entities[0];
        canvas.SplineDegree.Should().Be(3);
        canvas.SplineKnots.Should().Equal(result.Knots);
        canvas.SplineWeights.Should().Equal(result.Weights);
        canvas.StartTangentControlPointIndex.Should().Be(1);
        canvas.EndTangentControlPointIndex.Should().Be(3);
        canvas.Points.Select(point => new Point2(point.X, point.Y)).Should().Equal(result.GetSamplePoints());
    }

    [Theory]
    [InlineData("unclamped")]
    [InlineData("degree-one")]
    [InlineData("degenerate")]
    [InlineData("nonpositive-weight")]
    public void UnsupportedControlTangentIsNotAdvertisedAndRefusesSafely(string kind)
    {
        var controls = Cubic().ControlPoints.ToArray();
        if (kind == "degenerate") controls[1] = controls[0];
        var spline = new SplineEntity(EntityId.Create("spline"), kind == "degree-one" ? 1 : 3, controls,
            kind == "unclamped" ? new double[] { -3, -2, -1, 0, 1, 2, 3, 4 } : Array.Empty<double>(),
            kind == "nonpositive-weight" ? new double[] { 1, -1, 1, 1 } : null);
        var document = new DrawingDocument(new DrawingEntity[] { spline });
        spline.GetTangentControlPointIndex(true).Should().BeNull();
        SketchGeometryDragService.TryApplyDrag(document, "spline|point|tangent-start|0.25|0",
            new Point2(0.25, 0), new Point2(0.25, 0.4), false, out var next, out var status).Should().BeFalse();
        next.Should().BeSameAs(document);
        status.Should().Contain("no supported control tangent");
    }

    [Fact]
    public void CollapsingTheTangentHandleOntoTheEndpointRefusesSafely()
    {
        var document = new DrawingDocument(new DrawingEntity[] { Cubic() });
        SketchGeometryDragService.TryApplyDrag(document, "spline|point|tangent-start|0.25|0",
            new Point2(0.25, 0), new Point2(0, 0), false, out var next, out _).Should().BeFalse();
        next.Should().BeSameAs(document);
    }

    [Fact]
    public void StaleControlSplineTangentMetadataDoesNotChooseTheEditablePoint()
    {
        var original = Cubic();
        var spline = new SplineEntity(original.Id, original.Degree, original.ControlPoints, original.Knots,
            startTangentHandle: new Point2(90, 90), endTangentHandle: new Point2(-90, -90));
        var document = new DrawingDocument(new DrawingEntity[] { spline });
        SketchGeometryDragService.TryApplyDrag(document, "spline|point|tangent-start|0.25|0",
            new Point2(0.25, 0), new Point2(0.25, 0.4), false, out var next, out _).Should().BeTrue();
        var result = (SplineEntity)next.Entities[0];
        result.ControlPoints[1].Should().Be(new Point2(1, 1.6));
        result.StartTangentHandle.Should().Be(spline.StartTangentHandle);
        result.EndTangentHandle.Should().Be(spline.EndTangentHandle);
        var expected = new SplineEntity(spline.Id, 3, result.ControlPoints, result.Knots);
        result.GetSamplePoints().Should().Equal(expected.GetSamplePoints());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void QuadraticTangentMovesSharedMiddleControlButNeitherEndpoint(bool start)
    {
        var spline = new SplineEntity(EntityId.Create("spline"), 2,
            new[] { new Point2(0, 0), new Point2(2, 1), new Point2(4, 0) }, Array.Empty<double>());
        spline.GetTangentControlPointIndex(true).Should().Be(1);
        spline.GetTangentControlPointIndex(false).Should().Be(1);
        var from = start ? new Point2(0.5, 0.25) : new Point2(3.5, 0.25);
        var to = new Point2(from.X, 0.75);
        SketchGeometryDragService.TryApplyDrag(new DrawingDocument(new DrawingEntity[] { spline }),
            $"spline|point|tangent-{(start ? "start" : "end")}|0|0", from, to, false,
            out var next, out _).Should().BeTrue();
        ((SplineEntity)next.Entities[0]).ControlPoints.Should().Equal(new Point2(0, 0), new Point2(2, 3), new Point2(4, 0));
    }

    private static SplineEntity Cubic() => new(EntityId.Create("spline"), 3,
        new[] { new Point2(0, 0), new Point2(1, 0), new Point2(3, 1), new Point2(4, 1) },
        new double[] { 0, 0, 0, 0, 1, 1, 1, 1 });
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Operations;

public sealed class MeasurementServiceTests
{
    [Fact]
    public void MeasuresPointToPointDeltaAndDistance()
    {
        var measurement = MeasurementService.Measure(new Point2(2, 3), new Point2(8, 11));

        measurement.DeltaX.Should().Be(6);
        measurement.DeltaY.Should().Be(8);
        measurement.Distance.Should().Be(10);
    }

    [Fact]
    public void MeasuresFullPolylinePathLengthInsteadOfFirstSegmentOnly()
    {
        var polyline = new PolylineEntity(
            EntityId.Create("path"),
            new[] { new Point2(0, 0), new Point2(3, 0), new Point2(3, 4) });

        MeasurementService.TryMeasureEntity(polyline, out var measurement).Should().BeTrue();

        measurement.DeltaX.Should().Be(3);
        measurement.DeltaY.Should().Be(4);
        measurement.Distance.Should().Be(7);
    }

    [Fact]
    public void MeasuresArcLengthFromPositiveSweep()
    {
        var arc = new ArcEntity(EntityId.Create("arc"), new Point2(0, 0), 5, 0, 90);

        MeasurementService.TryMeasureEntity(arc, out var measurement).Should().BeTrue();

        measurement.DeltaX.Should().BeApproximately(-5, 0.000001);
        measurement.DeltaY.Should().BeApproximately(5, 0.000001);
        measurement.Distance.Should().BeApproximately(Math.PI * 5 / 2, 0.000001);
    }

    [Fact]
    public void MeasuresCircularEllipseArcFromSampledCurve()
    {
        var ellipseArc = new EllipseEntity(
            EntityId.Create("ellipse"),
            new Point2(0, 0),
            new Point2(5, 0),
            1,
            0,
            90);

        MeasurementService.TryMeasureEntity(ellipseArc, out var measurement).Should().BeTrue();

        measurement.DeltaX.Should().BeApproximately(-5, 0.000001);
        measurement.DeltaY.Should().BeApproximately(5, 0.000001);
        measurement.Distance.Should().BeApproximately(Math.PI * 5 / 2, 0.01);
    }

    [Fact]
    public void MeasuresSplineFromSampledCurve()
    {
        var spline = SplineEntity.FromFitPoints(
            EntityId.Create("spline"),
            new[] { new Point2(0, 0), new Point2(3, 4) });

        MeasurementService.TryMeasureEntity(spline, out var measurement).Should().BeTrue();

        measurement.DeltaX.Should().Be(3);
        measurement.DeltaY.Should().Be(4);
        measurement.Distance.Should().Be(5);
    }

    [Fact]
    public void MeasuresClosedPolygonPerimeterWithBoundsDeltas()
    {
        var square = new PolygonEntity(
            EntityId.Create("square"),
            new Point2(0, 0),
            Math.Sqrt(2),
            45,
            4);

        MeasurementService.TryMeasureEntity(square, out var measurement).Should().BeTrue();

        measurement.DeltaX.Should().BeApproximately(2, 0.000001);
        measurement.DeltaY.Should().BeApproximately(2, 0.000001);
        measurement.Distance.Should().BeApproximately(8, 0.000001);
    }

    [Fact]
    public void MeasuresPointEntityCoordinatesWithZeroDistance()
    {
        var point = new PointEntity(EntityId.Create("marker"), new Point2(2, 3));

        MeasurementService.TryMeasureEntity(point, out var measurement).Should().BeTrue();

        measurement.DeltaX.Should().Be(2);
        measurement.DeltaY.Should().Be(3);
        measurement.Distance.Should().Be(0);
    }

    [Fact]
    public void MeasuresEntityBoundsAsWidthHeightAndDiagonal()
    {
        var entities = new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(-2, 1), new Point2(2, 3)),
            new CircleEntity(EntityId.Create("hole"), new Point2(5, 1), 2)
        };

        var measurement = MeasurementService.MeasureBounds(entities);

        measurement.DeltaX.Should().Be(9);
        measurement.DeltaY.Should().Be(4);
        measurement.Distance.Should().BeApproximately(Math.Sqrt(97), 0.000001);
    }

    [Fact]
    public void MeasuresShortestDistanceFromPointToLineSegment()
    {
        var point = new PointEntity(EntityId.Create("marker"), new Point2(4, 3));
        var line = new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0));

        MeasurementService.TryMeasureShortestDistance(point, line, out var measurement).Should().BeTrue();

        measurement.DeltaX.Should().Be(0);
        measurement.DeltaY.Should().Be(-3);
        measurement.Distance.Should().Be(3);
    }

    [Fact]
    public void MeasuresZeroShortestDistanceForCrossingLineSegments()
    {
        var horizontal = new LineEntity(EntityId.Create("horizontal"), new Point2(0, 0), new Point2(10, 0));
        var vertical = new LineEntity(EntityId.Create("vertical"), new Point2(5, -2), new Point2(5, 2));

        MeasurementService.TryMeasureShortestDistance(horizontal, vertical, out var measurement).Should().BeTrue();

        measurement.DeltaX.Should().BeApproximately(0, 0.000001);
        measurement.DeltaY.Should().BeApproximately(0, 0.000001);
        measurement.Distance.Should().BeApproximately(0, 0.000001);
    }

    [Fact]
    public void MeasuresShortestDistanceBetweenParallelLineSegments()
    {
        var baseLine = new LineEntity(EntityId.Create("base"), new Point2(0, 0), new Point2(10, 0));
        var offsetLine = new LineEntity(EntityId.Create("offset"), new Point2(2, 4), new Point2(8, 4));

        MeasurementService.TryMeasureShortestDistance(baseLine, offsetLine, out var measurement).Should().BeTrue();

        measurement.DeltaX.Should().BeApproximately(0, 0.000001);
        measurement.DeltaY.Should().BeApproximately(4, 0.000001);
        measurement.Distance.Should().BeApproximately(4, 0.000001);
    }

    [Fact]
    public void MeasuresShortestDistanceBetweenCirclePerimeters()
    {
        var first = new CircleEntity(EntityId.Create("first"), new Point2(0, 0), 2);
        var second = new CircleEntity(EntityId.Create("second"), new Point2(7, 0), 1);

        MeasurementService.TryMeasureShortestDistance(first, second, out var measurement).Should().BeTrue();

        measurement.DeltaX.Should().BeApproximately(4, 0.000001);
        measurement.DeltaY.Should().BeApproximately(0, 0.000001);
        measurement.Distance.Should().BeApproximately(4, 0.000001);
    }

    [Fact]
    public void MeasuresShortestDistanceBetweenLineAndCircle()
    {
        var line = new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0));
        var circle = new CircleEntity(EntityId.Create("circle"), new Point2(5, 4), 1);

        MeasurementService.TryMeasureShortestDistance(line, circle, out var measurement).Should().BeTrue();

        measurement.DeltaX.Should().BeApproximately(0, 0.000001);
        measurement.DeltaY.Should().BeApproximately(3, 0.000001);
        measurement.Distance.Should().BeApproximately(3, 0.000001);
    }

    [Fact]
    public void MeasuresShortestDistanceAgainstSampledSplineGeometry()
    {
        var line = new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(10, 0));
        var spline = SplineEntity.FromFitPoints(
            EntityId.Create("spline"),
            new[] { new Point2(2, 3), new Point2(8, 3) });

        MeasurementService.TryMeasureShortestDistance(line, spline, out var measurement).Should().BeTrue();

        measurement.DeltaX.Should().BeApproximately(0, 0.000001);
        measurement.DeltaY.Should().BeApproximately(3, 0.000001);
        measurement.Distance.Should().BeApproximately(3, 0.000001);
    }
}

using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchDimensionSolverServiceTests
{
    [Fact]
    public void AppliesPointToPointLinearDistanceByMovingLastPoint()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(
                EntityId.Create("edge"),
                new Point2(0, 0),
                new Point2(3, 4),
                IsConstruction: true)
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension(
                "linear",
                SketchDimensionKind.LinearDistance,
                10,
                "edge:start",
                "edge:end"));

        var edge = solved.Entities[0].Should().BeOfType<LineEntity>().Subject;
        edge.Start.Should().Be(new Point2(0, 0));
        Distance(edge.Start, edge.End).Should().BeApproximately(10, 0.0001);
        edge.IsConstruction.Should().BeTrue();
        solved.Dimensions.Should().ContainSingle(dimension => dimension.Id == "linear");
    }

    [Fact]
    public void AppliesHorizontalDistanceByChangingLastPointX()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(2, 5), new Point2(4, 9))
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension(
                "horizontal",
                SketchDimensionKind.HorizontalDistance,
                8,
                "edge:start",
                "edge:end"));

        var edge = solved.Entities[0].Should().BeOfType<LineEntity>().Subject;
        edge.End.Should().Be(new Point2(10, 9));
    }

    [Fact]
    public void AppliesVerticalDistanceByChangingLastPointY()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("edge"), new Point2(2, 5), new Point2(9, 7))
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension(
                "vertical",
                SketchDimensionKind.VerticalDistance,
                6,
                "edge:start",
                "edge:end"));

        var edge = solved.Entities[0].Should().BeOfType<LineEntity>().Subject;
        edge.End.Should().Be(new Point2(9, 11));
    }

    [Fact]
    public void AppliesPointToLinePerpendicularDistance()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("base"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("probe"), new Point2(3, 2), new Point2(6, 2))
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension(
                "offset",
                SketchDimensionKind.PointToLineDistance,
                5,
                "base",
                "probe:start"));

        var probe = solved.Entities[1].Should().BeOfType<LineEntity>().Subject;
        probe.Start.Should().Be(new Point2(3, 5));
        probe.End.Should().Be(new Point2(6, 2));
    }

    [Fact]
    public void AppliesCircleRadius()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new CircleEntity(EntityId.Create("circle"), new Point2(2, 3), 4)
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension("radius", SketchDimensionKind.Radius, 12, "circle"));

        solved.Entities[0].Should().BeOfType<CircleEntity>()
            .Which.Radius.Should().Be(12);
    }

    [Fact]
    public void AppliesArcDiameter()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc"), new Point2(2, 3), 4, 10, 80)
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension("diameter", SketchDimensionKind.Diameter, 18, "arc"));

        var arc = solved.Entities[0].Should().BeOfType<ArcEntity>().Subject;
        arc.Radius.Should().Be(9);
        arc.StartAngleDegrees.Should().Be(10);
        arc.EndAngleDegrees.Should().Be(80);
    }

    [Fact]
    public void AppliesAngleBetweenTwoLinesByRotatingSecondLineEnd()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("anchor"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("driven"), new Point2(2, 2), new Point2(2, 7))
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension("angle", SketchDimensionKind.Angle, 45, "anchor", "driven"));

        var driven = solved.Entities[1].Should().BeOfType<LineEntity>().Subject;
        driven.Start.Should().Be(new Point2(2, 2));
        driven.End.X.Should().BeApproximately(2 + Math.Sqrt(12.5), 0.0001);
        driven.End.Y.Should().BeApproximately(2 + Math.Sqrt(12.5), 0.0001);
    }

    [Fact]
    public void AppliesAngleWithoutFlippingDrivenLineAcrossReference()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("anchor"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("driven"), new Point2(2, 2), new Point2(2, -3))
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension("angle", SketchDimensionKind.Angle, 45, "anchor", "driven"));

        var driven = solved.Entities[1].Should().BeOfType<LineEntity>().Subject;
        driven.Start.Should().Be(new Point2(2, 2));
        driven.End.X.Should().BeApproximately(2 + Math.Sqrt(12.5), 0.0001);
        driven.End.Y.Should().BeApproximately(2 - Math.Sqrt(12.5), 0.0001);
    }

    [Fact]
    public void UpdatesExistingDimensionAndPreservesConstraints()
    {
        var existing = new SketchDimension(
            "linear",
            SketchDimensionKind.LinearDistance,
            new[] { "edge:start", "edge:end" },
            4,
            isDriving: true);
        var constraint = new SketchConstraint(
            "horizontal",
            SketchConstraintKind.Horizontal,
            new[] { "edge" },
            SketchConstraintState.Satisfied);
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(4, 0))
            },
            new[] { existing },
            new[] { constraint });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension(
                "linear",
                SketchDimensionKind.LinearDistance,
                9,
                "edge:start",
                "edge:end"));

        solved.Dimensions.Should().ContainSingle()
            .Which.Value.Should().Be(9);
        solved.Constraints.Should().ContainSingle()
            .Which.Should().BeSameAs(constraint);
    }

    [Fact]
    public void AppliesLinearDistanceBetweenPointEntities()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PointEntity(EntityId.Create("anchor"), new Point2(0, 0)),
            new PointEntity(EntityId.Create("driven"), new Point2(3, 4))
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension("distance", SketchDimensionKind.LinearDistance, 10, "anchor", "driven"));

        solved.Entities.OfType<PointEntity>()
            .Single(point => point.Id.Value == "driven")
            .Location.Should().Be(new Point2(6, 8));
    }

    [Fact]
    public void AppliesPolylineSegmentLinearDistanceByMovingSegmentEndVertex()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolylineEntity(
                EntityId.Create("poly"),
                new[] { new Point2(0, 0), new Point2(3, 0), new Point2(3, 4) },
                isConstruction: true)
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension(
                "segment",
                SketchDimensionKind.LinearDistance,
                8,
                "poly|segment|1:start",
                "poly|segment|1:end"));

        var polyline = solved.Entities[0].Should().BeOfType<PolylineEntity>().Subject;
        polyline.Vertices.Should().Equal(
            new Point2(0, 0),
            new Point2(3, 0),
            new Point2(3, 8));
        polyline.IsConstruction.Should().BeTrue();
    }

    [Fact]
    public void FixedLastReferenceForcesLinearDimensionToMoveFirstReference()
    {
        var fix = new SketchConstraint(
            "fix-end",
            SketchConstraintKind.Fix,
            new[] { "edge:end" },
            SketchConstraintState.Satisfied);
        var document = new DrawingDocument(
            new DrawingEntity[]
            {
                new LineEntity(EntityId.Create("edge"), new Point2(0, 0), new Point2(4, 0))
            },
            Array.Empty<SketchDimension>(),
            new[] { fix });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension(
                "linear",
                SketchDimensionKind.LinearDistance,
                10,
                "edge:start",
                "edge:end"));

        var edge = solved.Entities[0].Should().BeOfType<LineEntity>().Subject;
        edge.End.Should().Be(new Point2(4, 0));
        edge.Start.Should().Be(new Point2(-6, 0));
    }

    private static SketchDimension DrivingDimension(
        string id,
        SketchDimensionKind kind,
        double value,
        params string[] referenceKeys) =>
        new(id, kind, referenceKeys, value, isDriving: true);

    private static double Distance(Point2 first, Point2 second)
    {
        var deltaX = second.X - first.X;
        var deltaY = second.Y - first.Y;
        return Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
    }
}

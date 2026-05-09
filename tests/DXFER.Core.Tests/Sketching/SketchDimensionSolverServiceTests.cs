using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Sketching;
using DXFER.Blazor.Sketching;
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
    public void AppliesParallelLineToLineDistanceByMovingSecondLine()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("base"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("offset"), new Point2(2, 2), new Point2(8, 2))
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension(
                "offset",
                SketchDimensionKind.PointToLineDistance,
                5,
                "base",
                "offset"));

        var offset = solved.Entities[1].Should().BeOfType<LineEntity>().Subject;
        offset.Start.Should().Be(new Point2(2, 5));
        offset.End.Should().Be(new Point2(8, 5));
    }

    [Fact]
    public void AppliesParallelPolylineSegmentToLineDistanceByMovingSecondSegment()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new LineEntity(EntityId.Create("base"), new Point2(0, 0), new Point2(10, 0)),
            new PolylineEntity(
                EntityId.Create("offset"),
                new[] { new Point2(2, 2), new Point2(8, 2), new Point2(8, 4) })
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension(
                "offset",
                SketchDimensionKind.PointToLineDistance,
                5,
                "base",
                "offset|segment|0"));

        var offset = solved.Entities[1].Should().BeOfType<PolylineEntity>().Subject;
        offset.Vertices[0].Should().Be(new Point2(2, 5));
        offset.Vertices[1].Should().Be(new Point2(8, 5));
        offset.Vertices[2].Should().Be(new Point2(8, 4));
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
    public void AppliesPolygonSideCountDimension()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolygonEntity(EntityId.Create("poly"), new Point2(2, 3), 4, 10, 6)
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension("sides", SketchDimensionKind.Count, 9, "poly"));

        solved.Entities[0].Should().BeOfType<PolygonEntity>()
            .Which.SideCount.Should().Be(9);
        solved.Dimensions.Should().ContainSingle()
            .Which.Value.Should().Be(9);
    }

    [Fact]
    public void AppliesPolygonRadiusDimension()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new PolygonEntity(EntityId.Create("poly"), new Point2(2, 3), 4, 10, 6)
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension("radius", SketchDimensionKind.Radius, 12, "poly"));

        var polygon = solved.Entities[0].Should().BeOfType<PolygonEntity>().Subject;
        polygon.Radius.Should().Be(12);
        polygon.SideCount.Should().Be(6);
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
    public void AppliesArcSweepAngle()
    {
        var document = new DrawingDocument(new DrawingEntity[]
        {
            new ArcEntity(EntityId.Create("arc"), new Point2(2, 3), 4, 15, 75)
        });

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension("sweep", SketchDimensionKind.Angle, 120, "arc"));

        var arc = solved.Entities[0].Should().BeOfType<ArcEntity>().Subject;
        arc.StartAngleDegrees.Should().Be(15);
        arc.EndAngleDegrees.Should().Be(135);
        arc.Radius.Should().Be(4);
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
            .Which.Should().BeEquivalentTo(constraint);
    }

    [Fact]
    public void UpdatingRectangleWidthPreservesCoincidentAndAxisConstraints()
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(10, 5) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var document = new DrawingDocument(entities, Array.Empty<SketchDimension>(), constraints);

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension(
                "top-width",
                SketchDimensionKind.LinearDistance,
                11,
                "rect-3:start",
                "rect-3:end"));

        solved.Entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(-1, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(10, 0), new Point2(10, 5)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(10, 5), new Point2(-1, 5)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(-1, 5), new Point2(-1, 0)));
        solved.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void UpdatingRectangleDimensionsAfterDragPreservesExistingDrivingDimensions()
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(10, 5) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var document = new DrawingDocument(entities, Array.Empty<SketchDimension>(), constraints);

        SketchGeometryDragService.TryApplyDrag(
                document,
                "rect-4|point|mid|0|2.5",
                new Point2(0, 2.5),
                new Point2(-3, 2.5),
                false,
                out var dragged,
                out _)
            .Should().BeTrue();

        var widthDimension = DrivingDimension(
            "width",
            SketchDimensionKind.LinearDistance,
            20,
            "rect-3:start",
            "rect-3:end");
        var widthSolved = SketchDimensionSolverService.ApplyDimension(dragged, widthDimension);
        var heightDimension = DrivingDimension(
            "height",
            SketchDimensionKind.LinearDistance,
            10,
            "rect-4:start",
            "rect-4:end");

        var solved = SketchDimensionSolverService.ApplyDimension(widthSolved, heightDimension);

        solved.Dimensions.Should().HaveCount(2);
        solved.Dimensions.Should().OnlyContain(dimension => SketchDimensionSolverService.IsDimensionSatisfied(solved, dimension));
        solved.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
        solved.Entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(-10, -5), new Point2(10, -5)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(10, -5), new Point2(10, 5)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(10, 5), new Point2(-10, 5)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(-10, 5), new Point2(-10, -5)));
    }

    [Fact]
    public void EditingExistingRectangleLineDimensionPreservesRectangleConstraints()
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(8.333333333333334, 4.166666666666667) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var existingDimension = DrivingDimension(
            "width",
            SketchDimensionKind.LinearDistance,
            8.333333333333334,
            "rect-1:start",
            "rect-1:end");
        var document = new DrawingDocument(entities, new[] { existingDimension }, constraints);

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension(
                "width",
                SketchDimensionKind.LinearDistance,
                20,
                "rect-1:start",
                "rect-1:end"));

        solved.Entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(0, 0), new Point2(20, 0)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(20, 0), new Point2(20, 4.166666666666667)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(20, 4.166666666666667), new Point2(0, 4.166666666666667)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(0, 4.166666666666667), new Point2(0, 0)));
        solved.Dimensions.Should().ContainSingle().Which
            .Should().Match<SketchDimension>(dimension =>
                dimension.Value == 20
                && SketchDimensionSolverService.IsDimensionSatisfied(solved, dimension));
        solved.Constraints.Should().OnlyContain(constraint => constraint.State == SketchConstraintState.Satisfied);
    }

    [Fact]
    public void AngleDimensionBetweenAlreadyConstrainedRectangleSidesIsUnsatisfied()
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(10, 5) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var document = new DrawingDocument(entities, Array.Empty<SketchDimension>(), constraints);

        var solved = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension(
                "angle",
                SketchDimensionKind.Angle,
                90,
                "rect-4",
                "rect-1"));

        var angle = solved.Dimensions.Should().ContainSingle().Subject;
        SketchDimensionSolverService.GetDimensionState(solved, angle)
            .Should().Be(SketchConstraintState.Unsatisfied);
        SketchDimensionSolverService.IsDimensionSatisfied(solved, angle).Should().BeFalse();
    }

    [Fact]
    public void DiagonalDistanceDimensionAcrossWidthHeightConstrainedRectangleIsUnsatisfied()
    {
        var sequence = 0;
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(30, 10) },
            prefix => EntityId.Create($"{prefix}-{++sequence}"),
            isConstruction: false);
        var constraints = SketchCreationConstraintFactory.CreateConstraintsForTool(
            "twopointrectangle",
            entities,
            kind => $"constraint-{kind}-{Guid.NewGuid():N}");
        var document = new DrawingDocument(entities, Array.Empty<SketchDimension>(), constraints);

        var withWidth = SketchDimensionSolverService.ApplyDimension(
            document,
            DrivingDimension(
                "width",
                SketchDimensionKind.LinearDistance,
                30,
                "rect-3:start",
                "rect-3:end"));
        var withHeight = SketchDimensionSolverService.ApplyDimension(
            withWidth,
            DrivingDimension(
                "height",
                SketchDimensionKind.LinearDistance,
                10,
                "rect-4:start",
                "rect-4:end"));

        var solved = SketchDimensionSolverService.ApplyDimension(
            withHeight,
            DrivingDimension(
                "diagonal",
                SketchDimensionKind.LinearDistance,
                Math.Sqrt(30 * 30 + 10 * 10),
                "rect-3:end",
                "rect-2:start"));

        var dimensions = solved.Dimensions.ToDictionary(dimension => dimension.Id);
        SketchDimensionSolverService.GetDimensionState(solved, dimensions["width"])
            .Should().Be(SketchConstraintState.Satisfied);
        SketchDimensionSolverService.GetDimensionState(solved, dimensions["height"])
            .Should().Be(SketchConstraintState.Satisfied);
        SketchDimensionSolverService.GetDimensionState(solved, dimensions["diagonal"])
            .Should().Be(SketchConstraintState.Unsatisfied);
        SketchDimensionSolverService.IsDimensionSatisfied(solved, dimensions["diagonal"]).Should().BeFalse();
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

    [Fact]
    public void TreatsKeyedEllipseAxisDimensionsAsSatisfied()
    {
        var sequence = 0;
        var ellipse = new EllipseEntity(EntityId.Create("ellipse-a"), new Point2(0, 0), new Point2(10, 0), 0.5);
        var dimensions = SketchCreationDimensionFactory.CreateDimensionsForTool(
            "ellipse",
            new DrawingEntity[] { ellipse },
            new Dictionary<string, double> { ["major"] = 20, ["minor"] = 10 },
            () => $"dim-{++sequence}");
        var document = new DrawingDocument(new DrawingEntity[] { ellipse }, dimensions, Array.Empty<SketchConstraint>());

        dimensions.Should().HaveCount(2);
        dimensions.Should().OnlyContain(dimension =>
            SketchDimensionSolverService.GetDimensionState(document, dimension) == SketchConstraintState.Satisfied);
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

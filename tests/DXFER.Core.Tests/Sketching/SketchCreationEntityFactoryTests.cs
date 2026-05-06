using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchCreationEntityFactoryTests
{
    private int _sequence;

    [Fact]
    public void CreatesLineGeometryAtTypedLength()
    {
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "line",
            new[] { new Point2(0, 0), new Point2(3, 4) },
            CreateEntityId,
            isConstruction: false,
            new Dictionary<string, double> { ["length"] = 10 });

        entities.Should().ContainSingle().Which.Should().BeOfType<LineEntity>()
            .Which.Should().Be(new LineEntity(EntityId.Create("line-1"), new Point2(0, 0), new Point2(6, 8)));
    }

    [Fact]
    public void CreatesTwoPointRectangleGeometryAtTypedWidthAndHeight()
    {
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "twopointrectangle",
            new[] { new Point2(0, 0), new Point2(2, 1) },
            CreateEntityId,
            isConstruction: false,
            new Dictionary<string, double> { ["width"] = 10, ["height"] = 5 });

        entities.Should().Equal(
            new LineEntity(EntityId.Create("rect-1"), new Point2(0, 0), new Point2(10, 0)),
            new LineEntity(EntityId.Create("rect-2"), new Point2(10, 0), new Point2(10, 5)),
            new LineEntity(EntityId.Create("rect-3"), new Point2(10, 5), new Point2(0, 5)),
            new LineEntity(EntityId.Create("rect-4"), new Point2(0, 5), new Point2(0, 0)));
    }

    [Fact]
    public void CreatesEllipseEntityFromCenterMajorAndMinorPoints()
    {
        var entities = Create("ellipse", new Point2(0, 0), new Point2(4, 0), new Point2(0, 2));

        var ellipse = entities.Should().ContainSingle().Subject.Should().BeOfType<EllipseEntity>().Subject;
        ellipse.Center.Should().Be(new Point2(0, 0));
        ellipse.MajorAxisEndPoint.Should().Be(new Point2(4, 0));
        ellipse.MinorRadiusRatio.Should().BeApproximately(0.5, 0.000001);
        ellipse.EndParameterDegrees.Should().Be(360);
    }

    [Fact]
    public void CreatesEllipseGeometryFromTypedAxisDiameters()
    {
        var entities = SketchCreationEntityFactory.CreateEntitiesForTool(
            "ellipse",
            new[] { new Point2(0, 0), new Point2(4, 0), new Point2(0, 2) },
            CreateEntityId,
            isConstruction: false,
            new Dictionary<string, double> { ["major"] = 10, ["minor"] = 6 });

        var ellipse = entities.Should().ContainSingle().Subject.Should().BeOfType<EllipseEntity>().Subject;
        ellipse.MajorAxisEndPoint.Should().Be(new Point2(5, 0));
        ellipse.MinorRadiusRatio.Should().BeApproximately(0.6, 0.000001);
    }

    [Fact]
    public void CreatesEllipticalArcEntityWithEndParameter()
    {
        var entities = Create("ellipticalarc", new Point2(0, 0), new Point2(4, 0), new Point2(0, 2), new Point2(0, 2));

        var ellipse = entities.Should().ContainSingle().Subject.Should().BeOfType<EllipseEntity>().Subject;
        ellipse.StartParameterDegrees.Should().Be(0);
        ellipse.EndParameterDegrees.Should().BeApproximately(90, 0.000001);
    }

    [Fact]
    public void CreatesInscribedAndCircumscribedParametricPolygons()
    {
        var inscribed = CreatePolygon("inscribedpolygon", 8);
        var circumscribed = CreatePolygon("circumscribedpolygon", 6);

        var inscribedPolygon = inscribed.Should().ContainSingle().Subject.Should().BeOfType<PolygonEntity>().Subject;
        var circumscribedPolygon = circumscribed.Should().ContainSingle().Subject.Should().BeOfType<PolygonEntity>().Subject;

        inscribedPolygon.Kind.Should().Be("polygon");
        inscribedPolygon.SideCount.Should().Be(8);
        inscribedPolygon.Circumscribed.Should().BeFalse();
        inscribedPolygon.Center.Should().Be(new Point2(0, 0));
        inscribedPolygon.Radius.Should().BeApproximately(10, 0.000001);
        inscribedPolygon.GetVertices()[0].Should().Be(new Point2(10, 0));

        circumscribedPolygon.SideCount.Should().Be(6);
        circumscribedPolygon.Circumscribed.Should().BeTrue();
        Distance(new Point2(0, 0), circumscribedPolygon.GetVertices()[0])
            .Should().BeApproximately(10 / Math.Cos(Math.PI / 6), 0.000001);
    }

    [Fact]
    public void CreatesConicSplineBezierSplineAndSlotGeometry()
    {
        Create("conic", new Point2(0, 0), new Point2(2, 3), new Point2(4, 0))
            .Should().ContainSingle().Which.Should().BeOfType<SplineEntity>().Which.Degree.Should().Be(2);

        Create("bezier", new Point2(0, 0), new Point2(1, 2), new Point2(3, 2), new Point2(4, 0))
            .Should().ContainSingle().Which.Should().BeOfType<SplineEntity>().Which.Degree.Should().Be(3);

        Create("splinecontrolpoint", new Point2(0, 0), new Point2(1, 2), new Point2(3, 2), new Point2(4, 0))
            .Should().ContainSingle().Which.Should().BeOfType<SplineEntity>().Which.ControlPoints.Should().HaveCount(4);

        var slot = Create("slot", new Point2(0, 0), new Point2(10, 0), new Point2(0, 2));
        slot.Should().HaveCount(5);
        slot.OfType<ArcEntity>().Should().Contain(arc => arc.Radius == 2);
        slot.OfType<LineEntity>().Should().Contain(line => line.IsConstruction);
    }

    [Fact]
    public void CreatesSplineAsFitPointSpline()
    {
        var fitPoints = new[]
        {
            new Point2(0, 0),
            new Point2(1, 2),
            new Point2(3, 1),
            new Point2(5, 4),
            new Point2(7, 0)
        };

        var spline = Create("spline", fitPoints)
            .Should().ContainSingle().Subject.Should().BeOfType<SplineEntity>().Subject;

        spline.FitPoints.Should().Equal(fitPoints);
        foreach (var point in fitPoints)
        {
            spline.GetSamplePoints().Should().Contain(point);
        }
    }

    private IReadOnlyList<DrawingEntity> Create(string toolName, params Point2[] points) =>
        SketchCreationEntityFactory.CreateEntitiesForTool(toolName, points, CreateEntityId, isConstruction: false);

    private IReadOnlyList<DrawingEntity> CreatePolygon(string toolName, int sideCount) =>
        SketchCreationEntityFactory.CreateEntitiesForTool(
            toolName,
            new[] { new Point2(0, 0), new Point2(10, 0) },
            CreateEntityId,
            isConstruction: false,
            new Dictionary<string, double> { ["sides"] = sideCount });

    private EntityId CreateEntityId(string prefix) => EntityId.Create($"{prefix}-{++_sequence}");

    private static double Distance(Point2 first, Point2 second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

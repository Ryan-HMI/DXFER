using DXFER.Blazor.Sketching;
using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class SketchCreationEntityFactoryTests
{
    private int _sequence;

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
    public void CreatesEllipticalArcEntityWithEndParameter()
    {
        var entities = Create("ellipticalarc", new Point2(0, 0), new Point2(4, 0), new Point2(0, 2), new Point2(0, 2));

        var ellipse = entities.Should().ContainSingle().Subject.Should().BeOfType<EllipseEntity>().Subject;
        ellipse.StartParameterDegrees.Should().Be(0);
        ellipse.EndParameterDegrees.Should().BeApproximately(90, 0.000001);
    }

    [Fact]
    public void CreatesInscribedAndCircumscribedPolygonLines()
    {
        var inscribed = Create("inscribedpolygon", new Point2(0, 0), new Point2(10, 0));
        var circumscribed = Create("circumscribedpolygon", new Point2(0, 0), new Point2(10, 0));

        inscribed.Should().HaveCount(7);
        circumscribed.Should().HaveCount(7);
        inscribed[0].Should().BeOfType<CircleEntity>()
            .Which.IsConstruction.Should().BeTrue();
        circumscribed[0].Should().BeOfType<CircleEntity>()
            .Which.IsConstruction.Should().BeTrue();
        inscribed.Skip(1).Should().AllBeOfType<LineEntity>();
        circumscribed.Skip(1).Should().AllBeOfType<LineEntity>();
        ((LineEntity)inscribed[1]).Start.Should().Be(new Point2(10, 0));
        Distance(new Point2(0, 0), ((LineEntity)circumscribed[1]).Start)
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

    private IReadOnlyList<DrawingEntity> Create(string toolName, params Point2[] points) =>
        SketchCreationEntityFactory.CreateEntitiesForTool(toolName, points, CreateEntityId, isConstruction: false);

    private EntityId CreateEntityId(string prefix) => EntityId.Create($"{prefix}-{++_sequence}");

    private static double Distance(Point2 first, Point2 second)
    {
        var dx = second.X - first.X;
        var dy = second.Y - first.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }
}

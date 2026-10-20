using DXFER.Core.Documents;
using DXFER.Core.Geometry;
using DXFER.Core.Operations;
using FluentAssertions;

namespace DXFER.Core.Tests.Sketching;

public sealed class PlaneGcsPatternValidationTests
{
    [Theory]
    [InlineData("point")]
    [InlineData("line")]
    [InlineData("polyline")]
    [InlineData("circle")]
    [InlineData("arc")]
    [InlineData("ellipse")]
    [InlineData("polygon")]
    [InlineData("spline")]
    public void ValidationAcceptsOnlySmallGeometryResidualsWithoutRepairingMembers(string kind)
    {
        var original = Document(kind);
        var member = Member(original);
        foreach (var drift in new[] { 1e-9, 1e-6 })
        {
            var moved = member.Transform(Transform2.Translation(drift, 0));
            var candidate = Replace(original, moved);

            DrawingPatternService.TryValidate(candidate, out var reason).Should().Be(drift < 1e-7, reason);
            Member(candidate).Should().BeSameAs(moved);
            candidate.Metadata.Should().BeSameAs(original.Metadata);
            Member(original).Should().BeSameAs(member);
        }
    }

    [Theory]
    [InlineData("point")]
    [InlineData("line")]
    [InlineData("polyline")]
    [InlineData("circle")]
    [InlineData("arc")]
    [InlineData("ellipse")]
    [InlineData("polygon")]
    [InlineData("spline")]
    public void ConstructionStateRemainsExact(string kind)
    {
        var original = Document(kind);
        var changed = Member(original).WithConstruction(true);
        DrawingPatternService.TryValidate(Replace(original, changed), out _).Should().BeFalse();
    }

    [Fact]
    public void PolylineClosureAndVertexCountRemainExactWithinCoordinateTolerance()
    {
        var original = Document("polyline");
        var member = (PolylineEntity)Member(original);
        var open = member.Vertices.ToArray();
        open[^1] = new(open[^1].X + 1e-9, open[^1].Y);
        DrawingPatternService.TryValidate(Replace(original,
            new PolylineEntity(member.Id, open)), out _).Should().BeFalse();
        DrawingPatternService.TryValidate(Replace(original,
            new PolylineEntity(member.Id, member.Vertices.Append(member.Vertices[^1]))), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SplineKnotsAndWeightsRemainExactEvenBelowGeometryTolerance(bool changeWeight)
    {
        var original = Document("spline");
        var member = (SplineEntity)Member(original);
        var knots = member.Knots.ToArray();
        var weights = member.Weights.ToArray();
        if (changeWeight) weights[0] += 1e-10;
        else knots[3] -= 1e-10;
        var changed = new SplineEntity(member.Id, member.Degree, member.ControlPoints, knots, weights,
            member.IsConstruction, member.FitPoints, member.StartTangentHandle, member.EndTangentHandle);

        DrawingPatternService.TryValidate(Replace(original, changed), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(0.000000001, true)]
    [InlineData(0.000001, false)]
    public void EllipseMajorAxisVectorUsesBoundedGeometricTolerance(double delta, bool valid)
    {
        var original = Document("ellipse");
        var member = (EllipseEntity)Member(original);
        var changed = member with { MajorAxisEndPoint = new(member.MajorAxisEndPoint.X + delta, member.MajorAxisEndPoint.Y) };
        DrawingPatternService.TryValidate(Replace(original, changed), out var reason).Should().Be(valid, reason);
    }

    [Fact]
    public void PolygonTopologyEntityTypeAndIdentityRemainExact()
    {
        var original = Document("polygon");
        var member = (PolygonEntity)Member(original);
        DrawingPatternService.TryValidate(Replace(original, member with { SideCount = member.SideCount + 1 }), out _)
            .Should().BeFalse();
        DrawingPatternService.TryValidate(Replace(original, member with { Circumscribed = !member.Circumscribed }), out _)
            .Should().BeFalse();
        DrawingPatternService.TryValidate(Replace(original, new PointEntity(member.Id, member.Center)), out _)
            .Should().BeFalse();
        DrawingPatternService.TryValidate(Replace(original, new PolygonEntity(EntityId.Create("wrong-id"),
            member.Center, member.Radius, member.RotationAngleDegrees, member.SideCount)), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(0.000001)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ChangedOrNonfiniteRadiusIsRejected(double radiusDelta)
    {
        var original = Document("circle");
        var member = (CircleEntity)Member(original);
        DrawingPatternService.TryValidate(Replace(original, member with { Radius = member.Radius + radiusDelta }), out _)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData(270, 360, 270, 0, true)]
    [InlineData(20, 110, 380, 470, true)]
    [InlineData(20, 110, 380, 110, true)]
    [InlineData(0, 360, 0, 0, false)]
    [InlineData(0, 0, 0, 360, false)]
    [InlineData(0, 450, 0, 90, false)]
    [InlineData(20, 110, 110, 20, false)]
    [InlineData(20, 110, 20, 110.000001, false)]
    public void ArcAngleNormalizationPreservesSweepTopology(double start, double end, double normalizedStart,
        double normalizedEnd, bool valid)
    {
        var original = Document("arc", new ArcEntity(EntityId.Create("seed"), new(2, 3), 2, start, end));
        var member = (ArcEntity)Member(original);
        var changed = member with { StartAngleDegrees = normalizedStart, EndAngleDegrees = normalizedEnd };
        var candidate = Replace(original, changed);

        DrawingPatternService.TryValidate(candidate, out var reason).Should().Be(valid, reason);
        Member(candidate).Should().BeSameAs(changed);
    }

    private static DrawingDocument Replace(DrawingDocument document, DrawingEntity member) =>
        new(document.Entities.Take(1).Append(member), document.Dimensions, document.Constraints, document.Metadata);

    private static DrawingEntity Member(DrawingDocument document) => document.Entities[1];

    private static DrawingDocument Document(string kind, DrawingEntity? source = null)
    {
        var id = EntityId.Create("seed");
        DrawingEntity seed = source ?? (kind switch
        {
            "point" => new PointEntity(id, new(2, 3)),
            "line" => new LineEntity(id, new(1, 2), new(3, 4)),
            "polyline" => new PolylineEntity(id, [new(0, 0), new(4, 0), new(4, 3), new(0, 3), new(0, 0)]),
            "circle" => new CircleEntity(id, new(2, 3), 2),
            "arc" => new ArcEntity(id, new(2, 3), 2, 20, 110),
            "ellipse" => new EllipseEntity(id, new(2, 3), new(4, 0), 0.5),
            "polygon" => new PolygonEntity(id, new(2, 3), 2, 20, 5),
            "spline" => new SplineEntity(id, 2, [new(0, 0), new(1, 2), new(4, 0)], [0, 0, 0, 1, 1, 1], [1, 1, 1]),
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        });
        DrawingPatternService.TryCreate(new DrawingDocument([seed]), new PatternDefinition
        {
            Id = "array", SourceEntityIds = ["seed"], Kind = PatternKind.Rectangular,
            Rows = 1, Columns = 2, XPitch = 10, YPitch = 1
        }, out var document, out var reason).Should().BeTrue(reason);
        return document;
    }
}
